using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The product may not assert a fact it has not established.
///
/// This is not a style rule. A screen that states a clinical or financial fact
/// is read by a biller who acts on it, and the action is irreversible in the
/// ways that matter: a claim goes out, a patient is not asked for their
/// deductible, a document is believed to be on file.
///
/// Three fabrications were found on the customer path and removed:
///
///   1. Customer.cshtml painted a green "Eligible" chip on EVERY insurance row,
///      unconditionally. It was markup, not data. A customer whose coverage
///      lapsed last month displayed as eligible with a tick beside it.
///
///   2. NewCustomer.cshtml had a "Validate Insurance" button that waited 1.1
///      seconds on a setTimeout and then printed "Active - DME covered -
///      Deductible met - 20% coinsurance" from a string literal. It contacted
///      nothing. "Deductible met" is the dangerous half: a biller who believes
///      it does not collect the patient's share, and routinely not collecting
///      patient responsibility is an inducement, which is its own offence.
///
///   3. DmeCustomerInsurances.EligStatus was written as the constant 'active'
///      on every insert and read by nothing. A fact-shaped column, already
///      populated, sitting exactly where a future 270/271 feature would look.
///
/// Nothing in this product has ever contacted a payer to ask about eligibility.
/// The honest state is absent, not 'active'.
/// </summary>
public class DmeNoFabricatedFactsTests
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

    private static string[] AllViews()
        => Directory.GetFiles(Path.Combine(RepoRoot(), "Views"), "*.cshtml", SearchOption.AllDirectories);

    /// <summary>
    /// No screen may tell a user that coverage is eligible, active or verified,
    /// while nothing in the product has asked a payer.
    ///
    /// Deliberately a scan of every view rather than the two that were wrong:
    /// the next person to add an insurance panel would copy the one that
    /// existed, and the chip was the most copyable thing on it.
    /// </summary>
    [Theory]
    [InlineData("Eligible")]
    [InlineData("Deductible met")]
    [InlineData("DME covered")]
    public void NoScreenClaimsCoverageThatWasNeverChecked(string claim)
    {
        foreach (var file in AllViews())
        {
            File.ReadAllText(file).Should().NotContain(claim,
                $"{Path.GetFileName(file)} would be asserting an eligibility result to a biller who acts on it, "
                + "and this product has never contacted a payer");
        }
    }

    /// <summary>
    /// And the button that produced it stays gone. A control that appears to
    /// perform a check is the fabrication, whatever text it eventually shows.
    /// </summary>
    [Fact]
    public void TheValidateInsuranceButtonStaysGone()
    {
        var view = Read("Views", "Dme", "NewCustomer.cshtml");

        view.Should().NotContain("validateBtn",
            "the button ran a setTimeout and printed a literal; there was no check behind it");
        view.Should().NotContain("Checking eligibility",
            "a spinner saying it is checking, that checks nothing, is the same claim made twice");
    }

    /// <summary>
    /// The benefit figures themselves STAY, and the screen says where they come
    /// from. They are entered by staff off the payer's portal or the member's
    /// card, so they are real recorded facts with a known source. Removing them
    /// would have been the opposite mistake.
    /// </summary>
    [Fact]
    public void TheStaffEnteredBenefitsRemainAndTheirSourceIsStated()
    {
        var view = Read("Views", "Dme", "NewCustomer.cshtml");

        view.Should().Contain("insCopay");
        view.Should().Contain("insCoins");
        view.Should().Contain("insDeductible");

        view.Should().Contain("does not check eligibility with the payer",
            "the user has to know the numbers are theirs to source, or they will assume the software did it");
    }

    /// <summary>
    /// No code may write the eligibility column back. The migration drops it,
    /// and this stops it being reintroduced by a copied INSERT.
    /// </summary>
    [Fact]
    public void NothingWritesAnEligibilityStatus()
    {
        var roots = new[] { "Controllers", "Services", "Helpers" };

        foreach (var root in roots)
        {
            var dir = Path.Combine(RepoRoot(), root);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                File.ReadAllText(file).Should().NotContain("EligStatus",
                    $"{Path.GetFileName(file)} would be recording a payer response nobody asked for");
            }
        }
    }

    /// <summary>
    /// The migration that removes it must ship with the change, and must be
    /// re-runnable. Every DME migration in this repository is safe to apply
    /// twice, because they are applied by hand.
    /// </summary>
    [Fact]
    public void TheMigrationThatDropsItExistsAndIsGuarded()
    {
        var sql = Read("Migrations", "Manual", "2026-09-08_DME_Drop_Fake_Eligibility.sql");

        sql.Should().Contain("IF EXISTS (SELECT 1 FROM sys.columns",
            "applying it twice must not fail");

        sql.Should().Contain("sp_executesql",
            "a batch naming a column being dropped is validated at compile time, "
            + "so an IF guard alone does not protect it");

        sql.Should().Contain("RAISERROR",
            "the house template ends by failing loudly if the change did not take");
    }
}
