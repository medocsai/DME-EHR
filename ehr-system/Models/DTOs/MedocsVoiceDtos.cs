namespace EHR.Models.DTOs;

public class MedocsVoiceResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Transcription { get; set; }
    public string? ExtractedData { get; set; }
    public string? TabKey { get; set; }
}

public class TelehealthNoteGenerationRequest
{
    public int EncounterId { get; set; }
    public string Transcription { get; set; } = string.Empty;
    public List<int>? SelectedTemplateIds { get; set; }
    public string? PatientName { get; set; }
}

/// <summary>
/// Request DTO for post-call telehealth extraction.
/// Sends the full accumulated transcription for combined extraction of all sections.
/// </summary>
public class TelehealthExtractionRequest
{
    public string Transcription { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for unified voice extraction.
/// Includes existing data so Gemini knows what's already recorded.
/// </summary>
public class UnifiedExtractionRequest
{
    public string Transcription { get; set; } = string.Empty;
    public string? ExistingData { get; set; }
}
