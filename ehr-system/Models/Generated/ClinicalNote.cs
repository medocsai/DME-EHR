using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class ClinicalNote
{
    public int ClinicalNoteId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int ProviderId { get; set; }

    public int? AppointmentId { get; set; }

    public int? EncounterId { get; set; }

    public int? TemplateId { get; set; }

    public int Type { get; set; }

    public int Status { get; set; }

    public DateOnly ServiceDate { get; set; }

    public string HtmlContent { get; set; }

    public string SearchHash { get; set; }

    public DateTime? SignedAt { get; set; }

    public int? SignedByUserId { get; set; }

    public string SignatureData { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual Encounter Encounter { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual ClinicalNoteTemplate Template { get; set; }

    public virtual Tenant Tenant { get; set; }

    /// <summary>Revisions (v2, v3, ...) created via the Amendment flow. Ordered by VersionNumber.</summary>
    public virtual ICollection<ClinicalNoteAmendment> Amendments { get; set; } = new List<ClinicalNoteAmendment>();

    /// <summary>Supplementary entries appended to this note. Ordered by CreatedAt.</summary>
    public virtual ICollection<ClinicalNoteAddendum> Addendums { get; set; } = new List<ClinicalNoteAddendum>();
}
