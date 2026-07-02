namespace EHR.Services.Storage.Helpers;

/// <summary>
/// Helper class for building metadata dictionaries for cloud storage files.
/// Ensures consistent metadata tagging across the application.
/// </summary>
public class MetadataBuilder
{
    /// <summary>
    /// Build metadata for a patient document.
    /// </summary>
    public Dictionary<string, string> BuildPatientDocumentMetadata(
        int tenantId,
        int patientId,
        string documentType,
        int? uploadedByUserId = null)
    {
        var metadata = new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "patientId", patientId.ToString() },
            { "documentType", documentType },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "patient-document" }
        };

        if (uploadedByUserId.HasValue)
        {
            metadata["uploadedByUserId"] = uploadedByUserId.Value.ToString();
        }

        return metadata;
    }

    /// <summary>
    /// Build metadata for a consent form.
    /// </summary>
    public Dictionary<string, string> BuildConsentFormMetadata(
        int tenantId,
        int patientId,
        string consentType,
        int? consentId = null)
    {
        var metadata = new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "patientId", patientId.ToString() },
            { "consentType", consentType },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "consent-form" }
        };

        if (consentId.HasValue)
        {
            metadata["consentId"] = consentId.Value.ToString();
        }

        return metadata;
    }

    /// <summary>
    /// Build metadata for an audio transcription file.
    /// </summary>
    public Dictionary<string, string> BuildAudioMetadata(
        int tenantId,
        int sessionId,
        int? sequenceNumber = null,
        decimal? durationSeconds = null)
    {
        var metadata = new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "sessionId", sessionId.ToString() },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "audio-transcription" }
        };

        if (sequenceNumber.HasValue)
        {
            metadata["sequenceNumber"] = sequenceNumber.Value.ToString();
        }

        if (durationSeconds.HasValue)
        {
            metadata["durationSeconds"] = durationSeconds.Value.ToString("F2");
        }

        return metadata;
    }

    /// <summary>
    /// Build metadata for a recording chunk.
    /// </summary>
    public Dictionary<string, string> BuildRecordingChunkMetadata(
        int tenantId,
        int sessionId,
        int sequenceNumber,
        decimal durationSeconds)
    {
        return new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "sessionId", sessionId.ToString() },
            { "sequenceNumber", sequenceNumber.ToString() },
            { "durationSeconds", durationSeconds.ToString("F2") },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "recording-chunk" },
            { "isEncrypted", "true" }
        };
    }

    /// <summary>
    /// Build metadata for a merged audio file.
    /// </summary>
    public Dictionary<string, string> BuildMergedAudioMetadata(
        int tenantId,
        int sessionId,
        int chunkCount,
        int totalDurationSeconds)
    {
        return new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "sessionId", sessionId.ToString() },
            { "chunkCount", chunkCount.ToString() },
            { "totalDurationSeconds", totalDurationSeconds.ToString() },
            { "mergedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "merged-audio" },
            { "isEncrypted", "true" }
        };
    }

    /// <summary>
    /// Build metadata for a provider signature.
    /// </summary>
    public Dictionary<string, string> BuildProviderSignatureMetadata(
        int tenantId,
        int providerId)
    {
        return new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "providerId", providerId.ToString() },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "provider-signature" }
        };
    }

    /// <summary>
    /// Build metadata for a patient profile picture.
    /// </summary>
    public Dictionary<string, string> BuildPatientProfilePictureMetadata(
        int tenantId,
        int patientId)
    {
        return new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "patientId", patientId.ToString() },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "patient-profile-picture" }
        };
    }

    /// <summary>
    /// Build metadata for a provider profile picture.
    /// </summary>
    public Dictionary<string, string> BuildProviderProfilePictureMetadata(
        int tenantId,
        int providerId)
    {
        return new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "providerId", providerId.ToString() },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "provider-profile-picture" }
        };
    }

    /// <summary>
    /// Build metadata for a tenant logo.
    /// </summary>
    public Dictionary<string, string> BuildTenantLogoMetadata(int tenantId)
    {
        return new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "tenant-logo" }
        };
    }

    /// <summary>
    /// Build metadata for clinical attachments.
    /// </summary>
    public Dictionary<string, string> BuildClinicalAttachmentMetadata(
        int tenantId,
        int episodeId,
        string attachmentType,
        int? noteId = null)
    {
        var metadata = new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "episodeId", episodeId.ToString() },
            { "attachmentType", attachmentType },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", "clinical-attachment" }
        };

        if (noteId.HasValue)
        {
            metadata["noteId"] = noteId.Value.ToString();
        }

        return metadata;
    }

    /// <summary>
    /// Build generic metadata with common fields.
    /// </summary>
    public Dictionary<string, string> BuildGenericMetadata(
        int tenantId,
        string fileCategory,
        Dictionary<string, string>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, string>
        {
            { "tenantId", tenantId.ToString() },
            { "uploadedDate", DateTime.UtcNow.ToString("o") },
            { "fileCategory", fileCategory }
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }
}
