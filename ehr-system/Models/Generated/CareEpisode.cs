using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

// =====================================================================================
// LEGACY MODEL — copied from PT (Physical Therapy) EHR. NOT used in Internal Medicine.
// =====================================================================================
// In Internal Medicine, ICD-10 diagnosis codes live on the Encounter via
// Encounter.IcdSelections (JSON array). The claim's BillingClaim.DiagnosisCodes
// is populated from there, NOT from CareEpisode.PrimaryDiagnosisCode/SecondaryDiagnoses.
//
// CareEpisode is kept dormant only because removing the table/relations would require
// a wider refactor. Do NOT add new features that depend on this model.
// =====================================================================================
public partial class CareEpisode
{
    public int CareEpisodeId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? PrimaryProviderId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string PrimaryDiagnosisCode { get; set; }

    public string PrimaryDiagnosisDescription { get; set; }

    /// <summary>
    /// Free-text notes for diagnosis (in addition to ICD code selection)
    /// </summary>
    public string DiagnosisNotes { get; set; }

    public string SecondaryDiagnoses { get; set; }

    public string Goals { get; set; }

    public string PlanOfCare { get; set; }

    /// <summary>
    /// Name of the referring physician or PCP (extracted from Initial Evaluation)
    /// </summary>
    public string PhysicianName { get; set; }

    public string ReferringProviderNpi { get; set; }

    public int? ExpectedVisits { get; set; }

    public int? VisitFrequency { get; set; }

    public int? Status { get; set; }

    public string DischargeReason { get; set; }

    /// <summary>
    /// How the episode was completed (0=None, 1=DischargeAppointment, 2=Manual)
    /// </summary>
    public int? CompletionMethod { get; set; }

    /// <summary>
    /// When the episode was marked as completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// User who marked the episode as completed
    /// </summary>
    public int? CompletedByUserId { get; set; }

    /// <summary>
    /// Co-pay amount for visits beyond insurance authorization
    /// </summary>
    public decimal? CopayAmount { get; set; }

    /// <summary>
    /// Number of additional visits patient will pay out of pocket
    /// </summary>
    public int? CopayVisits { get; set; }

    /// <summary>
    /// Count of missed visits (appointments with Missed status)
    /// </summary>
    public int? MissedVisits { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual Patient Patient { get; set; }

    public virtual Provider PrimaryProvider { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual ICollection<CareEpisodeConsent> CareEpisodeConsents { get; set; } = new List<CareEpisodeConsent>();
}
