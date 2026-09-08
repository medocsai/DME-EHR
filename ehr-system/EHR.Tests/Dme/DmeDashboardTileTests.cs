using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The dashboard tiles, and the clinical wreckage that was still bolted to the
/// header of every page.
///
/// WHY THESE ARE PINNED
/// A dashboard tile is the one number an owner looks at and then stops looking.
/// Every fault here was silent: the screen rendered, no error appeared, and the
/// figure was simply wrong in the direction that costs money. None of them
/// would have been caught by a test that only asked whether the page returned
/// 200.
/// </summary>
public class DmeDashboardTileTests
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

    /// <summary>
    /// The body of the Dashboard action, from its signature to the last ViewBag
    /// it sets. Scoped deliberately: the same words appear in other actions.
    /// </summary>
    private static string DashboardAction()
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"public IActionResult Dashboard\(.*?ViewBag\.ReadyCount",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty("the Dashboard action should still be there");
        return body;
    }

    // ------------------------------------------------------------- open orders

    /// <summary>
    /// The Recent orders panel reads TOP 6. The Open orders TILE must not be
    /// counted from that list.
    ///
    /// The original code did exactly that: it counted draft and confirmed rows
    /// inside a six row window sorted newest first. Two failures, both silent.
    /// The tile could never exceed 6 however many orders were open, and an
    /// order left open while six newer ones arrived vanished from the count
    /// entirely. On the seeded database there are three orders, so it looked
    /// correct, which is precisely why it survived.
    /// </summary>
    [Fact]
    public void OpenOrdersIsCountedByItsOwnQueryNotFromTheSixRowPanel()
    {
        var dashboard = DashboardAction();

        dashboard.Should().Contain(
            "SELECT COUNT(*) FROM dbo.vDmeOrders WHERE Status IN ('draft','confirmed')",
            "the tile has to ask the database how many orders are open, not count a display list");

        dashboard.Should().NotContain("orders.Count(",
            "counting the TOP 6 panel rows caps the tile at 6 and hides every older open order");
    }

    /// <summary>
    /// The count query must still be branch scoped. A tile that ignores the
    /// location filter reports the whole company to someone who may only be
    /// granted one branch.
    /// </summary>
    [Fact]
    public void TheOpenOrderCountIsStillNarrowedToTheBranchTheCallerCanSee()
    {
        DashboardAction().Should().MatchRegex(
            @"SELECT COUNT\(\*\) FROM dbo\.vDmeOrders WHERE Status IN \('draft','confirmed'\) AND"" \+ LocationFilter",
            "every DME read carries LocationFilter, and a COUNT is a read like any other");
    }

    // ---------------------------------------------------------- due to bill

    /// <summary>
    /// Overdue and due-soon are two different problems and must be counted
    /// separately.
    ///
    /// The original filter was `d.Value &lt;= 7`, which is true for every
    /// negative number, so a rental eighty days late was folded into a tile
    /// labelled "Due to bill (7d)". A supplier three months behind on rental
    /// billing saw a small calm number and nothing telling them otherwise.
    /// Unbilled rental months are the largest quiet loss in a DME business.
    ///
    /// The `&gt;= 0` half is the whole guard. Delete it and the bug is back
    /// with the tile still reading plausibly.
    /// </summary>
    [Fact]
    public void OverdueRentalsAreCountedApartFromRentalsDueInTheNextWeek()
    {
        var dashboard = DashboardAction();

        dashboard.Should().Contain("d.Value < 0",
            "a bill date already in the past is overdue, and has to be counted as its own fact");

        dashboard.Should().Contain("d.Value >= 0 && d.Value <= 7",
            "without the lower bound every overdue rental is swallowed by the seven day window");

        dashboard.Should().Contain("ViewBag.Overdue",
            "the view cannot show what the controller never handed it");
    }

    /// <summary>
    /// And the tile has to actually print the overdue figure. Counting it and
    /// then not rendering it would pass the test above while showing the user
    /// the same reassuring number as before.
    /// </summary>
    [Fact]
    public void TheTileShowsTheOverdueCountRatherThanOnlyTheTotal()
    {
        var view = Read("Views", "Dme", "Dashboard.cshtml");

        view.Should().Contain("@ViewBag.Overdue",
            "the overdue count is the number that changes what the user does next");

        view.Should().NotContain("Due to bill (7d)",
            "the label promised seven days while the tile counted everything overdue too");
    }

    // ------------------------------------------------------- supplier identity

    /// <summary>
    /// No view may name a supplier in its markup.
    ///
    /// The dashboard subtitle read "Lakeview Medical Supply" as a literal, so
    /// all seven tenants on this server were told they were looking at tenant
    /// one's data. Same class of fault as the NPI that used to be hardcoded in
    /// Cms.cshtml, and that one had already been fixed once.
    /// </summary>
    [Fact]
    public void NoViewCarriesASupplierNameAsALiteral()
    {
        var views = Directory.GetFiles(
            Path.Combine(RepoRoot(), "Views"), "*.cshtml", SearchOption.AllDirectories);

        foreach (var file in views)
        {
            File.ReadAllText(file).Should().NotContain("Lakeview Medical Supply",
                $"{Path.GetFileName(file)} would show tenant one's name to every other tenant");
        }
    }

    /// <summary>
    /// And the name that replaced it must come through the row level security
    /// scoped view. Reading dbo.Tenants directly here would return every tenant
    /// on the server, because Tenants is deliberately not in the policy.
    /// </summary>
    [Fact]
    public void TheSupplierNameIsReadThroughTheScopedBillingProviderView()
    {
        DashboardAction().Should().Contain("ViewBag.SupplierName = F.S(BillingProvider()?[\"BillingName\"])",
            "vDmeBillingProvider is scoped by its join to DmeSupplierProfile; dbo.Tenants is not scoped at all");
    }

    // -------------------------------------------------- the clinical leftovers

    /// <summary>
    /// The header carried three dead controls from the clinical EHR that was
    /// removed on 2026-08-25.
    ///
    /// The global search called /api/patients/search, and no PatientsController
    /// exists, so it had failed on every keystroke on every page for months. The
    /// Care Notes bell and the Messages button had no JavaScript behind them at
    /// all: the bell rendered, took a click, and did nothing.
    ///
    /// A dead control is worse than a missing one. The user does not report it,
    /// they conclude the product is broken and stop trusting the parts that work.
    ///
    /// SCOPED TO THE HEADER ON PURPOSE. GlobalBridge.js still holds an orphaned
    /// Collect Payment modal, 51 references calling /api/patients/search and
    /// /api/patients, with no markup left in any view to open it. That is a
    /// separate dead feature and its own piece of work; a blanket ban on the
    /// string here would fail for a reason that has nothing to do with the
    /// header controls this test exists to protect.
    /// </summary>
    [Theory]
    [InlineData("headerPatientSearch", "the global search called a controller that no longer exists")]
    [InlineData("careNotesBtn", "Care Notes is clinical, and had no JavaScript behind it")]
    [InlineData("patientMessagesBtn", "the Messages button was permanently d-none and wired to nothing")]
    [InlineData("Search patients", "this product has customers, not patients")]
    public void TheClinicalHeaderLeftoversStayRemoved(string fragment, string why)
    {
        Read("Views", "Shared", "_Layout.cshtml").Should().NotContain(fragment, why);
        Read("wwwroot", "js", "core", "GlobalBridge.js").Should().NotContain(fragment, why);
    }

    // ------------------------------------------------------------ the dropdown

    /// <summary>
    /// A select paints its chevron as a background image inside its own right
    /// padding. styles.css sets `padding` as a SHORTHAND on
    /// `.form-control, .form-select` in two separate blocks, and each one wipes
    /// that space, so any option long enough to fill the control rendered its
    /// last characters underneath the arrow. Every dropdown in the app, not
    /// just the dashboard month picker where it was noticed.
    ///
    /// THE ORDERING IS THE POINT. The corrective rule has the same specificity
    /// as the blocks it corrects, so the later one wins. Placed above them it
    /// compiles, ships, and silently does nothing, which is how the first
    /// attempt at this fix behaved.
    /// </summary>
    [Fact]
    public void TheSelectArrowRuleComesAfterEveryPaddingShorthandThatWouldUndoIt()
    {
        var css = Read("wwwroot", "css", "styles.css");

        var lastShorthand = css.LastIndexOf(".form-control, .form-select {", StringComparison.Ordinal);
        lastShorthand.Should().BeGreaterThan(-1, "the shared control styling should still be there");

        var arrowRule = css.LastIndexOf("padding-right: 2.25rem;", StringComparison.Ordinal);
        arrowRule.Should().BeGreaterThan(-1,
            "a select needs reserved room for the chevron it draws inside its own padding");

        arrowRule.Should().BeGreaterThan(lastShorthand,
            "same specificity means the later rule wins; placed first this fix does nothing at all");
    }
}
