using System.Text.Json;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 DB-touching tests for IntakePrefillService — the central read path
/// for patient intake data. Same service backs both the portal endpoint
/// (/api/intake/{section}/prefill) and the tablet endpoint
/// (/api/intake/p/{token}/{section}/prefill); the controllers only differ in
/// how they resolve patientId.
///
/// Contracts proven:
/// - Demographics decrypts ALL configured PHI fields before returning to client
///   (regression: empty firstName previously surfaced because controller never
///   called DecryptEntity).
/// - Demographics handles mixed legacy/encrypted/empty patient rows safely.
/// - SsnEncrypted is decrypted and last-4 extracted for masked display.
/// - Unknown section throws ArgumentException (controllers translate to 404).
/// - Read-only identity fields (DOB, Email) are returned as-is.
/// </summary>
[Collection("Db")]
public class IntakePrefillServiceTests
{
    private readonly SqlServerFixture _fx;

    private const int PatientId = 77;
    private const int TenantId = 1;

    public IntakePrefillServiceTests(SqlServerFixture fx) => _fx = fx;

    private IntakePrefillService NewService(EhrDbContext db) =>
        new IntakePrefillService(db, TestEncryptionHelper.Create());

    [Fact]
    public async Task GetAsync_Demographics_DecryptsAllConfiguredPhiFields()
    {
        // Patient row stored with all PHI fields as ciphertext. Service must
        // call DecryptEntity so the client receives plaintext for display.
        await _fx.ResetAsync();
        var enc = TestEncryptionHelper.Create();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-77",
                FirstName = enc.Encrypt("Kamal"),
                LastName = enc.Encrypt("Khan"),
                Phone = enc.Encrypt("555-1234"),
                Email = "kamal@test.com",  // Email column is encrypted in config but
                                           // many legacy rows have plaintext; both must work.
                Gender = "Male",
                DateOfBirth = new DateOnly(1995, 2, 1),
                Address = enc.Encrypt("1 Main St"),
                City = enc.Encrypt("Boston"),
                State = enc.Encrypt("MA"),
                ZipCode = enc.Encrypt("02101"),
                EmergencyContactName = enc.Encrypt("Jane"),
                EmergencyContactRelation = enc.Encrypt("Spouse"),
                EmergencyContactPhone = enc.Encrypt("555-1111"),
                EmergencyContactAltPhone = "555-2222",  // not in encryption config
                SsnEncrypted = enc.Encrypt("123-45-4442")
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var result = await svc.GetAsync("demographics", PatientId);

        // Round-trip the result through System.Text.Json so we can index by
        // property name (the service returns an anonymous type).
        var json = JsonSerializer.Serialize(result);
        var dto = JsonDocument.Parse(json).RootElement;

        dto.GetProperty("firstName").GetString().Should().Be("Kamal");
        dto.GetProperty("lastName").GetString().Should().Be("Khan");
        dto.GetProperty("phone").GetString().Should().Be("555-1234");
        dto.GetProperty("email").GetString().Should().Be("kamal@test.com");
        dto.GetProperty("dateOfBirth").GetString().Should().Be("1995-02-01");
        dto.GetProperty("gender").GetString().Should().Be("Male");
        dto.GetProperty("address").GetString().Should().Be("1 Main St");
        dto.GetProperty("city").GetString().Should().Be("Boston");
        dto.GetProperty("state").GetString().Should().Be("MA");
        dto.GetProperty("zipCode").GetString().Should().Be("02101");
        dto.GetProperty("emergencyContactName").GetString().Should().Be("Jane");
        dto.GetProperty("emergencyContactRelation").GetString().Should().Be("Spouse");
        dto.GetProperty("emergencyContactPhone").GetString().Should().Be("555-1111");
        dto.GetProperty("emergencyContactAltPhone").GetString().Should().Be("555-2222");

        // SSN is masked to last-4 — the full value never leaves the server.
        dto.GetProperty("ssnLast4").GetString().Should().Be("4442");
    }

