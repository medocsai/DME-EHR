namespace EHR.Helpers;

/// <summary>
/// Owns the browser session cookie that carries the JWT for server-rendered pages.
///
/// WHY THIS EXISTS
/// The SPA keeps its JWT in localStorage and sends it as an Authorization header.
/// That works for fetch/XHR, but a plain browser navigation to a Razor page
/// (/Dme/Customers) sends no header, so a server-rendered page can never see the
/// token. Before this existed the DME pages were simply left unauthenticated,
/// which meant anonymous requests could read and write customer PHI.
///
/// WHAT IT DOES
/// Issues, reads and clears one HttpOnly cookie holding the same JWT that the
/// SPA already received. The "SessionCookie" authentication scheme (Program.cs)
/// reads the token from here and runs the identical validation as the Bearer
/// scheme, so revocation (TokenVersion) applies to both paths equally.
///
/// WHY HttpOnly IS AN IMPROVEMENT, NOT A REGRESSION
/// The cookie cannot be read by script, so it is strictly less exposed to XSS
/// than the localStorage copy. It is SameSite=Strict and every state-changing
/// DME action carries [ValidateAntiForgeryToken], which together close CSRF.
///
/// WHO CALLS IT
/// AuthController (issue on successful verify-otp, clear on logout) and
/// Program.cs (read on every request via the SessionCookie scheme).
/// </summary>
public static class SessionCookie
{
    /// <summary>Cookie name. Double-underscore prefix matches the existing __medocs_dt device cookie.</summary>
    public const string Name = "__medocs_sess";

    /// <summary>Authentication scheme name registered in Program.cs for this cookie.</summary>
    public const string Scheme = "SessionCookie";

    /// <summary>
    /// Policy scheme that routes each request to Bearer or SessionCookie.
    /// Registered as the application default so HttpContext.User is populated
    /// identically for API calls and page navigations.
    /// </summary>
    public const string PolicyScheme = "MedocsSmartAuth";

    /// <summary>
    /// Write the session cookie. Lifetime deliberately mirrors the JWT's own
    /// expiry so the cookie can never outlive the token it carries.
    /// </summary>
    public static void Issue(HttpResponse response, string jwt, DateTimeOffset expiresUtc, bool isHttps)
    {
        response.Cookies.Append(Name, jwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,          // false on localhost HTTP, true behind HTTPS
            SameSite = SameSiteMode.Strict,
            Expires = expiresUtc,
            Path = "/"
        });
    }

    /// <summary>
    /// Remove the session cookie. Must pass the same Path/Secure/SameSite the
    /// cookie was written with, otherwise the browser keeps the original.
    /// </summary>
    public static void Clear(HttpResponse response, bool isHttps)
    {
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    /// <summary>Read the raw JWT out of the request, or null when absent.</summary>
    public static string? Read(HttpRequest request)
        => request.Cookies.TryGetValue(Name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
