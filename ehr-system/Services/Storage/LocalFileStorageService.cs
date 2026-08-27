using Microsoft.Extensions.Options;
using EHR.Configuration;

namespace EHR.Services.Storage;

/// <summary>
/// Files on the local disk, for when no cloud bucket is configured.
///
/// WHY THIS EXISTS
/// Proof-of-delivery attachments are a real feature that a developer, a test run
/// and a demo all have to be able to exercise. Requiring a Google Cloud service
/// account key before the upload button does anything would mean the feature
/// could not be proved by running it, which is the only way anything here gets
/// signed off.
///
/// It is NOT a stub. It stores, retrieves and deletes the same encrypted bytes
/// the cloud implementation does, under the same object keys, so the two are
/// interchangeable and a deploy is a configuration change rather than a code
/// change.
///
/// WHICH ONE RUNS
/// Program.cs picks: a configured bucket name means Google Cloud Storage,
/// nothing configured means here. See the registration comment there.
///
/// WHAT IT IS NOT FOR
/// A multi-server deployment. Two app instances behind a load balancer do not
/// share a disk, so a file uploaded to one is missing from the other. That is
/// the point at which the bucket stops being optional.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IOptions<GoogleCloudStorageOptions> options,
        IWebHostEnvironment env,
        ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;

        // Outside wwwroot deliberately. Anything under wwwroot is served as a
        // static file by URL, which would hand out proof-of-delivery documents
        // to anyone who could guess an object key, with no authentication, no
        // tenant check and no audit row.
        _root = Path.Combine(env.ContentRootPath, "App_Data", "storage");
        Directory.CreateDirectory(_root);

        _logger.LogInformation(
            "LocalFileStorageService active. No cloud bucket is configured; files are on disk at {Root}.", _root);
    }

    /// <summary>
    /// Maps an object key to a path under the storage root, refusing anything
    /// that climbs out of it. A key is assembled from ids by FilePathBuilder and
    /// never comes straight from a request, but a path traversal check is one
    /// line and the failure it prevents is arbitrary file write.
    /// </summary>
    private string Resolve(string objectKey)
    {
        var full = Path.GetFullPath(Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage key escapes the storage root.");

        return full;
    }

    private static string CombineKey(string folderPath, string fileName)
        => $"{folderPath.TrimEnd('/')}/{fileName.TrimStart('/')}";

    /// <inheritdoc />
    public async Task<FileUploadResult> UploadFileAsync(
        Stream fileStream, string fileName, string folderPath, string contentType,
        Dictionary<string, string>? metadata = null)
    {
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer);
        return await UploadBytesAsync(buffer.ToArray(), fileName, folderPath, contentType, metadata);
    }

    /// <inheritdoc />
    public async Task<FileUploadResult> UploadBytesAsync(
        byte[] fileBytes, string fileName, string folderPath, string contentType,
        Dictionary<string, string>? metadata = null)
    {
        try
        {
            var key = CombineKey(folderPath, fileName);
            var path = Resolve(key);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, fileBytes);

            return new FileUploadResult
            {
                Success = true,
                CloudPath = key,
                FileSize = fileBytes.Length,
                ContentType = contentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store {FileName} under {FolderPath}", fileName, folderPath);
            return new FileUploadResult { Success = false, ErrorMessage = "Could not store the file." };
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadFileAsync(string filePath)
    {
        var bytes = await DownloadBytesAsync(filePath);
        return bytes == null ? null : new MemoryStream(bytes);
    }

    /// <inheritdoc />
    public async Task<byte[]?> DownloadBytesAsync(string filePath)
    {
        try
        {
            var path = Resolve(filePath);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {FilePath}", filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteFileAsync(string filePath)
    {
        try
        {
            var path = Resolve(filePath);
            if (File.Exists(path)) File.Delete(path);
            return Task.FromResult(true);   // already gone counts as deleted
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string filePath)
    {
        try { return Task.FromResult(File.Exists(Resolve(filePath))); }
        catch { return Task.FromResult(false); }
    }

    /// <summary>
    /// Deliberately unsupported. A signed URL is valid for whoever holds it and
    /// bypasses both the tenant check and the audit row, so nothing in DME uses
    /// one: files are served through a controller action instead. Throwing keeps
    /// that decision from being quietly undone by a caller who assumed the two
    /// implementations were interchangeable in every respect.
    /// </summary>
    public Task<string> GetSignedUrlAsync(string filePath, int? expirationMinutes = null)
        => throw new NotSupportedException(
            "DME serves files through an authenticated, audited controller action, not signed URLs.");

    /// <inheritdoc />
    public Task<List<string>> ListFilesAsync(string folderPath)
    {
        try
        {
            var dir = Resolve(folderPath);
            if (!Directory.Exists(dir)) return Task.FromResult(new List<string>());

            var files = Directory.GetFiles(dir)
                .Select(f => CombineKey(folderPath, Path.GetFileName(f)))
                .ToList();

            return Task.FromResult(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list {FolderPath}", folderPath);
            return Task.FromResult(new List<string>());
        }
    }

    /// <inheritdoc />
    public async Task<bool> CopyFileAsync(string sourcePath, string destinationPath)
    {
        var bytes = await DownloadBytesAsync(sourcePath);
        if (bytes == null) return false;

        var dest = Resolve(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllBytesAsync(dest, bytes);
        return true;
    }
}
