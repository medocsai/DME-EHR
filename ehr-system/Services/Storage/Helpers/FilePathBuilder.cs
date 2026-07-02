namespace EHR.Services.Storage.Helpers;

/// <summary>
/// Helper class for constructing cloud storage file paths.
/// Ensures consistent path formatting across the application.
/// </summary>
public class FilePathBuilder
{
    /// <summary>
    /// Build the folder path for patient documents.
    /// Format: patient-documents/{tenantId}/{patientId}/{documentType}
    /// </summary>
    public string BuildPatientDocumentPath(int tenantId, int patientId, string? documentType = null)
    {
        var basePath = $"patient-documents/{tenantId}/{patientId}";
        return string.IsNullOrEmpty(documentType) ? basePath : $"{basePath}/{documentType}";
    }

    /// <summary>
    /// Build the folder path for consent forms.
    /// Format: consent-forms/{tenantId}/{patientId}
    /// </summary>
    public string BuildConsentFormPath(int tenantId, int patientId)
    {
        return $"consent-forms/{tenantId}/{patientId}";
    }

    /// <summary>
    /// Build the folder path for clinical attachments.
    /// Format: clinical-attachments/{tenantId}/{episodeId}
    /// </summary>
    public string BuildClinicalAttachmentPath(int tenantId, int episodeId)
    {
        return $"clinical-attachments/{tenantId}/{episodeId}";
    }

    /// <summary>
    /// Build the folder path for audio transcriptions.
    /// Format: audio-transcriptions/{tenantId}/{sessionId}
    /// </summary>
    public string BuildAudioTranscriptionPath(int tenantId, int sessionId)
    {
        return $"audio-transcriptions/{tenantId}/{sessionId}";
    }

    /// <summary>
    /// Build the folder path for recording chunks.
    /// Format: recording-chunks/{tenantId}
    /// </summary>
    public string BuildRecordingChunkPath(int tenantId)
    {
        return $"recording-chunks/{tenantId}";
    }

    /// <summary>
    /// Build the folder path for merged audio files.
    /// Format: merged-audio/{tenantId}
    /// </summary>
    public string BuildMergedAudioPath(int tenantId)
    {
        return $"merged-audio/{tenantId}";
    }

    /// <summary>
    /// Build the folder path for provider signatures.
    /// Format: provider-signatures/{tenantId}/{providerId}
    /// </summary>
    public string BuildProviderSignaturePath(int tenantId, int providerId)
    {
        return $"provider-signatures/{tenantId}/{providerId}";
    }

    /// <summary>
    /// Build the folder path for patient profile pictures.
    /// Format: patient-profile-pictures/{tenantId}/{patientId}
    /// </summary>
    public string BuildPatientProfilePicturePath(int tenantId, int patientId)
    {
        return $"patient-profile-pictures/{tenantId}/{patientId}";
    }

    /// <summary>
    /// Build the folder path for provider profile pictures.
    /// Format: provider-profile-pictures/{tenantId}/{providerId}
    /// </summary>
    public string BuildProviderProfilePicturePath(int tenantId, int providerId)
    {
        return $"provider-profile-pictures/{tenantId}/{providerId}";
    }

    /// <summary>
    /// Build the folder path for tenant logos.
    /// Format: tenant-logos/{tenantId}
    /// </summary>
    public string BuildTenantLogoPath(int tenantId)
    {
        return $"tenant-logos/{tenantId}";
    }

    /// <summary>
    /// Build the folder path for report templates.
    /// Format: report-templates/{tenantId}
    /// </summary>
    public string BuildReportTemplatePath(int tenantId)
    {
        return $"report-templates/{tenantId}";
    }

    /// <summary>
    /// Build the folder path for exported data files.
    /// Format: exports/{tenantId}/{exportType}
    /// </summary>
    public string BuildExportPath(int tenantId, string exportType)
    {
        return $"exports/{tenantId}/{exportType}";
    }

    /// <summary>
    /// Combine a folder path and file name into a full cloud path.
    /// </summary>
    public string CombinePath(string folderPath, string fileName)
    {
        // Ensure no double slashes
        folderPath = folderPath.TrimEnd('/');
        fileName = fileName.TrimStart('/');
        return $"{folderPath}/{fileName}";
    }

    /// <summary>
    /// Generate a unique storage file name (GUID-based) with original extension.
    /// </summary>
    public string GenerateStorageFileName(string originalFileName, string? suffix = null)
    {
        var extension = Path.GetExtension(originalFileName);
        var baseName = $"{Guid.NewGuid():N}";
        return string.IsNullOrEmpty(suffix) ? $"{baseName}{extension}" : $"{baseName}{suffix}{extension}";
    }

    /// <summary>
    /// Generate a unique encrypted file name with .enc extension.
    /// </summary>
    public string GenerateEncryptedFileName(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        return $"{Guid.NewGuid():N}{extension}.enc";
    }
}
