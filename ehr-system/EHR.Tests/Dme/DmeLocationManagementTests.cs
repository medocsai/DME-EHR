using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The Locations screen: the sidebar link and moving the primary branch.
///
/// WHY THESE EXIST
/// The sidebar "Locations" link called openLocationManagement(), which looked
/// for a modal removed in the 2026-08-25 cleanup, so clicking it did nothing.
/// Once restored, moving the primary to a lower LocationId failed with a 500:
/// both rows changed in one SaveChanges and UX_Locations_OnePrimaryPerTenant saw
/// two primaries for the length of one statement.
/// </summary>
public class DmeLocationManagementTests
{
    private static string Read(params string[] parts)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Controllers"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, Path.Combine(parts)));
    }

    /// <summary>The link is on every page, so the modals it opens must be too.</summary>
    [Fact]
    public void TheLayoutCarriesTheModalsTheLocationsLinkOpens()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");

        layout.Should().Contain("openLocationManagement()");
        foreach (var id in new[] { "locationManagementModal", "locationManagementList", "locationFormModal",
                                   "locationForm\"", "locationFormName", "locationFormTimezone", "locationFormPrimary" })
            layout.Should().Contain($"id=\"{id.TrimEnd('"')}\"", $"LocationModule looks for #{id.TrimEnd('"')}");
    }

    /// <summary>The script binds the form at init, so the markup must come first.</summary>
    [Fact]
    public void TheFormIsInThePageBeforeTheScriptThatBindsIt()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        layout.IndexOf("id=\"locationForm\"", StringComparison.Ordinal)
              .Should().BeLessThan(layout.IndexOf("LocationModule.js", StringComparison.Ordinal));
    }

    /// <summary>
    /// The old primary is demoted and SAVED before the new one is promoted, in
    /// every path that can make a branch primary, inside one transaction.
    /// </summary>
    [Fact]
    public void ThePrimaryIsDemotedAndSavedBeforeTheNewOneIsSet()
    {
        var service = Read("Services", "LocationService.cs");

        foreach (var method in new[] { "> CreateLocationAsync(", "> UpdateLocationAsync(", "> SetPrimaryLocationAsync(" })
        {
            // The implementation, not the interface declaration: the one with a body.
            var at = service.IndexOf("public async Task<", StringComparison.Ordinal);
            while (at >= 0 && !service.Substring(at, 120).Contains(method)) at = service.IndexOf("public async Task<", at + 1, StringComparison.Ordinal);
            at.Should().BeGreaterThan(-1, method);
            var next = service.IndexOf("\n    p", at + 20, StringComparison.Ordinal);
            var body = service[at..(next > 0 ? next : service.Length)];

            body.Should().Contain("BeginTransactionAsync", method);
            body.Should().Contain("DemoteOtherPrimariesAsync(", method);
            body.Should().Contain("CommitAsync", method);
        }

        var helper = service[service.IndexOf("private async Task DemoteOtherPrimariesAsync", StringComparison.Ordinal)..];
        helper.Should().Contain("await _context.SaveChangesAsync();", "the demotion is a save of its own");
    }

    /// <summary>The clinical fields stay out of the DME location form.</summary>
    [Fact]
    public void TheLocationFormCarriesNoClinicalFields()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");
        layout.Should().NotContain("locationFormPlaceOfService").And.NotContain("locationFormEnableLongevity");
    }
}
