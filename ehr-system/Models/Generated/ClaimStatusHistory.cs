using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class ClaimStatusHistory
{
    public int HistoryId { get; set; }

    public int TenantId { get; set; }

    public int ClaimId { get; set; }

    public int Status { get; set; }

    public string Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public virtual BillingClaim Claim { get; set; }

    public virtual Tenant Tenant { get; set; }
}
