using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class StripeConnectAccount
{
    public int StripeConnectAccountId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Stripe's account identifier (acct_xxx). Unique across all platforms.
    /// </summary>
    public string StripeAccountId { get; set; }

    /// <summary>
    /// Display name pulled from Stripe (legal entity name, e.g., "Sunshine Medical LLC").
    /// </summary>
    public string DisplayName { get; set; }

    public string BusinessEmail { get; set; }

    public string Country { get; set; }

    public string DefaultCurrency { get; set; }

    /// <summary>
    /// 0=Pending, 1=Active, 2=Restricted, 3=Disconnected
    /// </summary>
    public int Status { get; set; }

    public bool ChargesEnabled { get; set; }

    public bool PayoutsEnabled { get; set; }

    public bool DetailsSubmitted { get; set; }

    public DateTime ConnectedAt { get; set; }

    public int? ConnectedByUserId { get; set; }

    public DateTime? DisconnectedAt { get; set; }

    public DateTime? LastWebhookAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual User ConnectedByUser { get; set; }

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<InstallmentPlan> InstallmentPlans { get; set; } = new List<InstallmentPlan>();
}
