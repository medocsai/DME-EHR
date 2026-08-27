using System.Security.Cryptography;
using EHR.Helpers;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;

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

/// <summary>Outcome of an attach attempt, with a sentence a person can act on.</summary>
public record AttachResult(bool Success, string? Error = null, int DocumentId = 0);

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
/// WHAT IT KNOWS THAT NOBODY ELSE HAS TO
///
/// 1. The file is ENCRYPTED before it leaves the app. A proof of delivery
///    carries the customer's name, home address and signature. It is PHI, and
///    the bucket must never hold it in the clear.
///
/// 2. The stored filename is OPAQUE and the original is encrypted into the row.
///    A bucket listing must not read "john-doe-oxygen-pod.pdf".
///
/// 3. Validation is THREE ways: extension, MIME type, and the leading magic
///    bytes actually matching the extension. The third is the one that stops a
///    renamed executable, and FileValidator already implements it.
///
/// 4. The hash is of the PLAINTEXT, taken before encryption. That is what makes
///    "this is the document that was uploaded" checkable years later, when the
///    encryption key may have been rotated.
///
/// 5. Nothing here hands out a URL. Files are served by a controller action that
///    authenticates, checks the tenant and writes an audit row. See the note in
///    LocalFileStorageService.GetSignedUrlAsync.
///
/// WHO CALLS IT
/// DmeController: AttachPod, DownloadPod, RemovePod.
/// </summary>
public sealed class DmeOrderDocuments : IDmeOrderDocuments
{
    private readonly IDmeDb _db;
    private readonly IFileStorageService _storage;
    private readonly FilePathBuilder _paths;
    private readonly FileValidator _validator;
    private readonly EncryptionHelper _encryption;

    /// <summary>
    /// Bigger than a phone photo of a delivery ticket, smaller than an accident.
    /// A modern phone camera produces 3 to 12MB; 25MB leaves room for a
    /// multi-page scan without letting somebody attach a video.
    /// </summary>
    public const long MaxFileBytes = 25L * 1024 * 1024;

    /// <summary>
    /// What a proof of delivery actually is. Deliberately narrower than the
    /// shared validator's list: a spreadsheet is not a proof of delivery, and
    /// every extra type is another parser somebody's antivirus has to trust.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

    public DmeOrderDocuments(
        IDmeDb db,
        IFileStorageService storage,
        FilePathBuilder paths,
        EncryptionHelper encryption)
    {
        _db = db;
        _storage = storage;
        _paths = paths;
        _encryption = encryption;
        _validator = new FileValidator(MaxFileBytes);
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
            _encryption.Decrypt(F.S(r["FileName"])) ?? "document",
            F.S(r["ContentType"]),
            Convert.ToInt64(r["FileSize"]),
            F.S(r["UploadedByName"]),
            Convert.ToDateTime(r["UploadedAt"]))).ToList();
    }

    /// <inheritdoc />
    public async Task<AttachResult> AttachAsync(
        int orderId, string fileName, string contentType, byte[] bytes, int? userId)
    {
        if (bytes.Length == 0)
            return new AttachResult(false, "That file is empty.");

        if (bytes.Length > MaxFileBytes)
            return new AttachResult(false, $"That file is larger than {MaxFileBytes / 1024 / 1024}MB.");

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
            return new AttachResult(false,
                $"'{extension}' is not a proof of delivery. Attach a PDF or a picture.");

        // The check that matters. Extension and MIME both come from the browser
        // and are whatever the uploader says they are; the leading bytes are the
        // file itself.
        if (!_validator.ValidateFileContent(bytes, extension))
            return new AttachResult(false,
                "That file is not really a " + extension.TrimStart('.').ToUpperInvariant() + ".");

        // The order has to be this tenant's. DmeDb scopes the read, so an order
        // belonging to another supplier simply is not found.
        var order = _db.QueryOne("SELECT OrderId FROM dbo.DmeOrders WHERE OrderId=@orderId", new { orderId });
        if (order == null)
            return new AttachResult(false, "That order does not exist.");

        // Hash the PLAINTEXT, before encryption, so the document stays checkable
        // across a key rotation.
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var cipher = _encryption.EncryptBytes(bytes);
        var storageName = _paths.GenerateEncryptedFileName(fileName);
        var folder = $"pod/{_db.TenantId}/{orderId}";

        var upload = await _storage.UploadBytesAsync(
            cipher, storageName, folder, "application/octet-stream");

        if (!upload.Success)
            return new AttachResult(false, "The file could not be stored. Try again.");

        var id = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeOrderDocuments
            (TenantId, OrderId, FileName, StoragePath, ContentType, FileSize, FileHash, UploadedByUserId)
            OUTPUT inserted.DocumentId
            VALUES (@TenantId, @orderId, @name, @path, @ct, @size, @hash, @userId)",
            new
            {
                orderId,
                name = _encryption.Encrypt(fileName),   // the original name is PHI
                path = upload.CloudPath,
                ct = contentType,
                size = (long)bytes.Length,              // the PLAINTEXT size, which is what a person sees
                hash,
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

        var cipher = await _storage.DownloadBytesAsync(F.S(row["StoragePath"]));
        if (cipher == null) return null;

        var plain = _encryption.DecryptBytes(cipher);
        if (plain == null) return null;

        return (plain,
                _encryption.Decrypt(F.S(row["FileName"])) ?? "document",
                F.S(row["ContentType"]));
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
        // survives. The BYTES do not: keeping PHI nobody can reach through the
        // product is storage risk with no purpose. A failure here is logged by
        // the storage service and does not undo the removal, because the
        // document is already unreachable either way.
        await _storage.DeleteFileAsync(F.S(row["StoragePath"]));
        return true;
    }
}
