using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Encounter
{
    public int EncounterId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int ProviderId { get; set; }

    public int? AppointmentId { get; set; }

    public DateOnly EncounterDate { get; set; }

    public string ChiefComplaint { get; set; }

    public string HistoryOfPresentIllness { get; set; }

    public string ReviewOfSystems { get; set; }

    public string PhysicalExam { get; set; }

    public string Assessment { get; set; }

    public string Plan { get; set; }

    /// <summary>
    /// 0=Open, 1=Signed, 2=Locked, 3=Amended
    /// </summary>
    public int Status { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }

    public DateTime? SignedAt { get; set; }

    public int? SignedByUserId { get; set; }

    public string CptSelections { get; set; }

    /// <summary>
    /// JSON array of ICD-10 diagnosis codes selected by the provider for this encounter.
    /// Format: [{"code":"E11.65","description":"...","aiSuggested":true}, ...]
    /// Order in array = order on CMS-1500 Box 21 (first = primary diagnosis = letter A).
    /// Source of truth for claim's BillingClaim.DiagnosisCodes (snapshotted at checkout).
    /// </summary>
    public string IcdSelections { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<PatientVital> Vitals { get; set; } = new List<PatientVital>();
}
