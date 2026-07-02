using System;

namespace EHR.Models.Generated;

public partial class TreatmentPlan
{
    public int TreatmentPlanId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int ProviderId { get; set; }

    /// <summary>
    /// e.g., "Type 2 Diabetes Management", "Hypertension Control"
    /// </summary>
    public string ConditionName { get; set; }

    /// <summary>
    /// ICD-10 code for the condition
    /// </summary>
    public string IcdCode { get; set; }

    /// <summary>
    /// Treatment goals (e.g., "A1C below 7%", "BP below 130/80")
    /// </summary>
    public string Goals { get; set; }

    /// <summary>
    /// Follow-up interval in days (e.g., 90 for quarterly)
    /// </summary>
    public int? FollowUpIntervalDays { get; set; }

    public DateOnly? NextFollowUp { get; set; }

    /// <summary>
    /// 0=Active, 1=Completed, 2=OnHold, 3=Cancelled
    /// </summary>
    public int Status { get; set; }

    public string Notes { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
