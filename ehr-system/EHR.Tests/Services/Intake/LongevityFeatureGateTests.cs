using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 tests for LongevityFeatureGate. Reads the single boolean
/// Location.EnableLongevity column. No cache (removed 2026-04-24 after
/// a staleness bug — the DB read is fast enough not to need one).
///
/// Contract:
/// - Returns the column value for a real Location.
/// - Returns false for a missing Location (default of bool).
/// - Changes in the DB are reflected immediately (no cache).
/// </summary>
[Collection("Db")]
public class LongevityFeatureGateTests
{
    private readonly SqlServerFixture _fx;

    public LongevityFeatureGateTests(SqlServerFixture fx) => _fx = fx;

    private static LongevityFeatureGate NewGate(EhrDbContext db) => new(db);

    private async Task<int> SeedLocationAsync(int locationId, bool enableLongevity)
    {
        await using var db = _fx.CreateDbContext();
        db.Locations.Add(new Location
        {
            LocationId = locationId,
            TenantId = 1,
            Name = $"Loc-{locationId}",
            Address = "a", City = "c", State = "s",
            ZipCode = "0", Phone = "0",
            IsActive = true, IsPrimary = false,
            TimeZoneId = "UTC",
            EnableLongevity = enableLongevity
        });
        await db.SaveChangesAsync();
        return locationId;
    }

    [Fact]
    public async Task IsEnabledAsync_LocationEnabled_ReturnsTrue()
    {
        await _fx.ResetAsync();
        await SeedLocationAsync(100, enableLongevity: true);
        await using var db = _fx.CreateDbContext();
        var gate = NewGate(db);

        var result = await gate.IsEnabledAsync(100);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_LocationDisabled_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await SeedLocationAsync(101, enableLongevity: false);
        await using var db = _fx.CreateDbContext();
        var gate = NewGate(db);

        var result = await gate.IsEnabledAsync(101);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_LocationMissing_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var gate = NewGate(db);

        var result = await gate.IsEnabledAsync(999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_AfterFlagFlip_ReflectsNewValueImmediately()
    {
        await _fx.ResetAsync();
        await SeedLocationAsync(102, enableLongevity: true);
        await using var db = _fx.CreateDbContext();
        var gate = NewGate(db);

        (await gate.IsEnabledAsync(102)).Should().BeTrue();

        // Flip the DB value. Because there's no cache, the next read must reflect it.
        await using (var edit = _fx.CreateDbContext())
        {
            edit.Locations.Single(l => l.LocationId == 102).EnableLongevity = false;
            await edit.SaveChangesAsync();
        }

        (await gate.IsEnabledAsync(102)).Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_SeparateLocations_IndependentValues()
    {
        await _fx.ResetAsync();
        await SeedLocationAsync(200, enableLongevity: true);
        await SeedLocationAsync(201, enableLongevity: false);
        await using var db = _fx.CreateDbContext();
        var gate = NewGate(db);

        (await gate.IsEnabledAsync(200)).Should().BeTrue();
        (await gate.IsEnabledAsync(201)).Should().BeFalse();
    }
}
