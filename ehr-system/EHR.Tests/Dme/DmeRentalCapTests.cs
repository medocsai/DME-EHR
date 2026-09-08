using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The rental cap, and the double bill.
///
/// WHY THIS IS THE MOST SERIOUS GUARD IN THE PRODUCT
/// A capped rental runs for CapMonths and then the equipment becomes the
/// customer's property. Billing month 14 of a 13 month cap is billing for
/// equipment this supplier no longer owns. That is not a denial, it is a false
/// claim, and the consequence is the loss of a Medicare enrolment rather than a
/// letter asking for the money back.
///
/// Before this, BillNow contained NO cap check of any kind. The Rentals view
/// hid the button, which is presentation and nothing more: a replayed form, a
/// second browser tab, a page left open, or any future API client never runs
/// the page's Razor. The product was one POST away from a false claim.
///
/// Every fact below was also proved by execution against a running app before
/// it was written down: a direct POST to a capped rental wrote nothing, the
/// final month set the rental to ended, and three simultaneous posts produced
/// exactly one claim line.
/// </summary>
public class DmeRentalCapTests
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

    /// <summary>The body of BillNow, from its signature to its final redirect.</summary>
    private static string BillNow()
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"public async Task<IActionResult> BillNow\(.*?DME_RENTAL_BILLED.*?return RedirectToAction\(""Rentals""\);",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty("BillNow should still be there");
        return body;
    }

    // ------------------------------------------------------------- the cap

    /// <summary>
    /// The UPDATE that advances the month must itself refuse to run past the
    /// cap. Not an `if` before it, which a concurrent request can walk straight
    /// past between the read and the write: the condition has to be part of the
    /// write that claims the month.
    /// </summary>
    [Fact]
    public void TheWriteThatAdvancesTheMonthRefusesToPassTheCap()
    {
        var billNow = BillNow();

        billNow.Should().Contain("@cap IS NULL OR @billed < @cap",
            "the cap has to be a condition of the UPDATE, not a check somewhere above it");

        billNow.Should().Contain("SELECT COUNT(*) FROM dbo.DmeClaimLines WHERE RentalId = @id",
            "months billed is counted from the claim lines that exist, inside the same transaction");
    }

    /// <summary>
    /// A rental with no cap is not capped. Oxygen and anything the item master
    /// left blank must keep billing, so a null CapMonths cannot be treated as
    /// zero. Reversing this would silently stop billing everything uncapped.
    /// </summary>
    [Fact]
    public void ARentalWithNoCapKeepsBilling()
    {
        BillNow().Should().Contain("@cap IS NULL OR",
            "a missing cap means no cap, never a cap of zero");
    }

    /// <summary>
    /// And the user is told why, in a sentence. A guard that returns the user to
    /// an unchanged screen is indistinguishable from a button that does nothing,
    /// and the support call that follows costs more than the guard saved.
    /// </summary>
    [Fact]
    public void ARefusedBillingSaysWhyOnTheScreen()
    {
        BillNow().Should().Contain("RentalRefused(",
            "a refusal the user cannot see is a broken button as far as they know");

        Read("Views", "Dme", "Rentals.cshtml").Should().Contain("TempData[\"RentalError\"]",
            "the Rentals screen has to render the refusal it was redirected back with");
    }

    // -------------------------------------------------------- the double bill

    /// <summary>
    /// Two people clicking Bill now, or one person double clicking, used to
    /// produce two claims for the same rental month. The payer denies the second
    /// as a duplicate and a biller loses an afternoon to it.
    ///
    /// The UPDATE is guarded on the NextBillDate that was read, so the second
    /// caller matches no row and writes nothing. Same shape as the void guard on
    /// DmePayments, which is guarded on VoidedAt IS NULL for the same reason.
    /// </summary>
    [Fact]
    public void TheMonthCanOnlyBeClaimedOnce()
    {
        var billNow = BillNow();

        billNow.Should().Contain("NextBillDate IS NULL AND @wasNull = 1",
            "a rental with no next bill date must still be claimable exactly once");

        billNow.Should().Contain("OR NextBillDate = @billedThrough",
            "the date that was read is what the write is guarded on; without it two posts both win");

        billNow.Should().Contain("IF @@ROWCOUNT = 0",
            "losing the race has to be detected, not assumed impossible");
    }

    /// <summary>
    /// The month advance and the claim proving it commit together or not at all.
    /// Split across separate statements there is a window where the rental has
    /// moved on and no claim exists, which loses a month of revenue silently and
    /// leaves MonthsBilled disagreeing with the dates.
    /// </summary>
    [Fact]
    public void TheMonthAdvanceAndItsClaimAreOneTransaction()
    {
        var billNow = BillNow();

        billNow.Should().Contain("BEGIN TRAN");
        billNow.Should().Contain("ROLLBACK");
        billNow.Should().Contain("COMMIT");
        billNow.Should().Contain("SET XACT_ABORT ON",
            "without it a failed statement can leave the transaction open rather than aborting it");
    }

    // ---------------------------------------------------------- ending it

    /// <summary>
    /// The final month ends the rental.
    ///
    /// Leaving it 'active' forever was not cosmetic. The dashboard counted it as
    /// recurring monthly revenue it would never earn again, the schedule drew a
    /// bill-due marker for it every month, and it sat permanently overdue on a
    /// tile with no button able to clear it, because the view correctly hid the
    /// button at the cap.
    /// </summary>
    [Fact]
    public void TheFinalMonthEndsTheRental()
    {
        BillNow().Should().MatchRegex(
            @"Status = CASE WHEN @cap IS NOT NULL AND @billed \+ 1 >= @cap\s*\r?\n?\s*THEN 'ended' ELSE Status END",
            "the rental has to end on the month that reaches the cap, in the same write");
    }

    /// <summary>
    /// 'ended' is reused, not invented. F.StatusChip already knew the word, so
    /// a second spelling for the same state would render as a grey unknown chip
    /// and split every status filter in two.
    /// </summary>
    [Fact]
    public void TheEndedStateReusesTheWordTheChipMapAlreadyKnows()
    {
        Read("Helpers", "DmeDb.cs").Should().Contain("[\"ended\"]",
            "an invented status renders as an unlabelled grey chip");
    }

    /// <summary>
    /// And the screens that mean "still earning" must say active, so an ended
    /// rental drops out of them by itself rather than by a second rule someone
    /// has to remember to add.
    /// </summary>
    [Fact]
    public void TheRecurringRevenueFiguresCountOnlyActiveRentals()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain(
            "var live = rows.Where(r => F.S(r[\"Status\"]) == \"active\").ToList()",
            "monthly recurring revenue must not include rentals that have finished billing");
    }

    // ---------------------------------------------------------- the same 7d bug

    /// <summary>
    /// The Rentals screen carried the same fault the dashboard did: a bare
    /// `&lt;= 7` is true of every negative number, so rentals months late were
    /// counted under a label promising this week.
    /// </summary>
    [Fact]
    public void TheRentalsScreenSeparatesOverdueFromDueThisWeek()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain(
            "ViewBag.Overdue = live.Count(r => (F.DaysUntil(r[\"NextBillDate\"]) ?? 99) < 0)",
            "overdue is its own number on this screen too");

        Read("Views", "Dme", "Rentals.cshtml").Should().NotContain("Due this week",
            "the label promised seven days while the tile counted everything overdue as well");
    }
}
