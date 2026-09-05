using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EHR.Controllers;
using EHR.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Money is the biller's and the owner's.
///
/// WHY THIS EXISTS
/// Role 3 was renamed from Front Desk to Delivery on 2026-08-31, and the name
/// was the only thing that changed. Billing, Payments, Cms, PostPayment,
/// CreatePayment, VoidPayment, Submit and BillNow carried no role list at all,
/// so the driver holding the delivery van keys could post a payment against the
/// order he had just delivered, and the sidebar offered him the tab to do it.
///
/// That is the oldest control in this trade and it is not about trust: the
/// person who hands the equipment over must not also be the person who records
/// what was paid for it, or a delivery that never happened and a payment that
/// never arrived can be made to agree with each other.
///
/// A regression here is silent. Deleting the attribute breaks no build and no
/// page; it just reopens the hole.
/// </summary>
public class DmeSeparationOfDutiesTests
{
    /// <summary>
    /// Every action that reads or writes money. Named individually rather than
    /// pattern matched: a new money action should FAIL this test until somebody
    /// decides who may reach it, which is the whole point.
    /// </summary>
    public static TheoryData<string> MoneyActions => new()
    {
        "Billing", "Payments", "Cms", "PostPayment",
        "CreatePayment", "VoidPayment", "Submit", "BillNow",
    };

    [Theory]
    [MemberData(nameof(MoneyActions))]
    public void OnlyTheBillerAndTheOwnerReachMoney(string action)
    {
        var roles = typeof(DmeController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == action)
            .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(a => a.Roles)
            .ToArray();

        roles.Should().NotBeEmpty(
            $"{action} touches money and must name the roles allowed to reach it. " +
            "Without a list the class-level [Authorize] lets any signed-in user in, " +
            "which is how Delivery could post payments.");

        roles.Should().OnlyContain(r => r == DmeRoles.Money,
            $"{action} must carry DmeRoles.Money, not its own copy of the numbers. " +
            "The sidebar reads the same constant, so a list written out by hand " +
            "here is a tab that 403s or a page with no link to it.");
    }

    /// <summary>
    /// Intake takes the order; Delivery hands it over. Neither is told what it
    /// was worth, and neither should be shown a door they cannot open.
    /// </summary>
    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    public void IntakeAndDeliveryAreNotOnTheMoneyList(string role)
    {
        DmeRoles.Money.Split(',').Should().NotContain(role);
    }

    /// <summary>
    /// The lock and the label are the same fact. The nav used to render Billing
    /// and Payments for everybody, so the first thing a new Intake user learned
    /// about their permissions was wrong.
    /// </summary>
    [Fact]
    public void TheSidebarHidesWhatTheServerRefuses()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");

        layout.Should().Contain("@if (User.CanSeeMoney())",
            "the Billing and Payments links must be behind the same role check " +
            "the controller enforces.");

        // Both links inside the one guard, and nothing else needing it.
        var guarded = layout[layout.IndexOf("@if (User.CanSeeMoney())", StringComparison.Ordinal)..];
        guarded = guarded[..guarded.IndexOf("/Dme/Settings", StringComparison.Ordinal)];
        guarded.Should().Contain("/Dme/Billing").And.Contain("/Dme/Payments");
    }

    /// <summary>
    /// Server rendered, not the body-class CSS the admin-only links use. That
    /// mechanism runs after the user loads over the API, so the tab paints
    /// first and vanishes a moment later; if the call fails it never vanishes.
    /// The role is in the cookie the page was rendered against.
    /// </summary>
    [Fact]
    public void TheMoneyLinksAreNotHiddenByCssAlone()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        var billing = layout[layout.IndexOf("/Dme/Billing", StringComparison.Ordinal)..];
        billing = billing[..billing.IndexOf("</a>", StringComparison.Ordinal)];

        billing.Should().NotContain("requires-admin");
    }

    /// <summary>
    /// One list. The sidebar label said "Clinician" to a user the server called
    /// "Intake", because AuthModule carried its own array of role names that
    /// nobody remembered to update.
    /// </summary>
    [Fact]
    public void JavaScriptHasOneListOfRoleNames()
    {
        // The ARRAY, not the word: the comment above the delegation names the
        // roles it used to print, to explain what went wrong, and that is worth
        // keeping. An earlier draft of this assertion failed on its own comment.
        Read("wwwroot", "js", "modules", "auth", "AuthModule.js")
            .Should().Contain("window.UserRoles.getName(role)")
            .And.NotContain("const roleNames = [");

        var roles = Read("wwwroot", "js", "shared", "constants", "UserRoles.js");
        var names = roles[roles.IndexOf("_names: {", StringComparison.Ordinal)..];
        names = names[..names.IndexOf("}", StringComparison.Ordinal)];

        foreach (var name in new[] { "Super Admin", "Admin", "Intake", "Delivery", "Biller" })
            names.Should().Contain($"'{name}'");

        foreach (var gone in new[] { "'Clinician'", "'Front Desk'", "'Read Only'", "'Nurse'" })
            names.Should().NotContain(gone);
    }

    /// <summary>
    /// An unrecognised role number is bad data, and both sides say so rather
    /// than naming a real role. Role 6 was Nurse and displayed as "Read Only"
    /// on thirteen accounts: a screen answering a question about permissions
    /// with a guess.
    /// </summary>
    [Fact]
    public void AnUnknownRoleSaysUnknownOnBothSides()
    {
        Read("Models", "DTOs", "AllDtos.cs").Should().Contain("_ => \"Unknown\"");
        Read("wwwroot", "js", "shared", "constants", "UserRoles.js")
            .Should().Contain("|| 'Unknown'");
    }

    /// <summary>
    /// The administration links were in every user's sidebar and merely
    /// invisible: .requires-admin is CSS keyed off a body class the API sets
    /// after the page has painted. View source and they were all there, and
    /// /UserManagement actually opened, because the guard was on the API and
    /// not on the page.
    /// </summary>
    [Fact]
    public void TheAdministrationLinksAreHiddenByTheServerNotByCss()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");

        layout.Should().Contain("@if (User.CanAdminister())");
        // The CLASS on an element, not the word: the comment above the guard
        // names .requires-admin to explain what it replaced, which is the part
        // worth keeping. Same trap the role tests hit.
        layout.Should().NotContain("nav-item requires-admin",
            "a link hidden only by CSS is still in the markup of every user's " +
            "sidebar, and hiding it says nothing about whether the URL opens.");
    }

    /// <summary>
    /// An Intake user who typed /UserManagement got the page, the toolbar and
    /// the Add User button, with an empty table where the API had refused them.
    /// </summary>
    [Fact]
    public void TheUserManagementPageRefusesWhoeverItsDataRefuses()
    {
        var home = typeof(HomeController)
            .GetMethod("Users", BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Roles)
            .ToArray();

        home.Should().Contain(DmeRoles.Admin,
            "the page must carry the same role list as UsersController, which " +
            "serves the data behind it.");
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);

        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
