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
/// The national HCPCS Level II catalog, and the supplier's item master that
/// draws from it.
///
/// THE DISTINCTION THESE PROTECT
/// dbo.HcpcsCodes is the SUPPLIER'S item master: their price, their rental
/// terms, correctly tenant scoped. dbo.HcpcsNationalCodes is what CMS
/// publishes: global, nobody's private copy. Pouring 8,623 national codes into
/// the tenant table would have been the payer mistake again, with pricing
/// columns left blank on every row.
/// </summary>
public class DmeHcpcsCatalogTests
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

    private static (DmeHcpcsCatalog catalog, Mock<IDmeDb> db, List<(string Sql, object? Prms)> calls) Build(
        params (string Code, string Long, string Short, string? Coverage, DateTime? Terminated)[] rows)
    {
        var calls = new List<(string, object?)>();
        var db = new Mock<IDmeDb>();

        List<Dictionary<string, object?>> Rows() => rows.Select(r =>
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = r.Code,
                ["LongDescription"] = r.Long,
                ["ShortDescription"] = r.Short,
                ["CoverageCode"] = (object?)r.Coverage ?? DBNull.Value,
                ["TerminatedOn"] = (object?)r.Terminated ?? DBNull.Value
            }).ToList();

        db.Setup(d => d.Query(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(Rows);

        db.Setup(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(() => rows.Length == 0 ? null : Rows()[0]);

        return (new DmeHcpcsCatalog(db.Object), db, calls);
    }

    private static Dictionary<string, object?> Params(object? prms) => (Dictionary<string, object?>)prms!;

    // ------------------------------------------------------------------ search

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("of the")]
    public void NothingToSearchOn_AsksTheDatabaseNothing(string? term)
    {
        var (catalog, db, _) = Build(("E1390", "Oxygen concentrator", "Oxygen concentrator", "D", null));

        catalog.Search(term).Should().BeEmpty();
        db.Verify(d => d.Query(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void Search_LooksAtTheCodeAndBothDescriptions()
    {
        var (catalog, _, calls) = Build(("E1390", "Oxygen concentrator", "Oxygen concentrator", "D", null));

        catalog.Search("oxygen concentrator");

        calls[0].Sql.Should().Contain("LongDescription LIKE")
                             .And.Contain("ShortDescription LIKE")
                             .And.Contain("Code LIKE",
            "a buyer searches by what the thing is; a biller searches by code");
        calls[0].Sql.Should().Contain(" AND ", "every word has to appear");
    }

    /// <summary>
    /// A retired code cannot be billed. Offering one first is an invitation to a
    /// denial, so current codes sort ahead of retired ones.
    /// </summary>
    [Fact]
    public void Search_PutsStillBillableCodesFirst()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("wheelchair");

        calls[0].Sql.Should().Contain("TerminatedOn",
            "the ordering has to know which codes are dead");
    }

    [Fact]
    public void Search_EscapesLikeWildcards()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("100%_bed");

        Params(calls[0].Prms).Values.Select(v => v?.ToString())
            .Should().Contain(@"%100\%\_bed%");
        calls[0].Sql.Should().Contain("ESCAPE");
    }

    [Fact]
    public void Search_IsCappedEvenWhenACallerAsksForMore()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("bed", limit: 9000);

        Convert.ToInt32(Params(calls[0].Prms)["take"]).Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public void Find_NormalisesCaseSoATypedCodeStillResolves()
    {
        var (catalog, _, calls) = Build(("E1390", "Oxygen concentrator", "Oxygen concentrator", "D", null));

        catalog.Find("  e1390  ").Should().NotBeNull();

        calls[0].Prms!.GetType().GetProperty("code")!.GetValue(calls[0].Prms)!.ToString()
            .Should().Be("E1390", "codes are upper case in the national list");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_RefusesAnEmptyCodeWithoutAskingTheDatabase(string? code)
    {
        var (catalog, db, _) = Build(("E1390", "Oxygen concentrator", "Oxygen concentrator", "D", null));

        catalog.Find(code).Should().BeNull();
        db.Verify(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    // -------------------------------------------------------- derived, not stored

    [Fact]
    public void RetirementIsDerivedFromTheDate_NotStoredBesideIt()
    {
        var yesterday = new HcpcsMatch("E0000", "x", "x", null, DateTime.Today.AddDays(-1));
        var tomorrow = new HcpcsMatch("E0001", "x", "x", null, DateTime.Today.AddDays(1));
        var never = new HcpcsMatch("E0002", "x", "x", null, null);

        yesterday.IsRetired.Should().BeTrue();
        tomorrow.IsRetired.Should().BeFalse("a code retiring in the future is billable today");
        never.IsRetired.Should().BeFalse();
    }

    [Theory]
    [InlineData("I", true)]
    [InlineData("M", true)]
    [InlineData("S", true)]
    [InlineData("C", false)]
    [InlineData("D", false)]
    [InlineData(null, false)]
    public void MedicareCoverageIsDerivedFromTheCmsCode(string? coverage, bool neverPays)
    {
        new HcpcsMatch("E0000", "x", "x", coverage, null).MedicareNeverPays.Should().Be(neverPays);
    }

    // ------------------------------------------ the two tables stay separate

    [Fact]
    public void TheNationalListIsGlobal_AndTheItemMasterStaysTenantScoped()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Hcpcs_Catalog.sql");

        migration.Should().Contain("HcpcsNationalCodes",
            "the national list is its own table");
        Regex.IsMatch(migration, @"CREATE TABLE dbo\.HcpcsNationalCodes\s*\((?:(?!\)).)*TenantId",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Should().BeFalse("a national code set is not tenant data");
        migration.Should().NotContain("INSERT INTO dbo.HcpcsCodes",
            "the supplier's item master must not be filled with 8,623 rows of blank pricing");
    }

    [Fact]
    public void TheItemMasterKeepsItsPricingColumns()
    {
        var schema = Read("Migrations", "Manual", "2026-06-19_DME_Core_Schema.sql");

        foreach (var column in new[] { "PurchasePrice", "MonthlyRate", "Rentable", "IsSerialized" })
            schema.Should().Contain(column,
                "these are the supplier's own facts and are why the item master is a separate table");
    }

    // ------------------------------------------------ adding an item is guarded

    [Fact]
    public void AddItem_ChecksTheCodeAgainstTheNationalListBeforeFilingIt()
    {
        var controller = Read("Controllers", "HcpcsController.cs");

        controller.Should().Contain("_national.Find(hcpcs)",
            "an invented code is a claim rejected weeks after the equipment shipped");
        controller.Should().Contain("code.IsRetired",
            "a retired code cannot be billed, and the catalog is where that is cheap to catch");
        controller.Should().Contain("is already in your catalog",
            "adding the same code twice gives two prices for one item");
    }

    [Fact]
    public void AddItem_IsAdminOnlyAndAudited()
    {
        var controller = Read("Controllers", "HcpcsController.cs");

        var action = Regex.Match(controller,
            @"\[HttpPost\].*?public async Task<IActionResult> AddItem",
            RegexOptions.Singleline).Value;

        action.Should().Contain("[ValidateAntiForgeryToken]");
        action.Should().Contain(@"[Authorize(Roles = ""0,1"")]",
            "pricing is a commercial decision, not something a delivery driver sets");
        controller.Should().Contain("DME_CATALOG_ITEM_ADDED",
            "a change to what the business sells is worth an audit row");
    }

    [Fact]
    public void OnboardingScript_StillCopiesTheItemMasterButNotTheNationalList()
    {
        var onboard = Read("Migrations", "Manual", "DME_Onboard_New_Tenant.sql");

        onboard.Should().Contain("INSERT INTO dbo.HcpcsCodes",
            "a new supplier still needs a starting item master with prices");
        onboard.Should().NotContain("HcpcsNationalCodes",
            "but the national code list is global and needs no copy");
    }
}
