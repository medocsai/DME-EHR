using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class VwAraging
{
    public int TenantId { get; set; }

    public decimal? Current0To30 { get; set; }

    public decimal? Days31To60 { get; set; }

    public decimal? Days61To90 { get; set; }

    public decimal? Days91To120 { get; set; }

    public decimal? Over120Days { get; set; }

    public decimal? TotalAr { get; set; }
}
