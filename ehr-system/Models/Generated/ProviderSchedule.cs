using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class ProviderSchedule
{
    public int ScheduleId { get; set; }

    public int TenantId { get; set; }

    public int ProviderId { get; set; }

    public int? LocationId { get; set; }

    public int DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public bool? IsAvailable { get; set; }

    public virtual Location Location { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
