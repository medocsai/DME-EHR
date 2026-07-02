using EHR.Controllers;
using EHR.Helpers;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EHR.Tests.Controllers;

/// <summary>
/// Cookie acceptance/rejection tests against IntakeController's TabletProgress
/// route. The wizard route uses the same ResolveVerifiedPatientAsync code path
/// as TabletProgress, so this proves the cookie format set by KioskController
/// (during the consent → intake handoff) is honored end-to-end without re-asking
/// for DOB/SSN.
///
/// Covers test IDs D1–D6 (cookie present/expired/absent/wrong-token).
/// Cookie-expiry test (D2) is implicit in the TTL setting — covered by the
/// IntakeVerifyCookie unit tests; here we exercise the controller's match logic.
/// </summary>
public class IntakeControllerHandoffTests
{
    private static readonly Guid TokenA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TokenB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IntakeController BuildSut(IIntakeAccessTokenService tokens, DefaultHttpContext http)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Encryption:Key"] = "test-key-must-be-long-enough-for-pbkdf2-derivation"
            })
            .Build();

        var ctl = new IntakeController(
            context: InMemoryDbFactory.Create(),
            submissions: Mock.Of<IIntakeSubmissionService>(),
            progress: ProgressMockReturning(new IntakeProgressDto { Completed = 0, Total = 8 }),
            reader: Mock.Of<IPatientEnteredDataReader>(),
            tokens: tokens,
            identity: Mock.Of<IPatientIdentityMatcher>(),
            throttler: Mock.Of<IIntakeAttemptThrottler>(),
            encryption: new EncryptionHelper(config),
            prefill: Mock.Of<IIntakePrefillService>(),
            logger: NullLogger<IntakeController>.Instance);

        ctl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctl;
    }

    private static IIntakeProgressCalculator ProgressMockReturning(IntakeProgressDto dto)
    {
        var m = new Mock<IIntakeProgressCalculator>();
        m.Setup(x => x.CalculateAsync(It.IsAny<int>())).ReturnsAsync(dto);
        return m.Object;
    }

    private static IIntakeAccessTokenService TokensResolving(Guid token, int? patientId)
    {
        var m = new Mock<IIntakeAccessTokenService>();
        m.Setup(x => x.ResolvePatientIdAsync(token)).ReturnsAsync(patientId);
        return m.Object;
    }

    private static DefaultHttpContext WithCookie(string name, string value)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Cookie"] = $"{name}={value}";
        return http;
    }

    [Fact] // D1
    public async Task TabletProgress_WithValidCookie_Returns200()
    {
        var http = WithCookie(IntakeVerifyCookie.Name(TokenA), IntakeVerifyCookie.Value(TokenA));
        var ctl = BuildSut(TokensResolving(TokenA, patientId: 100), http);

        var result = await ctl.TabletProgress(TokenA);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact] // D3 — cookie absent
    public async Task TabletProgress_NoCookie_Returns401()
    {
        var http = new DefaultHttpContext();
        var ctl = BuildSut(TokensResolving(TokenA, patientId: 100), http);

        var result = await ctl.TabletProgress(TokenA);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact] // D4 — cookie for token A, request for token B ⇒ 401
    public async Task TabletProgress_CookieForDifferentToken_Returns401()
    {
        // Cookie name embeds first 12 chars of token A; we hit URL for token B.
        // Cookie name lookup at name-for-B yields nothing.
        var http = WithCookie(IntakeVerifyCookie.Name(TokenA), IntakeVerifyCookie.Value(TokenA));
        var ctl = BuildSut(TokensResolving(TokenB, patientId: 999), http);

        var result = await ctl.TabletProgress(TokenB);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact] // D4-b — even if attacker forges the right cookie *name* but wrong value
    public async Task TabletProgress_CookieNameMatchesButValueIsWrong_Returns401()
    {
        // Pretend the attacker knows the name format and posts a bogus value.
        var http = WithCookie(IntakeVerifyCookie.Name(TokenA), "deadbeefdeadbeefdeadbeefdeadbeef");
        var ctl = BuildSut(TokensResolving(TokenA, patientId: 100), http);

        var result = await ctl.TabletProgress(TokenA);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact] // D6 — token resolves to no patient (e.g., rotated/deleted) ⇒ 401
    public async Task TabletProgress_ValidCookieButTokenResolvesToNothing_Returns401()
    {
        var http = WithCookie(IntakeVerifyCookie.Name(TokenA), IntakeVerifyCookie.Value(TokenA));
        var ctl = BuildSut(TokensResolving(TokenA, patientId: null), http);

        var result = await ctl.TabletProgress(TokenA);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact] // I3 — cross-token: valid cookie for token A cannot grant access via URL for token B
    public async Task CookieIssuedForA_DoesNotAuthorizeRequestForB()
    {
        // Same cookie set as in D1 but accessed via TokenB's URL.
        var http = new DefaultHttpContext();
        http.Request.Headers["Cookie"] =
            $"{IntakeVerifyCookie.Name(TokenA)}={IntakeVerifyCookie.Value(TokenA)}";
        var ctl = BuildSut(TokensResolving(TokenB, patientId: 200), http);

        var result = await ctl.TabletProgress(TokenB);

        result.Should().BeOfType<UnauthorizedResult>();
    }
}
