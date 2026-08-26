using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EHR.Middleware;
using EHR.Tests.TestHelpers;
using EHR.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// How a Super Admin, who belongs to no tenant, comes to be looking at one
/// supplier's data, and why that cannot be abused.
///
/// WHY THIS EXISTS
/// The header clinic switcher writes a cookie the server reads. That is a
/// request-supplied value which decides whose data is returned, which is
/// exactly the shape of an access control bug. It is safe for one reason and
/// one reason only: it is consulted solely when the caller's token carries no
/// TenantId claim.
///
/// That guard is a single `tenantId == null` in the middleware. Nothing about
/// the code makes its importance obvious, no test elsewhere covers it, and
/// removing it would let any signed-in user read another clinic by editing a
/// cookie in their browser. So it gets its own test with its own explanation.
/// </summary>
public class SuperAdminTenantContextTests
{
    private const string ClinicCookie = "medocs_clinic";

    private static (HttpContext ctx, TenantProvider tenant) Request(
        int? tenantClaim = null, string? cookie = null, string? query = null)
    {
        var ctx = new DefaultHttpContext();

        if (tenantClaim != null)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("TenantId", tenantClaim.Value.ToString()) },
                authenticationType: "Test"));
        }

        if (cookie != null)
            ctx.Request.Headers["Cookie"] = $"{ClinicCookie}={cookie}";

        if (query != null)
            ctx.Request.QueryString = new QueryString($"?tenantId={query}");

        return (ctx, new TenantProvider());
    }

    // -------------------------------------------------- the middleware itself

    /// <summary>
    /// The whole point of the change: a Super Admin picks a clinic and the
    /// server-rendered screens follow.
    /// </summary>
    [Fact]
    public void SuperAdminWithNoTenantClaim_IsScopedByTheClinicCookie()
    {
        var (ctx, tenant) = Request(tenantClaim: null, cookie: "7");

        Resolve(ctx, tenant);

        tenant.TenantId.Should().Be(7);
    }

    /// <summary>
    /// The security property. A clinic admin's token names their tenant, so the
    /// cookie is never reached and forging it achieves nothing.
    /// </summary>
    [Fact]
    public void AUserWhoseTokenNamesATenant_CannotBeMovedByTheCookie()
    {
        var (ctx, tenant) = Request(tenantClaim: 1, cookie: "2");

        Resolve(ctx, tenant);

        tenant.TenantId.Should().Be(1,
            "the token claim is read first and every later source is guarded on the tenant " +
            "still being unknown. If this ever returns 2, any signed-in user can read another " +
            "clinic's patients by editing one cookie in their browser.");
    }

    /// <summary>
    /// A link that names a tenant is an instruction for this request. The cookie
    /// is a preference from some earlier moment. Clicking through to clinic B
    /// must not show clinic A.
    /// </summary>
    [Fact]
    public void AnExplicitLinkBeatsTheStickyCookie()
    {
        var (ctx, tenant) = Request(tenantClaim: null, cookie: "2", query: "5");

        Resolve(ctx, tenant);

        tenant.TenantId.Should().Be(5);
    }

    [Fact]
    public void RubbishInTheCookie_LeavesTheTenantUnknownRatherThanGuessing()
    {
        var (ctx, tenant) = Request(tenantClaim: null, cookie: "not-a-number");

        Resolve(ctx, tenant);

        tenant.TenantId.Should().BeNull(
            "an unparseable value must not fall through to some default tenant");
    }

    [Fact]
    public void NoCookieAndNoClaim_LeavesTheTenantUnknown()
    {
        var (ctx, tenant) = Request();

        Resolve(ctx, tenant);

        tenant.TenantId.Should().BeNull();
    }

    // ------------------------------------------- the redirect instead of a 500

    /// <summary>
    /// DmeDb throws when the tenant is unknown, and it is constructed by
    /// dependency injection before any action code runs, so the caller would
    /// get an unexplained 500. A Super Admin who has not picked a clinic hits
    /// this constantly.
    /// </summary>
    [Fact]
    public async Task ASignedInUserWithNoTenant_IsSentToTheClinicConsole()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "super") }, authenticationType: "Test"));
        ctx.Request.Path = "/Dme/Settings";

        var nextCalled = false;
        var middleware = new DmeTenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(ctx, new TenantProvider { TenantId = null });

        ctx.Response.StatusCode.Should().Be(302);
        ctx.Response.Headers.Location.ToString().Should().Contain("/Home/Tenants");
        nextCalled.Should().BeFalse("the DME controller must not be constructed, because DmeDb would throw");
    }

    [Fact]
    public async Task AUserWhoHasATenant_IsLeftAlone()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("TenantId", "1") }, authenticationType: "Test"));
        ctx.Request.Path = "/Dme/Dashboard";

        var nextCalled = false;
        var middleware = new DmeTenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(ctx, new TenantProvider { TenantId = 1 });

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    /// <summary>
    /// An anonymous caller belongs to [Authorize], which redirects to sign in.
    /// Stepping in first would send them to a console they cannot open.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_IsLeftToTheAuthorizationLayer()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/Dme/Dashboard";

        var nextCalled = false;
        var middleware = new DmeTenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(ctx, new TenantProvider { TenantId = null });

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task NonDmePaths_AreNotTouched()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "super") }, authenticationType: "Test"));
        ctx.Request.Path = "/Home/Tenants";

        var nextCalled = false;
        var middleware = new DmeTenantContextMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(ctx, new TenantProvider { TenantId = null });

        nextCalled.Should().BeTrue("the clinic console is where they are being sent, so it must stay reachable");
    }

    // ---------------------------------------------------- the two ends agree

    /// <summary>
    /// The cookie name is written by JavaScript and read by C#. Nothing compiles
    /// across that boundary, so a rename on one side silently stops the switcher
    /// working and the only symptom is that picking a clinic does nothing.
    /// </summary>
    [Fact]
    public void TheBrowserAndTheServerUseTheSameCookieName()
    {
        var root = ProductionRoot();
        var appJs = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "core", "App.js"));
        var middleware = File.ReadAllText(Path.Combine(root, "Middleware", "TenantResolutionMiddleware.cs"));

        appJs.Should().Contain(ClinicCookie, "App.js writes the clinic selection cookie");
        middleware.Should().Contain(ClinicCookie, "TenantResolutionMiddleware reads it");
    }

    /// <summary>
    /// Runs the REAL TenantResolutionMiddleware.
    ///
    /// An earlier draft of these tests re-implemented the resolution order in
    /// the test itself, which would have proved only that the copy agreed with
    /// itself. The middleware needs an EhrDbContext, so it gets an in-memory
    /// one: the subdomain branch never fires (a DefaultHttpContext host has one
    /// part) and the SESSION_CONTEXT call is already wrapped in a catch, so
    /// neither reaches a real database.
    /// </summary>
    private static void Resolve(HttpContext ctx, ITenantProvider tenant)
    {
        using var db = InMemoryDbFactory.Create();
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        middleware.InvokeAsync(ctx, tenant, new LocationProvider(), db).GetAwaiter().GetResult();
    }

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
