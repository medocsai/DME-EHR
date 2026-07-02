using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class VwTodaySchedule
{
    public int TenantId { get; set; }

    public int AppointmentId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public int AppointmentType { get; set; }

    public int? Status { get; set; }

    public int PatientId { get; set; }

    public string Mrn { get; set; }

    public string PatientName { get; set; }

    public int ProviderId { get; set; }

    public string ProviderName { get; set; }

    public string ProviderColor { get; set; }

    public string LocationName { get; set; }

    public decimal? CopayDue { get; set; }

    public decimal? CopayCollected { get; set; }

    public bool? InsuranceVerified { get; set; }
}
