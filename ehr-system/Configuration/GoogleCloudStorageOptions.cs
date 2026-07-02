namespace EHR.Configuration;

/// <summary>
/// Configuration options for Google Cloud Storage.
/// Loaded from appsettings.json section "GoogleCloudStorage".
/// </summary>
public class GoogleCloudStorageOptions
{
    /// <summary>
    /// The name of the GCS bucket to store files in.
    /// Example: "medocs-ptehr-files"
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to the service account credentials JSON file.
    /// If not provided, uses Application Default Credentials (ADC).
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// The Google Cloud project ID.
    /// Required for some operations.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Default expiration time in minutes for signed URLs.
    /// Default: 60 minutes (1 hour).
    /// </summary>
    public int SignedUrlExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum file size in bytes.
    /// Default: 100MB.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Enable file versioning in GCS.
    /// Useful for backup and recovery.
    /// </summary>
    public bool EnableVersioning { get; set; } = true;

    /// <summary>
    /// The base URL for accessing files (if using CDN or custom domain).
    /// If not set, uses the standard GCS URL format.
    /// </summary>
    public string? BaseUrl { get; set; }
}
