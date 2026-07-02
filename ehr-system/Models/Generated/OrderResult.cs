using System;

namespace EHR.Models.Generated;

public partial class OrderResult
{
    public int OrderResultId { get; set; }
    public int OrderId { get; set; }

    public string TestName { get; set; }
    public string ResultValue { get; set; }
    public string ResultUnit { get; set; }
    public string ReferenceRange { get; set; }
    public bool? IsAbnormal { get; set; }

    /// <summary>For imaging findings or referral consultation summary</summary>
    public string FindingsText { get; set; }

    public DateTime? ResultDate { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public virtual Order Order { get; set; }
}
