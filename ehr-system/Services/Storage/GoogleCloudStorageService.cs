using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using EHR.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EHR.Services.Storage;

/// <summary>
/// Google Cloud Storage implementation of IFileStorageService.
/// Handles all file operations for cloud storage.
/// </summary>
public class GoogleCloudStorageService : IFileStorageService
{
    private readonly StorageClient _storageClient;
    private readonly GoogleCloudStorageOptions _options;
    private readonly ILogger<GoogleCloudStorageService> _logger;
    private readonly UrlSigner? _urlSigner;

    public GoogleCloudStorageService(
        IOptions<GoogleCloudStorageOptions> options,
        ILogger<GoogleCloudStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Initialize storage client
        if (!string.IsNullOrEmpty(_options.CredentialsPath) && File.Exists(_options.CredentialsPath))
        {
            var credential = GoogleCredential.FromFile(_options.CredentialsPath);
            _storageClient = StorageClient.Create(credential);

            // Initialize URL signer for signed URLs
            var serviceAccountCredential = credential.UnderlyingCredential as ServiceAccountCredential;
            if (serviceAccountCredential != null)
            {
                _urlSigner = UrlSigner.FromCredential(serviceAccountCredential);
            }
        }
        else
        {
            // Use Application Default Credentials (ADC)
            _storageClient = StorageClient.Create();

            // Try to create URL signer from ADC
            try
            {
                var credential = GoogleCredential.GetApplicationDefault();
                var serviceAccountCredential = credential.UnderlyingCredential as ServiceAccountCredential;
                if (serviceAccountCredential != null)
                {
                    _urlSigner = UrlSigner.FromCredential(serviceAccountCredential);
                }
            }
            catch
            {
                _logger.LogWarning("Could not initialize URL signer - signed URLs will not be available");
            }
        }

        _logger.LogInformation("GoogleCloudStorageService initialized for bucket: {BucketName}", _options.BucketName);
    }

    /// <inheritdoc />
    public async Task<FileUploadResult> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string folderPath,
        string contentType,
        Dictionary<string, string>? metadata = null)
    {
        try
        {
            var objectName = CombinePath(folderPath, fileName);

            _logger.LogInformation("Uploading file to GCS: {ObjectName}", objectName);

            var uploadOptions = new UploadObjectOptions();
            var storageObject = new Google.Apis.Storage.v1.Data.Object
            {
                Bucket = _options.BucketName,
                Name = objectName,
                ContentType = contentType,
                Metadata = metadata
            };

            var result = await _storageClient.UploadObjectAsync(
                storageObject,
                fileStream,
                uploadOptions);

            _logger.LogInformation("File uploaded successfully: {ObjectName}, Size: {Size} bytes",
                objectName, result.Size);

            return new FileUploadResult
            {
                Success = true,
                CloudPath = objectName,
                FileSize = (long)(result.Size ?? 0),
                ContentType = contentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file: {FileName} to {FolderPath}", fileName, folderPath);
            return new FileUploadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<FileUploadResult> UploadBytesAsync(
        byte[] fileBytes,
        string fileName,
        string folderPath,
        string contentType,
        Dictionary<string, string>? metadata = null)
    {
        using var stream = new MemoryStream(fileBytes);
        var result = await UploadFileAsync(stream, fileName, folderPath, contentType, metadata);
        result.FileSize = fileBytes.Length;
        return result;
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadFileAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Downloading file from GCS: {FilePath}", filePath);

            var memoryStream = new MemoryStream();
            await _storageClient.DownloadObjectAsync(_options.BucketName, filePath, memoryStream);
            memoryStream.Position = 0;

            _logger.LogInformation("File downloaded successfully: {FilePath}, Size: {Size} bytes",
                filePath, memoryStream.Length);

            return memoryStream;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File not found in GCS: {FilePath}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file: {FilePath}", filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> DownloadBytesAsync(string filePath)
    {
        var stream = await DownloadFileAsync(filePath);
        if (stream == null) return null;

        using (stream)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteFileAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Deleting file from GCS: {FilePath}", filePath);

            await _storageClient.DeleteObjectAsync(_options.BucketName, filePath);

            _logger.LogInformation("File deleted successfully: {FilePath}", filePath);
            return true;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File not found for deletion: {FilePath}", filePath);
            return true; // Consider already deleted as success
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {FilePath}", filePath);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> FileExistsAsync(string filePath)
    {
        try
        {
            await _storageClient.GetObjectAsync(_options.BucketName, filePath);
            return true;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking file existence: {FilePath}", filePath);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetSignedUrlAsync(string filePath, int? expirationMinutes = null)
    {
        try
        {
            if (_urlSigner == null)
            {
                _logger.LogWarning("URL signer not available, returning direct GCS URL");
                return $"https://storage.googleapis.com/{_options.BucketName}/{filePath}";
            }

            var expiration = TimeSpan.FromMinutes(expirationMinutes ?? _options.SignedUrlExpirationMinutes);

            var signedUrl = await _urlSigner.SignAsync(
                _options.BucketName,
                filePath,
                expiration,
                HttpMethod.Get);

            _logger.LogInformation("Generated signed URL for: {FilePath}, Expires in: {Minutes} minutes",
                filePath, expirationMinutes ?? _options.SignedUrlExpirationMinutes);

            return signedUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate signed URL for: {FilePath}", filePath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<string>> ListFilesAsync(string folderPath)
    {
        try
        {
            var files = new List<string>();
            var prefix = folderPath.TrimEnd('/') + "/";

            await foreach (var obj in _storageClient.ListObjectsAsync(_options.BucketName, prefix))
            {
                files.Add(obj.Name);
            }

            _logger.LogInformation("Listed {Count} files in folder: {FolderPath}", files.Count, folderPath);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list files in folder: {FolderPath}", folderPath);
            return new List<string>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CopyFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            _logger.LogInformation("Copying file: {Source} to {Destination}", sourcePath, destinationPath);

            await _storageClient.CopyObjectAsync(
                _options.BucketName,
                sourcePath,
                _options.BucketName,
                destinationPath);

            _logger.LogInformation("File copied successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file: {Source} to {Destination}", sourcePath, destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Combine folder path and file name into a full object name.
    /// </summary>
    private static string CombinePath(string folderPath, string fileName)
    {
        folderPath = folderPath.TrimEnd('/');
        fileName = fileName.TrimStart('/');
        return $"{folderPath}/{fileName}";
    }
}
