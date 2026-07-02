using System;

namespace EHR.Models.Generated;

public partial class PatientAllergy
{
    public int PatientAllergyId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    public string AllergenName { get; set; }

    /// <summary>
    /// 0=Drug, 1=Food, 2=Environmental, 3=Other
    /// </summary>
    public int Type { get; set; }

    public string Reaction { get; set; }

    /// <summary>
    /// 0=Mild, 1=Moderate, 2=Severe, 3=LifeThreatening
    /// </summary>
    public int Severity { get; set; }

    public DateOnly? OnsetDate { get; set; }

    public bool IsActive { get; set; } = true;

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
