using System;
using System.Linq;
using EHR.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The session cookie carries a JWT to server-rendered pages, so its flags are
/// security controls, not preferences.
///
/// HttpOnly   script cannot read it, so an XSS cannot exfiltrate the session
///            (this is why the cookie is safer than the localStorage copy)
/// SameSite   Strict, so a cross-site request never carries it
/// Secure     on when the request is HTTPS; off on plain-HTTP localhost, or
///            the developer login silently stops working
/// Path       "/", because DME pages live outside /api
///
/// Each of these is one word in a CookieOptions initialiser. Nothing except a
/// test notices if one changes.
/// </summary>
public class SessionCookieTests
{
    private static (HttpContext ctx, string? setCookie) IssueOn(bool isHttps, DateTimeOffset expires)
    {
        var ctx = new DefaultHttpContext();
        SessionCookie.Issue(ctx.Response, "the.jwt.value", expires, isHttps);
        return (ctx, ctx.Response.Headers.SetCookie.FirstOrDefault());
    }

    [Fact]
    public void Issue_SetsHttpOnlyAndStrictSameSite()
    {
        var (_, setCookie) = IssueOn(isHttps: true, DateTimeOffset.UtcNow.AddMinutes(30));

        setCookie.Should().NotBeNull();
        setCookie!.Should().Contain("httponly",
            "a script-readable session cookie would be exfiltratable by any XSS");
        setCookie.Should().Contain("samesite=strict",
            "the browser now sends this automatically, so cross-site requests must not carry it");
        setCookie.Should().Contain("path=/",
            "DME pages are served outside /api and would not otherwise receive the cookie");
    }

    [Fact]
    public void Issue_OverHttps_MarksCookieSecure()
    {
        var (_, setCookie) = IssueOn(isHttps: true, DateTimeOffset.UtcNow.AddMinutes(30));

        setCookie!.Should().Contain("secure",
            "in production the session token must never travel over plain HTTP");
    }

    [Fact]
    public void Issue_OverPlainHttp_DoesNotMarkSecure()
    {
        var (_, setCookie) = IssueOn(isHttps: false, DateTimeOffset.UtcNow.AddMinutes(30));

        setCookie!.Should().NotContain("secure",
            "localhost development runs on plain HTTP; a Secure cookie there is never stored " +
            "and login appears to silently fail");
    }

    [Fact]
    public void Issue_UsesTheTokenExpiryGiven()
    {
        var expires = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var (_, setCookie) = IssueOn(isHttps: true, expires);

        setCookie!.Should().Contain("expires=",
            "the cookie must not outlive the JWT it carries");
        setCookie.Should().Contain("2030",
            "the expiry passed in is the token's own expiry and must be used verbatim");
    }

    [Fact]
    public void Read_ReturnsNull_WhenCookieAbsent()
    {
        var ctx = new DefaultHttpContext();

        SessionCookie.Read(ctx.Request).Should().BeNull(
            "absent must be distinguishable from present, or the auth scheme would try to " +
            "validate an empty token");
    }

    [Fact]
    public void Read_ReturnsNull_WhenCookieIsBlank()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"{SessionCookie.Name}=";

        SessionCookie.Read(ctx.Request).Should().BeNull(
            "a blank cookie is not a session; treating it as one sends an empty token to the validator");
    }

    [Fact]
    public void Read_ReturnsTheToken_WhenPresent()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"{SessionCookie.Name}=abc.def.ghi";

        SessionCookie.Read(ctx.Request).Should().Be("abc.def.ghi");
    }

    [Fact]
    public void Clear_ExpiresTheCookieImmediately()
    {
        var ctx = new DefaultHttpContext();

        SessionCookie.Clear(ctx.Response, isHttps: true);

        var setCookie = ctx.Response.Headers.SetCookie.FirstOrDefault();
        setCookie.Should().NotBeNull();
        setCookie!.Should().Contain(SessionCookie.Name);
        setCookie.Should().Contain("expires=Thu, 01 Jan 1970",
            "logout must remove the cookie, not merely stop refreshing it");
        setCookie.Should().Contain("path=/",
            "a delete with a different Path leaves the original cookie in place");
    }
}
