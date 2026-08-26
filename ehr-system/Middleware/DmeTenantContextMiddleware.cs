using EHR.Services;

namespace EHR.Middleware;

/// <summary>
/// Sends a signed-in user who has no tenant context away from the DME screens
/// with an explanation, instead of letting them hit an exception.
///
/// WHY THIS EXISTS
/// Every DME screen resolves its data through DmeDb, which THROWS when the
/// tenant is unknown. That throw is deliberate and must stay: the row level
/// security predicate treats "no context" as "show everything", so an unscoped
/// connection would quietly return every tenant's rows. Failing loudly is the
/// correct behaviour.
///
/// But DmeDb is constructed by dependency injection when the action is
/// selected, so the throw happens before any action code runs and the caller
/// gets a 500 with no explanation.
///
/// One real user hits this: the Super Admin. Their account belongs to no tenant,
/// because they administer the platform rather than a supplier. They reach a
/// supplier's data by naming the tenant (TenantResolutionMiddleware accepts
/// ?tenantId= or X-Tenant-Id when the token carries no TenantId claim). Landing
/// on /Dme/Settings without one is an easy mistake and a 500 is a terrible way
/// to learn about it.
///
/// This turns that into a redirect to the tenant console, which is where the
/// choice is made.
///
/// WHAT IT IS NOT
/// It is not an authorization check and grants nothing. A user whose TOKEN
/// carries a TenantId keeps it: the claim wins in TenantResolutionMiddleware, so
/// a clinic admin cannot reach another supplier by adding ?tenantId= to the URL.
///
/// WHERE IT SITS
/// After UseTenantResolution, so the tenant has already been resolved from every
/// source, and after UseAuthentication so the identity is known.
/// </summary>
public sealed class DmeTenantContextMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Paths whose controllers resolve data through DmeDb.</summary>
    private static readonly string[] DmePaths = { "/Dme", "/Hcpcs" };

    public DmeTenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        var path = context.Request.Path;
        var isDmePath = DmePaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

        // Anonymous callers are handled by [Authorize], which redirects to sign
        // in. Stepping in here first would send them somewhere they cannot use.
        if (isDmePath
            && context.User.Identity?.IsAuthenticated == true
            && tenantProvider.TenantId == null)
        {
            context.Response.Redirect("/Home/Tenants?needsTenant=1");
            return;
        }

        await _next(context);
    }
}

/// <summary>Registration helper, matching the style of UseTenantResolution.</summary>
public static class DmeTenantContextMiddlewareExtensions
{
    public static IApplicationBuilder UseDmeTenantContext(this IApplicationBuilder app)
        => app.UseMiddleware<DmeTenantContextMiddleware>();
}
