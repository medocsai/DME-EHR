using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// One cheque, several claims.
///
/// THE COMPLAINT
///
/// A payer settling twelve months of a rental sends ONE cheque. Posting that as
/// twelve receipts means typing the cheque number twelve times, twelve rows to
/// reconcile against one line on the bank statement, and no way to see that
/// forty dollars of it was never allocated, because "unapplied" is a property
/// of the cheque and the cheque had been cut into twelve pieces.
///
/// AND THE ANSWER WAS THAT NO TABLE WAS NEEDED
///
/// RehabDox solves this with a PaymentBatches header. DME does not need one:
/// dbo.DmePayments has NO ClaimId and its CustomerId is NULLABLE, so a receipt
/// has always been able to carry lines from any number of claims and any number
/// of customers. DmePaymentService validates claim lines one at a time and
/// checks the applied total against the receipt; it never assumed one claim.
/// CreatePayment already took a flat array of claim line ids.
///
/// The only claim-scoped thing in the whole path was the GET on the posting
/// screen. So the fix is a screen change, and the output of it is a table that
/// was never created.
///
/// PROVED BY EXECUTION before any of this was written down: a receipt of 50
/// dollars posted 25 against a line on CLM-02001 and 25 against a line on
/// CLM-02002, producing ONE row in DmePayments with TWO lines pointing at two
/// different claims.
/// </summary>
public class DmePaymentBatchTests
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

    private static string Action(string signature)
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            Regex.Escape(signature) + @"\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty($"{signature} should still be there");
        return body;
    }

    // ------------------------------------------------------- no new table

    /// <summary>
    /// The receipt header stays free of a claim. The moment DmePayments carries
    /// a ClaimId, a cheque can only ever settle one claim again and the whole
    /// thing is undone.
    /// </summary>
    [Fact]
    public void TheReceiptHeaderIsNotTiedToAClaim()
    {
        var service = Read("Services", "DmePaymentService.cs");

        var insert = Regex.Match(service,
            @"INSERT INTO dbo\.DmePayments[\s\S]{0,400}?\)", RegexOptions.Singleline).Value;

        insert.Should().NotBeEmpty("the receipt insert should still be there");
        insert.Should().NotContain("ClaimId",
            "a receipt is a cheque; a cheque does not belong to one claim");
    }

    /// <summary>
    /// And no PaymentBatches table was imported. The existing header IS the
    /// batch, and a second header above it would be two rows describing one
    /// cheque, which is the duplication this codebase keeps deleting.
    /// </summary>
    [Fact]
    public void NoSecondHeaderTableWasIntroduced()
    {
        var migrations = Directory.GetFiles(
            Path.Combine(RepoRoot(), "Migrations", "Manual"), "*.sql");

        foreach (var file in migrations)
            File.ReadAllText(file).Should().NotContain("DmePaymentBatches",
                "dbo.DmePayments already carries no ClaimId, so it is the batch header");
    }

    // -------------------------------------------------------- the screen

    /// <summary>
    /// The posting screen takes a LIST of claims, carried in the querystring.
    ///
    /// Stateless on purpose: a half-built receipt held in session or in a draft
    /// table is state somebody has to clean up when a tab is closed, and the URL
    /// already survives a refresh.
    /// </summary>
    [Fact]
    public void ThePostingScreenAcceptsSeveralClaims()
    {
        var get = Action("public IActionResult PostPayment");

        get.Should().Contain("string? also = null",
            "the extra claims travel in the querystring");
        get.Should().Contain("FROM dbo.vDmeClaimLines WHERE ClaimId IN (",
            "the form has to render lines from every claim on the receipt");
    }

    /// <summary>
    /// The extra claims are read through the same scoped view as everything
    /// else, so a claim id belonging to another supplier does not come back and
    /// never reaches the form. RLS is the guard; this is the read that uses it.
    /// </summary>
    [Fact]
    public void AClaimFromAnotherSupplierCannotBeAddedToAReceipt()
        => Action("public IActionResult PostPayment")
            .Should().Contain("FROM dbo.vDmeClaims WHERE ClaimId IN (",
                "the view is row level security scoped; the base table is not");

    /// <summary>
    /// A refused posting comes back with the receipt still assembled. Losing
    /// twelve added claims because one amount was wrong is the kind of thing
    /// that makes somebody go back to posting twelve receipts.
    /// </summary>
    [Fact]
    public void ARefusedPostingKeepsTheClaimsThatWereAdded()
    {
        var post = Action("public async Task<IActionResult> CreatePayment");

        post.Should().Contain("string? also",
            "the list has to survive the round trip");

        // EVERY failure path, not just one of them.
        //
        // There are two ways this action refuses: a receipt spanning two
        // customers, and anything DmePaymentService rejects. An earlier version
        // of this test asserted only that the string appeared SOMEWHERE, so
        // dropping it from one path still passed. A window-based regex was
        // worse: it ran past the correct redirect into the success one and
        // failed on correct code.
        //
        // Counted instead. Two refusals keep the receipt, one success does not
        // need to, and any mutation moves one of the numbers.
        var keeps = Regex.Matches(post,
            @"RedirectToAction\(""PostPayment"", new \{ id = claimId, also \}\)").Count;

        var loses = Regex.Matches(post,
            @"RedirectToAction\(""PostPayment"", new \{ id = claimId \}\)").Count;

        keeps.Should().Be(2,
            "both refusal paths must come back with the receipt still assembled");

        loses.Should().Be(1,
            "only the success path may drop the list, because the receipt is posted by then");
    }

    // ------------------------------------------------------- whose money

    /// <summary>
    /// THE ONE THING A MULTI-CLAIM RECEIPT ACTUALLY CHANGES.
    ///
    /// The customer on the header used to be read from the claim the screen
    /// opened on. With several claims that is the wrong customer as soon as the
    /// receipt covers somebody else, so it is derived from the LINES being paid.
    /// </summary>
    [Fact]
    public void TheCustomerIsDerivedFromTheLinesBeingPaidNotTheScreenYouOpenedOn()
    {
        var post = Action("public async Task<IActionResult> CreatePayment");

        post.Should().Contain("SELECT DISTINCT c.CustomerId FROM dbo.DmeClaims c",
            "whose money it is comes from what is being settled");

        post.Should().NotContain(
            "\"SELECT CustomerId FROM dbo.DmeClaims WHERE ClaimId=@claimId\"",
            "reading it from the opened claim names the wrong customer on a wider receipt");
    }

    /// <summary>
    /// A patient pays their OWN bills. One customer cheque covering two
    /// different people is not a thing, and guessing which of them to write on
    /// the receipt would misfile the money against somebody else's balance,
    /// which is now a number on a screen somebody chases.
    /// </summary>
    [Fact]
    public void ACustomerReceiptCoveringTwoCustomersIsRefused()
    {
        var post = Action("public async Task<IActionResult> CreatePayment");

        post.Should().Contain("if (owners.Count > 1)");
        post.Should().Contain("more than one customer",
            "and it says so, rather than silently picking one");
    }

    /// <summary>
    /// A PAYER cheque leaves CustomerId null and always has. One remittance from
    /// Medicare routinely settles claims for many different people, and naming
    /// one of them on the header would be picking a name at random.
    /// </summary>
    [Fact]
    public void APayerReceiptNamesNoCustomer()
    {
        var post = Action("public async Task<IActionResult> CreatePayment");

        post.Should().Contain("int? customerId = null;");
        post.Should().Contain("if (source == \"customer\" && lines.Count > 0)",
            "only a customer receipt gets a customer");
    }

    // -------------------------------------------------------- the money rules

    /// <summary>
    /// The service still refuses to apply more than the cheque was worth. That
    /// check is what makes a batch safe: twelve claims cannot quietly consume
    /// more than the one cheque covering them.
    /// </summary>
    [Fact]
    public void MoreCannotBeAppliedThanTheChequeWasWorth()
        => Read("Services", "DmePaymentService.cs")
            .Should().Contain("if (applied > input.Amount)",
                "the whole point of a batch is that the parts add up to the cheque");
}
