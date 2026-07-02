using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Consent
{
    public int ConsentId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int Type { get; set; }

    public string DocumentUrl { get; set; }

    public string SignedBy { get; set; }

    public DateTime SignedAt { get; set; }

    public string IpAddress { get; set; }

    public string SignatureData { get; set; }

    public string Version { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }
}
