using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class InstallmentPlan
{
    public int PlanId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public decimal TotalAmount { get; set; }

    public int NumberOfInstallments { get; set; }

    /// <summary>
    /// 0=Active, 1=Completed, 2=Cancelled, 3=Defaulted
    /// </summary>
    public int Status { get; set; }

    public string StripeCustomerId { get; set; }

    public string StripePaymentMethodId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    /// <summary>
    /// Location where the installment plan was created.
    /// All charges in this plan route to that location's connected account.
    /// </summary>
    public int? LocationId { get; set; }

    /// <summary>
    /// Snapshot of the connected Stripe account at plan creation time.
    /// Background processor uses this to charge against the same account
    /// even if the location's account changes later.
    /// </summary>
    public int? StripeConnectAccountId { get; set; }

    /// <summary>
    /// Concurrency lock for background processor. When set, indicates this plan
    /// is currently being processed. Released after processing or expires after 5 minutes.
    /// </summary>
    public Guid? ProcessingLockId { get; set; }

    public DateTime? ProcessingLockedAt { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual Location Location { get; set; }

    public virtual StripeConnectAccount StripeConnectAccount { get; set; }

    public virtual ICollection<InstallmentDetail> InstallmentDetails { get; set; } = new List<InstallmentDetail>();

    public virtual ICollection<InstallmentPlanAuditLog> AuditLogs { get; set; } = new List<InstallmentPlanAuditLog>();
}
