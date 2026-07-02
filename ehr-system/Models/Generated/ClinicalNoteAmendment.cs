using System;

namespace EHR.Models.Generated;

/// <summary>
/// A revision of a signed <see cref="ClinicalNote"/>. The original note stays
/// untouched as version 1; amendments are stored here as versions 2, 3, ...
/// Only the original signing clinician can create an amendment, and only within
/// the configured amendment window (see <c>ClinicalNotes:AmendmentWindowHours</c>
/// in appsettings.json) when the encounter is closed.
/// </summary>
public partial class ClinicalNoteAmendment
{
    public int AmendmentId { get; set; }

    public int ClinicalNoteId { get; set; }

    public int TenantId { get; set; }

    /// <summary>Sequence number starting at 2 (v1 is the original ClinicalNote).</summary>
    public int VersionNumber { get; set; }

    /// <summary>Full HTML content of this revision. Encrypted at rest.</summary>
    public string HtmlContent { get; set; }

    /// <summary>Required — provider-supplied reason for the amendment.</summary>
    public string Reason { get; set; }

    public DateTime SignedAt { get; set; }

    public int SignedByUserId { get; set; }

    public string SignatureData { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ClinicalNote ClinicalNote { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual User SignedByUser { get; set; }
}
