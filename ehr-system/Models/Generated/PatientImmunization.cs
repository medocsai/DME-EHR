using System;

namespace EHR.Models.Generated;

public partial class PatientImmunization
{
    public int PatientImmunizationId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    public string VaccineName { get; set; }

    /// <summary>
    /// CVX code (CDC vaccine code, e.g., 141 for Influenza)
    /// </summary>
    public string CvxCode { get; set; }

    public DateOnly AdministeredDate { get; set; }

    public string LotNumber { get; set; }

    public string Manufacturer { get; set; }

    public string Site { get; set; }

    public int? AdministeredByProviderId { get; set; }

    public string Notes { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

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

    public virtual Provider AdministeredByProvider { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual PatientIntakeSubmission IntakeSubmission { get; set; }

    public virtual User DeletedByUser { get; set; }
}
