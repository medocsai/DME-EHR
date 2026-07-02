using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Charge
{
    public int ChargeId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    // NoteId is deprecated - use ClinicalNoteId instead
    [Obsolete("Use ClinicalNoteId instead. This field will be removed in a future migration.")]
    public int? NoteId { get; set; }

    public int? ClinicalNoteId { get; set; }

    public int? AppointmentId { get; set; }

    public int ProviderId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public string Cptcode { get; set; }

    public string Cptdescription { get; set; }

    public int? Units { get; set; }

    public string Modifier1 { get; set; }

    public string Modifier2 { get; set; }

    public string Modifier3 { get; set; }

    public string Modifier4 { get; set; }

    public string Icdpointers { get; set; }

    public decimal ChargeAmount { get; set; }

    public decimal? AllowedAmount { get; set; }

    public decimal? PaidAmount { get; set; }

    public decimal? AdjustmentAmount { get; set; }

    public decimal? PatientResponsibility { get; set; }

    public string RevenueCode { get; set; }

    public int? Status { get; set; }

    public int? ClaimId { get; set; }

    /// <summary>
    /// Location where the service was rendered. Backfilled from Appointment.LocationId
    /// for charges from encounters; explicitly set for non-encounter charges.
    /// </summary>
    public int? LocationId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual BillingClaim Claim { get; set; }

    // Note navigation is deprecated - use ClinicalNote instead
    [Obsolete("Use ClinicalNote instead")]
    public virtual Note Note { get; set; }

    public virtual ClinicalNote ClinicalNote { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Location Location { get; set; }

    public virtual ICollection<PatientLedger> PatientLedgers { get; set; } = new List<PatientLedger>();

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
