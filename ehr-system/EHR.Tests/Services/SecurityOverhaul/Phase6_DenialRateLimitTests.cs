using System.Net;
using System.Threading.Tasks;
using EHR.Services.Security.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 6 §6 step 5: denial rate limit. Per-IP 403 counter -> 15-min lockout.
/// Tests use the tiny per-IP override config so the test runs in well under
/// a second; production defaults are 5 / 60s / 15min.
/// </summary>
[Trait("Phase", "6")]
public class Phase6_DenialRateLimitTests
{
    public Phase6_DenialRateLimitTests() => DenialRateLimitMiddleware.ResetAllState();

    private static DenialRateLimitMiddleware Build(int maxDenials = 3, int windowSeconds = 60, int lockoutMinutes = 15)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["DenialLimit:Enabled"] = "true",
                ["DenialLimit:MaxDenials"] = maxDenials.ToString(),
                ["DenialLimit:WindowSeconds"] = windowSeconds.ToString(),
                ["DenialLimit:LockoutMinutes"] = lockoutMinutes.ToString(),
            })
            .Build();
        // The middleware's _next runs per-test: each request sets the desired
        // downstream status code via SetStatusFromTest.
        return new DenialRateLimitMiddleware(SetStatusFromTest, cfg, NullLogger<DenialRateLimitMiddleware>.Instance);
    }

    private static HttpContext NewCtx(string ip, int responseStatus = 200)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Response.Body = new System.IO.MemoryStream();
        // Stash the desired downstream response status so the test next() can apply it.
        ctx.Items["__test_status"] = responseStatus;
        return ctx;
    }

    private static Task SetStatusFromTest(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("__test_status", out var v) && v is int s)
        {
            ctx.Response.StatusCode = s;
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UnderThreshold_DoesNotLock()
    {
        var mw = Build(maxDenials: 3, lockoutMinutes: 15);
        for (int i = 0; i < 3; i++)
        {
            var ctx = NewCtx("10.0.0.1", responseStatus: 403);
            await mw.InvokeAsync(ctx);
            ctx.Response.StatusCode.Should().Be(403, "downstream still ran and returned 403");
        }

        // 4th request: still allowed through (3 == maxDenials, threshold not exceeded yet)
        var ctxNext = NewCtx("10.0.0.1", responseStatus: 200);
        await mw.InvokeAsync(ctxNext);
        ctxNext.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ExceedsThreshold_NextRequestGets429()
    {
        var mw = Build(maxDenials: 3, lockoutMinutes: 15);

        // 4 denials in window -> exceeds the threshold of 3
        for (int i = 0; i < 4; i++)
        {
            var ctx = NewCtx("10.0.0.99", responseStatus: 403);
            await mw.InvokeAsync(ctx);
        }

        // Next request from same IP -> locked out -> 429
        var locked = NewCtx("10.0.0.99", responseStatus: 200);
        await mw.InvokeAsync(locked);
        locked.Response.StatusCode.Should().Be(429);
        locked.Response.Headers.RetryAfter.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DifferentIpsCountedIndependently()
    {
        var mw = Build(maxDenials: 3, lockoutMinutes: 15);

        // 4 denials from IP A: locks A
        for (int i = 0; i < 4; i++)
        {
            var ctx = NewCtx("10.0.0.10", responseStatus: 403);
            await mw.InvokeAsync(ctx);
        }

        // 1 request from IP B: should pass through unaffected
        var ctxB = NewCtx("10.0.0.11", responseStatus: 200);
        await mw.InvokeAsync(ctxB);
        ctxB.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Disabled_AllowsUnlimitedDenials()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["DenialLimit:Enabled"] = "false",
                ["DenialLimit:MaxDenials"] = "3",
            })
            .Build();
        var mw = new DenialRateLimitMiddleware(SetStatusFromTest, cfg, NullLogger<DenialRateLimitMiddleware>.Instance);

        for (int i = 0; i < 10; i++)
        {
            var ctx = NewCtx("10.0.0.50", responseStatus: 403);
            await mw.InvokeAsync(ctx);
            ctx.Response.StatusCode.Should().Be(403);
        }
    }
}
