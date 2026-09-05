using EHR.Helpers;

namespace EHR.Services;

/// <summary>One file attached to an order.</summary>
/// <param name="DocumentId">Row id.</param>
/// <param name="OrderId">The order it proves.</param>
/// <param name="FileName">The original name, decrypted.</param>
/// <param name="ContentType">What it is.</param>
/// <param name="FileSize">Bytes, of the plaintext.</param>
/// <param name="UploadedByName">Who attached it.</param>
/// <param name="UploadedAt">When.</param>
public record OrderDocument(
    int DocumentId, int OrderId, string FileName, string ContentType,
    long FileSize, string UploadedByName, DateTime UploadedAt);

/// <summary>Proof-of-delivery files attached to an order.</summary>
public interface IDmeOrderDocuments
{
    /// <summary>Live documents on one order, newest first. Never the removed ones.</summary>
    IReadOnlyList<OrderDocument> ForOrder(int orderId);

    /// <summary>
    /// Validates, encrypts and stores one file, then records it. Returns a
    /// refusal with a reason rather than throwing, because every refusal here is
    /// something a person did and can fix.
    /// </summary>
    Task<AttachResult> AttachAsync(int orderId, string fileName, string contentType, byte[] bytes, int? userId);

    /// <summary>
    /// The decrypted bytes and original name, or null when the document is not
    /// this tenant's, has been removed, or its bytes are missing.
    /// </summary>
    Task<(byte[] Bytes, string FileName, string ContentType)?> OpenAsync(int documentId);

    /// <summary>
    /// Removes a document from the order. The ROW survives with a DeletedAt: a
    /// proof of delivery is the evidence a claim was legitimate, and the record
    /// that one was attached and then withdrawn is itself worth keeping.
    /// </summary>
    Task<bool> RemoveAsync(int documentId);
}

/// <summary>
/// WHY THIS EXISTS
/// The client: "Proof of Delivery: Allow for the option to attach Proof of
/// delivery files as pdf, pictures, etc." The POD panel captured a typed name
/// and a drawn signature and nothing else, so the delivery ticket the driver
/// photographed had nowhere to go. On a drop-shipped order there is no signature
/// at all and the carrier's paperwork IS the proof.
///
/// WHAT IT KNOWS
/// Its own table, and nothing else. Every rule about the FILE itself, which is
/// the part that is easy to get quietly wrong, lives in DmeDocumentStore and is
/// shared with customer documents. That split happened on 2026-09-05, when
/// customer attachments needed the same rules and copying them would have put
/// the magic-bytes check in two places.
///
/// WHO CALLS IT
/// DmeController: AttachPod, DownloadPod, RemovePod.
/// </summary>
public sealed class DmeOrderDocuments : IDmeOrderDocuments
{
    private readonly IDmeDb _db;
    private readonly DmeDocumentStore _files;

    /// <summary>
    /// Kept as a re-export so [RequestSizeLimit] on the controller still reads
    /// as the POD limit at the point it is applied.
    /// </summary>
    public const long MaxFileBytes = DmeDocumentStore.MaxFileBytes;

    public DmeOrderDocuments(IDmeDb db, DmeDocumentStore files)
    {
        _db = db;
        _files = files;
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderDocument> ForOrder(int orderId)
    {
        // Reads the VIEW, which excludes removed rows. That exclusion is the
        // whole removal mechanism, exactly as vDmePayments excludes voided
        // receipts, so no caller has to remember a DeletedAt filter.
        var rows = _db.Query(
            "SELECT * FROM dbo.vDmeOrderDocuments WHERE OrderId=@orderId ORDER BY UploadedAt DESC",
            new { orderId });

        return rows.Select(r => new OrderDocument(
            F.I(r["DocumentId"]),
            F.I(r["OrderId"]),
            _files.Unseal(F.S(r["FileName"])),
            F.S(r["ContentType"]),
            Convert.ToInt64(r["FileSize"]),
            F.S(r["UploadedByName"]),
            Convert.ToDateTime(r["UploadedAt"]))).ToList();
    }

    /// <inheritdoc />
    public async Task<AttachResult> AttachAsync(
        int orderId, string fileName, string contentType, byte[] bytes, int? userId)
    {
        var problem = _files.Problem(fileName, bytes, "a proof of delivery");
        if (problem != null) return new AttachResult(false, problem);

        // The order has to be this tenant's. DmeDb scopes the read, so an order
        // belonging to another supplier simply is not found.
        var order = _db.QueryOne("SELECT OrderId FROM dbo.DmeOrders WHERE OrderId=@orderId", new { orderId });
        if (order == null)
            return new AttachResult(false, "That order does not exist.");

        var stored = await _files.PutAsync(fileName, bytes, $"pod/{_db.TenantId}/{orderId}");
        if (stored == null)
            return new AttachResult(false, "The file could not be stored. Try again.");

        var id = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeOrderDocuments
            (TenantId, OrderId, FileName, StoragePath, ContentType, FileSize, FileHash, UploadedByUserId)
            OUTPUT inserted.DocumentId
            VALUES (@TenantId, @orderId, @name, @path, @ct, @size, @hash, @userId)",
            new
            {
                orderId,
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
        // Through the view again: a removed document must not be downloadable,
        // and another tenant's is filtered out by row level security.
        var row = _db.QueryOne(
            "SELECT * FROM dbo.vDmeOrderDocuments WHERE DocumentId=@documentId",
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
            "SELECT StoragePath FROM dbo.vDmeOrderDocuments WHERE DocumentId=@documentId",
            new { documentId });

        if (row == null) return false;   // already removed, or not this tenant's

        // Guarded on DeletedAt IS NULL so two people clicking Remove produce one
        // removal and the first timestamp stands. Same shape as voiding a
        // payment.
        var updated = _db.Execute(
            "UPDATE dbo.DmeOrderDocuments SET DeletedAt = SYSUTCDATETIME() " +
            "WHERE DocumentId=@documentId AND TenantId=@TenantId AND DeletedAt IS NULL",
            new { documentId });

        if (updated != 1) return false;

        // The row is what proves a document was attached and withdrawn, so it
        // survives. The BYTES do not. A failure here is logged by the storage
        // service and does not undo the removal, because the document is already
        // unreachable either way.
        await _files.DeleteAsync(F.S(row["StoragePath"]));
        return true;
    }
}
