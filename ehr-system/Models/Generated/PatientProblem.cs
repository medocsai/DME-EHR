using System;

namespace EHR.Models.Generated;

public partial class PatientProblem
{
    public int PatientProblemId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    /// <summary>
    /// ICD-10 code (e.g., E11.9 for Type 2 Diabetes)
    /// </summary>
    public string IcdCode { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// 0=Active, 1=Resolved, 2=Inactive
    /// </summary>
    public int Status { get; set; }

    public DateOnly? OnsetDate { get; set; }

    public DateOnly? ResolvedDate { get; set; }

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
