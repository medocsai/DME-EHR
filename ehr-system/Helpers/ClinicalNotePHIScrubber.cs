using System.Globalization;
using System.Text.RegularExpressions;
using EHR.Models.Generated;

namespace EHR.Helpers;

/// <summary>
/// PHI context for scrubbing — known patient/provider values that must be
/// removed from clinical text before sending to any external AI service.
/// All fields nullable: the scrubber only replaces what it knows.
///
/// When called from a context with no patient (e.g. AiSearchController), pass
/// `new PhiContext()` (or any all-null instance). The scrubber will skip the
/// targeted-replacement step and apply only its regex patterns (phone, SSN,
/// MRN format, email).
///
/// Spec: rules/technical/encounter-summary.md
/// </summary>
public class PhiContext
{
    public string? PatientFirstName { get; set; }
    public string? PatientLastName { get; set; }
    public DateOnly? PatientDob { get; set; }
    public string? PatientMrn { get; set; }
    public string? ProviderFirstName { get; set; }
    public string? ProviderLastName { get; set; }
    public string? PatientPhone { get; set; }
    public string? PatientEmail { get; set; }

    /// <summary>
    /// Centralised builder used by every site that scrubs PHI before a Gemini
    /// call. Decrypts encrypted Patient fields (FirstName, LastName, Phone,
    /// Email) using the provided EncryptionHelper, with a graceful fallback
    /// to the stored value if decryption throws (legacy unencrypted data).
    /// Provider fields are stored plaintext and copied as-is.
    /// </summary>
    public static PhiContext Build(Patient? patient, Provider? provider, EncryptionHelper? helper)
    {
        string? Decrypt(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (helper == null) return s;
            try { return helper.Decrypt(s) ?? s; }
            catch { return s; }
        }

        return new PhiContext
        {
            PatientFirstName = Decrypt(patient?.FirstName),
            PatientLastName = Decrypt(patient?.LastName),
            PatientDob = patient?.DateOfBirth,
            PatientMrn = patient?.Mrn,
            ProviderFirstName = provider?.FirstName,
            ProviderLastName = provider?.LastName,
            PatientPhone = Decrypt(patient?.Phone),
            PatientEmail = Decrypt(patient?.Email)
        };
    }
}

/// <summary>
/// Removes PHI from clinical-note plain text before sending to Gemini.
/// Pure string-in / string-out. No DB, no API, no allocations beyond strings.
///
/// Strategy:
///   1. Targeted replacement of known values from PhiContext (with common
///      formatting variants: "First Last", "Last, First", "Dr. Last", DOB
///      reformats like MM/dd/yyyy and "Month dd, yyyy").
///   2. Regex fallback for structural PHI (MRN format, phone, SSN, email)
///      to catch anything that wasn't in PhiContext.
///
/// Does NOT scrub generic dates — clinical notes legitimately reference
/// symptom dates, lab dates, follow-up dates. Only the patient's exact DOB
/// (from PhiContext) is removed.
/// </summary>
public static class ClinicalNotePHIScrubber
{
    private const string PatientPlaceholder = "[PATIENT]";
    private const string DobPlaceholder = "[DOB]";
    private const string MrnPlaceholder = "[MRN]";
    private const string ProviderPlaceholder = "[PROVIDER]";
    private const string PhonePlaceholder = "[PHONE]";
    private const string EmailPlaceholder = "[EMAIL]";
    private const string SsnPlaceholder = "[SSN]";

    private static readonly Regex MrnPattern = new(
        @"\bIM-\d{4,8}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhonePattern = new(
        @"\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}",
        RegexOptions.Compiled);

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    public static string Scrub(string? plainText, PhiContext context)
    {
        if (string.IsNullOrWhiteSpace(plainText)) return plainText ?? string.Empty;
        if (context == null) return plainText;

        var text = plainText;

        // ----- Step 1: targeted replacement of known values -----

        // Patient name variations
        text = ReplaceName(text, context.PatientFirstName, context.PatientLastName, PatientPlaceholder);

        // Provider name variations (also strip "Dr. LastName" form)
        text = ReplaceName(text, context.ProviderFirstName, context.ProviderLastName, ProviderPlaceholder);
        if (!string.IsNullOrWhiteSpace(context.ProviderLastName))
        {
            text = ReplaceCaseInsensitive(text, $"Dr. {context.ProviderLastName}", ProviderPlaceholder);
            text = ReplaceCaseInsensitive(text, $"Dr {context.ProviderLastName}", ProviderPlaceholder);
        }

        // DOB — replace several common formats of the patient's DOB
        if (context.PatientDob.HasValue)
        {
            var dob = context.PatientDob.Value;
            var dobFormats = new[]
            {
                dob.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                dob.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                dob.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
                dob.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
                dob.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            };
            foreach (var fmt in dobFormats)
                text = ReplaceCaseInsensitive(text, fmt, DobPlaceholder);
        }

        // MRN exact value
        if (!string.IsNullOrWhiteSpace(context.PatientMrn))
            text = ReplaceCaseInsensitive(text, context.PatientMrn, MrnPlaceholder);

        // Phone exact value
        if (!string.IsNullOrWhiteSpace(context.PatientPhone))
            text = ReplaceCaseInsensitive(text, context.PatientPhone, PhonePlaceholder);

        // Email exact value
        if (!string.IsNullOrWhiteSpace(context.PatientEmail))
            text = ReplaceCaseInsensitive(text, context.PatientEmail, EmailPlaceholder);

        // ----- Step 2: regex fallback for anything not in PhiContext -----
        text = MrnPattern.Replace(text, MrnPlaceholder);
        text = PhonePattern.Replace(text, PhonePlaceholder);
        text = SsnPattern.Replace(text, SsnPlaceholder);
        text = EmailPattern.Replace(text, EmailPlaceholder);

        return text;
    }

    private static string ReplaceName(string text, string? first, string? last, string placeholder)
    {
        var hasFirst = !string.IsNullOrWhiteSpace(first);
        var hasLast = !string.IsNullOrWhiteSpace(last);
        if (!hasFirst && !hasLast) return text;

        if (hasFirst && hasLast)
        {
            text = ReplaceCaseInsensitive(text, $"{first} {last}", placeholder);
            text = ReplaceCaseInsensitive(text, $"{last}, {first}", placeholder);
            text = ReplaceCaseInsensitive(text, $"{last},{first}", placeholder);
        }
        return text;
    }

    private static string ReplaceCaseInsensitive(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search)) return source;
        return Regex.Replace(source, Regex.Escape(search), replacement, RegexOptions.IgnoreCase);
    }
}
