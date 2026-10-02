using EHR.Helpers;

namespace EHR.Services;

/// <summary>One file attached to a customer.</summary>
/// <param name="DocumentId">Row id.</param>
/// <param name="CustomerId">Whose it is.</param>
/// <param name="Kind">insurance-card or id.</param>
/// <param name="KindName">The same thing, spelled the way a person reads it.</param>
/// <param name="FileName">The original name, decrypted.</param>
/// <param name="ContentType">What it is.</param>
/// <param name="FileSize">Bytes, of the plaintext.</param>
/// <param name="UploadedByName">Who attached it.</param>
/// <param name="UploadedAt">When.</param>
public record CustomerDocument(
    int DocumentId, int CustomerId, string Kind, string KindName, string FileName,
    string ContentType, long FileSize, string UploadedByName, DateTime UploadedAt);

/// <summary>The insurance card and the photo ID: the documents about the PERSON.</summary>
public interface IDmeCustomerDocuments
{
    /// <summary>The kinds a document may be, in the order the picker offers them.</summary>
    IReadOnlyList<(string Value, string Name)> Kinds { get; }

    /// <summary>Live documents on one customer, newest first. Never the removed ones.</summary>
    IReadOnlyList<CustomerDocument> ForCustomer(int customerId);

    /// <summary>
    /// Validates, encrypts and stores one file, then records it. Returns a
    /// refusal with a reason rather than throwing, because every refusal here is
    /// something a person did and can fix.
    /// </summary>
    Task<AttachResult> AttachAsync(
        int customerId, string kind, string fileName, string contentType, byte[] bytes, int? userId);

    /// <summary>
    /// The decrypted bytes and original name, or null when the document is not
    /// this tenant's, has been removed, or its bytes are missing.
    /// </summary>
    Task<(byte[] Bytes, string FileName, string ContentType)?> OpenAsync(int documentId);

    /// <summary>
    /// Removes a document from the customer. The ROW survives with a DeletedAt.
    /// </summary>
    Task<bool> RemoveAsync(int documentId);
}

/// <summary>
/// WHY THIS EXISTS
/// The New Customer screen had nowhere to put the insurance card or the photo
/// ID, and intake is exactly when somebody is holding both.
///
/// WHY ONLY THOSE TWO
/// Everything that defends a CLAIM (the written order, the CMN, medical records,
/// prior authorisation, proof of delivery) is per item and per period, so it is
/// attached to the ORDER, in DmeOrderDocuments, beside the claim it defends.
/// Filing a CMN against the customer would put an audit answer one join away
/// from the thing being audited. The card and the ID describe the person, not
/// any one order, so they are the documents that belong here. The list is two
/// entries on purpose and CK_DmeCustomerDocuments_Kind holds the same two.
///
/// WHAT IT KNOWS
/// Its own table and the two kinds. Every rule about the FILE itself lives in
/// DmeDocumentStore and is shared with order documents, so the magic-bytes
/// check, the 25MB cap and the hash-before-encrypt ordering exist once.
///
/// WHO CALLS IT
/// DmeController: CreateCustomer, AttachCustomerDoc, DownloadCustomerDoc,
/// RemoveCustomerDoc.
/// </summary>
public sealed class DmeCustomerDocuments : IDmeCustomerDocuments
{
    private readonly IDmeDb _db;
    private readonly DmeDocumentStore _files;

    /// <summary>
    /// The kinds, and the words for them. Stored lowercase and hyphenated to
    /// match every other coded column in this schema, and pinned by
    /// CK_DmeCustomerDocuments_Kind so a row can never be filed as something a
    /// screen filtering this list would miss.
    /// </summary>
    private static readonly (string Value, string Name)[] KindList =
    {
        ("insurance-card", "Insurance card"),
        ("id",             "Photo ID"),
    };

    public DmeCustomerDocuments(IDmeDb db, DmeDocumentStore files)
    {
        _db = db;
        _files = files;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Value, string Name)> Kinds => KindList;

