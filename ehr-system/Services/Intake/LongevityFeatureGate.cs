using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services.Intake;

/// <summary>
/// Per-location feature flag: Longevity intake section. One boolean column on Location
/// (per Stock Problem: refactor to features table only when 2+ flags exist).
///
/// No cache. A boolean DB read with index is sub-millisecond and caching only
/// introduces staleness when admins flip the flag. Previous 30-second cache
/// caused a real bug on 2026-04-24 — removed.
/// </summary>
public interface ILongevityFeatureGate
{
    Task<bool> IsEnabledAsync(int locationId);
}

public class LongevityFeatureGate : ILongevityFeatureGate
{
    private readonly EhrDbContext _context;

    public LongevityFeatureGate(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsEnabledAsync(int locationId)
    {
        return await _context.Locations
            .Where(l => l.LocationId == locationId)
            .Select(l => l.EnableLongevity)
            .FirstOrDefaultAsync();
    }
}
