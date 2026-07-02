using System;

namespace EHR.Models.Generated;

/// <summary>
/// User presence status for real-time online/offline indicators.
/// </summary>
public enum PresenceStatus
{
    Offline = 0,
    Online = 1,
    Away = 2
}

/// <summary>
/// Tracks user online/offline status for the messaging system.
/// Updated via SignalR connection events.
/// </summary>
public partial class UserPresence
{
    public int UserPresenceId { get; set; }

    public int TenantId { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Current presence status: Offline, Online, or Away
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Last time the user was active (used for "away" detection)
    /// </summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// Current SignalR connection ID (for targeted messaging)
    /// </summary>
    public string? ConnectionId { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