    /// <inheritdoc />
    public IReadOnlyList<CustomerDocument> ForCustomer(int customerId)
    {
        // Reads the VIEW, which excludes removed rows. That exclusion IS the
        // removal mechanism, so no caller has to remember a DeletedAt filter.
        var rows = _db.Query(
            "SELECT * FROM dbo.vDmeCustomerDocuments WHERE CustomerId=@customerId ORDER BY UploadedAt DESC",
            new { customerId });

        return rows.Select(r =>
        {
            var kind = F.S(r["Kind"]);
            return new CustomerDocument(
                F.I(r["DocumentId"]),
                F.I(r["CustomerId"]),
                kind,
                NameOf(kind),
                _files.Unseal(F.S(r["FileName"])),
                F.S(r["ContentType"]),
                Convert.ToInt64(r["FileSize"]),
                F.S(r["UploadedByName"]),
                Convert.ToDateTime(r["UploadedAt"]));
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<AttachResult> AttachAsync(
        int customerId, string kind, string fileName, string contentType, byte[] bytes, int? userId)
    {
        // The kind is checked against the list rather than trusted from the
        // form. A posted value outside it would be refused by the check
        // constraint anyway, but as a 500 rather than a sentence.
        if (!KindList.Any(k => k.Value == kind))
            return new AttachResult(false, "Choose what kind of document that is.");

        var problem = _files.Problem(fileName, bytes, "a document we can file");
        if (problem != null) return new AttachResult(false, problem);

        // The customer has to be this tenant's. DmeDb scopes the read, so
        // somebody else's customer simply is not found.
        var customer = _db.QueryOne(
            "SELECT CustomerId FROM dbo.DmeCustomers WHERE CustomerId=@customerId", new { customerId });

        if (customer == null)
            return new AttachResult(false, "That customer does not exist.");

        var stored = await _files.PutAsync(fileName, bytes, $"customer/{_db.TenantId}/{customerId}");
        if (stored == null)
            return new AttachResult(false, "The file could not be stored. Try again.");

        var id = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeCustomerDocuments
            (TenantId, CustomerId, Kind, FileName, StoragePath, ContentType, FileSize, FileHash, UploadedByUserId)
            OUTPUT inserted.DocumentId
            VALUES (@TenantId, @customerId, @kind, @name, @path, @ct, @size, @hash, @userId)",
            new
            {
                customerId,
                kind,
                name = _files.Seal(fileName),          // the original name is PHI
                path = stored.StoragePath,
                ct = contentType,
                size = stored.Size,                    // the PLAINTEXT size, which is what a person sees
                hash = stored.Hash,
                userId = (object?)userId ?? DBNull.Value
            }));

        return new AttachResult(true, DocumentId: id);
    }

    /// <inheritdoc />
    public async Task<(byte[] Bytes, string FileName, string ContentType)?> OpenAsync(int documentId)
    {
        var row = _db.QueryOne(
            "SELECT * FROM dbo.vDmeCustomerDocuments WHERE DocumentId=@documentId",
            new { documentId });

        if (row == null) return null;

        var plain = await _files.GetAsync(F.S(row["StoragePath"]));
        if (plain == null) return null;

        return (plain, _files.Unseal(F.S(row["FileName"])), F.S(row["ContentType"]));
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(int documentId)
    {
        var row = _db.QueryOne(
            "SELECT StoragePath FROM dbo.vDmeCustomerDocuments WHERE DocumentId=@documentId",
            new { documentId });

        if (row == null) return false;   // already removed, or not this tenant's

        // Guarded on DeletedAt IS NULL so two people clicking Remove produce one
        // removal and the first timestamp stands.
        var updated = _db.Execute(
            "UPDATE dbo.DmeCustomerDocuments SET DeletedAt = SYSUTCDATETIME() " +
            "WHERE DocumentId=@documentId AND TenantId=@TenantId AND DeletedAt IS NULL",
            new { documentId });

        if (updated != 1) return false;

        await _files.DeleteAsync(F.S(row["StoragePath"]));
        return true;
    }

    private static string NameOf(string kind) =>
        KindList.FirstOrDefault(k => k.Value == kind).Name ?? kind;
}
