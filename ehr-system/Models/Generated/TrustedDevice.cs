using System;

namespace EHR.Models.Generated;

public partial class TrustedDevice
{
    public int TrustedDeviceId { get; set; }

    public int UserId { get; set; }

    public string DeviceTokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string UserAgent { get; set; }

    public virtual User User { get; set; }
}
