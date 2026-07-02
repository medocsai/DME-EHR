using System;

namespace EHR.Models.Generated;

/// <summary>
/// Represents a message in a patient-staff conversation.
/// Text-only messaging. Content encrypted at rest for HIPAA compliance.
/// </summary>
public partial class PatientMessage
{
    public int PatientMessageId { get; set; }

    public int TenantId { get; set; }

    public int PatientConversationId { get; set; }

    /// <summary>
    /// "Patient", "Provider", or "System"
    /// </summary>
    public string SenderType { get; set; } = null!;

    /// <summary>
    /// Set when SenderType is "Patient"
    /// </summary>
    public int? SenderPatientId { get; set; }

    /// <summary>
    /// Set when SenderType is "Provider" — the UserId of the staff member who sent the message
    /// </summary>
    public int? SenderUserId { get; set; }

    /// <summary>
    /// Message text content (encrypted at rest for HIPAA compliance)
    /// </summary>
    public string MessageText { get; set; } = null!;

    public bool IsReadByPatient { get; set; }

    public bool IsReadByUser { get; set; }

    public DateTime? ReadByPatientAt { get; set; }

    public DateTime? ReadByUserAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual PatientConversation PatientConversation { get; set; } = null!;
    public virtual Patient? SenderPatient { get; set; }
    public virtual User? SenderUser { get; set; }
}
