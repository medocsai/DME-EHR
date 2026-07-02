using System;

namespace EHR.Models.Generated;

public partial class PatientFamilyHistory
{
    public int PatientFamilyHistoryId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    /// <summary>
    /// e.g., "Mother", "Father", "Sibling", "Maternal Grandmother"
    /// </summary>
    public string Relation { get; set; }

    public string Condition { get; set; }

    /// <summary>
    /// Age at diagnosis, if known
    /// </summary>
    public int? AgeAtOnset { get; set; }

    public bool? IsDeceased { get; set; }

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
