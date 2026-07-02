using System;

namespace EHR.Models.Generated;

/// <summary>
/// Supplementary content appended to a signed <see cref="ClinicalNote"/> after the
/// fact. Always allowed (no time-window restriction), only by the original signing
/// clinician. Does not affect the note's HTML content, the encounter's codes, or
/// the billing claim — purely additive.
/// </summary>
public partial class ClinicalNoteAddendum
{
    public int AddendumId { get; set; }

    public int ClinicalNoteId { get; set; }

    public int TenantId { get; set; }

    /// <summary>Addendum text (HTML). Encrypted at rest.</summary>
    public string Content { get; set; }

    /// <summary>Required — provider-supplied reason for the addendum.</summary>
    public string Reason { get; set; }

    public DateTime SignedAt { get; set; }

    public int SignedByUserId { get; set; }

    public string SignatureData { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ClinicalNote ClinicalNote { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual User SignedByUser { get; set; }
}
