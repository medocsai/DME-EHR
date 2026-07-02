using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Kiosk settings for a clinic location.
/// Each location can have its own kiosk with a unique secure token.
/// </summary>
public partial class LocationKioskSettings
{
    public int LocationKioskSettingsId { get; set; }

    public int LocationId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Unique secure token for kiosk URL (GUID format, 32 hex chars)
    /// </summary>
    public string KioskToken { get; set; }

    /// <summary>
    /// Whether the kiosk is enabled for this location
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Session timeout in minutes (default 30)
    /// </summary>
    public int SessionTimeoutMinutes { get; set; }

    /// <summary>
    /// When the current token was generated
    /// </summary>
    public DateTime TokenGeneratedAt { get; set; }

    /// <summary>
    /// User ID who generated the current token
    /// </summary>
    public int? TokenGeneratedByUserId { get; set; }

    /// <summary>
    /// When the kiosk was last accessed
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// IP address of last access
    /// </summary>
    public string LastAccessIpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Location Location { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual User TokenGeneratedByUser { get; set; }
}
