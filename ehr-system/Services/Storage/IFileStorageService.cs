namespace EHR.Services.Storage;

/// <summary>
/// Interface for file storage operations.
/// Abstracts cloud storage (GCS) to enable testing and future provider changes.
/// All file operations should go through this interface.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Upload a file to cloud storage.
    /// </summary>
    /// <param name="fileStream">The file content as a stream</param>
    /// <param name="fileName">The original file name</param>
    /// <param name="folderPath">The folder path within the bucket (e.g., "patient-documents/1/123")</param>
    /// <param name="contentType">The MIME type of the file</param>
    /// <param name="metadata">Optional metadata to attach to the file</param>
    /// <returns>The full cloud storage path of the uploaded file</returns>
    Task<FileUploadResult> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string folderPath,
        string contentType,
        Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Upload encrypted file bytes to cloud storage.
    /// </summary>
    /// <param name="fileBytes">The encrypted file content as bytes</param>
    /// <param name="fileName">The storage file name</param>
    /// <param name="folderPath">The folder path within the bucket</param>
    /// <param name="contentType">The MIME type of the file</param>
    /// <param name="metadata">Optional metadata to attach to the file</param>
    /// <returns>The full cloud storage path of the uploaded file</returns>
    Task<FileUploadResult> UploadBytesAsync(
        byte[] fileBytes,
        string fileName,
        string folderPath,
        string contentType,
        Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Download a file from cloud storage.
    /// </summary>
    /// <param name="filePath">The full cloud storage path</param>
    /// <returns>The file content as a stream, or null if not found</returns>
    Task<Stream?> DownloadFileAsync(string filePath);

    /// <summary>
    /// Download a file as bytes from cloud storage.
    /// </summary>
    /// <param name="filePath">The full cloud storage path</param>
    /// <returns>The file content as bytes, or null if not found</returns>
    Task<byte[]?> DownloadBytesAsync(string filePath);

    /// <summary>
    /// Delete a file from cloud storage.
    /// </summary>
    /// <param name="filePath">The full cloud storage path</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteFileAsync(string filePath);

    /// <summary>
    /// Check if a file exists in cloud storage.
    /// </summary>
    /// <param name="filePath">The full cloud storage path</param>
    /// <returns>True if the file exists</returns>
    Task<bool> FileExistsAsync(string filePath);

    /// <summary>
    /// Generate a signed URL for secure, temporary file access.
    /// </summary>
    /// <param name="filePath">The full cloud storage path</param>
    /// <param name="expirationMinutes">How long the URL should be valid</param>
    /// <returns>A signed URL for downloading the file</returns>
    Task<string> GetSignedUrlAsync(string filePath, int? expirationMinutes = null);

    /// <summary>
    /// List all files in a folder.
    /// </summary>
    /// <param name="folderPath">The folder path to list</param>
    /// <returns>List of file paths in the folder</returns>
    Task<List<string>> ListFilesAsync(string folderPath);

    /// <summary>
    /// Copy a file within cloud storage.
    /// </summary>
    /// <param name="sourcePath">The source file path</param>
    /// <param name="destinationPath">The destination file path</param>
    /// <returns>True if copy was successful</returns>
    Task<bool> CopyFileAsync(string sourcePath, string destinationPath);
}

/// <summary>
/// Result of a file upload operation.
/// </summary>
public class FileUploadResult
{
    /// <summary>
    /// Whether the upload was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The full cloud storage path of the uploaded file.
    /// </summary>
    public string CloudPath { get; set; } = string.Empty;

    /// <summary>
    /// The size of the uploaded file in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Error message if upload failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The content type of the uploaded file.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;
}
