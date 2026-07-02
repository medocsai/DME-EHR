namespace EHR.Models.Generated;

/// <summary>
/// Security-specific columns added outside the auto-generated User.cs so a
/// future EF scaffolding rerun does not wipe them. See
/// migration_023_security_phase_2.sql for the corresponding schema.
/// </summary>
public partial class User
{
    /// <summary>
    /// Count of consecutive wrong-password attempts since the last successful
    /// login. Resets to 0 on a correct password. When this hits 5, the account
    /// is locked via PasswordLockedUntil for 15 minutes.
    /// </summary>
    public int FailedPasswordAttempts { get; set; }

    /// <summary>
    /// If non-null and in the future, password verification is refused even
    /// when correct — the account is in a 15-minute hard lockout window after
    /// 5 failed attempts. Cleared on the next successful login.
    /// </summary>
    public DateTime? PasswordLockedUntil { get; set; }

    /// <summary>
    /// Bumped on any security-relevant change (logout, password change, role
    /// change). The JWT carries the version it was issued with as a "tv"
    /// claim; auth middleware compares against this value and rejects on
    /// mismatch. Existing JWTs are invalidated immediately when this counter
    /// changes — no waiting for natural expiry.
    /// </summary>
    public int TokenVersion { get; set; } = 1;
}
