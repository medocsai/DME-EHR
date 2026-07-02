using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Represents an audio recording session linked to an appointment.
/// Used for capturing provider-patient conversations for transcription.
/// HIPAA-compliant: CompleteTranscription is encrypted at rest.
/// </summary>
public partial class RecordingSession
{
    public int SessionId { get; set; }

    public int TenantId { get; set; }

    public int AppointmentId { get; set; }

    public int ProviderId { get; set; }

    public int PatientId { get; set; }

    /// <summary>
    /// Recording status: NotStarted, Recording, Paused, Completed, Cancelled
    /// </summary>
    public string RecordingStatus { get; set; } = "NotStarted";

    /// <summary>
    /// When recording actually started (first Start Recording click)
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// When recording was completed or cancelled
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Total duration in seconds (excluding paused time)
    /// </summary>
    public int? TotalDurationSeconds { get; set; }

    /// <summary>
    /// Merged transcription from all chunks (encrypted - PHI)
    /// </summary>
    public string? CompleteTranscription { get; set; }

    /// <summary>
    /// Filename of the merged audio file (stored encrypted on disk)
    /// </summary>
    public string? MergedAudioFileName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    public int? UpdatedByUserId { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Provider Provider { get; set; } = null!;

    public virtual Patient Patient { get; set; } = null!;

    public virtual ICollection<TranscriptionChunk> TranscriptionChunks { get; set; } = new List<TranscriptionChunk>();
}
