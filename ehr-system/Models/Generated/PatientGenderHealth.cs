using System;

namespace EHR.Models.Generated;

/// <summary>
/// Gender-specific health data from intake Section 7. One row per patient.
/// Biological sex captured here (independent of Demographics.Gender which may
/// reflect gender identity). StructuredData holds the full questionnaire JSON
/// so we don't sprawl dozens of nullable columns for W/M/Sexual subsections.
/// </summary>
public partial class PatientGenderHealth
{
    public int PatientGenderHealthId { get; set; }

    public int PatientId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// "Female", "Male", "Intersex", or "Prefer not to say". Drives which
    /// subsection of the intake wizard is shown. Stored separately from
    /// Demographics.Gender (which may be identity, not biology).
    /// </summary>
    public string BiologicalSex { get; set; }

    /// <summary>
    /// Full structured answers as JSON: women, men, sexual sub-objects. Pre-fills
    /// the wizard on return visits.
    /// </summary>
    public string StructuredData { get; set; }

    public int Source { get; set; }

    public int? IntakeSubmissionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }
}
