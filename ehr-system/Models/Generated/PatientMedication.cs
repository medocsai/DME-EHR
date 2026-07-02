using System;

namespace EHR.Models.Generated;

public partial class PatientMedication
{
    public int PatientMedicationId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    public string DrugName { get; set; }

    /// <summary>
    /// e.g., "500mg", "10mg/5ml"
    /// </summary>
    public string Dosage { get; set; }

    /// <summary>
    /// e.g., "tablet", "capsule", "solution"
    /// </summary>
    public string Form { get; set; }

    /// <summary>
    /// e.g., "oral", "topical", "subcutaneous"
    /// </summary>
    public string Route { get; set; }

    /// <summary>
    /// e.g., "twice daily", "every 8 hours", "as needed"
    /// </summary>
    public string Frequency { get; set; }

    /// <summary>
    /// 0=Active, 1=Discontinued, 2=OnHold, 3=Completed
    /// </summary>
    public int Status { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? PrescribedByProviderId { get; set; }

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

    public virtual Provider PrescribedByProvider { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual PatientIntakeSubmission IntakeSubmission { get; set; }

    public virtual User DeletedByUser { get; set; }
}
