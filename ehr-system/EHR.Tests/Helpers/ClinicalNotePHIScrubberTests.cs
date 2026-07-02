using EHR.Helpers;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Helpers;

/// <summary>
/// Unit tests for the PHI scrubber that runs before every Gemini call site.
///
/// Why: this is a security boundary — every external AI request now depends
/// on the scrubber redacting names, DOB, MRN, phone, SSN, and email. A
/// regression here leaks patient identifiers to a third-party AI service.
///
/// What: pure-function tests of `ClinicalNotePHIScrubber.Scrub` and the
/// `PhiContext.Build` factory. No DB, no DI — fast and deterministic.
/// Spec: rules/technical/encounter-summary.md
/// </summary>
public class ClinicalNotePHIScrubberTests
{
    private static PhiContext OsmanContext() => new PhiContext
    {
        PatientFirstName = "Osman",
        PatientLastName  = "Ahmed",
        PatientDob       = new DateOnly(2000, 8, 29),
        PatientMrn       = "IM-000671",
        ProviderFirstName = "James",
        ProviderLastName  = "Anderson",
        PatientPhone = "555-123-4567",
        PatientEmail = "osman_ahmed@testmd.com"
    };

    // ============================================================
    // NULL / EMPTY HANDLING
    // ============================================================

    [Fact]
    public void Scrub_NullText_ReturnsEmpty()
    {
        ClinicalNotePHIScrubber.Scrub(null, OsmanContext()).Should().BeEmpty();
    }

    [Fact]
    public void Scrub_EmptyText_ReturnsEmpty()
    {
        ClinicalNotePHIScrubber.Scrub("", OsmanContext()).Should().BeEmpty();
    }

    [Fact]
    public void Scrub_WhitespaceOnly_ReturnsAsIs()
    {
        ClinicalNotePHIScrubber.Scrub("   ", OsmanContext()).Should().Be("   ");
    }

    [Fact]
    public void Scrub_NullContext_ReturnsTextUnchanged()
    {
        // Documented behavior: if the caller passes null context (not the
        // empty PhiContext), scrubber bails out without applying regex.
        const string text = "Patient phone is 555-123-4567";
        ClinicalNotePHIScrubber.Scrub(text, null!).Should().Be(text);
    }

    // ============================================================
    // TARGETED REPLACEMENT — patient name
    // ============================================================

