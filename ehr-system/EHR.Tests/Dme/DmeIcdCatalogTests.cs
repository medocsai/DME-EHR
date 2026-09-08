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

        // A FRESH cache per test. It is a singleton in the application, so a
        // shared one here would let the first test load the code set and every
        // test after it assert against those rows.
        return (new DmeIcdCatalog(db.Object, new IcdCodeCache()), db, calls);
    }


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
            "a search with no words must not even load the code set");
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
        var (catalog, _, _) = Build(("E66.9", "Obesity, unspecified"));

        var hits = catalog.Search(typed);

        hits.Should().ContainSingle();
        hits[0].Code.Should().Be("E66.9", "the stored spelling is what comes back");
    }

    [Fact]
    public void Search_MatchesWordsInTheDescription_InAnyOrder()
    {
        var (catalog, _, _) = Build(
            ("M17.0", "Bilateral primary osteoarthritis of knee"),
            ("M17.9", "Osteoarthritis of knee, unspecified"),
            ("E66.9", "Obesity, unspecified"));

        catalog.Search("knee osteoarthritis").Should().HaveCount(2);
        catalog.Search("osteoarthritis knee").Should().HaveCount(2);
    }

    /// <summary>
    /// EVERY word has to match. A second word that widened the result instead
    /// of narrowing it would make the picker useless exactly when an operator
    /// is trying to be more specific.
    /// </summary>
    [Fact]
    public void Search_NarrowsWithEachWordRatherThanWidening()
    {
        var (catalog, _, _) = Build(
            ("M17.0", "Bilateral primary osteoarthritis of knee"),
            ("E66.9", "Obesity, unspecified"));

        catalog.Search("osteoarthritis").Should().ContainSingle();
        catalog.Search("osteoarthritis obesity").Should().BeEmpty(
            "no single code carries both words, so requiring both must return nothing");
    }

    /// <summary>
    /// The search runs in memory now, so a percent sign is an ordinary
    /// character rather than a SQL wildcard. It must still be treated as one
    /// the operator typed, not as "match anything".
    /// </summary>
    [Fact]
    public void Search_TreatsWildcardCharactersAsOrdinaryText()
    {
        var (catalog, _, _) = Build(
            ("E66.9", "Obesity, unspecified"),
            ("Z99.9", "100% dependence on machine"));

        catalog.Search("100%").Should().ContainSingle()
            .Which.Code.Should().Be("Z99.9");

        catalog.Search("%").Should().ContainSingle(
            "a bare percent matches the one description that literally contains one");
    }

    [Fact]
    public void Search_IsCappedEvenWhenACallerAsksForMore()
    {
        var rows = Enumerable.Range(0, 80)
            .Select(i => ($"M17.{i}", $"Osteoarthritis variant {i}"))
            .ToArray();

        var (catalog, _, _) = Build(rows);

        catalog.Search("osteoarthritis", limit: 90000).Should().HaveCount(50,
            "the control is a picker, not an export of the whole code set");
    }

    /// <summary>
    /// COPD is the case that started this. CMS writes "Chronic obstructive
    /// pulmonary disease" and a biller types "copd", so before the alias table
    /// the search returned ZERO of 74,719 codes for the diagnosis an oxygen
    /// concentrator is billed under.
    /// </summary>
    [Theory]
    [InlineData("copd", "J44.9")]
    [InlineData("osa", "G47.33")]
    [InlineData("chf", "I50.9")]
    public void Search_KnowsTheAbbreviationsBillersActuallyType(string typed, string expected)
    {
        var (catalog, _, _) = Build(
            ("J44.9", "Chronic obstructive pulmonary disease, unspecified"),
            ("G47.33", "Obstructive sleep apnea (adult) (pediatric)"),
            ("I50.9", "Heart failure, unspecified"));

        catalog.Search(typed).Should().Contain(m => m.Code == expected);
    }

    /// <summary>
    /// And the full wording still works. An alias is an alternative, never a
    /// replacement: somebody who types what CMS printed must still find it.
    /// </summary>
    [Fact]
    public void Search_StillMatchesTheWordingCmsActuallyPublished()
    {
        var (catalog, _, _) = Build(
            ("J44.9", "Chronic obstructive pulmonary disease, unspecified"));

        catalog.Search("obstructive pulmonary").Should().ContainSingle();
    }

    /// <summary>
    /// The whole code set is read ONCE, however many searches follow.
    ///
    /// This is the point of the cache. Searching 74,719 rows in SQL was
    /// measured at about 1.5 seconds per keystroke against 54ms for the payer
    /// picker on the same form, and no index fixes a LIKE '%word%'.
    /// </summary>
    [Fact]
    public void TheCodeSetIsLoadedOnceAndReusedForEverySearch()
    {
        var (catalog, db, _) = Build(("E66.9", "Obesity, unspecified"));

        catalog.Search("obesity");
        catalog.Search("obesity");
        catalog.Search("unspecified");
        catalog.Find("E669");

        db.Verify(d => d.Query(It.IsAny<string>(), It.IsAny<object>()), Times.Once,
            "the national code set is immutable between annual releases");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_RefusesAnEmptyCodeWithoutAskingTheDatabase(string? code)
    {
        var (catalog, db, _) = Build(("E66.9", "Obesity, unspecified"));

        catalog.Find(code).Should().BeNull();
        db.Verify(d => d.Query(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Theory]
    [InlineData("E669")]
    [InlineData("E66.9")]
    [InlineData("  E66.9  ")]
    public void Find_ReturnsTheStoredSpellingOfTheCode(string typed)
    {
        var (catalog, _, _) = Build(
            ("E66.9", "Obesity, unspecified"),
            ("E66.01", "Morbid (severe) obesity due to excess calories"));

        var dx = catalog.Find(typed);

        dx.Should().NotBeNull();
        dx!.Code.Should().Be("E66.9", "what gets filed is the catalog's spelling, not the operator's");
        dx.Description.Should().Be("Obesity, unspecified");
    }

    /// <summary>
    /// Find is an EXACT match, not a prefix one. E66 must not silently resolve
    /// to E66.9: a header code on a claim is a denial, and quietly picking a
    /// child code for the operator files a diagnosis nobody chose.
    /// </summary>
    [Fact]
    public void Find_DoesNotResolveAPartialCode()
    {
        var (catalog, _, _) = Build(("E66.9", "Obesity, unspecified"));

        catalog.Find("E66").Should().BeNull();
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
    /// <summary>
    /// Scoped to the action rather than searched for across the whole file.
    /// There are now TWO places a diagnosis is filed, one on the create form and
    /// one on the customer screen, and an unscoped regex matched whichever
    /// happened to appear first.
    /// </summary>
    private static string Action(string name)
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"IActionResult> " + name + @"\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty($"{name} should still be there");
        return body;
    }

    [Fact]
    public void CreateCustomer_ResolvesTheDiagnosisFromTheCatalog()
    {
        var create = Action("CreateCustomer");

        create.Should().Contain("_icd.Find(dxCode)",
            "an invalid code must file no diagnosis at all");

        var insert = Regex.Match(create,
            @"INSERT INTO dbo\.DmeCustomerDiagnoses.*?\}\);",
            RegexOptions.Singleline).Value;

        insert.Should().NotBeEmpty("the diagnosis insert should still be there");
        insert.Should().Contain("diagnosis.Code").And.Contain("diagnosis.Description",
            "both facts come from the catalog lookup, not from a posted form field");
    }

    /// <summary>
    /// And the same rule on the screen where a diagnosis is added afterwards.
    /// This is the path that matters more: a diagnosis is added to an existing
    /// customer far more often than at the moment they are created.
    /// </summary>
    [Fact]
    public void AddDiagnosis_ResolvesTheCodeFromTheCatalogToo()
    {
        var add = Action("AddDiagnosis");

        add.Should().Contain("_icd.Find(code)",
            "a code the catalog does not know must file nothing");

        var insert = Regex.Match(add,
            @"INSERT INTO dbo\.DmeCustomerDiagnoses.*?\}\);",
            RegexOptions.Singleline).Value;

        insert.Should().Contain("icd.Code").And.Contain("icd.Description",
            "the wording is read from CMS, never taken from the browser");
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
