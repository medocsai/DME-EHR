using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Which branches a user may see, and the one mistake that would undo all of it.
///
/// WHY THIS EXISTS
/// Location started as a working filter: anyone could switch to any branch and
/// the all-branches view showed the whole business. Right for the owner, wrong
/// for the driver at one depot. dbo.UserLocations makes it a real restriction
/// for the roles that should have one.
///
/// THE MISTAKE THESE TESTS EXIST TO PREVENT
/// A restricted user with no grants must see NOTHING. The tempting shape is:
///
///     if (allowed.Any()) { query = query.Where(...); }
///
/// which silently drops the filter for exactly the caller it was meant to
/// contain, so the least configured user gets the most access. RehabDox
/// documents finding that idiom in its own codebase. It must never appear here.
/// </summary>
public class DmeUserLocationScopeTests
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

    private sealed class FakeScope : IDmeLocationScope
    {
        public bool IsUnrestricted { get; init; }
        public IReadOnlyCollection<int> AllowedLocationIds { get; init; } = Array.Empty<int>();
    }

    private static IDmeDb Db(IDmeLocationScope scope, int? chosenLocation = null)
        => new DmeDb(Config(),
                     new FakeTenantProvider { TenantId = 1 },
                     new LocationProvider { LocationId = chosenLocation },
                     scope);

    // ------------------------------------------------- the rule that matters

    /// <summary>
    /// The whole point. No grants means no rows, never every row.
    /// </summary>
    [Fact]
    public void ARestrictedUserWithNoGrantsSeesNothing()
    {
        var db = Db(new FakeScope { IsUnrestricted = false, AllowedLocationIds = Array.Empty<int>() });

        db.LocationScope().Should().Be("(1=0)",
            "a misconfigured user must see nothing rather than everything. If this ever " +
            "returns a predicate that matches rows, the least configured user in the system " +
            "has the most access.");
        db.LocationGrants().Should().Be("(1=0)");
    }

    [Fact]
    public void ARestrictedUserSeesOnlyTheirGrantedBranches()
    {
        var db = Db(new FakeScope { IsUnrestricted = false, AllowedLocationIds = new[] { 3, 4 } });

        db.LocationScope().Should().Be("(1=1 AND LocationId IN (3,4))");
    }

    /// <summary>
    /// Choosing a branch narrows further; it never widens. A restricted user who
    /// selects a branch they were not granted still gets nothing from it,
    /// because both halves have to be satisfied.
    /// </summary>
    [Fact]
    public void ChoosingABranchNarrowsAndNeverWidens()
    {
        var db = Db(new FakeScope { IsUnrestricted = false, AllowedLocationIds = new[] { 3 } }, chosenLocation: 9);

        db.LocationScope().Should().Be("(LocationId = @LocationId AND LocationId IN (3))",
            "the grant clause survives the choice, so selecting a branch outside the grant " +
            "returns nothing rather than that branch's rows");
    }

    [Fact]
    public void AnUnrestrictedUserIsNotNarrowedAtAll()
    {
        var db = Db(new FakeScope { IsUnrestricted = true });

        db.LocationScope().Should().Be("(1=1)");
        db.LocationGrants().Should().Be("(1=1)");
    }

    [Fact]
    public void AnUnrestrictedUserCanStillChooseOneBranch()
    {
        var db = Db(new FakeScope { IsUnrestricted = true }, chosenLocation: 4);

        db.LocationScope().Should().Be("(LocationId = @LocationId)");
    }

    /// <summary>
    /// The branch LIST must not carry the chosen half, or the switcher would
    /// offer only the branch already selected and switching would be impossible.
    /// </summary>
    [Fact]
    public void TheBranchListIgnoresWhichBranchIsCurrentlyChosen()
    {
        var db = Db(new FakeScope { IsUnrestricted = false, AllowedLocationIds = new[] { 3, 4 } }, chosenLocation: 3);

        db.LocationGrants().Should().Be("(LocationId IN (3,4))");
    }

    [Fact]
    public void AJoinedQueryCanQualifyTheColumn()
    {
        var db = Db(new FakeScope { IsUnrestricted = false, AllowedLocationIds = new[] { 2 } }, chosenLocation: 2);

        var sql = db.LocationScope("c.LocationId");

        sql.Should().Be("(c.LocationId = @LocationId AND c.LocationId IN (2))");
        sql.Should().Contain("@LocationId",
            "the PARAMETER name must survive qualification. An earlier draft did this with a " +
            "string replace on the finished predicate, which rewrote @LocationId into " +
            "@c.LocationId and produced SQL that still parsed and matched nothing.");
    }

    // ------------------------------------------------ who bypasses the scoping

    /// <summary>
    /// Roles 0 and 1 bypassing location scoping is a DECISION, not an oversight.
    /// A clinic admin administers the whole supplier and is usually the owner
    /// asking for the all-branches roll-up in the first place.
    ///
    /// This test exists so that changing it has to be deliberate.
    /// </summary>
    [Theory]
    [InlineData(0, true, "Super Admin")]
    [InlineData(1, true, "Clinic Admin, who administers the whole supplier")]
    [InlineData(2, false, "Clinician")]
    [InlineData(3, false, "Front Desk")]
    [InlineData(4, false, "Biller")]
    [InlineData(7, false, "Nurse")]
    public void OnlyAdminRolesBypassBranchScoping(int role, bool expectedUnrestricted, string who)
    {
        var scope = ScopeForRole(role);

        scope.IsUnrestricted.Should().Be(expectedUnrestricted,
            $"{who} is {(expectedUnrestricted ? "not" : "")} scoped by branch");
    }

    /// <summary>
    /// A token with an unreadable role must be treated as the MOST restricted,
    /// not the least. Defaulting to 0 here would hand a bypass to anyone whose
    /// token was shaped slightly differently than expected.
    /// </summary>
    [Fact]
    public void AnUnreadableRoleIsTreatedAsRestricted()
    {
        var scope = ScopeForRole(null);

        scope.IsUnrestricted.Should().BeFalse(
            "failing open on a malformed token would make the bypass reachable by anyone who " +
            "could produce one");
    }

    private static IDmeLocationScope ScopeForRole(int? role)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("UserId", "5")
        };
        if (role.HasValue) claims.Add(new System.Security.Claims.Claim("Role", role.Value.ToString()));

        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "Test"))
        };

        var accessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(ctx);

        return new DmeLocationScope(accessor.Object, Config());
    }

    // ---------------------------------------------------- the shape to avoid

    /// <summary>
    /// A source scan for the fail-open idiom. It is cheap, and the alternative
    /// is trusting that nobody ever writes the natural-looking version.
    /// </summary>
    [Fact]
    public void NoCodeSkipsTheBranchFilterWhenTheGrantSetIsEmpty()
    {
        var offenders = new List<string>();

        foreach (var file in DmeSourceFiles())
        {
            // Comments are stripped first. DmeLocationScope.cs quotes the bad
            // idiom verbatim in order to warn about it, and a scan that cannot
            // tell a warning from the thing it warns about is a scan nobody will
            // keep.
            var text = StripComments(File.ReadAllText(file));

            // if (allowed.Any()) { ...Where... }  and its Count > 0 sibling.
            if (Regex.IsMatch(text, @"if\s*\(\s*\w*[Aa]llowed\w*\s*\.\s*(Any\(\)|Count\s*>\s*0)\s*\)\s*\{?[^}]*(Where|IN \()",
                              RegexOptions.Singleline))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Should().BeEmpty(
            "filtering only when the grant set is non-empty hands the whole tenant to the " +
            "user with no grants, which is the opposite of what the grants are for. " +
            $"Offending files: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Creating a restricted user with no branch produces an account that signs
    /// in and shows nothing, with no message saying why. Refusing is kinder and
    /// is the server's job, not the form's.
    /// </summary>
    [Fact]
    public void CreatingARestrictedUserWithNoBranchIsRefusedOnTheServer()
    {
        var service = File.ReadAllText(
            Path.Combine(ProductionRoot(), "Services", "UserManagementService.cs"));

        service.Should().Contain("Assign at least one location",
            "the rule cannot live only in the browser: a second client or a replayed form " +
            "reaches the service directly");
    }

    /// <summary>
    /// Remove line and block comments so a source scan matches real code only.
    /// Crude on purpose: it does not understand string literals, which is fine
    /// because no SQL string here contains a comment marker.
    /// </summary>
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        source = Regex.Replace(source, @"^[ \t]*//.*$", "", RegexOptions.Multiline);
        return source;
    }

    private static string[] DmeSourceFiles()
    {
        var root = ProductionRoot();
        return new[]
            {
                Path.Combine(root, "Controllers", "DmeController.cs"),
                Path.Combine(root, "Controllers", "LocationController.cs"),
                Path.Combine(root, "Helpers", "DmeDb.cs"),
                Path.Combine(root, "Services", "DmeLocationScope.cs"),
                Path.Combine(root, "Services", "DmePaymentService.cs"),
                Path.Combine(root, "Services", "UserManagementService.cs"),
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
