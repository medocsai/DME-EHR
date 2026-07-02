using System.Threading.Tasks;
using EHR.Services.Security.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 5 §6 step 3: CSRF middleware (double-submit cookie). Pure unit
/// tests with a fake HttpContext; no real auth, no DB.
/// </summary>
[Trait("Phase", "5")]
public class Phase5_CsrfMiddlewareTests
{
    private static CsrfMiddleware Build(RequestDelegate next, bool enabled = true)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Csrf:Enabled"] = enabled.ToString().ToLowerInvariant()
            })
            .Build();
        return new CsrfMiddleware(next, cfg, NullLogger<CsrfMiddleware>.Instance);
    }

    [Fact]
    public async Task SafeMethod_SetsXsrfCookie_AndAllowsThrough()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/patients";

        await mw.InvokeAsync(ctx);

        called.Should().BeTrue();
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("XSRF-TOKEN=");
    }

    [Fact]
    public async Task UnsafeMethod_WithoutHeader_Returns403()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/patients";
        ctx.Response.Body = new System.IO.MemoryStream();

        await mw.InvokeAsync(ctx);

        called.Should().BeFalse("downstream must not run when CSRF check fails");
        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UnsafeMethod_WithMismatchedHeader_Returns403()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/patients";
        ctx.Request.Headers["Cookie"] = "XSRF-TOKEN=cookieValue";
        ctx.Request.Headers["X-CSRF-Token"] = "headerValue-DIFFERENT";
        ctx.Response.Body = new System.IO.MemoryStream();

        await mw.InvokeAsync(ctx);

        called.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UnsafeMethod_WithMatchingHeader_AllowsThrough()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/patients";
        var token = "samevalue";
        ctx.Request.Headers["Cookie"] = $"XSRF-TOKEN={token}";
        ctx.Request.Headers["X-CSRF-Token"] = token;

        await mw.InvokeAsync(ctx);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task AuthEndpoint_AlwaysExempt_EvenWithoutHeader()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/auth/login";

        await mw.InvokeAsync(ctx);

        called.Should().BeTrue("auth endpoints must not require CSRF");
    }

    [Fact]
    public async Task Disabled_AllowsUnsafeWithoutHeader()
    {
        var called = false;
        var mw = Build(_ => { called = true; return Task.CompletedTask; }, enabled: false);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/patients";

        await mw.InvokeAsync(ctx);

        called.Should().BeTrue("Csrf:Enabled=false means transition mode");
    }
}
