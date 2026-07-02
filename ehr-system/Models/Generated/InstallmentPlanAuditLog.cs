using System;

namespace EHR.Models.Generated;

public partial class InstallmentPlanAuditLog
{
    public long Id { get; set; }

    public int PlanId { get; set; }

    public int? DetailId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Action: Created, ChargeAttempted, ChargeSucceeded, ChargeFailed,
    /// Retried, Defaulted, Completed, Cancelled, Disconnected
    /// </summary>
    public string Action { get; set; }

    public int? OldStatus { get; set; }

    public int? NewStatus { get; set; }

    /// <summary>
    /// Free text or JSON with extra context (e.g., Stripe error message, retry count)
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// System, Webhook, User
    /// </summary>
    public string ActorType { get; set; }

    public int? ActorId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual InstallmentPlan Plan { get; set; }

    public virtual InstallmentDetail Detail { get; set; }

    public virtual Tenant Tenant { get; set; }
}
