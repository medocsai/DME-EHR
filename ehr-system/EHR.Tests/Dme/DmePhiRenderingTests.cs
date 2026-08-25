using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Every read of encrypted customer data must be decrypted before it renders.
///
/// WHY THIS EXISTS
/// This bug class shipped twice in one night and neither time did anything
/// throw. Once encryption landed:
///
///   1. vDmeOrders/vDmeRentals concatenated two ciphertexts in SQL, which can
///      never be decrypted, and the Orders, Rentals, Dashboard and Schedule
///      screens rendered base64 where the customer name belonged.
///   2. The New Order customer picker selected FirstName/LastName straight from
///      DmeCustomers without decrypting, so the dropdown listed base64.
///
/// Both returned HTTP 200. Both passed a smoke test that only checked status
/// codes. The only signal was looking at the page.
///
/// So the guard is a source scan: any DmeController action that reads customer
/// name columns must also call the PHI gateway in the same action. It is a
/// heuristic rather than a proof, and it is deliberately cheap, because the
/// alternative is trusting everyone to remember.
/// </summary>
public class DmePhiRenderingTests
{
    /// <summary>
    /// Reads that pull encrypted columns, either from the base table or through
    /// a view that exposes the encrypted parts.
    /// </summary>
    private static readonly Regex ReadsCustomerNames = new(
        @"FROM\s+dbo\.DmeCustomers|CustomerFirstName|FROM\s+dbo\.vDmeOrders|FROM\s+dbo\.vDmeRentals|FROM\s+dbo\.vDmeClaims",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Any call into the PHI gateway counts as handling it.</summary>
    private static readonly Regex CallsPhiGateway = new(
        @"_phi\.(DecryptRow|DecryptRows|ComposeCustomerName|ComposeCustomerNames|Encrypt|IsEncrypted|SearchHash|BuildSearchTokens)",
        RegexOptions.Compiled);

    /// <summary>
    /// Actions that legitimately read those tables without touching names:
    /// existence checks and id lookups that select no PHI column.
    /// </summary>
    private static readonly string[] ExemptActions = { "CreateOrder", "Submit" };

    [Fact]
    public void EveryActionReadingCustomerData_AlsoDecryptsIt()
    {
        var source = File.ReadAllText(ControllerPath());
        var offenders = SplitIntoMethods(source)
            .Where(m => !ExemptActions.Contains(m.name))
            .Where(m => ReadsCustomerNames.IsMatch(m.body))
            .Where(m => !CallsPhiGateway.IsMatch(m.body))
            .Select(m => m.name)
            .ToArray();

        offenders.Should().BeEmpty(
            "these actions read encrypted customer data and never decrypt it, so the page " +
            "renders base64 while still returning HTTP 200. That is invisible to any check " +
            $"that only looks at status codes. Offending actions: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The views must not hand back a pre-joined CustomerName. Two ciphertexts
    /// concatenated in SQL cannot be decrypted by anyone, with any key: the
    /// value is destroyed at the point the database builds it.
    /// </summary>
    [Fact]
    public void TheViewsDoNotConcatenateEncryptedNamesInSql()
    {
        var migration = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Migrations", "Manual", "2026-08-25_DME_Single_Source_Of_Truth.sql"));

        migration.Should().NotMatchRegex(
            @"cu\.FirstName\s*\+.*cu\.LastName",
            "joining encrypted first and last names in SQL produces a value that can never be " +
            "decrypted; the view must return the parts and let the application compose them");
    }

    /// <summary>
    /// Crude method splitter: good enough to attribute a SQL string to the
    /// action it sits in, which is all this test needs.
    /// </summary>
    private static (string name, string body)[] SplitIntoMethods(string source)
    {
        var signature = new Regex(
            @"public\s+(?:async\s+)?(?:Task<)?IActionResult>?\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        var matches = signature.Matches(source).Cast<Match>().ToArray();
        return matches.Select((m, i) =>
        {
            var start = m.Index;
            var end = i + 1 < matches.Length ? matches[i + 1].Index : source.Length;
            return (m.Groups[1].Value, source[start..end]);
        }).ToArray();
    }

    private static string ControllerPath() =>
        Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs");

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
