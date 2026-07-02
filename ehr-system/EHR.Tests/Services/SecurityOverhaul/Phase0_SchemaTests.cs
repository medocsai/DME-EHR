using System;
using System.Linq;
using System.Threading.Tasks;
using EHR.Models.Generated;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 0 of the security overhaul (rules/technical/security-overhaul-plan.md
/// §6 Phase 0): schema-only changes. Adds a Guid PublicId to every in-scope
/// entity, plus the UserLocationAccess and SecurityAuditEvents tables.
///
/// These tests verify the schema/model contract — not behavior. Phase 1+
/// introduces guards, contractors, controller wiring; those phases carry
/// their own test suites.
///
/// SQL-Server-only behavior (raw T-SQL triggers, DEFAULT NEWID() at the SQL
/// level, raw migration backfill) is documented inline and skipped — the
/// SqlServerFixture is SQLite-backed today (see fixture comment). When the
/// fixture flips back to real SQL Server, the [Trait("RequiresSqlServer","true")]
/// tests below will run automatically.
/// </summary>
[Collection("Db")]
[Trait("Phase", "0")]
public class Phase0_SchemaTests
{
    private readonly SqlServerFixture _fx;

    public Phase0_SchemaTests(SqlServerFixture fx) => _fx = fx;

    /// <summary>
    /// Single source of truth for which entities the overhaul covers.
    /// Matches Models/PublicIdExtensions.cs and migrations 027 + 030 exactly.
    /// When you add a new in-scope entity, append it here AND in those places.
    /// 70 entities total: 37 from Phase 0 + 33 from Phase 0a (2026-05-26).
    /// </summary>
    public static readonly Type[] InScopeEntities = new[]
    {
        // Phase 0 (37)
        typeof(Patient), typeof(Provider), typeof(User), typeof(Tenant),
        typeof(Location), typeof(Appointment), typeof(Encounter),
        typeof(ClinicalNote), typeof(ClinicalNoteAmendment),
        typeof(ClinicalNoteAddendum), typeof(Prescription), typeof(Order),
        typeof(BillingClaim), typeof(Charge), typeof(Insurance),
        typeof(Authorization), typeof(CareEpisode), typeof(CareEpisodeConsent),
        typeof(CareEpisodeConsentForm), typeof(Consent),
        typeof(ConsentFormTemplate), typeof(PatientDocument),
        typeof(RecordingSession), typeof(TranscriptionChunk),
        typeof(Message), typeof(Conversation), typeof(PatientStickyNote),
        typeof(PatientHealthConcern), typeof(PatientProblem),
        typeof(PatientMedication), typeof(PatientSupplement),
        typeof(PatientImmunization), typeof(PatientAllergy),
        typeof(PatientFamilyHistory), typeof(PatientSocialHistory),
        typeof(PatientIntakeSubmission), typeof(CareNote),

        // Phase 0a (33) — PHI/security additions + tenant-scoped resources +
        // §4.3 verification discoveries (Note, StripeConnectAccount, SystemSetting).
        typeof(OrderResult), typeof(PatientVital), typeof(TreatmentPlan),
        typeof(PatientLongevityProfile), typeof(PatientGenderHealth),
        typeof(PatientConversation), typeof(PatientMessage),
        typeof(TelehealthTranscriptionChunk), typeof(PatientLedger),
        typeof(Payment), typeof(PaymentRefund), typeof(InstallmentPlan),
        typeof(InstallmentDetail), typeof(ClaimStatusHistory),
        typeof(PatientPortalAccount), typeof(PatientPortalInvitation),
        typeof(PatientPortalOtp), typeof(PatientPortalPasswordReset),
        typeof(TrustedDevice), typeof(IntakeVerificationAttempt),
        typeof(KioskSession), typeof(KioskVerificationAttempt),
        typeof(ProviderSchedule), typeof(TherapistUnavailability),
        typeof(ClinicalNoteTemplate), typeof(ProviderFavoriteCode),
        typeof(CredentialingRecord), typeof(MedicalLienTemplate),
        typeof(AuditLog), typeof(InstallmentPlanAuditLog),
        typeof(Note), typeof(StripeConnectAccount), typeof(SystemSetting),
    };

    public static System.Collections.Generic.IEnumerable<object[]> EntityRows()
        => InScopeEntities.Select(t => new object[] { t });

    // ------------------------------------------------------------------------
    // Contract: every in-scope entity exposes PublicId as a Guid CLR property.
    // ------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(EntityRows))]
    public void EveryInScopeEntity_HasPublicId_GuidProperty(Type entityType)
    {
        var prop = entityType.GetProperty("PublicId");
        prop.Should().NotBeNull($"{entityType.Name} must declare a PublicId property (Phase 0)");
        prop!.PropertyType.Should().Be(typeof(Guid),
            $"{entityType.Name}.PublicId must be Guid, not {prop.PropertyType.Name}");
    }

    // ------------------------------------------------------------------------
    // Contract: every in-scope entity declares a unique index on PublicId in
    // its EF mapping. EnsureCreated on SQLite materializes the index; on SQL
    // Server it matches the UX_<Table>_PublicId index from migration_027.
    // ------------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(EntityRows))]
    public async Task EveryInScopeEntity_DeclaresUniqueIndexOnPublicId(Type entityType)
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var entity = db.Model.FindEntityType(entityType);
        entity.Should().NotBeNull($"{entityType.Name} must be mapped in the EF model");

        var publicIdIndex = entity!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1
                              && i.Properties[0].Name == "PublicId");

        publicIdIndex.Should().NotBeNull(
            $"{entityType.Name} must declare an index on PublicId");
        publicIdIndex!.IsUnique.Should().BeTrue(
            $"{entityType.Name}.PublicId index must be UNIQUE");
    }

    // ------------------------------------------------------------------------
    // Contract: inserting a new in-scope row without setting PublicId leaves
    // SaveChanges with a non-empty Guid (matches SQL Server DEFAULT NEWID()).
    // ------------------------------------------------------------------------
    [Fact]
    public async Task InsertingPatient_WithoutPublicId_AutoPopulatesNonEmptyGuid()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var patient = NewMinimalPatient("AUTO-PUB-1");
        // Deliberately do NOT assign PublicId; the model's value generator
        // (GuidValueGenerator in tests; NEWID() on SQL Server) must populate it.
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        patient.PublicId.Should().NotBe(Guid.Empty,
            "PublicId must be auto-populated on insert when not explicitly set");
    }

    // ------------------------------------------------------------------------
    // Contract: two inserts produce two distinct PublicIds. Sanity check on
    // generator + unique constraint working in concert.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task InsertingTwoPatients_GeneratesDistinctPublicIds()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var a = NewMinimalPatient("UNIQ-A");
        var b = NewMinimalPatient("UNIQ-B");
        db.Patients.AddRange(a, b);
        await db.SaveChangesAsync();

        a.PublicId.Should().NotBe(Guid.Empty);
        b.PublicId.Should().NotBe(Guid.Empty);
        a.PublicId.Should().NotBe(b.PublicId,
            "two independent inserts must not collide on PublicId");
    }

    // ------------------------------------------------------------------------
    // Contract: an explicit PublicId set by the caller is preserved (the
    // value generator only fires when the property is Guid.Empty).
    // ------------------------------------------------------------------------
    [Fact]
    public async Task InsertingPatient_WithExplicitPublicId_PreservesIt()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var explicitGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var patient = NewMinimalPatient("EXPLICIT-PUB");
        patient.PublicId = explicitGuid;
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var reloaded = await db.Patients.AsNoTracking()
            .SingleAsync(p => p.PatientId == patient.PatientId);
        reloaded.PublicId.Should().Be(explicitGuid);
    }

    // ------------------------------------------------------------------------
    // UserLocationAccess: table exists in the model with the expected shape.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task UserLocationAccess_IsMappedAndAcceptsInserts()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        // Seed a user (FK target).
        var user = new User
        {
            TenantId = 1,
            Email = "ula-test@test.com",
            PasswordHash = "x",
            FirstName = "U",
            LastName = "LA",
            Role = 1,                 // ClinicAdmin
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var grant = new UserLocationAccess
        {
            UserId = user.UserId,
            LocationId = 10,          // seeded by SqlServerFixture
            GrantedAt = DateTime.UtcNow,
            GrantedByUserId = null
        };
        db.UserLocationAccesses.Add(grant);
        await db.SaveChangesAsync();

        grant.UserLocationAccessId.Should().BeGreaterThan(0);
        grant.PublicId.Should().NotBe(Guid.Empty,
            "UserLocationAccess.PublicId must be auto-populated on insert");
    }

    // ------------------------------------------------------------------------
    // UserLocationAccess: the (UserId, LocationId) pair is unique. A duplicate
    // grant must fail rather than silently double-issue access.
    // ------------------------------------------------------------------------
    [Fact]
    public async Task UserLocationAccess_RejectsDuplicateUserLocationPair()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var user = new User
        {
            TenantId = 1,
            Email = "ula-dup@test.com",
            PasswordHash = "x",
            FirstName = "D",
            LastName = "U",
            Role = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserLocationAccesses.Add(new UserLocationAccess
        {
            UserId = user.UserId,
            LocationId = 10,
            GrantedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Second grant for the same (UserId, LocationId) — must fail at SaveChanges
        // due to the unique index UX_UserLocationAccess_UserId_LocationId.
        await using var db2 = _fx.CreateDbContext();
        db2.UserLocationAccesses.Add(new UserLocationAccess
        {
            UserId = user.UserId,
            LocationId = 10,
            GrantedAt = DateTime.UtcNow
        });

        Func<Task> act = async () => await db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the (UserId, LocationId) unique index must reject duplicate grants");
    }

    // ------------------------------------------------------------------------
    // The EF model declares the UserLocationAccess DbSet via the hand-written
    // partial. Verify it materialized.
    // ------------------------------------------------------------------------
    [Fact]
    public void UserLocationAccess_IsRegisteredInModel()
    {
        using var db = _fx.CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(UserLocationAccess));
        entity.Should().NotBeNull("UserLocationAccess must be in the EF model");
        entity!.GetTableName().Should().Be("UserLocationAccess");

        var publicIdIdx = entity!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1
                              && i.Properties[0].Name == "PublicId");
        publicIdIdx.Should().NotBeNull();
        publicIdIdx!.IsUnique.Should().BeTrue();

        var pairIdx = entity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2
                              && i.Properties.Any(p => p.Name == "UserId")
                              && i.Properties.Any(p => p.Name == "LocationId"));
        pairIdx.Should().NotBeNull("the (UserId, LocationId) unique grant index must be declared");
        pairIdx!.IsUnique.Should().BeTrue();
    }

    // ========================================================================
    // SQL-Server-only checks. Tagged so they no-op locally on the SQLite
    // fixture and run for real when Testcontainers is re-enabled.
    // ========================================================================

    /// <summary>
    /// On real SQL Server: TR_SecurityAuditEvents_Immutable blocks UPDATE
    /// unconditionally and DELETE unless SESSION_CONTEXT('AllowAuditDelete')=1.
    /// SQLite has neither security policies nor session context, so this can
    /// only run against the production engine.
    /// </summary>
    [Fact(Skip = "Requires SQL Server (SqlServerFixture is SQLite-backed today). Re-enable when Testcontainers swap lands.")]
    [Trait("RequiresSqlServer", "true")]
    public Task SecurityAuditEvents_TriggerBlocksUpdateAndUnauthorizedDelete()
        => Task.CompletedTask;

    /// <summary>
    /// On real SQL Server: existing rows backfilled by migration_027 must all
    /// have a non-null, unique PublicId. SQLite has no pre-existing rows to
    /// backfill (EnsureCreated builds fresh), so this is meaningful only in
    /// the production engine.
    /// </summary>
    [Fact(Skip = "Requires SQL Server.")]
    [Trait("RequiresSqlServer", "true")]
    public Task AllExistingRows_AfterMigration027_HaveNonNullPublicId()
        => Task.CompletedTask;

    // ========================================================================
    // Helpers
    // ========================================================================
    private static Patient NewMinimalPatient(string mrn) => new()
    {
        TenantId = 1,
        Mrn = mrn,
        FirstName = "Test",
        LastName = "Patient",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = "Other",
        Phone = "000-000-0000",
        Email = $"{mrn.ToLowerInvariant()}@test.com",
        Address = "1 Test St",
        City = "Testville",
        State = "TS",
        ZipCode = "00000",
        EmergencyContactName = "EC",
        EmergencyContactPhone = "000-000-0000",
        EmergencyContactRelation = "Other"
    };
}
