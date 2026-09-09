using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// What a customer owes.
///
/// WHY IT HAD TO EXIST
///
/// PatientResponsibility was computed per claim line in vDmeClaimLines and
/// rolled up nowhere. No screen in the product answered "who owes us money".
///
/// That is not a reporting nicety. PR money is the patient's share: deductible,
/// coinsurance and copay. A supplier is REQUIRED to make a genuine effort to
/// collect it, because routinely waiving patient responsibility is treated as
/// inducing patients to use you. A supplier cannot make that effort against a
/// number no screen displays.
/// </summary>
public class DmeCustomerBalanceTests
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

    private static string Migration()
        => Read("Migrations", "Manual", "2026-09-09_DME_Customer_Balances.sql");

    private static string Action(string name)
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"public IActionResult " + name + @"\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty($"{name} should still be there");
        return body;
    }

    // ---------------------------------------------------------- the derivation

    /// <summary>
    /// The patient's share is READ from vDmeClaimLines, never re-derived from
    /// the payment lines underneath it.
    ///
    /// vDmeClaimLines already answers "what is the patient responsibility on
    /// this line". A second derivation of the same question is precisely the
    /// drift this codebase keeps removing: two views would disagree the first
    /// time either changed, and neither would be obviously wrong.
    /// </summary>
    [Fact]
    public void ThePatientShareIsReadFromTheViewThatAlreadyOwnsIt()
    {
        var sql = Migration();

        sql.Should().Contain("FROM   dbo.vDmeClaimLines cl",
            "one view owns the per-line answer; this one adds its answers up");

        sql.Should().NotContain("DmePaymentLineAdjustments",
            "re-reading the CAS segments here would be a second derivation of the same fact");
    }

    /// <summary>
    /// Only CUSTOMER money reduces what the customer owes. A payer's cheque
    /// reduces what the PAYER owes. Dropping the Source filter would show every
    /// customer as paid up the moment their insurer settled.
    /// </summary>
    [Fact]
    public void OnlyMoneyFromTheCustomerReducesTheirBalance()
    {
        Migration().Should().Contain("v.Source     = 'customer'",
            "a payer cheque settles the payer's half, not the patient's");
    }

    /// <summary>
    /// Voided receipts fall out on their own because both source views already
    /// exclude them. Asserted so that a future rewrite reaching for the base
    /// tables has to notice it is losing the reversal mechanism.
    /// </summary>
    [Fact]
    public void VoidedMoneyDoesNotCountAsCollected()
    {
        Migration().Should().Contain("v.IsVoided   = 0",
            "a voided receipt was never collected");
    }

    /// <summary>
    /// The branch filter works off the customer, like every other DME read.
    /// Without LocationId on the view, the Payments screen could not narrow the
    /// list to the branch somebody is looking at.
    /// </summary>
    [Fact]
    public void TheBalanceCarriesTheBranchItIsDerivedFrom()
    {
        Migration().Should().Contain("cu.LocationId");
        Action("Payments").Should().Contain("dbo.vDmeCustomerBalances WHERE Balance > 0",
            "the owed list shows people who owe, not everybody");
    }

    // ------------------------------------------------------------------ PHI

    /// <summary>
    /// THE ONE THAT ACTUALLY BROKE, and it rendered on screen before it was
    /// caught.
    ///
    /// The view returns FirstName and LastName separately, because two AES-GCM
    /// ciphertexts joined in SQL cannot be decrypted by anyone with any key. So
    /// the screen calls ComposeCustomerNames. That handles the NAME.
    ///
    /// Phone is PHI as well and is in DmeCustomerPhi.EncryptedColumns, so it
    /// needs DecryptRows too. Without it the chase-them-up column rendered
    /// 56 characters of base64, while the name beside it looked perfectly
    /// normal, which is exactly what made it easy to miss.
    /// </summary>
    [Fact]
    public void TheOwedListDecryptsEveryPhiColumnItShows()
    {
        var payments = Action("Payments");

        payments.Should().Contain("_phi.ComposeCustomerNames(balances)",
            "the two name columns arrive separately and encrypted");

        payments.Should().Contain("_phi.DecryptRows(balances)",
            "Phone is PHI too; composing the name alone leaves it as base64 on the page");
    }

    /// <summary>
    /// And the view must keep the names in two columns. A view that
    /// concatenated them in SQL would hand the app one string that no key can
    /// open, and the screen would render base64 with nothing to fix it.
    /// </summary>
    [Fact]
    public void TheViewKeepsTheNameInTwoSeparateColumns()
    {
        var sql = Migration();

        sql.Should().Contain("cu.FirstName            AS CustomerFirstName");
        sql.Should().Contain("cu.LastName             AS CustomerLastName");
        sql.Should().NotContain("FirstName + ' ' + ",
            "two ciphertexts joined in SQL are not decryptable");
    }

    // ---------------------------------------------------------------- the screen

    /// <summary>
    /// Oldest first. The oldest balance is the one about to become
    /// uncollectable, so it is the one somebody should be phoning about.
    /// </summary>
    [Fact]
    public void TheOwedListLeadsWithTheOldestDebt()
        => Action("Payments").Should().Contain("ORDER BY OldestOutstandingServiceDate");

    /// <summary>
    /// The customer's own screen shows it too. Somebody with the customer on
    /// the phone should not have to go and find a list.
    /// </summary>
    [Fact]
    public void TheCustomerScreenShowsWhatThatCustomerOwes()
    {
        Action("Customer").Should().Contain("dbo.vDmeCustomerBalances WHERE CustomerId=@id");
        Read("Views", "Dme", "Customer.cshtml").Should().Contain("Customer balance");
    }

    /// <summary>
    /// The known limit is written down in the migration rather than left for
    /// somebody to discover.
    ///
    /// PatientResponsibility SUMS across remittances on one claim line, so a
    /// primary followed by a secondary overstates: primary says the patient owes
    /// 36, secondary pays 30 and says they owe 6, and the sum is 42.
    ///
    /// Left as a sum deliberately, because vDmeClaimLines already sums and two
    /// different answers to one question is worse than one answer with a
    /// documented limit. This test exists so the note cannot be deleted quietly
    /// while the behaviour stays.
    /// </summary>
    [Fact]
    public void TheSecondaryPayerLimitIsDocumentedWhereItWillBeFound()
    {
        var sql = Migration();

        sql.Should().Contain("A KNOWN LIMIT");
        sql.Should().Contain("secondary",
            "somebody posting a secondary payment has to know this figure reads high");
    }
}
