namespace EHR.Models.Generated;

/// <summary>
/// Stores transcription text from telehealth AI Scribe chunks.
/// Each chunk corresponds to a segment of the conversation, persisted
/// as it comes so transcription survives page navigations.
/// </summary>
public partial class TelehealthTranscriptionChunk
{
    public int ChunkId { get; set; }
    public int TenantId { get; set; }
    public int EncounterId { get; set; }
    public int SequenceNumber { get; set; }
    public string TranscriptionText { get; set; } = string.Empty; // Encrypted PHI
    public decimal? DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual Encounter Encounter { get; set; } = null!;
}
