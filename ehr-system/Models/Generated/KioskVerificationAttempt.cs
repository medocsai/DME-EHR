using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Tracks verification attempts for rate limiting.
/// Used to prevent brute force attacks on patient verification.
/// </summary>
public partial class KioskVerificationAttempt
{
    public int KioskVerificationAttemptId { get; set; }

    public int TenantId { get; set; }

    public int LocationId { get; set; }

    /// <summary>
    /// IP address of the attempt
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    /// Whether the attempt was successful
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Hashed SSN last 4 attempted (for tracking patterns)
    /// </summary>
    public string AttemptedSsnLast4Hash { get; set; }

    /// <summary>
    /// DOB attempted
    /// </summary>
    public DateOnly? AttemptedDob { get; set; }

    /// <summary>
    /// ZIP attempted
    /// </summary>
    public string AttemptedZip { get; set; }

    /// <summary>
    /// Patient ID if verification was successful
    /// </summary>
    public int? PatientId { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string UserAgent { get; set; }

    public DateTime AttemptedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }

    public virtual Location Location { get; set; }

    public virtual Patient Patient { get; set; }
}
