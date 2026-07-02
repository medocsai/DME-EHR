using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class AuditLog
{
    public long AuditId { get; set; }

    public int? TenantId { get; set; }

    public int? UserId { get; set; }

    public string UserEmail { get; set; }

    public string EntityType { get; set; }

    public int? EntityId { get; set; }

    public string Action { get; set; }

    public string OldValues { get; set; }

    public string NewValues { get; set; }

    public string Changes { get; set; }

    public string IpAddress { get; set; }

    public string UserAgent { get; set; }

    public DateTime? Timestamp { get; set; }

    public virtual Tenant Tenant { get; set; }
}
