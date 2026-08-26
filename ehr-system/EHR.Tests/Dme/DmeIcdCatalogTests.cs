using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The ICD-10-CM catalog: the whole CMS FY2026 code set, replacing the twelve
/// codes that were hardcoded in DmeController.
///
/// WHAT THESE GUARD
///
/// 1. The catalog stays GLOBAL. ICD-10-CM is a national code set; a per-tenant
///    copy of 74,719 rows is the stock problem at scale.
/// 2. A diagnosis filed against a customer is resolved from the catalog, never
///    taken from the browser, and an invalid code files nothing.
/// 3. A code is found whether or not the operator typed the dot.
/// </summary>
public class DmeIcdCatalogTests
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

    private static (DmeIcdCatalog catalog, Mock<IDmeDb> db, List<(string Sql, object? Prms)> calls) Build(
        params (string Code, string Description)[] rows)
    {
        var calls = new List<(string, object?)>();
        var db = new Mock<IDmeDb>();

        db.Setup(d => d.Query(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(rows.Select(r => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
          {
              ["Code"] = r.Code, ["Description"] = r.Description
          }).ToList());

        db.Setup(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(rows.Length == 0 ? null
              : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
              {
                  ["Code"] = rows[0].Code, ["Description"] = rows[0].Description
              });

        return (new DmeIcdCatalog(db.Object), db, calls);
    }

    private static Dictionary<string, object?> Params(object? prms) => (Dictionary<string, object?>)prms!;

    private static List<string> Terms(object? prms)
        => Params(prms).Where(kv => kv.Key.StartsWith("w") || kv.Key.StartsWith("b"))
                       .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                       .Select(kv => kv.Value!.ToString()!)
                       .ToList();

    // ------------------------------------------------------------------ search

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("of the")]
    public void NothingToSearchOn_AsksTheDatabaseNothing(string? term)
    {
        var (catalog, db, _) = Build(("E66.9", "Obesity, unspecified"));

        catalog.Search(term).Should().BeEmpty();

        db.Verify(d => d.Query(It.IsAny<string>(), It.IsAny<object>()), Times.Never,
            "an empty WHERE over 74,719 rows returns the whole code set");
    }

    /// <summary>
    /// The code the client actually asked about. A biller reads "E66.9" off a
    /// referral; a system that only accepts one spelling of the same code is
    /// the same complaint again.
    /// </summary>
    [Theory]
    [InlineData("E66.9")]
    [InlineData("E669")]
    public void ACodeIsFoundWithOrWithoutItsDot(string typed)
    {
        var (catalog, _, calls) = Build(("E66.9", "Obesity, unspecified"));

        catalog.Search(typed).Should().ContainSingle();

        Params(calls[0].Prms)["exact"].Should().Be("E669",
            "the comparison is done undotted on both sides");
        calls[0].Sql.Should().Contain("REPLACE(Code,'.','')");
    }

    [Fact]
    public void Search_MatchesWordsInTheDescription_InAnyOrder()
    {
        var (catalog, _, calls) = Build(("M17.0", "Bilateral primary osteoarthritis of knee"));

        catalog.Search("knee osteoarthritis");

        var terms = Terms(calls[0].Prms);
        terms.Should().Contain("%knee%").And.Contain("%osteoarthritis%");
        calls[0].Sql.Should().Contain(" AND ",
            "every word must appear, or a second word only widens the result");
    }

    [Fact]
    public void Search_EscapesLikeWildcards()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("100%_pain");

        Terms(calls[0].Prms).Should().Contain(@"%100\%\_pain%");
        calls[0].Sql.Should().Contain("ESCAPE",
            "escaping the term does nothing unless the query declares the escape character");
    }

    [Fact]
    public void Search_IsCappedEvenWhenACallerAsksForMore()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("pain", limit: 90000);

        Convert.ToInt32(Params(calls[0].Prms)["take"]).Should().BeLessThanOrEqualTo(50,
            "the control is a picker, not an export of the whole code set");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_RefusesAnEmptyCodeWithoutAskingTheDatabase(string? code)
    {
        var (catalog, db, _) = Build(("E66.9", "Obesity, unspecified"));

        catalog.Find(code).Should().BeNull();
        db.Verify(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Theory]
    [InlineData("E669")]
    [InlineData("E66.9")]
    [InlineData("  E66.9  ")]
    public void Find_ReturnsTheStoredSpellingOfTheCode(string typed)
    {
        var (catalog, _, calls) = Build(("E66.9", "Obesity, unspecified"));

        var dx = catalog.Find(typed);

        dx.Should().NotBeNull();
        dx!.Code.Should().Be("E66.9", "what gets filed is the catalog's spelling, not the operator's");
        dx.Description.Should().Be("Obesity, unspecified");

        // The lookup itself has to be dot insensitive. Asserting only the
        // returned record proves nothing here: a fake database hands back the
        // row whatever it is asked for, so the PARAMETER is the evidence.
        var sent = calls[0].Prms!.GetType().GetProperty("code")!.GetValue(calls[0].Prms)!.ToString();
        sent.Should().Be("E669", "a code typed either way must reach the same row");
        calls[0].Sql.Should().Contain("REPLACE(Code,'.','')",
            "and the stored side has to be compared undotted too");
    }

    // ------------------------------------------------- the catalog stays global

    [Fact]
    public void IcdCatalog_IsNotTenantScopedAndHoldsOnlyBillableCodes()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Icd10_Catalog.sql");

        migration.Should().NotContain("TenantId NOT NULL",
            "ICD-10-CM is a national code set, not tenant data");
        migration.Should().Contain("icd10cm_codes_2026.txt",
            "the source has to be named, because the order file includes non-billable headers");
        migration.Should().Contain("E66.9",
            "the code the client asked about is worth verifying on every run");
    }

    [Fact]
    public void NoDmeCodeWritesTenantIdOntoIcdCodes()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.Combine("obj", "")) && !f.Contains(Path.Combine("bin", ""))))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("dbo.IcdCodes")) continue;

            foreach (System.Text.RegularExpressions.Match m in
                     Regex.Matches(text, @"INSERT\s+INTO\s+dbo\.IcdCodes[^;]*", RegexOptions.IgnoreCase))
                if (m.Value.Contains("TenantId", StringComparison.OrdinalIgnoreCase))
                    offenders.Add(Path.GetFileName(file));
        }

        offenders.Should().BeEmpty("dbo.IcdCodes is global, like dbo.DmePayers and dbo.DmeCarcCodes");
    }

    [Fact]
    public void OnboardingScript_DoesNotCopyTheIcdCatalogIntoANewTenant()
    {
        var onboard = Read("Migrations", "Manual", "DME_Onboard_New_Tenant.sql");

        onboard.Should().NotContain("IcdCodes",
            "a new supplier gets the national code set the moment it exists");
    }

    // ------------------------------------------ the server decides the diagnosis

    [Fact]
    public void TheHardcodedTwelveCodeListIsGone()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().NotContain("IcdList",
            "twelve codes in a C# array is the complaint the client raised");
    }

    [Fact]
    public void CreateCustomer_ResolvesTheDiagnosisFromTheCatalog()
    {
        var controller = Read("Controllers", "DmeController.cs");

        var insert = Regex.Match(controller,
            @"INSERT INTO dbo\.DmeCustomerDiagnoses.*?\}\);",
            RegexOptions.Singleline).Value;

        insert.Should().NotBeEmpty("the diagnosis insert should still be there");
        insert.Should().Contain("diagnosis.Code").And.Contain("diagnosis.Description",
            "both facts come from the catalog lookup, not from a posted form field");

        controller.Should().Contain("_icd.Find(dxCode)",
            "an invalid code must file no diagnosis at all");
    }

    /// <summary>
    /// The description is STORED on the diagnosis rather than joined, and that
    /// is deliberate: CMS rewords codes every October, and what a claim was
    /// billed under must not move underneath it. The same reasoning as
    /// DmeClaims.CustomerName and DmeRentals.MonthlyRate.
    /// </summary>
    [Fact]
    public void ADiagnosisKeepsItsOwnCopyOfTheWording()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Icd10_Catalog.sql");

        migration.Should().Contain("DmeCustomerDiagnoses",
            "the migration has to say why the stored copy is not a duplicate");
    }
}
