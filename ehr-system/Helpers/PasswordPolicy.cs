namespace EHR.Helpers;

/// <summary>
/// The rule for what counts as an acceptable staff password.
///
/// WHY THIS EXISTS
/// The patient portal validated password strength (PatientPortalInvitationService:
/// minimum 8, maximum 128). Staff accounts had NO validation at any of the three
/// places a password is set: user creation, password reset and password change.
/// A clinic administrator, or the Super Admin who can create tenants, could be
/// given the password "a" and BCrypt would happily hash it.
///
/// That is backwards. A staff account holds an entire tenant's PHI; a portal
/// account holds one person's.
///
/// WHY LENGTH RATHER THAN CHARACTER CLASSES
/// NIST SP 800-63B advises against forced composition rules (one upper, one
/// digit, one symbol): they push people towards Password1! and predictable
/// substitutions without adding real entropy, and they annoy users into reusing
/// passwords. Length is the control that actually helps, so the minimum is 12
/// and there is no composition requirement.
///
/// What IS rejected is the small set of things that are weak regardless of
/// length: a single repeated character, and a password containing the user's
/// own email name.
///
/// THE 72-BYTE DETAIL
/// BCrypt only hashes the first 72 bytes of input. Anything beyond that is
/// silently ignored, so two different 200-character passwords sharing a prefix
/// would be the same password. The maximum is set below that so nobody is ever
/// given a false sense of a longer secret.
///
/// WHO CALLS IT
/// UserManagementService, at every point a staff password is set.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>
    /// Minimum length. Chosen over the portal's 8 because a staff account holds
    /// a whole tenant's records rather than one person's.
    /// </summary>
    public const int MinimumLength = 12;

    /// <summary>
    /// Maximum length. Below BCrypt's 72-byte input limit so no part of the
    /// password a user typed is silently discarded.
    /// </summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// Check a proposed password. Returns null when acceptable, otherwise the
    /// message to show the user.
    ///
    /// <paramref name="email"/> is optional and used only to stop somebody
    /// setting their password to their own username.
    /// </summary>
    public static string? Validate(string? password, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Password is required.";

        // Trailing spaces are invisible and a common source of "it worked
        // yesterday" reports, so measure what will actually be stored.
        if (password.Length < MinimumLength)
            return $"Password must be at least {MinimumLength} characters.";

        if (password.Length > MaximumLength)
            return $"Password must be {MaximumLength} characters or fewer.";

        if (password.Distinct().Count() == 1)
            return "Password cannot be a single repeated character.";

        // "john.smith@clinic.com" -> "john.smith". A password containing the
        // account name is guessable no matter how long it is.
        if (!string.IsNullOrWhiteSpace(email))
        {
            var localPart = email.Split('@')[0];
            if (localPart.Length >= 4 &&
                password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
            {
                return "Password must not contain your email address.";
            }
        }

        return null;
    }

    /// <summary>True when the password satisfies the policy.</summary>
    public static bool IsAcceptable(string? password, string? email = null)
        => Validate(password, email) == null;
}
