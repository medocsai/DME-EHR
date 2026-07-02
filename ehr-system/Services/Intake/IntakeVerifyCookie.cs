using Microsoft.AspNetCore.Http;

namespace EHR.Services.Intake;

/// <summary>
/// Centralizes the format/attributes for the patient-intake verify cookie so that
/// both <see cref="EHR.Controllers.IntakeController"/> (set after DOB/SSN verify)
/// and <see cref="EHR.Controllers.KioskController"/> (set after consent submit, as
/// part of the consent → intake handoff feature) use the same scheme without
/// duplicating string literals or attribute choices.
///
/// Security model (matches the pre-existing IntakeController behavior):
///   - Cookie name embeds the first 12 hex chars of the intake token GUID, so a
///     cookie issued for patient A's token is unreadable on patient B's URL.
///   - Cookie value is the full 32-char "N"-format GUID. Verification is a
///     direct equality check against the URL token (no HMAC/encryption needed:
///     the token itself is the secret, and the cookie just asserts "the holder
///     of this URL has already proven identity recently").
///   - HttpOnly + Secure (in HTTPS) + SameSite=Strict + short TTL bound the
///     blast radius if a tablet is left unattended.
///
/// See rules/technical/consent-to-intake-handoff.md for the design rationale.
/// </summary>
public static class IntakeVerifyCookie
{
    public const string Prefix = "intake_verify_";

    /// <summary>Default TTL when set after a direct DOB+SSN verify (4 hours).</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(4);

    /// <summary>Shorter TTL when set as part of the consent→intake handoff (30 minutes).</summary>
    public static readonly TimeSpan HandoffTtl = TimeSpan.FromMinutes(30);

    public static string Name(Guid token) => $"{Prefix}{token.ToString("N")[..12]}";

    public static string Value(Guid token) => token.ToString("N");

    /// <summary>True iff the cookie value parses to the same GUID as the URL token.</summary>
    public static bool Matches(string cookieValue, Guid token)
        => !string.IsNullOrEmpty(cookieValue)
           && Guid.TryParseExact(cookieValue, "N", out var parsed)
           && parsed == token;

    public static CookieOptions Options(bool isHttps, TimeSpan ttl) => new()
    {
        HttpOnly = true,
        Secure = isHttps, // HTTPS in prod, off on localhost HTTP dev
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.Add(ttl),
    };
}
