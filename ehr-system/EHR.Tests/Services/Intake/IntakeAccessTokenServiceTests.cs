using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 DB-touching tests for IntakeAccessTokenService. Runs against a real
/// SQL Server 2022 container. The service owns the opaque GUID token on
/// Patient.IntakePortalToken used by /intake/p/{token}.
///
/// Security rule under test: token never reveals PatientId; resolution is only
/// via the column, deleted patients don't resolve, and empty GUID never matches.
/// </summary>
[Collection("Db")]
public class IntakeAccessTokenServiceTests
{
    private readonly SqlServerFixture _fx;

    public IntakeAccessTokenServiceTests(SqlServerFixture fx) => _fx = fx;

    private static Patient NewPatient(int id, Guid? token = null, bool isDeleted = false) => new()
    {
        PatientId = id,
        TenantId = 1,
        Mrn = $"MRN-{id}",
        FirstName = "Test",
        LastName = "Patient",
        Gender = "M",
        DateOfBirth = new DateOnly(1990, 1, 1),
        IntakePortalToken = token,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task GetOrCreateTokenAsync_PatientHasNoToken_IssuesNewGuidAndPersists()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(NewPatient(100, token: null));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var token = await svc.GetOrCreateTokenAsync(100);

        token.Should().NotBe(Guid.Empty);
        await using var verify = _fx.CreateDbContext();
        verify.Patients.Single(p => p.PatientId == 100).IntakePortalToken.Should().Be(token);
    }

    [Fact]
    public async Task GetOrCreateTokenAsync_PatientHasExistingToken_ReturnsSameToken()
    {
        await _fx.ResetAsync();
        var existing = Guid.NewGuid();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(NewPatient(101, token: existing));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var token = await svc.GetOrCreateTokenAsync(101);

        token.Should().Be(existing);
    }

    [Fact]
    public async Task GetOrCreateTokenAsync_EmptyGuidTreatedAsMissing_IssuesNewToken()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(NewPatient(102, token: Guid.Empty));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var token = await svc.GetOrCreateTokenAsync(102);

        token.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GetOrCreateTokenAsync_PatientDoesNotExist_Throws()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var act = async () => await svc.GetOrCreateTokenAsync(999);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RotateTokenAsync_ReplacesExistingToken_OldTokenNoLongerResolves()
    {
        await _fx.ResetAsync();
        var old = Guid.NewGuid();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(NewPatient(200, token: old));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var rotated = await svc.RotateTokenAsync(200);

        rotated.Should().NotBe(old);
        rotated.Should().NotBe(Guid.Empty);
        (await svc.ResolvePatientIdAsync(old)).Should().BeNull();
        (await svc.ResolvePatientIdAsync(rotated)).Should().Be(200);
    }

    [Fact]
    public async Task RotateTokenAsync_PatientDoesNotExist_Throws()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var act = async () => await svc.RotateTokenAsync(999);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolvePatientIdAsync_ValidToken_ReturnsPatientId()
    {
        await _fx.ResetAsync();
        var token = Guid.NewGuid();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(NewPatient(300, token: token));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var id = await svc.ResolvePatientIdAsync(token);

        id.Should().Be(300);
    }

    [Fact]
    public async Task ResolvePatientIdAsync_UnknownToken_ReturnsNull()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var id = await svc.ResolvePatientIdAsync(Guid.NewGuid());

        id.Should().BeNull();
    }

    [Fact]
    public async Task ResolvePatientIdAsync_EmptyGuid_ReturnsNullWithoutQuerying()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var id = await svc.ResolvePatientIdAsync(Guid.Empty);

        id.Should().BeNull();
    }

    [Fact]
    public async Task ResolvePatientIdAsync_DeletedPatient_ReturnsNull()
    {
        await _fx.ResetAsync();
        var token = Guid.NewGuid();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(NewPatient(400, token: token, isDeleted: true));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = new IntakeAccessTokenService(db);

        var id = await svc.ResolvePatientIdAsync(token);

        id.Should().BeNull();
    }
}
