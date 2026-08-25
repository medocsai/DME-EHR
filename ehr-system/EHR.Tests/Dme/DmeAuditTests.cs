using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EHR.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The DME audit trail has to reconstruct a change, not merely note that a
/// page was opened.
///
/// WHY THIS EXISTS
/// The PhiAccessAudit filter records "who looked at this". It cannot record
/// "what did they change it from", because by the time a filter runs the old
/// values are gone. An audit log that only proves someone opened a screen is
/// cosmetic; a billing dispute or a HIPAA investigation needs the before and
/// the after, attributed to a person and an address.
///
/// These tests pin the parts that are easy to lose in a refactor: the identity
/// coming off the request rather than the call site, and both sides of the
/// change actually being written.
/// </summary>
public class DmeAuditTests
{
    private static (DmeAudit audit, Mock<IAuditService> service) Build(ClaimsPrincipal? user, string? ip = "10.0.0.7")
    {
        var service = new Mock<IAuditService>();
        var ctx = new DefaultHttpContext();
        if (user != null) ctx.User = user;
        if (ip != null) ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(ctx);

        return (new DmeAudit(service.Object, accessor.Object), service);
    }

    private static ClaimsPrincipal UserWith(string userId, string email) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("UserId", userId),
            new Claim(ClaimTypes.Email, email),
        }, authenticationType: "Test"));

    [Fact]
    public async Task RecordAsync_AttributesTheChangeToTheCallerAndTheirAddress()
    {
        var (audit, service) = Build(UserWith("42", "biller@clinic.com"));

        await audit.RecordAsync("DME_CLAIM_SUBMITTED", "DmeClaim", 3,
            before: new { Status = "ready" },
            after: new { Status = "submitted" });

        service.Verify(s => s.LogAccessAsync(
            42,
            "biller@clinic.com",
            "DME_CLAIM_SUBMITTED",
            "DmeClaim",
            3,
            It.IsAny<string>(),
            It.IsAny<string>(),
            "10.0.0.7"), Times.Once,
            "the log has to say who made the change and from where, taken from the request " +
            "rather than trusted from the call site");
    }

    [Fact]
    public async Task RecordAsync_WritesBothSidesOfTheChange()
    {
        var (audit, service) = Build(UserWith("1", "a@b.com"));
        string? oldValues = null, newValues = null;

        service.Setup(s => s.LogAccessAsync(
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<int?, string?, string, string, int?, string?, string?, string?>(
                (_, _, _, _, _, o, n, _) => { oldValues = o; newValues = n; })
            .Returns(Task.CompletedTask);

        await audit.RecordAsync("DME_RENTAL_BILLED", "DmeRental", 2,
            before: new { MonthsBilled = 0 },
            after: new { MonthsBilled = 1, ClaimNumber = "CLM-02007" });

        oldValues.Should().Contain("\"MonthsBilled\":0",
            "without the before value the entry cannot show what actually changed");
        newValues.Should().Contain("\"MonthsBilled\":1");
        newValues.Should().Contain("CLM-02007",
            "the claim the billing produced is what makes the entry traceable to money");
    }

    [Fact]
    public async Task RecordAsync_LeavesTheBeforeSideNull_ForACreate()
    {
        var (audit, service) = Build(UserWith("1", "a@b.com"));
        string? oldValues = "not-set";

        service.Setup(s => s.LogAccessAsync(
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<int?, string?, string, string, int?, string?, string?, string?>(
                (_, _, _, _, _, o, _, _) => oldValues = o)
            .Returns(Task.CompletedTask);

        await audit.RecordAsync("DME_CUSTOMER_CREATED", "DmeCustomer", 9, before: null, after: new { AccountNo = "LMS-1006" });

        oldValues.Should().BeNull(
            "a create has no prior state; writing the string \"null\" would read as a recorded value");
    }

    [Fact]
    public async Task RecordAsync_StillLogs_WhenThereIsNoAuthenticatedUser()
    {
        var (audit, service) = Build(user: null);

        await audit.RecordAsync("DME_CLAIM_SUBMITTED", "DmeClaim", 3, null, new { Status = "submitted" });

        service.Verify(s => s.LogAccessAsync(
            null, null, "DME_CLAIM_SUBMITTED", "DmeClaim", 3,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once,
            "an unattributable change is exactly the one worth recording; dropping the row " +
            "because the user is unknown destroys the evidence");
    }
}
