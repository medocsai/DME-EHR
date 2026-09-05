using System.Text.RegularExpressions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The session timeout is an IDLE timeout. It used to be a fixed run from sign
/// in: the token was minted once and nothing renewed it, so somebody working
/// without pause was signed out as fast as an unattended browser, and the screen
/// went on looking signed in while every request under it came back 401.
/// </summary>
public class DmeSlidingSessionTests
{
    [Fact]
    public void TheSessionIsRenewedOnActivity()
    {
        var source = Middleware();

        Assert.Contains("RenewToken", source);
        Assert.Contains("SessionCookie.Issue", source);
    }

    /// <summary>
    /// An explicit Authorization header always beats the cookie, so renewing
    /// only the cookie would leave the SPA expiring on the original clock while
    /// the server-rendered half slid forward. The header carries it back.
    /// </summary>
    [Fact]
    public void TheRenewedTokenReachesTheBrowserCopyToo()
    {
        Assert.Contains("X-Session-Token", Middleware());

        var api = File.ReadAllText(Path.Combine(
            ProductionRoot(), "wwwroot", "js", "core", "ApiService.js"));
        Assert.Contains("X-Session-Token", api);
        Assert.Contains("applyRenewedToken", api);

        var auth = File.ReadAllText(Path.Combine(
            ProductionRoot(), "wwwroot", "js", "modules", "auth", "AuthModule.js"));
        Assert.Contains("applyRenewedToken", auth);
        Assert.Contains("localStorage.setItem('authToken', token)", auth);
    }

    /// <summary>
    /// Renewal re-signs the claims the caller already proved. Looking the user
    /// back up would recompute their default location and move a biller working
    /// in one branch back to the primary one, which is exactly why
    /// RefreshTokenAsync is not used here.
    /// </summary>
    [Fact]
    public void RenewalKeepsTheBranchAndEverythingElseTheCallerAlreadyProved()
    {
        var svc = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Services", "AuthService.cs"));

        var body = Regex.Match(svc, @"public string RenewToken\(.*?\n    \}", RegexOptions.Singleline).Value;

        Assert.Contains("principal.Claims", body);
        Assert.DoesNotContain("LocationsForUserAsync", body);
        Assert.DoesNotContain("_context.Users", body);

        // exp copied across would sit beside the new one, and the older wins.
        Assert.Contains("\"exp\"", body);
    }

    /// <summary>
    /// Not on every request: a typeahead would otherwise re-sign a JWT per
    /// keystroke. And never around sign in or sign out, which mint and clear
    /// tokens themselves.
    /// </summary>
    [Fact]
    public void RenewalIsThrottledAndStaysAwayFromTheAuthEndpoints()
    {
        var source = Middleware();

        Assert.Contains("lifetime / 2", source);
        Assert.Contains("/api/auth", source);
    }

    [Fact]
    public void ItRunsAfterAuthenticationOrThereIsNoIdentityToRenew()
    {
        var program = File.ReadAllText(Path.Combine(ProductionRoot(), "Program.cs"));

        var auth = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var slide = program.IndexOf("app.UseSlidingSession();", StringComparison.Ordinal);

        Assert.True(auth >= 0 && slide > auth,
            "UseSlidingSession must be registered after UseAuthentication.");
    }

    private static string Middleware() => File.ReadAllText(Path.Combine(
        ProductionRoot(), "Middleware", "SlidingSessionMiddleware.cs"));

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
