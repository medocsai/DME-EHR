using System;

namespace EHR.Models.Generated;

public partial class PatientSocialHistory
{
    public int PatientSocialHistoryId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    /// <summary>
    /// e.g., "Tobacco Use", "Alcohol Use", "Drug Use", "Exercise", "Diet", "Occupation", "Sexual Activity"
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// e.g., "Current smoker, 1 pack/day for 20 years"
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// e.g., "Current", "Former", "Never"
    /// </summary>
    public string Status { get; set; }

    public string Notes { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Provenance. 0=System, 1=Clinic, 2=Patient, 3=Kiosk, 4=Import. Existing rows default to Clinic.
    /// </summary>
    public int Source { get; set; } = 1;

    public int? IntakeSubmissionId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public int? DeletedByUserId { get; set; }

    public string DeletedReason { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual PatientIntakeSubmission IntakeSubmission { get; set; }

    public virtual User DeletedByUser { get; set; }
}
