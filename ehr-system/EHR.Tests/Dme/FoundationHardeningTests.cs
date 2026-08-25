using System;
using System.IO;
using System.Linq;
using EHR.Helpers;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Two foundation gaps found while completing the security pass, both of which
/// look like nothing until they matter.
///
/// 1. STAFF PASSWORDS HAD NO POLICY
///    The patient portal validated password strength. Staff accounts did not,
///    at any of the four places a password is set. A clinic administrator, or
///    the Super Admin who creates tenants, could be given the password "a".
///    A staff account holds a whole tenant's PHI; a portal account holds one
///    person's. The protection was on the wrong side.
///
/// 2. EXCEPTION DETAIL WAS RETURNED TO CALLERS
///    Six endpoints, four of them on the Super Admin clinic console, returned
///    the exception message AND the full stack trace in the response body, in
///    every environment. A stack trace maps internal namespaces, file paths and
///    often the failing SQL; a SqlException message can carry column values,
///    which on these tables means PHI.
/// </summary>
public class FoundationHardeningTests
{
    // ------------------------------------------------------------------
    // Password policy
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("a")]
    [InlineData("password")]
    [InlineData("Passw0rd!")]      // 9 characters: the kind of thing that "looks strong"
    [InlineData("           ")]
    [InlineData("")]
    [InlineData(null)]
    public void WeakPasswords_AreRejected(string? password)
    {
        PasswordPolicy.IsAcceptable(password).Should().BeFalse(
            $"'{password}' would protect an account holding an entire tenant's PHI");
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("DemoPass@2026")]
    [InlineData("a-perfectly-fine-long-passphrase")]
    public void StrongPasswords_AreAccepted(string password)
    {
        PasswordPolicy.Validate(password).Should().BeNull(
            "length is the control that matters; composition rules push people towards " +
            "Password1! and reuse");
    }

    [Fact]
    public void RepeatedCharacters_AreRejectedRegardlessOfLength()
    {
        PasswordPolicy.IsAcceptable(new string('a', 40)).Should().BeFalse(
            "40 a's clears any length rule and has almost no entropy");
    }

    [Fact]
    public void PasswordContainingTheOwnEmailName_IsRejected()
    {
        PasswordPolicy.IsAcceptable("johnsmith-is-my-password", "johnsmith@clinic.com")
            .Should().BeFalse("a password built from the account name is guessable at any length");
    }

    [Fact]
    public void ShortEmailNames_DoNotBlockUnrelatedPasswords()
    {
        // A 2 or 3 character local part ("dr@clinic.com") would otherwise match
        // half the dictionary and reject reasonable passwords.
        PasswordPolicy.Validate("thunderstorm-canopy", "dr@clinic.com").Should().BeNull(
            "a very short email name must not turn into a substring ban on ordinary words");
    }

    [Fact]
    public void MaximumLength_StaysUnderBCryptsInputLimit()
    {
        PasswordPolicy.MaximumLength.Should().BeLessThan(72,
            "BCrypt hashes only the first 72 bytes. Allowing more would silently discard " +
            "part of what the user typed and give a false sense of a longer secret");
    }

    [Fact]
    public void MinimumLength_IsAtLeastAsStrictAsThePatientPortal()
    {
        PasswordPolicy.MinimumLength.Should().BeGreaterThanOrEqualTo(8,
            "the portal already required 8; staff hold far more data and must not be weaker");
    }

    // ------------------------------------------------------------------
    // Exception detail must not reach the caller
    // ------------------------------------------------------------------

    [Fact]
    public void NoController_ReturnsAStackTraceToTheCaller()
    {
        var offenders = ControllerSources()
            .Where(f => File.ReadAllText(f.path).Contains("ex.StackTrace"))
            .Select(f => f.name)
            .ToArray();

        offenders.Should().BeEmpty(
            "a stack trace names internal namespaces, file paths and often the failing SQL. " +
            $"Offending controllers: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoController_PutsAnExceptionMessageInA500Body()
    {
        var offenders = ControllerSources()
            .Where(f =>
            {
                var text = File.ReadAllText(f.path);
                // Look for a 500 response whose payload mentions the exception.
                return System.Text.RegularExpressions.Regex.IsMatch(
                    text, @"StatusCode\(\s*500[^;]{0,200}ex\.(Message|ToString|InnerException)",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
            })
            .Select(f => f.name)
            .ToArray();

        offenders.Should().BeEmpty(
            "a SqlException message can contain column values, which on these tables is PHI. " +
            "Use this.ServerError(ex, \"...\"), which logs the detail and returns a correlation id. " +
            $"Offending controllers: {string.Join(", ", offenders)}");
    }

    private static (string name, string path)[] ControllerSources() =>
        Directory.EnumerateFiles(Path.Combine(ProductionRoot(), "Controllers"), "*.cs")
                 .Select(p => (Path.GetFileName(p), p))
                 .ToArray();

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
