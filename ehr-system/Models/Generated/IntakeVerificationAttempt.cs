using System;

namespace EHR.Models.Generated;

/// <summary>
/// Rate-limit log for tablet intake verify attempts. Parallel to KioskVerificationAttempt —
/// intentionally not shared in v1 (refactor when a 3rd flow appears).
/// </summary>
public partial class IntakeVerificationAttempt
{
    public int IntakeVerificationAttemptId { get; set; }

    public int TenantId { get; set; }

    public int LocationId { get; set; }

    /// <summary>
    /// Patient ID if verification succeeded; null on failures.
    /// </summary>
    public int? PatientId { get; set; }

    public DateOnly? AttemptedDob { get; set; }

    /// <summary>
    /// HMAC-SHA256 of attempted SSN last-4 (via EncryptionHelper.GenerateSearchHash).
    /// </summary>
    public string AttemptedSsnLast4Hash { get; set; }

    public bool IsSuccessful { get; set; }

    public string IpAddress { get; set; }

    public string UserAgent { get; set; }

    public DateTime AttemptedAt { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual Location Location { get; set; }

    public virtual Patient Patient { get; set; }
}
