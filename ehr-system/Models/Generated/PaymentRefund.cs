using System;

namespace EHR.Models.Generated;

public partial class PaymentRefund
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Stripe's refund ID (re_xxx).
    /// </summary>
    public string StripeRefundId { get; set; }

    public string StripeChargeId { get; set; }

    /// <summary>
    /// Refund amount in cents. Can be partial (less than original payment).
    /// </summary>
    public int AmountCents { get; set; }

    /// <summary>
    /// Reason from Stripe: duplicate, fraudulent, requested_by_customer
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// 0=Pending, 1=Succeeded, 2=Failed, 3=Canceled
    /// </summary>
    public int Status { get; set; }

    public DateTime RefundedAt { get; set; }

    /// <summary>
    /// True if the refund was initiated by the clinic in their own Stripe dashboard.
    /// False if initiated from within IMEHR (future feature).
    /// </summary>
    public bool CreatedByExternal { get; set; }

    public virtual Payment Payment { get; set; }

    public virtual Tenant Tenant { get; set; }
}
