using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class CredentialingRecord
{
    public int CredentialingId { get; set; }

    public int TenantId { get; set; }

    public int ProviderId { get; set; }

    public string Source { get; set; }

    public string PayerName { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public int? Status { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string DocumentUrls { get; set; }

    public string Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
