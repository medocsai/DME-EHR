using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class TherapistUnavailability
{
    public int UnavailabilityId { get; set; }

    public int TenantId { get; set; }

    public int ProviderId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public int Type { get; set; }

    public string Reason { get; set; }

    public bool IsFullDay { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public bool IsApproved { get; set; }

    public int? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string RecurrencePattern { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
