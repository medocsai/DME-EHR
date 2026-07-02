using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Insurance
{
    public int InsuranceId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public string PayerName { get; set; }

    public string PayerId { get; set; }

    public string PolicyNumber { get; set; }

    public string GroupNumber { get; set; }

    public string SubscriberName { get; set; }

    public string SubscriberFirstName { get; set; }

    public string SubscriberLastName { get; set; }

    public DateOnly? SubscriberDob { get; set; }

    public string SubscriberRelationship { get; set; }

    public string SubscriberId { get; set; }

    public DateOnly? EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public int? Type { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? LastVerifiedAt { get; set; }

    public int? EligibilityStatus { get; set; }

    public decimal? Copay { get; set; }

    public decimal? Coinsurance { get; set; }

    public decimal? DeductibleTotal { get; set; }

    public decimal? DeductibleMet { get; set; }

    /// <summary>
    /// Number of visits allowed by the insurance plan (general coverage, not authorization-specific)
    /// </summary>
    public int? AllowedVisits { get; set; }

    public string CoverageNotes { get; set; }

    public string PlanName { get; set; }

    public bool? InNetwork { get; set; }

    public decimal? OutOfPocketMax { get; set; }

    /// <summary>Raw OA JSON response. Contains PHI - encrypted at rest.</summary>
    public string LastEligibilityResponseJson { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public decimal? Deductible { get; set; }

    public int? InsuranceCategory { get; set; }

    public string AttorneyName { get; set; }

    public string AttorneyPhone { get; set; }

    public string AttorneyEmail { get; set; }

    public virtual ICollection<Authorization> Authorizations { get; set; } = new List<Authorization>();

    public virtual ICollection<BillingClaim> BillingClaims { get; set; } = new List<BillingClaim>();

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }
}
