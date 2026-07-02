using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Represents a messaging conversation between a patient and a staff user.
/// Separate from the internal staff messaging system (Conversation table).
/// One conversation per patient-user pair per tenant.
/// </summary>
public partial class PatientConversation
{
    public int PatientConversationId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    /// <summary>
    /// The staff user in this conversation (any role: Clinician, Admin, FrontDesk, MA, Nurse, etc.)
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Preview text from the last message (encrypted, truncated to 500 chars)
    /// </summary>
    public string? LastMessageText { get; set; }

    public DateTime? LastMessageAt { get; set; }

    /// <summary>
    /// "Patient", "Provider", or "System" — who sent the last message
    /// </summary>
    public string? LastMessageSenderType { get; set; }

    public int PatientUnreadCount { get; set; }

    public int UserUnreadCount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual ICollection<PatientMessage> PatientMessages { get; set; } = new List<PatientMessage>();
}
