using System;

namespace EHR.Models.Generated;

/// <summary>
/// Represents an individual audio chunk from a recording session.
/// Chunks are created when silence is detected during recording.
/// HIPAA-compliant: TranscriptionText is encrypted at rest.
/// </summary>
public partial class TranscriptionChunk
{
    public int ChunkId { get; set; }

    public int SessionId { get; set; }

    /// <summary>
    /// Order of this chunk within the session (1-based)
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Filename of the chunk audio file (stored encrypted on disk)
    /// </summary>
    public string? ChunkAudioFileName { get; set; }

    /// <summary>
    /// Duration of this audio chunk in seconds
    /// </summary>
    public decimal? ChunkDurationSeconds { get; set; }

    /// <summary>
    /// When this chunk was recorded
    /// </summary>
    public DateTime RecordedTimestamp { get; set; }

    /// <summary>
    /// Transcription text from Gemini API (encrypted - PHI)
    /// </summary>
    public string? TranscriptionText { get; set; }

    /// <summary>
    /// Status: Pending, Processing, Completed, Failed
    /// </summary>
    public string TranscriptionStatus { get; set; } = "Pending";

    /// <summary>
    /// When transcription was completed
    /// </summary>
    public DateTime? TranscriptionCompletedAt { get; set; }

    /// <summary>
    /// Error message if transcription failed
    /// </summary>
    public string? TranscriptionError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    public virtual RecordingSession RecordingSession { get; set; } = null!;
}
