using System;

namespace EHR.Models.Generated;

public partial class Prescription
{
    public int PrescriptionId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int ProviderId { get; set; }

    public int? EncounterId { get; set; }

    /// <summary>
    /// Brand or display name of the drug
    /// </summary>
    public string DrugName { get; set; }

    /// <summary>
    /// Generic drug name
    /// </summary>
    public string GenericName { get; set; }

    /// <summary>
    /// National Drug Code
    /// </summary>
    public string NDCCode { get; set; }

    /// <summary>
    /// RxNorm Concept Unique Identifier
    /// </summary>
    public string RxNormCode { get; set; }

    /// <summary>
    /// e.g., "10mg", "500mg", "250mg/5ml"
    /// </summary>
    public string Strength { get; set; }

    /// <summary>
    /// 0=Tablet, 1=Capsule, 2=Liquid, etc. See DosageForm enum
    /// </summary>
    public int DosageForm { get; set; }

    /// <summary>
    /// Total quantity to dispense (e.g., 30, 90)
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Number of days the prescription should last
    /// </summary>
    public int DaysSupply { get; set; }

    /// <summary>
    /// Amount per dose (e.g., "1", "2", "0.5")
    /// </summary>
    public string DoseAmount { get; set; }

    /// <summary>
    /// Unit per dose (e.g., "tablet", "capsule", "ml", "puff")
    /// </summary>
    public string DoseUnit { get; set; }

    /// <summary>
    /// 0=Oral, 1=Topical, 2=Subcutaneous, etc. See MedicationRoute enum
    /// </summary>
    public int Route { get; set; }

    /// <summary>
    /// 0=Daily, 1=BID, 2=TID, etc. See MedicationFrequency enum
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// Full SIG text, e.g., "Take 1 tablet by mouth once daily"
    /// </summary>
    public string DirectionsFreeText { get; set; }

    /// <summary>
    /// Number of refills authorized (0 = no refills)
    /// </summary>
    public int Refills { get; set; }

    /// <summary>
    /// Dispense As Written - if true, no generic substitution
    /// </summary>
    public bool DAW { get; set; }

    public string PharmacyName { get; set; }

    public string PharmacyPhone { get; set; }

    public string PharmacyAddress { get; set; }

    /// <summary>
    /// 0=Draft, 1=Active, 2=Sent, 3=Filled, 4=Cancelled, 5=Expired
    /// </summary>
    public int Status { get; set; }

    public bool IsControlledSubstance { get; set; }

    /// <summary>
    /// DEA Schedule (2-5) for controlled substances, null if not controlled
    /// </summary>
    public int? DEASchedule { get; set; }

    /// <summary>
    /// ICD-10 diagnosis code for medical necessity
    /// </summary>
    public string DiagnosisCode { get; set; }

    public DateOnly PrescribedDate { get; set; }

    public DateOnly? ExpirationDate { get; set; }

    public string Notes { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Patient Patient { get; set; }
    public virtual Provider Provider { get; set; }
    public virtual Encounter Encounter { get; set; }
    public virtual Tenant Tenant { get; set; }
}
