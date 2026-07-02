namespace EHR.Services.Storage.Helpers;

/// <summary>
/// Helper class for validating files before upload.
/// Centralizes validation logic for security and consistency.
/// </summary>
public class FileValidator
{
    private readonly long _maxFileSizeBytes;

    // Allowed document MIME types
    private static readonly HashSet<string> AllowedDocumentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/bmp",
        "image/tiff",

        // Documents
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain"
    };

    // Allowed document extensions
    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt"
    };

    // Allowed audio MIME types
    private static readonly HashSet<string> AllowedAudioContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm",
        "audio/mp3",
        "audio/mpeg",
        "audio/wav",
        "audio/wave",
        "audio/x-wav",
        "audio/ogg",
        "audio/mp4",
        "audio/m4a",
        "audio/aac"
    };

    // Allowed audio extensions
    private static readonly HashSet<string> AllowedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".mp3", ".wav", ".ogg", ".m4a", ".aac"
    };

    // Allowed signature image types
    private static readonly HashSet<string> AllowedSignatureContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png"
    };

    private static readonly HashSet<string> AllowedSignatureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png"
    };

    public FileValidator(long maxFileSizeBytes = 100 * 1024 * 1024)
    {
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    /// <summary>
    /// Validate a document file (patient documents, attachments).
    /// </summary>
    public FileValidationResult ValidateDocument(Stream fileStream, string fileName, string contentType)
    {
        return ValidateFile(fileStream, fileName, contentType,
            AllowedDocumentContentTypes, AllowedDocumentExtensions, _maxFileSizeBytes);
    }

    /// <summary>
    /// Validate an audio file.
    /// </summary>
    public FileValidationResult ValidateAudio(Stream fileStream, string fileName, string contentType)
    {
        return ValidateFile(fileStream, fileName, contentType,
            AllowedAudioContentTypes, AllowedAudioExtensions, _maxFileSizeBytes);
    }

    /// <summary>
    /// Validate a signature image file.
    /// Max size: 2MB for signatures.
    /// </summary>
    public FileValidationResult ValidateSignature(Stream fileStream, string fileName, string contentType)
    {
        const long maxSignatureSize = 2 * 1024 * 1024; // 2MB
        return ValidateFile(fileStream, fileName, contentType,
            AllowedSignatureContentTypes, AllowedSignatureExtensions, maxSignatureSize);
    }

    /// <summary>
    /// Validate a file against provided criteria.
    /// </summary>
    private FileValidationResult ValidateFile(
        Stream fileStream,
        string fileName,
        string contentType,
        HashSet<string> allowedContentTypes,
        HashSet<string> allowedExtensions,
        long maxSize)
    {
        // Validate file name
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Failure("File name is required");
        }

        // Validate extension
        var extension = Path.GetExtension(fileName);
        if (!allowedExtensions.Contains(extension))
        {
            return FileValidationResult.Failure(
                $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");
        }

        // Validate content type
        if (!allowedContentTypes.Contains(contentType))
        {
            return FileValidationResult.Failure(
                $"Content type '{contentType}' is not allowed");
        }

        // Validate file size
        if (fileStream.Length > maxSize)
        {
            var maxSizeMB = maxSize / (1024 * 1024);
            return FileValidationResult.Failure(
                $"File size exceeds maximum allowed size of {maxSizeMB}MB");
        }

        return FileValidationResult.Valid();
    }

    /// <summary>
    /// Validate file content using magic bytes to prevent disguised files.
    /// </summary>
    public bool ValidateFileContent(byte[] bytes, string extension)
    {
        if (bytes.Length < 4) return false;

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".png" => bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            ".gif" => bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46,
            ".pdf" => bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46,
            ".doc" => bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0,
            ".docx" or ".xlsx" => bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04,
            ".xls" => bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0,
            ".txt" => true, // Text files don't have specific magic bytes
            ".bmp" => bytes[0] == 0x42 && bytes[1] == 0x4D,
            ".webp" => bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46,
            ".tiff" or ".tif" => (bytes[0] == 0x49 && bytes[1] == 0x49) || (bytes[0] == 0x4D && bytes[1] == 0x4D),
            ".wav" => bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46,
            ".mp3" => (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) || (bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33),
            ".webm" => bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3,
            ".ogg" => bytes.Length >= 4 && bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53,
            ".enc" => true, // Encrypted files - content validation not applicable
            _ => true // Allow by default for other extensions
        };
    }
}

/// <summary>
/// Result of file validation.
/// </summary>
public class FileValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public static FileValidationResult Valid() => new() { IsValid = true };
    public static FileValidationResult Failure(string message) => new() { IsValid = false, ErrorMessage = message };
}