    [Fact]
    public async Task GetAsync_Demographics_PlaintextLegacyData_ReturnsUnchanged()
    {
        // Legacy patients may have plaintext FirstName/LastName/etc. before the
        // encryption rule was enforced. DecryptEntity uses IsEncrypted() to skip
        // these rows so they round-trip unchanged. This test guards against
        // accidental breakage of mixed legacy/encrypted production data.
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-77",
                FirstName = "Aman",
                LastName = "Sani",
                Email = "aman@test.com",
                Phone = "555-9999",
                Gender = "Male",
                DateOfBirth = new DateOnly(1990, 1, 1)
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var result = await svc.GetAsync("demographics", PatientId);
        var dto = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

        dto.GetProperty("firstName").GetString().Should().Be("Aman");
        dto.GetProperty("lastName").GetString().Should().Be("Sani");
        dto.GetProperty("email").GetString().Should().Be("aman@test.com");
        dto.GetProperty("phone").GetString().Should().Be("555-9999");
    }

    [Fact]
    public async Task GetAsync_Demographics_EmptyNamesBecomeEmptyStrings()
    {
        // Bug-discovery scenario: a patient row with EMPTY FirstName/LastName
        // (the wipe pattern from the SaveDemographics bug) returns empty
        // strings — not null, not garbage. Form renders blank inputs cleanly.
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-77",
                FirstName = "",
                LastName = "",
                Email = "kamal@test.com",
                DateOfBirth = new DateOnly(1995, 2, 1)
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var result = await svc.GetAsync("demographics", PatientId);
        var dto = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

        dto.GetProperty("firstName").GetString().Should().Be("");
        dto.GetProperty("lastName").GetString().Should().Be("");
        dto.GetProperty("email").GetString().Should().Be("kamal@test.com");
    }

    [Fact]
    public async Task GetAsync_Demographics_UnknownPatient_ReturnsEmptyObject()
    {
        // Patient not found → empty {} so the form renders blank without
        // throwing. (The controller already validated the user is allowed
        // to read THIS patientId; service trusts the caller.)
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var result = await svc.GetAsync("demographics", 99999);
        var dto = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

        // Empty anonymous type = no properties.
        dto.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_UnknownSection_ThrowsArgumentException()
    {
        // The dispatch contract: unknown section names throw cleanly so the
        // controller can return 404. Should not silently return empty.
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var act = async () => await svc.GetAsync("not-a-real-section", PatientId);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown intake prefill section*");
    }

    [Theory]
    [InlineData("demographics")]
    [InlineData("concerns")]
    [InlineData("medical-history")]
    [InlineData("lifestyle")]
    [InlineData("medications")]
    [InlineData("gender-health")]
    [InlineData("longevity")]
    public async Task GetAsync_AllConfiguredSections_DispatchSuccessfully(string sectionName)
    {
        // Smoke test for the dispatch table: every section name from the
        // EncounterFormRenderer STEPS array must be handled by the service.
        // If someone adds a section to the wizard without updating the
        // service, this test fails fast.
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-77",
                FirstName = "Test", LastName = "Patient", Gender = "M",
                Email = "test@example.com",
                DateOfBirth = new DateOnly(1985, 6, 15)
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        // Should not throw. Empty result is acceptable for sections with no
        // rows yet — what matters is the dispatch path exists.
        var act = async () => await svc.GetAsync(sectionName, PatientId);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAsync_SectionName_IsCaseInsensitiveAndTrimmed()
    {
        // "Demographics" / " demographics " / "DEMOGRAPHICS" all work.
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-77",
                FirstName = "Test", LastName = "Patient", Gender = "M",
                Email = "test@example.com",
                DateOfBirth = new DateOnly(1985, 6, 15)
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var act1 = async () => await svc.GetAsync("Demographics", PatientId);
        var act2 = async () => await svc.GetAsync("  demographics  ", PatientId);
        var act3 = async () => await svc.GetAsync("DEMOGRAPHICS", PatientId);

        await act1.Should().NotThrowAsync();
        await act2.Should().NotThrowAsync();
        await act3.Should().NotThrowAsync();
    }
}
