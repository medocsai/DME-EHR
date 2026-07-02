using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Active kiosk session after patient verification.
/// Used to track state during the consent signing process.
/// </summary>
public partial class KioskSession
{
    public int KioskSessionId { get; set; }

    public int TenantId { get; set; }

    public int LocationId { get; set; }

    /// <summary>
    /// Unique session token
    /// </summary>
    public string SessionToken { get; set; }

    /// <summary>
    /// Verified patient ID
    /// </summary>
    public int PatientId { get; set; }

    /// <summary>
    /// Appointment for this session
    /// </summary>
    public int AppointmentId { get; set; }

    /// <summary>
    /// Care Episode ID if available
    /// </summary>
    public int? CareEpisodeId { get; set; }

    /// <summary>
    /// IP address
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    /// User agent
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// When the session was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the session expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Last activity timestamp
    /// </summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Current form index being viewed (0-based)
    /// </summary>
    public int CurrentFormIndex { get; set; }

    /// <summary>
    /// JSON of forms progress
    /// Format: { "0": { "viewed": true, "viewedAt": "...", "signatures": {...} }, ... }
    /// </summary>
    public string FormsProgressJson { get; set; }

    /// <summary>
    /// Whether the session is completed (consent submitted)
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Whether the session is invalidated
    /// </summary>
    public bool IsInvalidated { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }

    public virtual Location Location { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual CareEpisode CareEpisode { get; set; }
}
