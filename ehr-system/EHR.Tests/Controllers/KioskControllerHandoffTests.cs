using EHR.Controllers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EHR.Tests.Controllers;

/// <summary>
/// Unit tests for KioskController.SubmitConsent — specifically the consent →
/// intake handoff cookie + audit row branch. Uses DefaultHttpContext to assert
/// directly on Response.Cookies and an in-memory EhrDbContext to assert on the
/// AuditLog row. No WebApplicationFactory / TestServer required.
///
/// Covers test IDs B1–B6 (cookie issued or not based on response shape),
/// B8 / K1 (audit row content), L1 (no PHI in response payload),
/// L4 (no plaintext token in audit / logs).
/// </summary>
public class KioskControllerHandoffTests
{
    private const string SessionToken = "fake-session-token";

    private static (KioskController ctl, EhrDbContext db, DefaultHttpContext http) BuildSut(
        KioskSubmitConsentResponseDto stubResponse)
    {
        var consent = new Mock<IConsentService>();
        consent.Setup(s => s.SubmitConsentAsync(
                It.IsAny<string>(), It.IsAny<KioskSubmitConsentRequestDto>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(stubResponse);

        var db = InMemoryDbFactory.Create();

        var ctl = new KioskController(
            kioskService: Mock.Of<IKioskService>(),
            templateService: Mock.Of<IConsentTemplateService>(),
            consentService: consent.Object,
            paymentService: Mock.Of<IPaymentService>(),
            db: db,
            logger: NullLogger<KioskController>.Instance);

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        http.Request.Headers["User-Agent"] = "TestAgent/1.0";
        ctl.ControllerContext = new ControllerContext { HttpContext = http };

        return (ctl, db, http);
    }

    private static KioskSubmitConsentResponseDto SuccessWith(KioskIntakeHandoffDto handoff, Guid? tokenForCookie)
    {
        return new KioskSubmitConsentResponseDto
        {
            Success = true,
            Message = "ok",
            ConsentId = 42,
            PatientFirstName = "Test",
            AppointmentTime = "10:00 AM",
            ProviderName = "Dr. Test",
            Intake = handoff,
            IntakeTokenForCookie = tokenForCookie,
            PatientIdForAudit = handoff != null ? 100 : null,
            KioskSessionIdForAudit = handoff != null ? 7 : null,
            TenantIdForAudit = handoff != null ? 1 : null,
        };
    }

    [Fact] // B1, B3 — cookie is set when handoff is present
    public async Task SubmitConsent_WithHandoff_SetsIntakeVerifyCookie()
    {
        var intakeToken = Guid.NewGuid();
        var handoff = new KioskIntakeHandoffDto
        {
            State = "not_started", Url = "/x", Completed = 0, Total = 8
        };
        var (ctl, _, http) = BuildSut(SuccessWith(handoff, intakeToken));

        await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        var setCookieHeaders = http.Response.Headers["Set-Cookie"].ToString();
        setCookieHeaders.Should().Contain(IntakeVerifyCookie.Name(intakeToken));
        setCookieHeaders.Should().Contain(IntakeVerifyCookie.Value(intakeToken));
        setCookieHeaders.Should().Contain("httponly", "cookie must be HttpOnly");
        setCookieHeaders.Should().Contain("samesite=strict");
    }

    [Fact] // B4 — no handoff (already submitted) ⇒ no cookie
    public async Task SubmitConsent_NoHandoffBlock_DoesNotSetCookie()
    {
        var (ctl, _, http) = BuildSut(SuccessWith(handoff: null, tokenForCookie: null));

        await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        http.Response.Headers["Set-Cookie"].ToString().Should().NotContain(IntakeVerifyCookie.Prefix);
    }

    [Fact] // B5 — handoff null because no token ⇒ no cookie
    public async Task SubmitConsent_HandoffNull_NoCookieRegardlessOfToken()
    {
        // Even if a stray token slipped through, no Intake means no cookie.
        var (ctl, _, http) = BuildSut(SuccessWith(handoff: null, tokenForCookie: Guid.NewGuid()));

        await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        http.Response.Headers["Set-Cookie"].ToString().Should().NotContain(IntakeVerifyCookie.Prefix);
    }

    [Fact] // B6 — failure response ⇒ no cookie
    public async Task SubmitConsent_FailureResponse_DoesNotSetCookie()
    {
        var (ctl, _, http) = BuildSut(new KioskSubmitConsentResponseDto
        {
            Success = false, Message = "validation failed",
        });

        await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        http.Response.Headers["Set-Cookie"].ToString().Should().NotContain(IntakeVerifyCookie.Prefix);
    }

    [Fact] // B8 / K1 — audit row written with correct shape
    public async Task SubmitConsent_WithHandoff_WritesAuditRow()
    {
        var intakeToken = Guid.NewGuid();
        var (ctl, db, _) = BuildSut(SuccessWith(new KioskIntakeHandoffDto
        {
            State = "partial", Url = "/x", Completed = 3, Total = 8
        }, intakeToken));

        await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        var row = db.AuditLogs.SingleOrDefault();
        row.Should().NotBeNull();
        row!.Action.Should().Be("consent-to-intake-handoff-cookie-issued");
        row.EntityType.Should().Be("KioskSession");
        row.EntityId.Should().Be(7);
        row.TenantId.Should().Be(1);
        row.UserId.Should().BeNull("the patient is not a system user");
        row.NewValues.Should().Contain("\"intakeState\":\"partial\"");
        row.NewValues.Should().Contain("\"completed\":3");
        row.NewValues.Should().Contain("intakeTokenHash");
        row.NewValues.Should().NotContain(intakeToken.ToString("N"),
            "L4: raw token must never appear in audit");
        row.NewValues.Should().NotContain(intakeToken.ToString("D"),
            "L4: raw token must never appear in audit");
    }

    [Fact] // K1 — no audit row when no handoff
    public async Task SubmitConsent_NoHandoff_NoAuditRow()
    {
        var (ctl, db, _) = BuildSut(SuccessWith(handoff: null, tokenForCookie: null));

        await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        db.AuditLogs.Should().BeEmpty();
    }

    [Fact] // L1 — internal-only signal fields are stripped from the wire response
    public async Task SubmitConsent_StripsInternalFieldsBeforeReturning()
    {
        var (ctl, _, _) = BuildSut(SuccessWith(new KioskIntakeHandoffDto
        {
            State = "partial", Url = "/x", Completed = 3, Total = 8
        }, Guid.NewGuid()));

        var result = await ctl.SubmitConsent(SessionToken, new KioskSubmitConsentRequestDto());

        var ok = result.Result as OkObjectResult;
        ok.Should().NotBeNull();
        var dto = ok!.Value as KioskSubmitConsentResponseDto;
        dto.Should().NotBeNull();
        dto!.IntakeTokenForCookie.Should().BeNull("internal signal must not leak to client");
        dto.PatientIdForAudit.Should().BeNull();
        dto.KioskSessionIdForAudit.Should().BeNull();
        dto.TenantIdForAudit.Should().BeNull();
        dto.Intake.Should().NotBeNull("the public Intake block stays — that's the point");
    }
}