    [Fact]
    public void Scrub_PatientFullName_FirstLastOrder_Replaced()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "Osman Ahmed presented with cough.", OsmanContext());
        result.Should().NotContain("Osman").And.NotContain("Ahmed");
        result.Should().Contain("[PATIENT]");
    }

    [Fact]
    public void Scrub_PatientFullName_LastCommaFirstOrder_Replaced()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "Patient Name: Ahmed, Osman", OsmanContext());
        result.Should().NotContain("Osman").And.NotContain("Ahmed");
        result.Should().Contain("[PATIENT]");
    }

    [Fact]
    public void Scrub_PatientName_CaseInsensitive()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "OSMAN AHMED came in today.", OsmanContext());
        result.Should().NotContain("OSMAN").And.NotContain("AHMED");
    }

    // ============================================================
    // TARGETED REPLACEMENT — provider name
    // ============================================================

    [Fact]
    public void Scrub_ProviderFullName_Replaced()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "James Anderson saw the patient.", OsmanContext());
        result.Should().NotContain("James").And.NotContain("Anderson");
        result.Should().Contain("[PROVIDER]");
    }

    [Fact]
    public void Scrub_ProviderWithDrPrefix_Replaced()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "Dr. Anderson recommended follow-up.", OsmanContext());
        result.Should().NotContain("Anderson");
        result.Should().Contain("[PROVIDER]");
    }

    // ============================================================
    // TARGETED REPLACEMENT — DOB in multiple formats
    // ============================================================

    [Theory]
    [InlineData("Born on 08/29/2000.")]
    [InlineData("DOB: 8/29/2000")]
    [InlineData("Date of birth 2000-08-29")]
    [InlineData("Born August 29, 2000.")]
    [InlineData("DOB Aug 29, 2000")]
    public void Scrub_PatientDob_VariousFormats_Replaced(string text)
    {
        var result = ClinicalNotePHIScrubber.Scrub(text, OsmanContext());
        result.Should().Contain("[DOB]");
    }

    [Fact]
    public void Scrub_GenericDate_NotInDob_Untouched()
    {
        // Visit date, symptom date — clinical notes contain many dates.
        // Only the patient's exact DOB should be scrubbed.
        var result = ClinicalNotePHIScrubber.Scrub(
            "Symptoms started on 03/12/2026.", OsmanContext());
        result.Should().Contain("03/12/2026");
        result.Should().NotContain("[DOB]");
    }

    // ============================================================
    // REGEX FALLBACK PATTERNS
    // ============================================================

    [Fact]
    public void Scrub_MrnFormat_ReplacedByRegex()
    {
        // Regex catches IM-XXXXXX format even if not in PhiContext.
        var phi = new PhiContext { /* no Mrn populated */ };
        var result = ClinicalNotePHIScrubber.Scrub(
            "Reference: IM-987654 in chart.", phi);
        result.Should().Contain("[MRN]").And.NotContain("IM-987654");
    }

    [Theory]
    [InlineData("Phone: 555-123-4567")]
    [InlineData("Call (555) 123-4567")]
    [InlineData("Reach me at 555.123.4567")]
    public void Scrub_PhoneNumber_ReplacedByRegex(string text)
    {
        var phi = new PhiContext { /* no phone populated */ };
        var result = ClinicalNotePHIScrubber.Scrub(text, phi);
        result.Should().Contain("[PHONE]");
    }

    [Fact]
    public void Scrub_Ssn_ReplacedByRegex()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "SSN 123-45-6789 on file.", new PhiContext());
        result.Should().Contain("[SSN]").And.NotContain("123-45-6789");
    }

    [Fact]
    public void Scrub_Email_ReplacedByRegex()
    {
        var result = ClinicalNotePHIScrubber.Scrub(
            "Contact: someone@example.org", new PhiContext());
        result.Should().Contain("[EMAIL]").And.NotContain("someone@example.org");
    }

    // ============================================================
    // REGEX-ONLY MODE (AiSearchController use case)
    // ============================================================

    [Fact]
    public void Scrub_EmptyContext_StillRunsRegexFallback()
    {
        // Site #16/#17 (AiSearchController) has no patient context.
        // It passes `new PhiContext()` and relies on regex fallback for
        // structural PHI like phones and SSNs.
        var result = ClinicalNotePHIScrubber.Scrub(
            "Patient phone (555) 999-1111, ssn 999-00-1234.",
            new PhiContext());
        result.Should().Contain("[PHONE]");
        result.Should().Contain("[SSN]");
    }

    [Fact]
    public void Scrub_EmptyContext_DoesNotCatchInlineNames()
    {
        // Documented limitation of regex-only mode: unknown inline names
        // are not detected. This is acceptable for AiSearchController
        // where no patient context is available.
        var result = ClinicalNotePHIScrubber.Scrub(
            "Mrs. Smith reports headache.",
            new PhiContext());
        result.Should().Contain("Smith");
    }

    // ============================================================
    // IDEMPOTENCE
    // ============================================================

    [Fact]
    public void Scrub_RunTwice_SameResult()
    {
        // Important for sites where the same text might pass through
        // multiple scrub layers (e.g. callsite scrub + downstream scrub).
        const string text = "Osman Ahmed (DOB 08/29/2000), MRN IM-000671, ph 555-123-4567";
        var once  = ClinicalNotePHIScrubber.Scrub(text,  OsmanContext());
        var twice = ClinicalNotePHIScrubber.Scrub(once,  OsmanContext());
        twice.Should().Be(once);
    }

    // ============================================================
    // FULL CLINICAL-NOTE-LIKE STRING — END-TO-END SMOKE
    // ============================================================

    [Fact]
    public void Scrub_RealisticNoteHeader_StripsAllIdentifiers()
    {
        const string note = @"
Patient Name: Osman Ahmed
DOB: 08/29/2000
MRN: IM-000671
Phone: 555-123-4567
Provider: James Anderson

Subjective:
Patient reports cough and fatigue for 3 days.";

        var result = ClinicalNotePHIScrubber.Scrub(note, OsmanContext());

        result.Should().NotContain("Osman");
        result.Should().NotContain("Ahmed");
        result.Should().NotContain("08/29/2000");
        result.Should().NotContain("IM-000671");
        result.Should().NotContain("555-123-4567");
        result.Should().NotContain("James");
        result.Should().NotContain("Anderson");

        // Clinical content survives.
        result.Should().Contain("cough and fatigue");
        result.Should().Contain("Subjective");
    }
}
