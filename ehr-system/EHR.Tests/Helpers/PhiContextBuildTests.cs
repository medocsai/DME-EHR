using EHR.Helpers;
using EHR.Models.Generated;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Helpers;

/// <summary>
/// Unit tests for `PhiContext.Build(patient, provider, helper)`.
///
/// Why: this is the centralised constructor used by every Gemini call site.
/// Patient name/phone/email are stored encrypted on the Patient row — if
/// `Build` ever forgets to decrypt one, the scrubber receives ciphertext,
/// fails to match plaintext occurrences in the note, and PHI leaks to AI.
///
/// Spec: rules/technical/encounter-summary.md
/// </summary>
public class PhiContextBuildTests
{
    private static Patient SamplePatient(EncryptionHelper enc) => new Patient
    {
        PatientId      = 1,
        FirstName      = enc.Encrypt("Osman") ?? "",
        LastName       = enc.Encrypt("Ahmed") ?? "",
        DateOfBirth    = new DateOnly(2000, 8, 29),
        Mrn            = "IM-000671",
        Phone          = enc.Encrypt("555-123-4567") ?? "",
        Email          = enc.Encrypt("osman@testmd.com") ?? ""
    };

    private static Provider SampleProvider() => new Provider
    {
        ProviderId = 10,
        FirstName  = "James",
        LastName   = "Anderson"
    };

    [Fact]
    public void Build_WithEncryptedPatientFields_DecryptsAllConfiguredFields()
    {
        var enc = TestEncryptionHelper.Create();
        var patient = SamplePatient(enc);
        var provider = SampleProvider();

        var phi = PhiContext.Build(patient, provider, enc);

        phi.PatientFirstName.Should().Be("Osman");
        phi.PatientLastName.Should().Be("Ahmed");
        phi.PatientPhone.Should().Be("555-123-4567");
        phi.PatientEmail.Should().Be("osman@testmd.com");
        phi.PatientMrn.Should().Be("IM-000671");      // mrn stored plaintext
        phi.PatientDob.Should().Be(new DateOnly(2000, 8, 29));
        phi.ProviderFirstName.Should().Be("James");
        phi.ProviderLastName.Should().Be("Anderson");
    }

    [Fact]
    public void Build_NullPatient_ReturnsEmptyPatientFields_ProviderStillCopied()
    {
        var enc = TestEncryptionHelper.Create();
        var phi = PhiContext.Build(null, SampleProvider(), enc);

        phi.PatientFirstName.Should().BeNull();
        phi.PatientLastName.Should().BeNull();
        phi.PatientDob.Should().BeNull();
        phi.PatientMrn.Should().BeNull();
        phi.PatientPhone.Should().BeNull();
        phi.PatientEmail.Should().BeNull();
        phi.ProviderFirstName.Should().Be("James");
        phi.ProviderLastName.Should().Be("Anderson");
    }

    [Fact]
    public void Build_NullProvider_ReturnsPatientFields_ProviderEmpty()
    {
        var enc = TestEncryptionHelper.Create();
        var phi = PhiContext.Build(SamplePatient(enc), null, enc);

        phi.PatientFirstName.Should().Be("Osman");
        phi.ProviderFirstName.Should().BeNull();
        phi.ProviderLastName.Should().BeNull();
    }

    [Fact]
    public void Build_NullEncryptionHelper_ReturnsRawStoredValues()
    {
        // If helper is null, fields are returned as-is. Acceptable for cases
        // where the values happen to be plaintext (legacy) or where the
        // caller is OK with raw values.
        var patient = new Patient
        {
            FirstName = "Plaintext-First",
            LastName = "Plaintext-Last",
            Phone = "555-000-0000"
        };

        var phi = PhiContext.Build(patient, null, null);

        phi.PatientFirstName.Should().Be("Plaintext-First");
        phi.PatientLastName.Should().Be("Plaintext-Last");
        phi.PatientPhone.Should().Be("555-000-0000");
    }

    [Fact]
    public void Build_PlaintextLegacyData_GracefulFallback()
    {
        // EncryptionHelper.Decrypt() throws on plaintext data; Build should
        // catch and return the original string so legacy unencrypted rows
        // don't crash the scrubber path.
        var enc = TestEncryptionHelper.Create();
        var patient = new Patient
        {
            FirstName = "NotEncrypted-Osman",   // not actually encrypted
            LastName  = "NotEncrypted-Ahmed",
            Phone     = "555-123-4567"
        };

        var phi = PhiContext.Build(patient, null, enc);

        phi.PatientFirstName.Should().Be("NotEncrypted-Osman");
        phi.PatientLastName.Should().Be("NotEncrypted-Ahmed");
        phi.PatientPhone.Should().Be("555-123-4567");
    }

    [Fact]
    public void Build_AllNullInputs_ProducesEmptyContext()
    {
        var phi = PhiContext.Build(null, null, null);
        phi.PatientFirstName.Should().BeNull();
        phi.PatientLastName.Should().BeNull();
        phi.PatientDob.Should().BeNull();
        phi.PatientMrn.Should().BeNull();
        phi.PatientPhone.Should().BeNull();
        phi.PatientEmail.Should().BeNull();
        phi.ProviderFirstName.Should().BeNull();
        phi.ProviderLastName.Should().BeNull();
    }

    [Fact]
    public void Build_RoundTrip_ScrubberUsesDecryptedValues()
    {
        // End-to-end: encrypted patient → Build → Scrub. Verifies the chain
        // actually redacts the plaintext name from a sample note. Catches
        // regressions where Build forgets to decrypt before handing to the
        // scrubber (the original debug-session bug).
        var enc = TestEncryptionHelper.Create();
        var patient = SamplePatient(enc);

        var phi = PhiContext.Build(patient, SampleProvider(), enc);

        var note = "Osman Ahmed presented today. Reach at 555-123-4567.";
        var scrubbed = ClinicalNotePHIScrubber.Scrub(note, phi);

        scrubbed.Should().NotContain("Osman");
        scrubbed.Should().NotContain("Ahmed");
        scrubbed.Should().NotContain("555-123-4567");
        scrubbed.Should().Contain("[PATIENT]");
        scrubbed.Should().Contain("[PHONE]");
    }
}
