using System.Security.Cryptography;
using System.Text;

namespace EHR.Helpers;

/// <summary>
/// Helpers for emitting log lines that need to identify a person without
/// writing raw PHI/PII to the log. HIPAA 45 CFR 164.312(b) requires audit
/// controls; raw email/DOB/SSN/phone in app logs ends up in IIS access logs,
/// log aggregators, and on-call paste-buffers — all places HIPAA does not
/// expect PHI to live.
///
/// Use these helpers as the values in structured log placeholders, e.g.
///     _logger.LogWarning("OTP sent to {EmailHash}", PhiLog.Hash(email));
/// instead of
///     _logger.LogWarning("OTP sent to {Email}", email);
/// </summary>
public static class PhiLog
{
    /// <summary>
    /// Stable 10-character SHA-256 prefix of a value, lowercase hex. Use to
    /// correlate two log lines about the same email/phone without revealing
    /// the original value. Hash is per-process deterministic — same input
    /// produces same output, but values cannot be brute-forced from short
    /// hashes alone (10 hex = 40 bits, plus you'd need the full email/phone
    /// space to enumerate).
    /// </summary>
    public static string Hash(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(none)";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 5).ToLowerInvariant(); // 10 hex chars
    }

    /// <summary>
    /// Mask an email like "alice@hospital.com" -> "a***e@hospital.com". Keeps
    /// the domain (useful for noticing patterns like a flood of @gmail.com
    /// signups) and the first/last char of the local part for human grok.
    /// Returns "(none)" for null/empty.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "(none)";
        var at = email.IndexOf('@');
        if (at < 1) return "***";
        var local = email[..at];
        var domain = email[at..];
        if (local.Length <= 2) return $"***{domain}";
        return $"{local[0]}***{local[^1]}{domain}";
    }

    /// <summary>
    /// Mask a phone like "555-123-4567" -> "***-***-4567" (last 4 only).
    /// Returns "(none)" for null/empty.
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return "(none)";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "***";
        return $"***-***-{digits[^4..]}";
    }
}
