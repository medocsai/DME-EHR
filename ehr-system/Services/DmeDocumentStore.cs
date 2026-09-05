using System.Security.Cryptography;
using EHR.Helpers;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;

namespace EHR.Services;

/// <summary>What a stored file looks like once it is safely on disk or in the bucket.</summary>
/// <param name="StoragePath">The opaque object key.</param>
/// <param name="Hash">SHA-256 of the PLAINTEXT, taken before encryption.</param>
/// <param name="Size">Plaintext bytes, which is the number a person recognises.</param>
public record StoredFile(string StoragePath, string Hash, long Size);

/// <summary>Outcome of an attach attempt, with a sentence a person can act on.</summary>
public record AttachResult(bool Success, string? Error = null, int DocumentId = 0);

/// <summary>
/// The rules about FILES, in one place.
///
/// WHY THIS EXISTS
/// Proof of delivery on an order and the CMN on a customer are different facts
/// and live in different tables, but they are the same problem: a scanned PDF
/// or a phone photo that carries somebody's name and address, which has to be
/// checked, encrypted, stored under a name that gives nothing away, and handed
/// back only through an audited action.
///
/// Those rules were written once, for proof of delivery, in 2026-08-27. Copying
/// them for customer documents would have put the magic-bytes check in two
/// places, and the version that gets forgotten is always the one that mattered.
/// This class is the one that knows; DmeOrderDocuments and DmeCustomerDocuments
/// each know only their own table.
///
/// WHAT IT KNOWS THAT NOBODY ELSE HAS TO
///
/// 1. The file is ENCRYPTED before it leaves the app. These documents carry the
///    customer's name, home address and sometimes their signature. It is PHI,
///    and the bucket must never hold it in the clear.
///
/// 2. The stored filename is OPAQUE. A bucket listing must not read
///    "john-doe-oxygen-pod.pdf".
///
/// 3. Validation is THREE ways: extension, size, and the leading magic bytes
///    actually matching the extension. The third is the one that stops a renamed
///    executable, because the first two are whatever the browser claims.
///
/// 4. The hash is of the PLAINTEXT, taken BEFORE encryption. That is what makes
///    "this is the document that was uploaded" checkable years later, when the
///    encryption key may have been rotated.
///
/// 5. Nothing here hands out a URL. Files are served by a controller action that
///    authenticates, checks the tenant and writes an audit row. See the note in
///    LocalFileStorageService.GetSignedUrlAsync.
///
/// WHO CALLS IT
/// DmeOrderDocuments and DmeCustomerDocuments. Nothing else should: a caller
/// reaching past them is a caller with no table to record what it stored.
/// </summary>
public sealed class DmeDocumentStore
{
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
    /// What these documents actually are. Deliberately narrower than the shared
    /// validator's list: a spreadsheet is not a proof of delivery or a
    /// prescription, and every extra type is another parser somebody's antivirus
    /// has to trust.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

    public DmeDocumentStore(
        IFileStorageService storage,
        FilePathBuilder paths,
        EncryptionHelper encryption)
    {
        _storage = storage;
        _paths = paths;
        _encryption = encryption;
        _validator = new FileValidator(MaxFileBytes);
    }

    /// <summary>
    /// Is this file one we will take. Returns the sentence to show the person,
    /// or null when it is fine.
    ///
    /// <paramref name="noun"/> is what the screen calls this kind of document,
    /// so the refusal reads "that is not a proof of delivery" rather than naming
    /// a class nobody outside the code has heard of.
    /// </summary>
    public string? Problem(string fileName, byte[] bytes, string noun)
    {
        if (bytes.Length == 0)
            return "That file is empty.";

        if (bytes.Length > MaxFileBytes)
            return $"That file is larger than {MaxFileBytes / 1024 / 1024}MB.";

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
            return $"'{extension}' is not {noun}. Attach a PDF or a picture.";

        // The check that matters. Extension and MIME both come from the browser
        // and are whatever the uploader says they are; the leading bytes are the
        // file itself.
        if (!_validator.ValidateFileContent(bytes, extension))
            return "That file is not really a " + extension.TrimStart('.').ToUpperInvariant() + ".";

        return null;
    }

    /// <summary>
    /// Hash, encrypt and store. Returns null when the storage service refused,
    /// which is the one failure here that is nobody's fault and worth retrying.
    /// </summary>
    public async Task<StoredFile?> PutAsync(string fileName, byte[] bytes, string folder)
    {
        // Hash the PLAINTEXT, before encryption, so the document stays checkable
        // across a key rotation. The order of these two lines is the whole point
        // and a test pins it.
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var cipher = _encryption.EncryptBytes(bytes);

        var upload = await _storage.UploadBytesAsync(
            cipher, _paths.GenerateEncryptedFileName(fileName), folder, "application/octet-stream");

        return upload.Success ? new StoredFile(upload.CloudPath, hash, bytes.Length) : null;
    }

    /// <summary>
    /// The plaintext bytes back, or null when they are missing or fail to
    /// decrypt.
    ///
    /// DecryptBytes returns NULL on tamper, unlike the string overload which
    /// returns its input. Handing a caller ciphertext to serve as a PDF would
    /// turn a detected tamper into a silent corrupt download.
    /// </summary>
    public async Task<byte[]?> GetAsync(string storagePath)
    {
        var cipher = await _storage.DownloadBytesAsync(storagePath);
        return cipher == null ? null : _encryption.DecryptBytes(cipher);
    }

    /// <summary>
    /// Delete the bytes. The ROW is the caller's business and always survives:
    /// the record that a document was attached and withdrawn is itself evidence.
    /// Keeping PHI nobody can reach through the product is storage risk with no
    /// purpose.
    /// </summary>
    public Task DeleteAsync(string storagePath) => _storage.DeleteFileAsync(storagePath);

    /// <summary>The original name is PHI, so it is encrypted into the row.</summary>
    public string Seal(string fileName) => _encryption.Encrypt(fileName);

    /// <summary>The original name back out of the row.</summary>
    public string Unseal(string? sealedName) => _encryption.Decrypt(F.S(sealedName)) ?? "document";
}
