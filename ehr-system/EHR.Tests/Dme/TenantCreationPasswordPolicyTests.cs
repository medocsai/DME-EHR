using System;
using System.Linq;
using System.Threading.Tasks;
using EHR.Models;
using EHR.Services;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Creating a clinic also creates its first Clinic Admin, so it is a place a
/// staff password is set, and it must obey the same policy as every other.
///
/// WHY THIS EXISTS
/// The password policy was rolled out to "all four places a staff password is
/// set". This was a fifth, and it was missed: TenantService hashed the supplied
/// password and stored it without checking anything, while the form in front of
/// the Super Admin advertised a minimum of 8 against a policy of 12.
///
/// So the weakest password in the product was permitted on the account with the
/// most authority inside a clinic, through the one route only Medocs uses. That
/// is the sort of gap that is found by an auditor rather than by a user.
/// </summary>
public class TenantCreationPasswordPolicyTests
{
    private static TenantService Build()
        => new(InMemoryDbFactory.Create(),
               new LocationProvider(),
               new Mock<ILocationService>().Object);

    private static TenantCreateDto Clinic(string adminPassword) => new()
    {
        Name = "Riverside Medical Supply",
        Address = "1 Main St",
        City = "Dallas",
        State = "TX",
        ZipCode = "75201",
        TaxId = "75-0000000",
        NPI = "1234567890",
        Plan = 2,
        AdminEmail = "admin@riverside.example",
        AdminPassword = adminPassword,
        AdminFirstName = "Pat",
        AdminLastName = "Riley",
        InitialLocationName = "Main Office",
    };

    [Theory]
    [InlineData("short", "shorter than the minimum")]
    [InlineData("elevenchar", "one under the minimum")]
    [InlineData("", "empty")]
    public async Task AClinicCannotBeCreatedWithAWeakAdminPassword(string password, string why)
    {
        var service = Build();

        var act = async () => await service.CreateTenantAsync(Clinic(password));

        await act.Should().ThrowAsync<InvalidOperationException>(
            $"the admin password is {why}, and this account administers an entire clinic");
    }

    [Fact]
    public async Task TheRefusalHappensBeforeAnythingIsWritten()
    {
        var db = InMemoryDbFactory.Create();
        var service = new TenantService(db, new LocationProvider(), new Mock<ILocationService>().Object);

        try { await service.CreateTenantAsync(Clinic("short")); } catch (InvalidOperationException) { }

        db.Tenants.Should().BeEmpty(
            "the tenant, its location and its admin are written across two SaveChanges calls. " +
            "Failing partway would leave a clinic with no administrator and nothing to signal it.");
    }

    [Fact]
    public async Task AStrongPasswordIsAccepted()
    {
        var service = Build();

        var tenant = await service.CreateTenantAsync(Clinic("correct-horse-battery"));

        tenant.Should().NotBeNull();
        tenant.Name.Should().Be("Riverside Medical Supply");
    }

    /// <summary>
    /// The stored password must be a hash. Obvious, and worth pinning: the
    /// property is a plain string and assigning the DTO value straight to it
    /// compiles perfectly.
    /// </summary>
    [Fact]
    public async Task TheAdminPasswordIsStoredHashed()
    {
        var db = InMemoryDbFactory.Create();
        var service = new TenantService(db, new LocationProvider(), new Mock<ILocationService>().Object);
        const string password = "correct-horse-battery";

        var tenant = await service.CreateTenantAsync(Clinic(password));

        var admin = db.Users.Single(u => u.TenantId == tenant.TenantId);
        admin.PasswordHash.Should().NotBe(password);
        BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash).Should().BeTrue();
    }

    /// <summary>
    /// The number in front of the Super Admin has to match the number the server
    /// enforces. It said 8 against a policy of 12, which is a form that rejects
    /// what it just told you to type.
    /// </summary>
    [Fact]
    public void TheClinicFormAdvertisesTheRealMinimum()
    {
        var root = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && root.Name != "ehr-system") root = root.Parent;
        var modal = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root!.FullName, "Views", "Shared", "_ModalsAdmin.cshtml"));

        var min = EHR.Helpers.PasswordPolicy.MinimumLength;
        modal.Should().Contain($"minlength=\"{min}\"");
        modal.Should().Contain($"Minimum {min} characters");
        modal.Should().NotContain("Minimum 8 characters");
    }
}
