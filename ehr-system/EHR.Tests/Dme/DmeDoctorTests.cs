using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Referring physicians, and the NPI check digit.
///
/// WHY THIS WAS A BLOCKER RATHER THAN A GAP
///
/// dbo.DmeDoctors held three seeded rows and the product had no way, anywhere,
/// to add a fourth. Every DMEPOS claim needs an ordering physician in CMS-1500
/// boxes 17 and 17b, and a referral arrives from whichever doctor the patient
/// happened to see.
///
/// So a supplier receiving a referral from a doctor not among the three had two
/// options and both are bad: file the order under whichever of the three is
/// closest, which puts a physician's name and NPI on a claim they never signed,
/// or not process the referral at all.
///
/// THE CHECK DIGIT
///
/// An NPI is ten digits and the last one is a Luhn check over the constant
/// prefix 80840 plus the first nine. 80840 is the health industry's ISO issuer
/// identifier, and CMS chose it so that an NPI validates under an algorithm
/// every card system already implements.
///
/// It proves the number is WELL FORMED, not that the doctor exists: that needs
/// the NPPES registry and a network call. The message says "cannot be right"
/// rather than "does not exist", which is the same rule that took the
/// eligibility chip out.
/// </summary>
public class DmeDoctorTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Controllers"))) d = d.Parent;
        d.Should().NotBeNull("the tests must be able to find the ehr-system folder");
        return d!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    // ------------------------------------------------------- the check digit

    /// <summary>
    /// 1234567893 is the worked example in CMS's own check digit documentation.
    /// If this ever fails, the algorithm is wrong, not the number.
    /// </summary>
    [Theory]
    [InlineData("1234567893")]
    [InlineData("1999999984")]
    public void ARealNpiPassesTheCheckDigit(string npi)
        => Npi.IsPossible(npi).Should().BeTrue();

    /// <summary>
    /// The same digits with the check digit wrong. 1234567890 is what somebody
    /// types when they are filling a field in rather than reading a referral,
    /// and it is the supplier NPI sitting in this product's own demo data.
    /// </summary>
    [Theory]
    [InlineData("1234567890")]
    [InlineData("1234567892")]
    [InlineData("0000000000")]
    public void AWrongCheckDigitIsRejected(string npi)
        => Npi.IsPossible(npi).Should().BeFalse();

    [Theory]
    [InlineData("123456789")]      // nine
    [InlineData("12345678931")]    // eleven
    [InlineData("123456789X")]     // not all digits
    [InlineData("")]
    [InlineData(null)]
    public void AnythingThatIsNotTenDigitsIsRejected(string? npi)
        => Npi.IsPossible(npi).Should().BeFalse();

    /// <summary>
    /// People write an NPI with spaces and dashes because that is how it is
    /// printed. The stored form is ten digits, so the punctuation comes off
    /// before anything judges it.
    /// </summary>
    [Theory]
    [InlineData("1999-999-984", "1999999984")]
    [InlineData(" 1234567893 ", "1234567893")]
    [InlineData("123 456 7893", "1234567893")]
    public void PunctuationIsStrippedBeforeTheNumberIsJudged(string typed, string expected)
        => Npi.Normalise(typed).Should().Be(expected);

    /// <summary>
    /// A blank stays blank rather than becoming an empty string, so a doctor
    /// recorded before their NPI is known keeps a NULL and does not collide
    /// with every other such doctor under the unique index.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no npi yet")]
    public void NothingUsableNormalisesToNull(string? typed)
        => Npi.Normalise(typed).Should().BeNull();

    /// <summary>
    /// ONE implementation, two callers. The supplier's own NPI on the Settings
    /// screen goes in box 33a of every claim they ever file; the physician's
    /// goes in box 17b. A rule written twice disagrees with itself eventually,
    /// and the disagreement shows up as some claims rejecting and others not.
    /// </summary>
    [Fact]
    public void TheCheckDigitLivesInExactlyOnePlace()
    {
        var copies = 0;

        foreach (var dir in new[] { "Controllers", "Services", "Helpers" })
        {
            var full = Path.Combine(RepoRoot(), dir);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                if (File.ReadAllText(file).Contains("\"80840\""))
                    copies++;
        }

        copies.Should().Be(1, "the NPI prefix should appear in Helpers/Npi.cs and nowhere else");
    }

    // ------------------------------------------------------------ the service

    private static (DmeDoctors doctors, Mock<IDmeDb> db) Build(params (int Id, string Npi)[] existing)
    {
        var db = new Mock<IDmeDb>();

        db.Setup(d => d.Scalar(It.Is<string>(s => s.Contains("SELECT DoctorId FROM dbo.DmeDoctors")), It.IsAny<object>()))
          .Returns((string _, object? p) =>
          {
              var npi = p!.GetType().GetProperty("npi")!.GetValue(p)?.ToString();
              foreach (var e in existing) if (e.Npi == npi) return e.Id;
              return null;
          });

        db.Setup(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()))
          .Returns(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
          {
              ["DoctorId"] = 1, ["FirstName"] = "A", ["LastName"] = "B",
              ["Npi"] = DBNull.Value, ["Specialty"] = DBNull.Value,
              ["Phone"] = DBNull.Value, ["RetiredAt"] = DBNull.Value
          });

        db.Setup(d => d.Scalar(It.Is<string>(s => s.Contains("INSERT INTO dbo.DmeDoctors")), It.IsAny<object>()))
          .Returns(99);

        return (new DmeDoctors(db.Object), db);
    }

    [Fact]
    public void ADoctorNeedsAName()
    {
        var (doctors, _) = Build();

        doctors.Add("", "Nameless", "1234567893", null, null)
               .Error.Should().Contain("first and last name");
    }

    [Fact]
    public void AnImpossibleNpiIsRefusedWithASentenceNotAConstraintViolation()
    {
        var (doctors, _) = Build();
        var result = doctors.Add("Bad", "Digit", "1234567890", null, null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("check digit",
            "the operator has to know WHICH part is wrong to fix it");
    }

    [Fact]
    public void ADuplicateNpiIsRefused()
    {
        var (doctors, _) = Build((7, "1234567893"));

        doctors.Add("Someone", "Else", "1234567893", null, null)
               .Error.Should().Contain("already on the list");
    }

    /// <summary>
    /// A doctor may be recorded before their NPI is known. Refusing that would
    /// stop an order being raised at all, which is worse than a claim that
    /// cannot yet be billed.
    /// </summary>
    [Fact]
    public void ADoctorCanBeRecordedBeforeTheirNpiIsKnown()
        => Build().doctors.Add("Not Yet", "Known", null, null, null).Success.Should().BeTrue();

    // ------------------------------------------------------------ the record

    /// <summary>
    /// Retired, never deleted. Every order they signed names them in box 17, and
    /// deleting the row would leave those claims pointing at nothing.
    /// </summary>
    [Fact]
    public void ADoctorIsRetiredRatherThanDeleted()
    {
        var service = Read("Services", "DmeDoctors.cs");

        service.Should().NotContain("DELETE FROM dbo.DmeDoctors",
            "a doctor who stops referring still signed every order they signed");

        service.Should().Contain("SET RetiredAt = SYSUTCDATETIME()")
               .And.Contain("AND RetiredAt IS NULL",
                   "guarded so two people clicking Retire produce one retirement");
    }

    /// <summary>
    /// Find does NOT filter retired doctors. An order signed before they stopped
    /// referring still has to name them, so the lookup has to reach them. Same
    /// decision as DmeDistributors.Find.
    /// </summary>
    [Fact]
    public void FindReachesARetiredDoctor()
    {
        var find = Regex.Match(
            Read("Services", "DmeDoctors.cs"),
            @"public Doctor\? Find\(int doctorId\).*?\n    \}",
            RegexOptions.Singleline).Value;

        find.Should().NotContain("RetiredAt IS NULL",
            "only the PICKER hides them; history still has to resolve them");
    }

    [Theory]
    [InlineData("AddDoctor", "DME_DOCTOR_ADDED")]
    [InlineData("UpdateDoctor", "DME_DOCTOR_UPDATED")]
    [InlineData("RetireDoctor", "DME_DOCTOR_RETIRED")]
    public void EveryChangeToADoctorIsAudited(string action, string eventName)
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"IActionResult> " + action + @"\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty($"{action} should be there");
        body.Should().Contain(eventName,
            "the NPI on a claim is a physician's identity; changing it is worth a record");
    }

    /// <summary>
    /// The screen is NOT admin only, unlike Distributors. Whoever raises an
    /// order needs the doctor who signed the referral to be on the list, and
    /// that is a daily job rather than a commercial decision.
    /// </summary>
    [Fact]
    public void TheDoctorScreenIsAvailableToWhoeverRaisesOrders()
    {
        var doctorsAction = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"[^\n]*\n[^\n]*\n\s*public IActionResult Doctors\(\)",
            RegexOptions.Singleline).Value;

        doctorsAction.Should().NotContain("[Authorize(Roles",
            "a front desk taking a referral has to be able to add the doctor on it");

        Read("Views", "Shared", "_Layout.cshtml").Should().Contain("/Dme/Doctors")
            .And.NotContain("\"/Dme/Doctors\" class=\"nav-item requires-admin");
    }

    /// <summary>
    /// The supplier's OWN NPI is checked by the same rule.
    ///
    /// It goes in CMS-1500 box 33a on every claim they will ever file, so a typo
    /// there is not one rejected claim, it is all of them. This is the second
    /// caller that makes the shared helper worth having.
    /// </summary>
    [Fact]
    public void TheSuppliersOwnNpiIsCheckedToo()
    {
        var save = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"IActionResult> SaveSupplier\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        save.Should().NotBeEmpty("the supplier save action should be there");
        save.Should().Contain("Npi.Normalise(npi)");
        save.Should().Contain("!Npi.IsPossible(npi)",
            "box 33a is on every claim this supplier files");
    }
}
