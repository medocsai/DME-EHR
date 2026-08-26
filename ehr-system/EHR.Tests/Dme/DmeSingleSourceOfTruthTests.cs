using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Derived values must stay derived.
///
/// WHY THIS EXISTS
/// Three DME numbers were stored and trusted, and two had already drifted in
/// the seed data before anyone touched the system:
///
///   DmeRentals.MonthsBilled   stored 3 / 2 / 4   truth was 2 / 0 / 0
///   HcpcsCodes.OnHand         stored 14 for E1390, one unit actually in stock
///   DmeClaims.Total           agreed today, with nothing keeping it so
///
/// The columns are gone and the values now come from vDmeRentals, vHcpcsCatalog
/// and vDmeClaims. The failure mode this test guards is not a crash: it is
/// somebody adding the column back "for performance" or "just to cache it", at
/// which point the two copies start disagreeing again and nobody notices for
/// weeks.
///
/// It is a source scan rather than a database check on purpose, so it runs in
/// CI with no SQL Server and fails at the moment the code is written.
/// </summary>
public class DmeSingleSourceOfTruthTests
{
    /// <summary>
    /// Columns that were removed because they duplicated a fact that lives
    /// elsewhere, paired with where the value legitimately comes from now.
    /// </summary>
    public static TheoryData<string, string, string> BannedWrites => new()
    {
        { "MonthsBilled", "DmeRentals",  "COUNT of DmeClaimLines rows carrying that RentalId (vDmeRentals)" },
        { "OnHand",       "HcpcsCodes",  "SUM of DmeStockMovements.Qty (vHcpcsCatalog)" },
        { "Total",        "DmeClaims",   "SUM of DmeClaimLines.Charge (vDmeClaims)" },
    };

    [Theory]
    [MemberData(nameof(BannedWrites))]
    public void DmeCode_DoesNotWriteRemovedDerivedColumn(string column, string table, string derivedFrom)
    {
        var offenders = DmeSourceFiles()
            .Select(f => new { File = Path.GetFileName(f), Text = File.ReadAllText(f) })
            // Look for the column being written: an INSERT column list or a SET clause.
            .Where(x => Regex.IsMatch(x.Text, $@"(INSERT\s+INTO\s+dbo\.{table}\b[^;]*\b{column}\b)|(UPDATE\s+dbo\.{table}\s+SET[^;""]*\b{column}\s*=)",
                                      RegexOptions.IgnoreCase | RegexOptions.Singleline))
            .Select(x => x.File)
            .ToArray();

        offenders.Should().BeEmpty(
            $"{table}.{column} was removed because it duplicated a fact that already exists. " +
            $"It is now derived from {derivedFrom}. Writing it again reintroduces a second copy " +
            $"that has to be kept in sync by hand, which is exactly how it drifted before. " +
            $"Offending files: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The read models are the point of the exercise: if a screen goes back to
    /// selecting straight from the base tables it silently loses the computed
    /// columns and starts showing blanks or, worse, a stale stored value if one
    /// is ever reintroduced.
    /// </summary>
    [Theory]
    [InlineData("vDmeRentals")]
    [InlineData("vDmeClaims")]
    [InlineData("vDmeOrders")]
    [InlineData("vHcpcsCatalog")]
    [InlineData("vDmePayments")]
    [InlineData("vDmePaymentLines")]
    [InlineData("vDmeClaimLines")]
    public void DmeCode_ReadsThroughTheViews(string view)
    {
        var used = DmeSourceFiles().Any(f => File.ReadAllText(f).Contains(view, StringComparison.OrdinalIgnoreCase));

        used.Should().BeTrue(
            $"{view} exists so the computed values cannot be bypassed. If nothing reads it, " +
            "the screens have gone back to the base tables and the derivation is dead code.");
    }

    /// <summary>
    /// Point-in-time facts are NOT duplication and must stay stored. A claim is
    /// a document as filed: if the customer later changes insurer, the claim
    /// must still show the payer it was actually submitted to. This test exists
    /// so a future cleanup pass does not "helpfully" derive these too.
    /// </summary>
    [Theory]
    [InlineData("PayerName", "the payer a claim was actually submitted to, which must not change when the customer switches insurer")]
    [InlineData("CustomerName", "the name the claim was filed under")]
    public void ClaimKeepsItsPointInTimeFacts(string column, string why)
    {
        var writesIt = DmeSourceFiles()
            .Any(f => Regex.IsMatch(File.ReadAllText(f),
                     $@"INSERT\s+INTO\s+dbo\.DmeClaims\b[^;]*\b{column}\b",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline));

        writesIt.Should().BeTrue(
            $"DmeClaims.{column} is deliberately stored, not derived: it records {why}. " +
            "Deriving it by join would silently rewrite history on every past claim.");
    }

    /// <summary>
    /// The claim's payment outcome is derived in vDmeClaims from the payments
    /// posted against it. DmeClaims.Status owns the submission lifecycle only,
    /// and the database now carries CK_DmeClaims_Status to enforce that.
    ///
    /// This is the source-side half of the same guard, because the failure it
    /// prevents is not a constraint violation somebody notices: it is a well
    /// meaning "let's just mark the claim paid while we are here", which gives
    /// the same question two answers and lets them drift apart the first time a
    /// payment is voided.
    /// </summary>
    [Theory]
    [InlineData("paid")]
    [InlineData("denied")]
    [InlineData("partial")]
    public void DmeCode_NeverStoresAPaymentOutcomeOnTheClaim(string outcome)
    {
        var offenders = DmeSourceFiles()
            .Select(f => new { File = Path.GetFileName(f), Text = File.ReadAllText(f) })
            .Where(x => Regex.IsMatch(x.Text,
                $@"UPDATE\s+dbo\.DmeClaims\s+SET[^;""]*\bStatus\s*=\s*'{outcome}'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            .Select(x => x.File)
            .ToArray();

        offenders.Should().BeEmpty(
            $"'{outcome}' is a fact about the payments posted against the claim, not a workflow " +
            "state anybody sets. vDmeClaims.PaymentStatus computes it, so a stored copy would be " +
            $"a second answer to the same question. Offending files: {string.Join(", ", offenders)}");
    }

    /// <summary>DME production source files (controllers, helpers, services).</summary>
    private static string[] DmeSourceFiles()
    {
        var root = ProductionRoot();
        return new[]
            {
                Path.Combine(root, "Controllers", "DmeController.cs"),
                Path.Combine(root, "Controllers", "HcpcsController.cs"),
                Path.Combine(root, "Helpers", "DmeDb.cs"),
                Path.Combine(root, "Services", "DmePaymentService.cs"),
            }
            .Where(File.Exists)
            .ToArray();
    }

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
