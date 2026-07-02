using System;
using System.Threading.Tasks;
using EHR.Configuration;
using EHR.Helpers;
using EHR.Models.Generated;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 2 §6 step 1: DecryptEntity must not silently re-flush plaintext on
/// the next SaveChangesAsync. The helper now accepts an optional DbContext;
/// when supplied, the entity is marked Unchanged after decrypt so EF stops
/// tracking the in-place property writes. Tests prove that contract.
///
/// These tests exercise the auto-detach behavior directly (without depending
/// on encrypt-store-decrypt roundtrip through SQLite, which has provider
/// quirks unrelated to the Phase 2 change). Sanity check at the top proves
/// the encryption helper roundtrips in memory.
/// </summary>
[Collection("Db")]
[Trait("Phase", "2")]
public class Phase2_DecryptEntityAutoDetachTests
{
    private readonly SqlServerFixture _fx;
    private readonly EncryptionHelper _enc;

    public Phase2_DecryptEntityAutoDetachTests(SqlServerFixture fx)
    {
        _fx = fx;
        EncryptionConfiguration.Initialize();
        _enc = TestEncryptionHelper.Create();
    }

    /// <summary>
    /// Sanity: the encryption helper itself roundtrips correctly. Establishes
    /// the baseline so subsequent tests can isolate behavior changes.
    /// </summary>
    [Fact]
    public void Encrypt_Decrypt_Roundtrip_Works()
    {
        var enc = _enc.Encrypt("hello world");
        enc.Should().NotBe("hello world");
        _enc.IsEncrypted(enc).Should().BeTrue();
        _enc.Decrypt(enc).Should().Be("hello world");
    }

    /// <summary>
    /// Core contract: when a tracked entity goes through DecryptEntity with a
    /// context, its state must be set to Unchanged so a later SaveChanges does
    /// not flush whatever was in memory back to the DB.
    /// </summary>
    [Fact]
    public async Task DecryptEntity_WithContext_OnTrackedModifiedEntity_RevertsStateToUnchanged()
    {
        await _fx.ResetAsync();
        await using var seed = _fx.CreateDbContext();

        seed.Patients.Add(NewPlainPatient("DETACH-1"));
        await seed.SaveChangesAsync();

        await using var ctx = _fx.CreateDbContext();
        var patient = await ctx.Patients.FirstAsync(p => p.Mrn == "DETACH-1");

        // Simulate the DANGER: a service mutates a property of the tracked
        // entity (e.g. as if DecryptEntity replaced the ciphertext with
        // plaintext). EF marks it Modified.
        patient.FirstName = "Mutated";
        ctx.Entry(patient).State.Should().Be(EntityState.Modified);

        // SECURITY: DecryptEntity with context reverts the state so the
        // mutation does not flush to the DB on next SaveChanges.
        _enc.DecryptEntity(patient, ctx);
        ctx.Entry(patient).State.Should().Be(EntityState.Unchanged,
            "DecryptEntity must mark the tracked entity Unchanged to prevent silent re-save");

        // Prove the SaveChanges no-op: the DB row is unchanged.
        await ctx.SaveChangesAsync();
        await using var verify = _fx.CreateDbContext();
        var stored = await verify.Patients.AsNoTracking().FirstAsync(p => p.Mrn == "DETACH-1");
        stored.FirstName.Should().Be("Original",
            "the mutated in-memory value must NOT have been persisted");
    }

    [Fact]
    public async Task DecryptEntity_WithoutContext_LeavesEntityStateAlone()
    {
        await _fx.ResetAsync();
        await using var seed = _fx.CreateDbContext();
        seed.Patients.Add(NewPlainPatient("DETACH-2"));
        await seed.SaveChangesAsync();

        await using var ctx = _fx.CreateDbContext();
        var patient = await ctx.Patients.FirstAsync(p => p.Mrn == "DETACH-2");

        patient.FirstName = "MutatedNoContext";
        ctx.Entry(patient).State.Should().Be(EntityState.Modified);

        _enc.DecryptEntity(patient);   // no context -> no auto-detach
        ctx.Entry(patient).State.Should().Be(EntityState.Modified,
            "no context = no auto-detach. Callers without context must use AsNoTracking or accept the modification will flush.");
    }

    [Fact]
    public async Task DecryptEntity_OnAsNoTrackingRead_IsAlwaysSafe()
    {
        await _fx.ResetAsync();
        await using var seed = _fx.CreateDbContext();
        seed.Patients.Add(NewPlainPatient("DETACH-3"));
        await seed.SaveChangesAsync();

        await using var ctx = _fx.CreateDbContext();
        var patient = await ctx.Patients.AsNoTracking().FirstAsync(p => p.Mrn == "DETACH-3");

        // AsNoTracking returns a detached graph; even passing the context is
        // a no-op (entity isn't tracked anyway). DecryptEntity should not
        // throw.
        _enc.DecryptEntity(patient, ctx);
        ctx.Entry(patient).State.Should().Be(EntityState.Detached);

        // Prove no accidental flush: mutate, save, reload, original is intact.
        patient.FirstName = "ShouldNotPersist";
        await ctx.SaveChangesAsync();

        await using var verify = _fx.CreateDbContext();
        var stored = await verify.Patients.AsNoTracking().FirstAsync(p => p.Mrn == "DETACH-3");
        stored.FirstName.Should().Be("Original");
    }

    [Fact]
    public async Task DecryptEntity_Idempotent_RepeatedCallsKeepStateUnchanged()
    {
        await _fx.ResetAsync();
        await using var seed = _fx.CreateDbContext();
        seed.Patients.Add(NewPlainPatient("DETACH-4"));
        await seed.SaveChangesAsync();

        await using var ctx = _fx.CreateDbContext();
        var patient = await ctx.Patients.FirstAsync(p => p.Mrn == "DETACH-4");
        patient.FirstName = "Touched";

        _enc.DecryptEntity(patient, ctx);
        ctx.Entry(patient).State.Should().Be(EntityState.Unchanged);

        // Second + third calls must not flip state back to Modified.
        _enc.DecryptEntity(patient, ctx);
        _enc.DecryptEntity(patient, ctx);
        ctx.Entry(patient).State.Should().Be(EntityState.Unchanged);
    }

    private static Patient NewPlainPatient(string mrn) => new()
    {
        TenantId = 1,
        Mrn = mrn,
        FirstName = "Original",
        LastName = "Patient",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = "Other",
        Phone = "000-000-0000",
        Email = "u@test.com",
        Address = "1 Test St",
        City = "Testville",
        State = "TS",
        ZipCode = "00000",
        EmergencyContactName = "EC",
        EmergencyContactPhone = "000-000-0000",
        EmergencyContactRelation = "Other"
    };
}
