using System.Text.RegularExpressions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// A rental could be started and never stopped. Equipment came back and the
/// rental went on asking to be billed every month, which is a claim for
/// something the customer does not have. It also produced a dead end: a
/// customer with an active rental cannot be archived, and nothing could end
/// the rental, so they could never come off the working list.
/// </summary>
public class DmeRentalEndTests
{
    /// <summary>
    /// Status was a second copy of "EndedAt IS NULL". Two copies of one fact
    /// disagree eventually, and then two screens answer the same question
    /// differently, so the column is gone and the view computes it.
    /// </summary>
    [Fact]
    public void RentalStatusIsComputedFromTheEndDateNotStored()
    {
        var migration = Migration();

        Assert.Contains("DROP COLUMN Status", migration);
        Assert.Contains("CASE WHEN r.EndedAt IS NULL THEN 'active' ELSE 'ended' END AS Status", migration);
    }

    /// <summary>
    /// The cap is Medicare's rule, not a display. Past it the equipment belongs
    /// to the customer; carrying on billing is money clawed back with a penalty.
    /// The screen counted the months and then let you bill anyway.
    /// </summary>
    [Fact]
    public void BillingStopsAtTheCap()
    {
        var body = Method("public async Task<IActionResult> BillNow");

        Assert.Contains("CapReached", body);
        Assert.Contains("month cap", body);
    }

    /// <summary>An ended rental is equipment that has come back.</summary>
    [Fact]
    public void AnEndedRentalCannotBeBilled()
    {
        Assert.Contains("has ended and cannot be billed", Method("public async Task<IActionResult> BillNow"));
    }

    /// <summary>
    /// On-hand is a SUM over the stock ledger, not a counter. Without the
    /// return row the equipment is on the shelf but invisible to the figures,
    /// and the ledger could only ever go down.
    /// </summary>
    [Fact]
    public void ReturnedEquipmentGoesBackOnTheLedgerAndOffTheCustomer()
    {
        var body = Method("public async Task<IActionResult> EndRental");

        Assert.Contains("'return','DmeRental'", body);
        Assert.Contains("SET Status='in-stock', CustomerId=NULL", body);
    }

    /// <summary>
    /// Not everything comes back: written off, lost, or kept by the customer at
    /// the end of a cap. Returning it regardless would inflate on-hand with
    /// equipment nobody has.
    /// </summary>
    [Fact]
    public void EquipmentThatIsNotComingBackDoesNotRejoinStock()
    {
        var body = Method("public async Task<IActionResult> EndRental");

        Assert.Contains("bool backToStock", body);
        Assert.Contains("if (backToStock)", body);
    }

    /// <summary>
    /// Guarded on EndedAt IS NULL so two clicks end it once and the date stays
    /// the first one. Same shape as retiring a distributor or voiding a payment.
    /// </summary>
    [Fact]
    public void EndingNeedsAReasonAndHappensOnce()
    {
        var body = Method("public async Task<IActionResult> EndRental");

        Assert.Contains("Say why the rental is ending", body);
        Assert.Contains("EndedAt IS NULL", body);
        Assert.Contains("DME_RENTAL_ENDED", body);
    }

    /// <summary>
    /// The screen used to recompute the cap in the markup. A screen and a guard
    /// that calculate the same rule separately are a disagreement waiting to
    /// happen, so both now read the one value the view computes.
    /// </summary>
    [Fact]
    public void TheScreenAndTheGuardAgreeOnWhatCappedMeans()
    {
        var view = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Views", "Dme", "Rentals.cshtml"));

        Assert.Contains("F.B(r[\"CapReached\"])", view);
        Assert.DoesNotContain("F.I(r[\"MonthsBilled\"]) >= F.I(r[\"CapMonths\"])", view);
    }

    private static string Method(string signature)
    {
        var c = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Controllers", "DmeController.cs"));
        var start = c.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " not found on DmeController.");
        var end = c.IndexOf("\n    }", start, StringComparison.Ordinal);
        return c[start..(end > 0 ? end : c.Length)];
    }

    private static string Migration() => File.ReadAllText(Path.Combine(
        ProductionRoot(), "Migrations", "Manual", "2026-08-31_DME_Rental_End.sql"));

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
