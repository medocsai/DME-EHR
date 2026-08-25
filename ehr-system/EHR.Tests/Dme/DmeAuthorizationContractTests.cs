using System;
using System.Linq;
using System.Reflection;
using EHR.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The DME product must never serve tenant data anonymously.
///
/// WHY THIS EXISTS
/// Before 2026-08-25 neither DME controller carried [Authorize], and the login
/// gate was a client-side JavaScript check that hid a div. An anonymous GET of
/// /Dme/Customers returned 200 with customer names, phone numbers and account
/// numbers, and an anonymous POST to /Dme/Submit moved a claim to submitted.
/// Verified by running it, not by reading the code.
///
/// A regression here is silent: removing [Authorize] breaks no build and no
/// page, it just quietly reopens the hole. These tests are the alarm.
///
/// WHAT THEY DO NOT COVER
/// That authorization is correctly WIRED (the MedocsSmartAuth policy scheme
/// resolving cookie or header) is an integration concern, verified end to end
/// against a running app. These tests cover the contract that is easy to break
/// by editing one line: the attributes themselves.
/// </summary>
public class DmeAuthorizationContractTests
{
    /// <summary>Every controller that serves DME tenant data.</summary>
    public static TheoryData<Type> DmeControllers => new()
    {
        typeof(DmeController),
        typeof(HcpcsController),
    };

    [Theory]
    [MemberData(nameof(DmeControllers))]
    public void DmeController_RequiresAuthorization(Type controller)
    {
        var authorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();

        authorize.Should().NotBeEmpty(
            $"{controller.Name} serves tenant data and must carry [Authorize]. " +
            "Without it every action is anonymous: this exact gap served customer " +
            "PHI to unauthenticated requests before 2026-08-25.");
    }

    [Theory]
    [MemberData(nameof(DmeControllers))]
    public void NoDmeAction_OptsOutOfAuthorization(Type controller)
    {
        var anonymous = PublicActions(controller)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null)
            .Select(m => m.Name)
            .ToArray();

        anonymous.Should().BeEmpty(
            $"[AllowAnonymous] on a DME action silently reopens anonymous access to tenant data. " +
            $"Offending actions: {string.Join(", ", anonymous)}");
    }

    /// <summary>
    /// Anything that changes state must also carry CSRF protection. The session
    /// cookie means the browser now sends credentials automatically, so a
    /// cross-site form post would otherwise be accepted as the logged-in user.
    /// </summary>
    [Fact]
    public void EveryDmePostAction_ValidatesAntiForgeryToken()
    {
        var unprotected = PublicActions(typeof(DmeController))
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>(inherit: true) != null)
            .Where(m => m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>(inherit: true) == null)
            .Select(m => m.Name)
            .ToArray();

        unprotected.Should().BeEmpty(
            "a state-changing DME action without [ValidateAntiForgeryToken] is CSRF-able now that " +
            "the browser holds a session cookie and sends it automatically. " +
            $"Offending actions: {string.Join(", ", unprotected)}");
    }

    /// <summary>
    /// Reads must leave an audit trail. The filter is what satisfies HIPAA
    /// 45 CFR 164.312(b) for "who looked at this".
    /// </summary>
    [Theory]
    [MemberData(nameof(DmeControllers))]
    public void DmeController_AuditsPhiAccess(Type controller)
    {
        var audit = controller
            .GetCustomAttributes(typeof(EHR.Helpers.PhiAccessAuditAttribute), inherit: true);

        audit.Should().NotBeEmpty(
            $"{controller.Name} returns PHI, so every successful request must record an " +
            "AuditLogs row via [PhiAccessAudit].");
    }

    /// <summary>
    /// Public, non-inherited, non-special methods that MVC would route to.
    /// </summary>
    private static MethodInfo[] PublicActions(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName)
        .ToArray();
}
