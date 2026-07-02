using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 DB-touching tests for IntakeAttemptThrottler. Runs against a real
/// SQL Server 2022 container (via SqlServerFixture). Rule under test:
/// "5 failed attempts per (LocationId, IpAddress) per 15-minute rolling window."
///
/// Stock-Problem fit: the throttler computes the current failure count from
/// IntakeVerificationAttempts rows every time, never from a cached counter.
/// Tests prove that contract.
/// </summary>
[Collection("Db")]
public class IntakeAttemptThrottlerTests
{
    private readonly SqlServerFixture _fx;

    private const int TenantId = 1;
    private const int LocationId = 10;
    private const string Ip = "10.0.0.1";

    public IntakeAttemptThrottlerTests(SqlServerFixture fx) => _fx = fx;

    private IntakeAttemptThrottler NewThrottler(EhrDbContext db)
        => new(db, NullLogger<IntakeAttemptThrottler>.Instance);

    private static IntakeVerificationAttempt Attempt(
        bool success, DateTime at, int tenantId = TenantId, int locationId = LocationId, string ip = Ip)
        => new()
        {
            TenantId = tenantId,
            LocationId = locationId,
            IsSuccessful = success,
            IpAddress = ip,
            UserAgent = "test-agent",
            AttemptedAt = at,
            AttemptedSsnLast4Hash = "hash"
        };

    [Fact]
    public async Task CheckAsync_NoPriorAttempts_Allows()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var throttler = NewThrottler(db);

        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_BlankIp_AllowsWithoutQuerying()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var throttler = NewThrottler(db);

        var result = await throttler.CheckAsync(TenantId, LocationId, "");

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_FourRecentFailures_StillAllows()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var now = DateTime.UtcNow;
        for (int i = 0; i < 4; i++)
            db.IntakeVerificationAttempts.Add(Attempt(success: false, at: now.AddMinutes(-i)));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(4);
    }

    [Fact]
    public async Task CheckAsync_FiveRecentFailures_BlocksWithLockoutEndAt()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var now = DateTime.UtcNow;
        var oldest = now.AddMinutes(-10);
        db.IntakeVerificationAttempts.Add(Attempt(false, oldest));
        for (int i = 1; i < 5; i++)
            db.IntakeVerificationAttempts.Add(Attempt(false, now.AddMinutes(-i)));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeFalse();
        result.RecentFailures.Should().Be(5);
        result.LockoutEndAt.Should().NotBeNull();
        // Lockout ends 15 minutes after the oldest failure in window.
        result.LockoutEndAt!.Value.Should().BeCloseTo(oldest.AddMinutes(15), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CheckAsync_SuccessfulAttemptsIgnored_DoesNotCountTowardLimit()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var now = DateTime.UtcNow;
        // 5 successes + 0 failures → should still allow.
        for (int i = 0; i < 5; i++)
            db.IntakeVerificationAttempts.Add(Attempt(success: true, at: now.AddMinutes(-i)));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_OldFailuresOutsideWindow_DoNotCount()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var now = DateTime.UtcNow;
        // 5 failures, all >15 minutes ago → window expired, should allow.
        for (int i = 0; i < 5; i++)
            db.IntakeVerificationAttempts.Add(Attempt(false, now.AddMinutes(-20 - i)));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_DifferentIpSameLocation_DoesNotCount()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            db.IntakeVerificationAttempts.Add(Attempt(false, now.AddMinutes(-i), ip: "10.0.0.99"));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_DifferentLocationSameIp_DoesNotCount()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        // Seed extra Location so FK passes (same tenant, different location id).
        db.Locations.Add(new Location
        {
            LocationId = 999, TenantId = TenantId, Name = "Other", Address = "a",
            City = "c", State = "s", ZipCode = "0", Phone = "0",
            IsActive = true, IsPrimary = false, TimeZoneId = "UTC", EnableLongevity = false
        });
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            db.IntakeVerificationAttempts.Add(Attempt(false, now.AddMinutes(-i), locationId: 999));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_DifferentTenantSameIpSameLocation_DoesNotCount()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        // Seed extra Tenant + Location under that tenant so FK passes.
        db.Tenants.Add(new Tenant
        {
            TenantId = 999, Name = "Other", Subdomain = "other", Phone = "0",
            Email = "o@o.com", Address = "a", City = "c", State = "s"
        });
        db.Locations.Add(new Location
        {
            LocationId = 998, TenantId = 999, Name = "OtherLoc", Address = "a",
            City = "c", State = "s", ZipCode = "0", Phone = "0",
            IsActive = true, IsPrimary = true, TimeZoneId = "UTC", EnableLongevity = false
        });
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            db.IntakeVerificationAttempts.Add(Attempt(false, now.AddMinutes(-i), tenantId: 999, locationId: 998));
        await db.SaveChangesAsync();

        var throttler = NewThrottler(db);
        var result = await throttler.CheckAsync(TenantId, LocationId, Ip);

        result.Allowed.Should().BeTrue();
        result.RecentFailures.Should().Be(0);
    }

    [Fact]
    public async Task LogAttemptAsync_PersistsRowWithSuppliedFields()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        // Patient 42 must exist because LogAttempt writes PatientId and FK is enforced.
        db.Patients.Add(new Patient
        {
            PatientId = 42, TenantId = TenantId, Mrn = "MRN-42",
            FirstName = "Test", LastName = "Patient", Gender = "M",
            DateOfBirth = new DateOnly(1985, 6, 15)
        });
        await db.SaveChangesAsync();
        var throttler = NewThrottler(db);

        await throttler.LogAttemptAsync(
            tenantId: TenantId,
            locationId: LocationId,
            patientId: 42,
            attemptedDob: new DateOnly(1985, 6, 15),
            attemptedSsnLast4Hash: "hashed",
            success: true,
            ipAddress: Ip,
            userAgent: "Mozilla/5.0");

        await using var verify = _fx.CreateDbContext();
        var row = verify.IntakeVerificationAttempts.Single();
        row.TenantId.Should().Be(TenantId);
        row.LocationId.Should().Be(LocationId);
        row.PatientId.Should().Be(42);
        row.AttemptedDob.Should().Be(new DateOnly(1985, 6, 15));
        row.AttemptedSsnLast4Hash.Should().Be("hashed");
        row.IsSuccessful.Should().BeTrue();
        row.IpAddress.Should().Be(Ip);
        row.UserAgent.Should().Be("Mozilla/5.0");
        row.AttemptedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LogAttemptAsync_LongUserAgent_TruncatedTo500Chars()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var throttler = NewThrottler(db);
        var longUa = new string('x', 1000);

        await throttler.LogAttemptAsync(
            TenantId, LocationId, patientId: null, attemptedDob: null,
            attemptedSsnLast4Hash: "h", success: false, ipAddress: Ip, userAgent: longUa);

        await using var verify = _fx.CreateDbContext();
        var row = verify.IntakeVerificationAttempts.Single();
        row.UserAgent!.Length.Should().Be(500);
    }
}
