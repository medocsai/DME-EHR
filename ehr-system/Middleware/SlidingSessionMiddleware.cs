using System.Security.Claims;
using EHR.Helpers;
using EHR.Services;

namespace EHR.Middleware;

/// <summary>
/// Turns the session timeout into an IDLE timeout.
///
/// HIPAA:SessionTimeoutMinutes was being applied as a fixed run from sign in:
/// the token was minted once with that lifetime and nothing ever renewed it, so
/// a biller posting claims without pause was signed out mid-sentence exactly as
/// fast as an unattended browser. That is stricter than the rule it was written
/// for, which asks for automatic logoff after INACTIVITY, and it produced the
/// worst possible symptom: the page still looked signed in while every request
/// underneath it came back 401.
///
/// Now each authenticated request pushes the deadline back. Stop working for the
/// timeout and the session dies, which is the control that was intended.
///
/// Renewal is not on every request. It happens once the token is past halfway,
/// so a busy screen does not re-sign a JWT per keystroke of a typeahead.
/// </summary>
public class SlidingSessionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// The SPA keeps its own copy in localStorage and sends it as a Bearer
    /// header, and an explicit Authorization header always beats the cookie. So
    /// renewing only the cookie would leave the SPA half of the product expiring
    /// on the old clock. The header is how the new token gets back to it.
    /// </summary>
    public const string RenewedTokenHeader = "X-Session-Token";

    public SlidingSessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAuthService auth)
    {
        if (ShouldRenew(context, out var remaining))
        {
            try
            {
                var token = auth.RenewToken(context.User);

                var expires = DateTime.UtcNow.AddMinutes(auth.SessionTimeoutMinutes);
                SessionCookie.Issue(context.Response, token, expires, context.Request.IsHttps);
                context.Response.Headers[RenewedTokenHeader] = token;
            }
            catch
            {
                // A session that cannot be extended is not a request that should
                // fail. The caller is still authenticated for now; they will be
                // asked to sign in when the current token runs out.
            }
        }

        await _next(context);
    }

    private static bool ShouldRenew(HttpContext context, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (context.User?.Identity?.IsAuthenticated != true) return false;

        // Sign in, OTP and sign out mint or clear tokens themselves. Renewing
        // around them would either duplicate the work or resurrect the session
        // the caller just ended.
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)) return false;

        var exp = context.User.FindFirst("exp")?.Value;
        if (!long.TryParse(exp, out var seconds)) return false;

        remaining = DateTimeOffset.FromUnixTimeSeconds(seconds) - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return false;

        var lifetime = TimeSpan.FromMinutes(
            context.RequestServices.GetRequiredService<IAuthService>().SessionTimeoutMinutes);

        return remaining < lifetime / 2;
    }
}

public static class SlidingSessionMiddlewareExtensions
{
    /// <summary>
    /// Must sit AFTER UseAuthentication: it reads the identity that step built.
    /// </summary>
    public static IApplicationBuilder UseSlidingSession(this IApplicationBuilder app)
        => app.UseMiddleware<SlidingSessionMiddleware>();
}
