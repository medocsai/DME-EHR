using System;
using System.IO;
using System.Reflection;
using EHR.Controllers;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The shape rules on a customer: date of birth, email, SSN last four, state
/// and ZIP.
///
/// WHY THESE EXIST
/// The browser filters these fields as they are typed (FormFields.js), which is
/// a courtesy. The rule is CustomerFieldProblem on the server, shared by create
/// and edit so the two cannot drift. State and ZIP are printed onto the
/// CMS-1500, where a wrong one is a denial.
/// </summary>
public class DmeCustomerFieldRulesTests
{
    private static string? Problem(string? dob = null, string? email = null,
        string? ssn = null, string? state = null, string? zip = null)
    {
        var m = typeof(DmeController).GetMethod("CustomerFieldProblem",
            BindingFlags.NonPublic | BindingFlags.Static);
        m.Should().NotBeNull("the rule lives in one method, shared by create and edit");
        return (string?)m!.Invoke(null, new object?[] { dob, email, ssn, state, zip });
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Controllers"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    [Fact]
    public void BlankIsAFactNotAMistake()
        => Problem().Should().BeNull("every one of these fields is optional");

    [Theory]
    [InlineData("01/15/1950")]
    [InlineData("1950-01-15")]
    public void AReadableDateOfBirthIsAccepted(string dob)
        => Problem(dob: dob).Should().BeNull();

    /// <summary>
    /// DateInput.Parse returns null for blank AND for unreadable. Without this
    /// a typed DOB was silently dropped and the customer saved without one.
    /// </summary>
    [Fact]
    public void AnUnreadableDateOfBirthIsRefusedNotDropped()
        => Problem(dob: "15th of Jan").Should().Contain("date of birth");

    [Theory]
    [InlineData("john@example")]
    [InlineData("john example.com")]
    public void ANonAddressIsRefused(string email)
        => Problem(email: email).Should().Contain("email");

    [Theory]
    [InlineData("123")]
    [InlineData("12a4")]
    [InlineData("123-45-6789")]
    public void TheSsnBoxTakesTheLastFourOnly(string ssn)
        => Problem(ssn: ssn).Should().Contain("SSN");

    [Theory]
    [InlineData("Texas")]
    [InlineData("T1")]
    public void StateIsTheTwoLetterCode(string state)
        => Problem(state: state).Should().Contain("State");

    [Theory]
    [InlineData("75201")]
    [InlineData("752011234")]
    [InlineData("75201-1234")]
    public void ZipIsFiveOrNineDigits(string zip)
        => Problem(zip: zip).Should().BeNull("the CMS-1500 takes ZIP or ZIP+4");

    [Theory]
    [InlineData("7520")]
    [InlineData("ABCDE")]
    public void AnythingElseIsNotAZip(string zip)
        => Problem(zip: zip).Should().Contain("ZIP");

    /// <summary>Create AND edit both run the rule, or the edit form is a way round it.</summary>
    [Fact]
    public void CreateAndEditBothApplyTheRules()
    {
        var controller = Read("Controllers", "DmeController.cs");

        foreach (var action in new[] { "CreateCustomer(", "UpdateCustomer(" })
        {
            var at = controller.IndexOf("public async Task<IActionResult> " + action, StringComparison.Ordinal);
            at.Should().BeGreaterThan(-1, action);
            var body = controller[at..(at + 3000)];
            body.Should().Contain("CustomerFieldProblem(", $"{action} must apply the shared rules");
        }
    }

    /// <summary>The field filters are loaded on every page, and both forms use them.</summary>
    [Fact]
    public void BothCustomerFormsCarryTheFieldFilters()
    {
        Read("Views", "Shared", "_Layout.cshtml").Should().Contain("js/dme/FormFields.js");

        foreach (var view in new[] { "NewCustomer.cshtml", "EditCustomer.cshtml" })
        {
            var text = Read("Views", "Dme", view);
            foreach (var cls in new[] { "js-phone", "js-name", "js-digits", "js-upper", "js-email", "js-dateinput" })
                text.Should().Contain(cls, $"{view} should use {cls}");
            text.Should().Contain("Select&hellip;", $"{view}: gender starts on a prompt, not a blank");
        }
    }
}
