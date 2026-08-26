using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Services;

namespace EHR.Middleware;

/// <summary>
/// Middleware that resolves tenant and location context from various sources.
/// Location context is resolved after tenant context and provides sub-filtering within a tenant.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider, ILocationProvider locationProvider, EhrDbContext dbContext)
    {
        // Try to resolve tenant from various sources
        int? tenantId = null;
        string? subdomain = null;

        // ===== TENANT RESOLUTION =====

        // 1. From JWT claims (highest priority)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("TenantId");
            if (tenantClaim != null && int.TryParse(tenantClaim.Value, out var tid))
            {
                tenantId = tid;
            }

            var subdomainClaim = context.User.FindFirst("TenantSubdomain");
            if (subdomainClaim != null)
            {
                subdomain = subdomainClaim.Value;
            }
        }

        // 2. From header (for API calls)
        if (tenantId == null && context.Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenantId))
        {
            if (int.TryParse(headerTenantId.FirstOrDefault(), out var tid))
            {
                tenantId = tid;
            }
        }

        // 3. From subdomain
        if (tenantId == null)
        {
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length >= 2 && parts[0] != "www" && parts[0] != "api")
            {
                subdomain = parts[0];
                var tenant = dbContext.Tenants.FirstOrDefault(t => t.Subdomain == subdomain);
                if (tenant != null && (tenant.IsDeleted == null || tenant.IsDeleted == false))
                {
                    tenantId = tenant.TenantId;
                }
            }
        }

        // 4. From query string (for development/testing)
        if (tenantId == null && context.Request.Query.TryGetValue("tenantId", out var queryTenantId))
        {
            if (int.TryParse(queryTenantId.FirstOrDefault(), out var tid))
            {
                tenantId = tid;
            }
        }

        // 5. From the clinic the Super Admin picked in the header switcher.
        //
        // WHY THIS EXISTS
        // The switcher used to write only localStorage and raise a JavaScript
        // event, which the SPA modules listen for. Every DME screen is a server
        // rendered Razor page and sees neither, so switching clinic did nothing
        // to them and the only way in was a ?tenantId= link.
        //
        // WHY IT IS LAST
        // It is a sticky preference and must lose to anything more specific. A
        // link that names a tenant (step 4) is an explicit instruction for THIS
        // request; a cookie is what was chosen at some point in the past.
        // Reversing them would mean clicking through to clinic B and being shown
        // clinic A because the cookie still said so.
        //
        // WHY IT GRANTS NOTHING
        // Every step here is guarded on tenantId still being null, and step 1
        // fills it from the token whenever the caller HAS a tenant. So this is
        // only ever reached by an identity with no TenantId claim, which is
        // Super Admin. A clinic admin who forges this cookie changes nothing:
        // their claim already won three steps ago.
        if (tenantId == null && context.Request.Cookies.TryGetValue("medocs_clinic", out var cookieTenantId))
        {
            if (int.TryParse(cookieTenantId, out var tid))
            {
                tenantId = tid;
            }
        }

        // Set tenant context
        tenantProvider.TenantId = tenantId;
        tenantProvider.TenantSubdomain = subdomain;

        // Add to HttpContext items for easy access
        context.Items["TenantId"] = tenantId;
        context.Items["TenantSubdomain"] = subdomain;

        // ===== LOCATION RESOLUTION (Multi-Location Support) =====
        // Location is resolved within the tenant context
        int? locationId = null;
        string? locationName = null;

        // 1. From JWT claims (highest priority - set during login or location switch)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var locationIdClaim = context.User.FindFirst("LocationId");
            if (locationIdClaim != null && int.TryParse(locationIdClaim.Value, out var lid))
            {
                // Validate that the location is still active
                // This handles the case where a location was deactivated after the user logged in
                if (tenantId.HasValue)
                {
                    var location = dbContext.Locations.FirstOrDefault(l =>
                        l.LocationId == lid &&
                        l.TenantId == tenantId.Value &&
                        l.IsActive == true);

                    if (location != null)
                    {
                        locationId = lid;
                        locationName = location.Name;
                    }
                    // If location is inactive or not found, locationId remains null
                    // The user will need to switch to an active location
                }
                else
                {
                    // For super admin without tenant, trust the JWT claim
                    locationId = lid;
                    var locationNameClaim = context.User.FindFirst("LocationName");
                    if (locationNameClaim != null)
                    {
                        locationName = locationNameClaim.Value;
                    }
                }
            }
        }

        // 2. From header (for API calls - allows overriding location without re-authentication)
        if (context.Request.Headers.TryGetValue("X-Location-Id", out var headerLocationId))
        {
            if (int.TryParse(headerLocationId.FirstOrDefault(), out var lid))
            {
                // Validate that the location belongs to the tenant
                if (tenantId.HasValue)
                {
                    var location = dbContext.Locations.FirstOrDefault(l =>
                        l.LocationId == lid &&
                        l.TenantId == tenantId.Value &&
                        l.IsActive == true);

                    if (location != null)
                    {
                        locationId = lid;
                        locationName = location.Name;
                    }
                }
            }
        }

        // 3. From query string (for development/testing)
        if (locationId == null && context.Request.Query.TryGetValue("locationId", out var queryLocationId))
        {
            if (int.TryParse(queryLocationId.FirstOrDefault(), out var lid))
            {
                // Validate that the location belongs to the tenant
                if (tenantId.HasValue)
                {
                    var location = dbContext.Locations.FirstOrDefault(l =>
                        l.LocationId == lid &&
                        l.TenantId == tenantId.Value &&
                        l.IsActive == true);

                    if (location != null)
                    {
                        locationId = lid;
                        locationName = location.Name;
                    }
                }
            }
        }

        // Set location context
        locationProvider.LocationId = locationId;
        locationProvider.LocationName = locationName;

        // Add to HttpContext items for easy access
        context.Items["LocationId"] = locationId;
        context.Items["LocationName"] = locationName;

        // Set SQL Server SESSION_CONTEXT('CurrentTenantId') for the
        // RowLevelSecurity policy (F2). Always run — the policy may be in
        // STATE = OFF, but having the context set means enabling the policy
        // is a single ALTER SECURITY POLICY statement with no code change.
        // Skips on tenant=null (Super Admin / unauth) so the policy's
        // IS NULL fallback fires (rows visible).
        if (tenantId.HasValue)
        {
            try
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC sp_set_session_context N'CurrentTenantId', {tenantId.Value}");
            }
            catch
            {
                // Don't break the request if the session context call fails
                // (DB issue, transient connection). Log the issue separately.
            }
        }

        await _next(context);
    }
}

public static class TenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantResolutionMiddleware>();
    }
}
