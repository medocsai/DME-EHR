using System.Text;
using EHR.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EHR.Configuration;

/// <summary>
/// Builds the JWT validation used by BOTH authentication schemes.
///
/// WHY THIS EXISTS
/// The app authenticates two different callers with the same token:
///   - "Bearer"        the SPA, token in the Authorization header
///   - "SessionCookie" server-rendered Razor pages, token in an HttpOnly cookie
/// Both must apply identical rules. If the cookie path skipped the TokenVersion
/// check, logout and password reset would revoke the SPA session but silently
/// leave the page session alive. Duplicating the handler is how that drift
/// happens, so there is exactly one copy here and both schemes call it.
///
/// WHO CALLS IT
/// Program.cs, once per scheme.
/// </summary>
public static class JwtBearerSetup
{
    /// <summary>
    /// Apply the shared signing/lifetime parameters and the revocation check.
    /// <paramref name="tokenFromCookie"/> switches where the raw token is read
    /// from; everything after that point is identical for both schemes.
    /// </summary>
    public static void Configure(JwtBearerOptions options, IConfiguration config, bool tokenFromCookie)
    {
        var jwtKey = config["Jwt:Key"] ?? "YourSecretKeyHere12345678901234567890";
        var key = Encoding.UTF8.GetBytes(jwtKey);

        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"] ?? "IMEHR",
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"] ?? "IMEHRUsers",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (tokenFromCookie)
                {
                    // Page navigations carry no Authorization header. Take the
                    // token from the HttpOnly cookie instead.
                    var cookieToken = SessionCookie.Read(context.Request);
                    if (!string.IsNullOrEmpty(cookieToken)) context.Token = cookieToken;
                    return Task.CompletedTask;
                }

                // SignalR cannot set headers on the websocket handshake, so the
                // hub endpoints pass the token as a query string parameter.
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },

            OnTokenValidated = ValidateNotRevokedAsync
        };
    }

    /// <summary>
    /// Runs AFTER signature/lifetime checks pass and BEFORE the request reaches
    /// MVC. Enforces token-version invalidation: bumping User.TokenVersion
    /// (logout, password change, password reset) immediately fails every
    /// existing token for that user without waiting for the natural expiry.
    /// </summary>
    private static async Task ValidateNotRevokedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal == null) { context.Fail("No principal."); return; }

        // Patient portal tokens carry a "PatientId" claim instead of "UserId"
        // and have no "tv" version (their backing record lives in
        // PatientPortalAccounts, not Users). Their session is enforced
        // separately via PatientPortalAccounts.IsActive / FailedLoginAttempts /
        // LockoutEndAt at login time, plus the JWT's 4-hour expiry. Skip the
        // User-table TokenVersion check for these tokens; applying it would
        // 401 every portal request and bounce the patient back to login.
        if (principal.FindFirst("PatientId") != null) return;

        var userIdStr = principal.FindFirst("UserId")?.Value
                        ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var tvStr = principal.FindFirst("tv")?.Value;

        if (!int.TryParse(userIdStr, out var uid) || !int.TryParse(tvStr, out var tv))
        {
            // Old tokens issued before the version claim shipped have no "tv" —
            // fail them so users re-login. The absence of "tv" is itself an
            // invalidation signal.
            context.Fail("Token missing required version claim.");
            return;
        }

        // Resolve EhrDbContext from the request scope (do not capture from the
        // root provider — DbContext is scoped).
        var db = context.HttpContext.RequestServices
            .GetRequiredService<EHR.Models.Generated.EhrDbContext>();
        var current = await db.Users
            .Where(u => u.UserId == uid)
            .Select(u => new { u.TokenVersion, u.IsActive })
            .FirstOrDefaultAsync();

        if (current == null || current.IsActive != true)
        {
            context.Fail("User not found or inactive.");
            return;
        }
        if (current.TokenVersion != tv)
        {
            context.Fail("Token has been revoked (version mismatch).");
        }
    }
}
