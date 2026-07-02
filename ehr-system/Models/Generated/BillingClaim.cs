using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class BillingClaim
{
    public int ClaimId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? InsuranceId { get; set; }

    // ── FK references ─────────────────────────────────────────
    public int? ClinicalNoteId { get; set; }

    public int? AppointmentId { get; set; }

    public int? CareEpisodeId { get; set; }

    public int? AuthorizationId { get; set; }

    public int? ProviderId { get; set; }

    public int? LocationId { get; set; }

    // ── Core claim fields ─────────────────────────────────────
    public string ClaimNumber { get; set; }

    public string PayerClaimNumber { get; set; }

    public DateOnly ServiceDateFrom { get; set; }

    public DateOnly ServiceDateTo { get; set; }

    public decimal TotalCharged { get; set; }

    public decimal? TotalAllowed { get; set; }

    public decimal? TotalPaid { get; set; }

    public decimal? TotalAdjustment { get; set; }

    public decimal? PatientResponsibility { get; set; }

    public int? Status { get; set; }

    public int? Type { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string DenialReasonCode { get; set; }

    public string DenialReason { get; set; }

    public string Edidata { get; set; }

    public string ResponseData { get; set; }

    public string Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedBy { get; set; }

    // ── CMS 1500 Box 1: Insurance Type ────────────────────────
    public int? InsuranceTypeCode { get; set; }

    // ── CMS 1500 Box 12/13: Signatures ────────────────────────
    public bool? PatientSignatureOnFile { get; set; }

    public bool? InsuredSignatureOnFile { get; set; }

    // ── CMS 1500 Box 17: Referring Provider ───────────────────
    public string ReferringProviderName { get; set; }

    public string ReferringProviderNpi { get; set; }

    // ── CMS 1500 Box 21: Diagnosis Codes (JSON) ──────────────
    public string DiagnosisCodes { get; set; }

    // ── CMS 1500 Box 23: Prior Authorization ──────────────────
    public string PriorAuthorizationNumber { get; set; }

    // ── CMS 1500 Box 24b: Place of Service ────────────────────
    public string PlaceOfServiceCode { get; set; }

    // ── CMS 1500 Box 25: Federal Tax ID ───────────────────────
    public string FederalTaxId { get; set; }

    // ── CMS 1500 Box 26: Patient Account Number ──────────────
    public string PatientAccountNumber { get; set; }

    // ── CMS 1500 Box 27: Accept Assignment ────────────────────
    public bool? AcceptAssignment { get; set; }

    // ── CMS 1500 Box 29: Amount Paid ──────────────────────────
    public decimal? AmountPaid { get; set; }

    // ── CMS 1500 Box 31: Rendering Provider ───────────────────
    public string RenderingProviderName { get; set; }

    public string RenderingProviderNpi { get; set; }

    // ── CMS 1500 Box 32: Facility ─────────────────────────────
    public string FacilityName { get; set; }

    public string FacilityAddress { get; set; }

    public string FacilityNpi { get; set; }

    // ── CMS 1500 Box 33: Billing Provider ─────────────────────
    public string BillingProviderName { get; set; }

    public string BillingProviderAddress { get; set; }

    public string BillingProviderNpi { get; set; }

    public string BillingProviderTaxonomy { get; set; }

    // ── Insured snapshot (Box 4-11) ───────────────────────────
    public string InsuredName { get; set; }

    public string InsuredAddress { get; set; }

    public string InsuredCity { get; set; }

    public string InsuredState { get; set; }

    public string InsuredZip { get; set; }

    public DateOnly? InsuredDob { get; set; }

    public string InsuredGender { get; set; }

    public string InsuredPolicyNumber { get; set; }

    public string InsuredGroupNumber { get; set; }

    public string SubscriberRelationship { get; set; }

    // ── UB04-specific fields ──────────────────────────────────
    public string TypeOfBill { get; set; }

    public DateOnly? AdmissionDate { get; set; }

    public int? AdmissionType { get; set; }

    public string PatientDischargeStatus { get; set; }

    public string ConditionCodes { get; set; }

    public string ValueCodes { get; set; }

    public string OccurrenceCodes { get; set; }

    // ── Navigation properties ─────────────────────────────────
    public virtual ICollection<Charge> Charges { get; set; } = new List<Charge>();

    public virtual ICollection<ClaimStatusHistory> ClaimStatusHistories { get; set; } = new List<ClaimStatusHistory>();

    public virtual Insurance Insurance { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Tenant Tenant { get; set; }

    public virtual ClinicalNote ClinicalNote { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual CareEpisode CareEpisode { get; set; }

    public virtual Authorization Authorization { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Location Location { get; set; }
}
