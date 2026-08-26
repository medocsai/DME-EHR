using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// A DME supplier with several branches is one business. Location separates the
/// working data; the tenant is what actually keeps suppliers apart.
///
/// WHY THIS EXISTS
/// The obvious design is a LocationId on every table, and RehabDox already ran
/// that experiment on live data:
///
///   Appointments.LocationId    NULL on 164 of 165 rows
///   Patients                   no LocationId at all, only PreferredLocationId
///
/// The copy on the child row drifted to NULL, so the filter had to become a
/// three-way fallback repeated in eight or more places. Where they derive from
/// the patient instead, there is no fallback and no bug.
///
/// So here the branch is stored ONCE, on the customer, plus separately on
/// inventory because stock is physical and belongs to no customer. These tests
/// pin both halves of that, and the reason location is deliberately NOT a
/// security boundary.
/// </summary>
public class DmeLocationScopingTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=Test;Trusted_Connection=True;"
        }).Build();

    private sealed class FakeTenantProvider : ITenantProvider
    {
        public int? TenantId { get; set; } = 1;
        public string? TenantSubdomain { get; set; }
    }

    // ------------------------------------------------- location is not a wall

    /// <summary>
    /// The asymmetry that makes the whole design work. A missing TENANT throws,
    /// because the row level security predicate treats "no context" as "show
    /// everything" and an unscoped connection would leak across suppliers. A
    /// missing LOCATION is simply every branch, which is the owner's roll-up.
    /// </summary>
    [Fact]
    public void NoLocationMeansEveryBranch_WhileNoTenantStillThrows()
    {
        var db = new DmeDb(Config(), new FakeTenantProvider { TenantId = 1 }, new LocationProvider { LocationId = null });

        db.LocationId.Should().BeNull(
            "an absent location is the all-branches view, not an error. The owner of a " +
            "three-depot supplier needs the whole business in one number.");

        var noTenant = () => new DmeDb(Config(), new FakeTenantProvider { TenantId = null }, new LocationProvider());
        noTenant.Should().Throw<InvalidOperationException>(
            "the tenant is the security boundary and its absence must never be silently tolerated");
    }

    [Fact]
    public void AChosenBranchIsCarriedOntoEveryQuery()
    {
        var db = new DmeDb(Config(), new FakeTenantProvider { TenantId = 1 }, new LocationProvider { LocationId = 4 });

        db.LocationId.Should().Be(4);
    }

    /// <summary>
    /// Location must never become a row level security predicate. If it did, the
    /// all-branches roll-up would need a hole punched through the isolation to
    /// work, and holes punched through isolation are how tenants start seeing
    /// each other.
    /// </summary>
    [Fact]
    public void LocationIsNeverAddedToTheTenantIsolationPolicy()
    {
        var offenders = MigrationFiles()
            .Select(f => new { File = Path.GetFileName(f), Text = File.ReadAllText(f) })
            .Where(x => Regex.IsMatch(x.Text,
                @"PREDICATE\s+dbo\.fn_\w*Predicate\s*\(\s*LocationId",
                RegexOptions.IgnoreCase))
            .Select(x => x.File)
            .ToArray();

        offenders.Should().BeEmpty(
            "the tenant is the wall and the location is a filter inside it. A location " +
            "predicate in the security policy would make the owner's all-branches view " +
            $"impossible without weakening tenant isolation. Offending files: {string.Join(", ", offenders)}");
    }

    // ------------------------------------------- stored once, derived elsewhere

    /// <summary>
    /// The lesson from the reference product, as a ratchet. Only the customer
    /// and the two inventory tables may carry a LocationId column. Anything else
    /// is a second copy of a fact, and second copies drift to NULL.
    /// </summary>
    [Theory]
    [InlineData("DmeOrders")]
    [InlineData("DmeClaims")]
    [InlineData("DmeClaimLines")]
    [InlineData("DmeRentals")]
    [InlineData("DmePayments")]
    [InlineData("DmePaymentLines")]
    [InlineData("DmeCmns")]
    [InlineData("DmeCustomerInsurances")]
    public void NoLocationIdIsAddedToATableThatCanDeriveOne(string table)
    {
        var offenders = MigrationFiles()
            .Select(f => new { File = Path.GetFileName(f), Text = File.ReadAllText(f) })
            .Where(x => Regex.IsMatch(x.Text,
                $@"ALTER\s+TABLE\s+dbo\.{table}\s+ADD\s+LocationId",
                RegexOptions.IgnoreCase))
            .Select(x => x.File)
            .ToArray();

        offenders.Should().BeEmpty(
            $"{table} reaches its branch through the customer. Storing a copy is exactly what " +
            "put NULL on 164 of 165 rows in the reference product and forced a fallback chain " +
            $"into eight query sites. Offending files: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The three tables that own a location must actually be given one on write.
    ///
    /// Asserted against the INSERTs rather than the migration, because the
    /// inventory columns are added through sp_executesql inside a cursor and no
    /// literal ALTER statement exists to match. Behaviour is the better target
    /// anyway: a column nothing populates is not a stored fact, and NOT NULL
    /// would turn every delivery into an error the first time it was missed.
    /// </summary>
    [Theory]
    [InlineData("DmeCustomers", "a customer is filed to a branch when they are created")]
    [InlineData("DmeSerializedUnits", "a delivered unit leaves from a specific depot")]
    [InlineData("DmeStockMovements", "stock physically moves out of one branch, not the company")]
    public void EveryInsertIntoALocationOwningTableSuppliesOne(string table, string why)
    {
        var controller = File.ReadAllText(Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        var inserts = Regex.Matches(controller,
            $@"INSERT\s+INTO\s+dbo\.{table}\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        inserts.Should().NotBeEmpty($"DmeController writes {table}");

        foreach (System.Text.RegularExpressions.Match insert in inserts)
        {
            insert.Groups[1].Value.Should().Contain("LocationId",
                $"{table} stores its own branch: {why}");
        }
    }

    // ------------------------------------------------------- the query pattern

    /// <summary>
    /// Every location filter has to carry the "or all of them" half. A bare
    /// `LocationId = @LocationId` would silently return nothing at all for a
    /// user viewing the whole business, because NULL never equals anything.
    /// </summary>
    [Fact]
    public void EveryLocationFilterAllowsTheAllBranchesCase()
    {
        var offenders = new List<string>();

        foreach (var file in DmeSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"[\w\.]*LocationId\s*=\s*@LocationId"))
            {
                // Look back far enough to cover the whole enclosing condition.
                // The inventory query guards with a CASE several lines above the
                // comparison, so a tight window reports it as an offender when
                // it is not.
                var start = Math.Max(0, m.Index - 400);
                var window = text.Substring(start, Math.Min(m.Index - start + m.Length + 20, text.Length - start));
                if (!window.Contains("@LocationId IS NULL"))
                    offenders.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        }

        offenders.Should().BeEmpty(
            "a filter written as LocationId = @LocationId returns NOTHING when the parameter is " +
            "null, so the all-branches view would show an empty product rather than the whole " +
            $"business. Offending sites: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// A branch posted on a form is a request-supplied value that decides where
    /// a record lands, so it has to be checked against the caller's tenant.
    /// Row level security covers TenantId on the insert and would happily accept
    /// a foreign LocationId sitting beside it.
    /// </summary>
    [Fact]
    public void ABranchSuppliedByAFormIsCheckedAgainstTheTenant()
    {
        var controller = File.ReadAllText(Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        controller.Should().MatchRegex(
            @"FROM\s+dbo\.Locations\s+WHERE\s+LocationId=@locationId\s+AND\s+TenantId=@TenantId",
            "CreateCustomer must confirm the posted branch belongs to this tenant before " +
            "filing a customer into it");
    }

    /// <summary>
    /// ...and then the CHECKED value has to be the one that is written.
    ///
    /// DmeDb puts an @LocationId on every command, holding the branch the caller
    /// is VIEWING. SQL parameter names are case insensitive, so an insert that
    /// writes @locationId is naming that same injected parameter. The customer
    /// insert did exactly that and passed no value of its own, so:
    ///
    ///   viewing Houston, filing into Dallas -> the customer landed in Houston
    ///   viewing ALL branches                -> NULL, and the save 500'd
    ///
    /// Found by the verification script on 2026-08-27. The branch picker on the
    /// form was decorative, and nothing said so.
    /// </summary>
    [Fact]
    public void TheBranchThatWasCheckedIsTheBranchThatIsWritten()
    {
        var controller = File.ReadAllText(Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        var insert = Regex.Match(controller,
            @"INSERT INTO dbo\.DmeCustomers.*?\}\)\);",
            RegexOptions.Singleline).Value;

        insert.Should().NotBeEmpty("the customer insert should still be there");

        // Comments explain the trap by naming it, so strip them: the assertion
        // is about the code, not about the prose warning against it.
        var code = Regex.Replace(insert, @"//.*$", "", RegexOptions.Multiline);

        code.Should().NotContain("@locationId",
            "that is the same parameter DmeDb injects for the branch being VIEWED, " +
            "so the chosen branch is silently discarded");

        code.Should().Contain("branchId = Convert.ToInt32(branch)",
            "the value written must be the one that was checked against the tenant");
    }

    private static string[] MigrationFiles()
    {
        var dir = Path.Combine(ProductionRoot(), "Migrations", "Manual");
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.sql") : Array.Empty<string>();
    }

    private static string[] DmeSourceFiles()
    {
        var root = ProductionRoot();
        return new[]
            {
                Path.Combine(root, "Controllers", "DmeController.cs"),
                Path.Combine(root, "Controllers", "HcpcsController.cs"),
                Path.Combine(root, "Helpers", "DmeDb.cs"),
                Path.Combine(root, "Services", "DmePaymentService.cs"),
                Path.Combine(root, "Services", "DmeSftpAccountService.cs"),
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
