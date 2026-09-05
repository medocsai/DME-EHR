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
/// The payer catalog: Office Ally's national list of 4,017 payers, replacing the
/// four demo rows the client complained about.
///
/// WHAT THESE GUARD
///
/// 1. The catalog stays GLOBAL. The moment somebody puts TenantId back on
///    dbo.DmePayers, every supplier gets a private copy of a national list and
///    the copies start drifting. That is the stock problem with 4,017 rows.
/// 2. The customer form never trusts a payer NAME from the browser. It posts an
///    id, and the name and Payer ID that end up on the insurance record, and
///    therefore on a claim, are read out of the catalog on the server.
/// 3. A search term cannot smuggle LIKE wildcards.
/// </summary>
public class DmePayerCatalogTests
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

    // ------------------------------------------------------------------ search

    /// <summary>
    /// A DmeDb that returns whatever the catalog asked for, and remembers the
    /// SQL and parameters so the query shape can be asserted without a database.
    /// </summary>
    private static (DmePayerCatalog catalog, Mock<IDmeDb> db, List<(string Sql, object? Prms)> calls) Build(
        params (int Id, string Name, string Code)[] rows)
    {
        var calls = new List<(string, object?)>();
        var db = new Mock<IDmeDb>();

        db.Setup(d => d.Query(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(rows.Select(r => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
          {
              ["PayerId"] = r.Id, ["Name"] = r.Name, ["PayerCode"] = r.Code
          }).ToList());

        db.Setup(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(rows.Length == 0 ? null
              : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
              {
                  ["PayerId"] = rows[0].Id, ["Name"] = rows[0].Name, ["PayerCode"] = rows[0].Code
              });

        return (new DmePayerCatalog(db.Object), db, calls);
    }

    private static Dictionary<string, object?> Params(object? prms)
        => (Dictionary<string, object?>)prms!;

    private static string Param(object? prms, string name)
        => Params(prms)[name]!.ToString()!;

    /// <summary>Every LIKE term the query was built with, in order.</summary>
    private static List<string> Terms(object? prms)
        => Params(prms).Where(kv => kv.Key.StartsWith("w"))
                       .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                       .Select(kv => kv.Value!.ToString()!)
                       .ToList();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTerm_AsksTheDatabaseNothing(string? term)
    {
        var (catalog, db, _) = Build((1, "Aetna", "60054"));

        catalog.Search(term).Should().BeEmpty();

        // The point is not the empty result, it is that a typeahead which has
        // been asked nothing does not scan 4,017 rows on every focus.
        db.Verify(d => d.Query(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void Search_MatchesAnywhereInTheName_NotJustTheStart()
    {
        var (catalog, _, calls) = Build((1, "BCBS Texas (HCSC)", "84980"));

        catalog.Search("texas");

        Terms(calls[0].Prms).Should().Contain("%texas%",
            "anchoring the match at the start of the name finds almost nothing");
    }

    [Fact]
    public void Search_RequiresEveryWord_InAnyOrder()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("texas bcbs");

        var terms = Terms(calls[0].Prms);
        terms.Should().Contain("%texas%").And.Contain("%bcbs%");
        calls[0].Sql.Should().Contain(" AND ",
            "every word has to appear, or a second word only ever widens the result");
    }

    /// <summary>
    /// The failure this whole control existed to fix, found by typing into it.
    /// The catalog spells it "BCBS Texas (HCSC)". A biller reads "Blue Cross of
    /// Texas" off the card. Before this, that search returned nothing at all,
    /// which is worse than the four-item dropdown it replaced.
    /// </summary>
    [Fact]
    public void Search_KnowsBlueCrossAndBcbsAreTheSameInsurer()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("blue cross of texas");

        var terms = Terms(calls[0].Prms);
        terms.Should().Contain("%bcbs%", "otherwise 145 of the 163 Blue Cross plans are unreachable");
        terms.Should().Contain("%texas%");
        terms.Should().NotContain("%of%", "a noise word must not have to appear in the payer's name");
    }

    [Fact]
    public void Search_IgnoresATermThatIsNothingButNoise()
    {
        var (catalog, db, _) = Build((1, "Aetna", "60054"));

        catalog.Search("of the").Should().BeEmpty();

        db.Verify(d => d.Query(It.IsAny<string>(), It.IsAny<object>()), Times.Never,
            "with every word dropped there is no query left to run, and an empty WHERE returns all 4,017");
    }

    [Fact]
    public void Search_EscapesLikeWildcards()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("100%_care");

        Terms(calls[0].Prms).Should().Contain(@"%100\%\_care%",
            "an unescaped % matches all 4,017 rows, which reads as a broken search");
        calls[0].Sql.Should().Contain("ESCAPE",
            "escaping the term does nothing unless the query declares the escape character");
    }

    [Fact]
    public void Search_IsCappedEvenWhenACallerAsksForMore()
    {
        var (catalog, _, calls) = Build();

        catalog.Search("a", limit: 5000);

        int.Parse(Param(calls[0].Prms, "take")).Should().BeLessThanOrEqualTo(50,
            "the control is a picker, not an export");
    }

    [Fact]
    public void Find_RefusesAnIdThatWasNeverChosen()
    {
        var (catalog, db, _) = Build((1, "Aetna", "60054"));

        catalog.Find(0).Should().BeNull();
        catalog.Find(-3).Should().BeNull();

        db.Verify(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void Find_ReturnsTheNameAndPayerCodeTogether()
    {
        var (catalog, _, _) = Build((77, "Aetna Advantage", "60054"));

        var payer = catalog.Find(77);

        payer.Should().NotBeNull();
        payer!.Name.Should().Be("Aetna Advantage");
        payer.PayerCode.Should().Be("60054");
    }

    // ------------------------------------------------- the catalog stays global

    [Fact]
    public void PayerCatalog_IsNotTenantScoped()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Payer_Catalog.sql");

        migration.Should().Contain("DROP COLUMN",
            "the migration is what takes TenantId off dbo.DmePayers");
        migration.Should().Contain("TenantIsolationPolicy",
            "and what takes it out of the row level security policy");
    }

    [Fact]
    public void NoDmeCodeWritesTenantIdOntoDmePayers()
    {
        // A national list copied per tenant is the stock problem: thousands of
        // duplicates of a fact none of them owns, drifting the first time one is
        // corrected.
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(RepoRoot(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.Combine("obj", "")) && !f.Contains(Path.Combine("bin", ""))))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("DmePayers")) continue;

            foreach (System.Text.RegularExpressions.Match m in
                     Regex.Matches(text, @"INSERT\s+INTO\s+dbo\.DmePayers[^;]*", RegexOptions.IgnoreCase))
                if (m.Value.Contains("TenantId", StringComparison.OrdinalIgnoreCase))
                    offenders.Add(Path.GetFileName(file));
        }

        offenders.Should().BeEmpty(
            "dbo.DmePayers is a global reference table like dbo.DmeCarcCodes and has no TenantId");
    }

    [Fact]
    public void OnboardingScript_NoLongerCopiesPayersIntoANewTenant()
    {
        var onboard = Read("Migrations", "Manual", "DME_Onboard_New_Tenant.sql");

        Regex.IsMatch(onboard, @"INSERT\s+INTO\s+dbo\.DmePayers", RegexOptions.IgnoreCase)
            .Should().BeFalse("a new supplier gets the global catalog the moment it exists");
    }

    // ------------------------------------------- the server decides the payer

    [Fact]
    public void CreateCustomer_TakesAPayerIdAndNotAPayerName()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain("int insPayerId",
            "the form posts the catalog id");
        controller.Should().NotContain("string? insPayer,",
            "a payer NAME posted from the browser must not reach the insurance record");
    }

    /// <summary>
    /// The insert lives in SaveCustomerInsurance now, which CreateCustomer and
    /// UpdateCustomer both call, because New Customer and Edit customer are one
    /// form. The rule it pins is unchanged: the payer's name and the Payer ID a
    /// claim is addressed to are read from the catalog, never from the browser.
    /// </summary>
    [Fact]
    public void TheInsuranceRowIsResolvedFromTheCatalog()
    {
        var controller = Read("Controllers", "DmeController.cs");

        var insert = Regex.Match(controller,
            @"INSERT INTO dbo\.DmeCustomerInsurances.*?\}\);",
            RegexOptions.Singleline).Value;

        insert.Should().NotBeEmpty("the insurance insert should still be there");
        insert.Should().Contain("chosen!.Name").And.Contain("chosen.PayerCode",
            "both facts come from the catalog lookup, not from posted form fields");

        // And an EDIT cannot smuggle one in either: the update takes the payer
        // from the catalog or from the row already on file, never from a field.
        var update = Regex.Match(controller,
            @"UPDATE dbo\.DmeCustomerInsurances.*?\}\);",
            RegexOptions.Singleline).Value;

        update.Should().NotBeEmpty("editing a customer must reach the same row");
        update.Should().Contain("pn = name").And.Contain("pid = code");
    }

    [Fact]
    public void CodeSearches_AreNotOnTheControllerThatAuditsEveryRequestAsAPhiRead()
    {
        var dme = Read("Controllers", "DmeController.cs");
        var lookups = Read("Controllers", "LookupsController.cs");

        dme.Should().Contain("[PhiAccessAudit", "this is the guard's premise, not the guard");
        lookups.Should().NotContain("[PhiAccessAudit]",
            "a typeahead fires per keystroke and would bury the real PHI access trail");
        lookups.Should().Contain("[Authorize]",
            "no endpoint in this product is anonymous");
    }
}
