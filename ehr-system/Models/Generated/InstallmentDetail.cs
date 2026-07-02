using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class InstallmentDetail
{
    public int DetailId { get; set; }

    public int PlanId { get; set; }

    public int TenantId { get; set; }

    public int InstallmentNumber { get; set; }

    public DateOnly DueDate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// 0=Pending, 1=Paid, 2=Failed, 3=Delinquent
    /// </summary>
    public int Status { get; set; }

    public int RetryCount { get; set; }

    public int? PaymentId { get; set; }

    public string StripePaymentIntentId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public virtual InstallmentPlan Plan { get; set; }

    public virtual Payment Payment { get; set; }

    public virtual Tenant Tenant { get; set; }
}
