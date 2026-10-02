using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// Service interface for managing clinic locations within a tenant.
/// Locations provide a sub-filter within tenant isolation.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Get all locations for the current tenant
    /// </summary>
    Task<List<LocationListDto>> GetLocationsAsync(bool includeInactive = false);

    /// <summary>
    /// Get locations for dropdown selection
    /// </summary>
    Task<List<LocationDropdownDto>> GetLocationsForDropdownAsync();

    /// <summary>
    /// Get a specific location by ID
    /// </summary>
    Task<LocationDetailDto?> GetLocationByIdAsync(int locationId);

    /// <summary>
    /// Get the default (primary) location for the current tenant
    /// </summary>
    Task<Location?> GetDefaultLocationAsync();

    /// <summary>
    /// Create a new location
    /// </summary>
    Task<Location> CreateLocationAsync(LocationCreateDto dto);

    /// <summary>
    /// Update an existing location
    /// </summary>
    Task<Location?> UpdateLocationAsync(int locationId, LocationUpdateDto dto);

    /// <summary>
    /// Deactivate a location (soft delete - cannot delete if patients exist)
    /// </summary>
    Task<bool> DeactivateLocationAsync(int locationId);

    /// <summary>
    /// Reactivate a deactivated location
    /// </summary>
    Task<bool> ReactivateLocationAsync(int locationId);

    /// <summary>
    /// Validate that a location belongs to the current tenant
    /// </summary>
    Task<bool> ValidateLocationAccessAsync(int locationId);

    /// <summary>
    /// Set a location as the primary/default location
    /// </summary>
    Task<bool> SetPrimaryLocationAsync(int locationId);

    /// <summary>
    /// Get the timezone ID for a specific location
    /// </summary>
    Task<string> GetLocationTimezoneAsync(int locationId);

    /// <summary>
    /// Get a list of available timezones for dropdown selection
    /// </summary>
    List<TimezoneOption> GetAvailableTimezones();
}

/// <summary>
/// Service for managing clinic locations within a tenant.
/// </summary>
public class LocationService : ILocationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public LocationService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<LocationListDto>> GetLocationsAsync(bool includeInactive = false)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return new List<LocationListDto>();

        var query = _context.Locations
            .Where(l => l.TenantId == _tenantProvider.TenantId.Value);

        if (!includeInactive)
        {
            query = query.Where(l => l.IsActive == true);
        }

        var locations = await query
            .OrderByDescending(l => l.IsPrimary)
            .ThenBy(l => l.Name)
            .Select(l => new LocationListDto
            {
                LocationId = l.LocationId,
                TenantId = l.TenantId,
                Name = l.Name,
                Address = l.Address,
                City = l.City,
                State = l.State,
                ZipCode = l.ZipCode,
                Phone = l.Phone,
                IsActive = l.IsActive ?? true,
                IsPrimary = l.IsPrimary ?? false,
                TimeZoneId = l.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId,
                CreatedAt = l.CreatedAt,
                EnableLongevity = l.EnableLongevity
            })
            .ToListAsync();

        // Add timezone abbreviations (requires computation not available in LINQ to SQL)
        foreach (var location in locations)
        {
            location.TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(location.TimeZoneId);
        }

        return locations;
    }

    public async Task<List<LocationDropdownDto>> GetLocationsForDropdownAsync()
    {
        if (!_tenantProvider.TenantId.HasValue)
            return new List<LocationDropdownDto>();

        var locations = await _context.Locations
            .Where(l => l.TenantId == _tenantProvider.TenantId.Value && l.IsActive == true)
            .OrderByDescending(l => l.IsPrimary)
            .ThenBy(l => l.Name)
            .Select(l => new LocationDropdownDto
            {
                LocationId = l.LocationId,
                Name = l.Name,
                IsPrimary = l.IsPrimary ?? false,
                IsActive = l.IsActive ?? true,
                TimeZoneId = l.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId
            })
            .ToListAsync();

        // Add timezone abbreviations
        foreach (var location in locations)
        {
            location.TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(location.TimeZoneId);
        }

        return locations;
    }

    public async Task<LocationDetailDto?> GetLocationByIdAsync(int locationId)
    {
        var query = _context.Locations
            .Include(l => l.Tenant)
            .Where(l => l.LocationId == locationId);

        // Tenant isolation - verify location belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(l => l.TenantId == _tenantProvider.TenantId.Value);
        }

        var location = await query.FirstOrDefaultAsync();

        if (location == null)
            return null;

        var timeZoneId = location.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        return new LocationDetailDto
        {
            LocationId = location.LocationId,
            TenantId = location.TenantId,
            TenantName = location.Tenant?.Name,
            Name = location.Name,
            Address = location.Address,
            City = location.City,
            State = location.State,
            ZipCode = location.ZipCode,
            Phone = location.Phone,
            IsActive = location.IsActive ?? true,
            IsPrimary = location.IsPrimary ?? false,
            TimeZoneId = timeZoneId,
            TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId),
            TimeZoneDisplayName = TimezoneHelper.GetTimezoneDisplayName(timeZoneId),
            UtcOffset = TimezoneHelper.GetUtcOffset(timeZoneId),
            CreatedAt = location.CreatedAt,
            FacilityNpi = location.FacilityNpi,
            PlaceOfServiceCode = location.PlaceOfServiceCode,
            EnableLongevity = location.EnableLongevity
        };
    }

    public async Task<Location?> GetDefaultLocationAsync()
    {
        if (!_tenantProvider.TenantId.HasValue)
            return null;

        // First try to get the primary location
        var defaultLocation = await _context.Locations
            .Where(l => l.TenantId == _tenantProvider.TenantId.Value &&
                       l.IsActive == true &&
                       l.IsPrimary == true)
            .FirstOrDefaultAsync();

        // If no primary location, get the first active location
        if (defaultLocation == null)
        {
            defaultLocation = await _context.Locations
                .Where(l => l.TenantId == _tenantProvider.TenantId.Value && l.IsActive == true)
                .OrderBy(l => l.CreatedAt)
                .FirstOrDefaultAsync();
        }

        return defaultLocation;
    }

    public async Task<Location> CreateLocationAsync(LocationCreateDto dto)
    {
        if (!_tenantProvider.TenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required to create a location");

        var tenantId = _tenantProvider.TenantId.Value;

        await using var tx = await _context.Database.BeginTransactionAsync();

        // If this is set as primary, demote the current one FIRST (see
        // DemoteOtherPrimariesAsync for why it is a save of its own).
        if (dto.IsPrimary)
            await DemoteOtherPrimariesAsync(tenantId, exceptLocationId: null);

        // Validate timezone if provided
        var timeZoneId = dto.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
        if (!TimezoneHelper.IsValidTimezone(timeZoneId))
        {
            throw new ArgumentException($"Invalid timezone identifier: {dto.TimeZoneId}. Please use a valid IANA timezone (e.g., 'America/New_York').");
        }

        var location = new Location
        {
            TenantId = tenantId,
            Name = dto.Name,
            Address = dto.Address,
            City = dto.City,
            State = dto.State,
            ZipCode = dto.ZipCode,
            Phone = dto.Phone,
            IsPrimary = dto.IsPrimary,
            TimeZoneId = timeZoneId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            PortalCode = Guid.NewGuid().ToString("N")[..8],
            EnableLongevity = dto.EnableLongevity
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        return location;
    }

    public async Task<Location?> UpdateLocationAsync(int locationId, LocationUpdateDto dto)
    {
        var location = await _context.Locations.FindAsync(locationId);
        if (location == null)
            return null;

        // Tenant isolation
        if (_tenantProvider.TenantId.HasValue && location.TenantId != _tenantProvider.TenantId.Value)
            return null;

        await using var tx = await _context.Database.BeginTransactionAsync();

        // If setting as primary, demote the current one FIRST (see
        // DemoteOtherPrimariesAsync for why it is a save of its own).
        if (dto.IsPrimary == true && location.IsPrimary != true)
            await DemoteOtherPrimariesAsync(location.TenantId, exceptLocationId: locationId);

        // Cannot unset primary if this is the only location or the only primary
        if (dto.IsPrimary == false && location.IsPrimary == true)
        {
            var otherActiveLocations = await _context.Locations
                .CountAsync(l => l.TenantId == location.TenantId && l.LocationId != locationId && l.IsActive == true);

            if (otherActiveLocations == 0)
            {
                throw new InvalidOperationException("Cannot remove primary status. There must be at least one primary location.");
            }
        }

        // Validate timezone if provided
        if (dto.TimeZoneId != null && !TimezoneHelper.IsValidTimezone(dto.TimeZoneId))
        {
            throw new ArgumentException($"Invalid timezone identifier: {dto.TimeZoneId}. Please use a valid IANA timezone (e.g., 'America/New_York').");
        }

        // Apply updates
        if (dto.Name != null) location.Name = dto.Name;
        if (dto.Address != null) location.Address = dto.Address;
        if (dto.City != null) location.City = dto.City;
        if (dto.State != null) location.State = dto.State;
        if (dto.ZipCode != null) location.ZipCode = dto.ZipCode;
        if (dto.Phone != null) location.Phone = dto.Phone;
        if (dto.IsActive.HasValue) location.IsActive = dto.IsActive.Value;
        if (dto.IsPrimary.HasValue) location.IsPrimary = dto.IsPrimary.Value;
        if (dto.TimeZoneId != null) location.TimeZoneId = dto.TimeZoneId;
        if (dto.FacilityNpi != null) location.FacilityNpi = dto.FacilityNpi;
        if (dto.PlaceOfServiceCode != null) location.PlaceOfServiceCode = dto.PlaceOfServiceCode;
        if (dto.EnableLongevity.HasValue) location.EnableLongevity = dto.EnableLongevity.Value;

        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        return location;
    }

    public async Task<bool> DeactivateLocationAsync(int locationId)
    {
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location == null)
            return false;

        // Tenant isolation
        if (_tenantProvider.TenantId.HasValue && location.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Cannot deactivate the primary location
        if (location.IsPrimary == true)
        {
            throw new InvalidOperationException("Cannot deactivate the primary location. Please set another location as primary first.");
        }

        location.IsActive = false;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ReactivateLocationAsync(int locationId)
    {
        var location = await _context.Locations.FindAsync(locationId);

        if (location == null)
            return false;

        // Tenant isolation
        if (_tenantProvider.TenantId.HasValue && location.TenantId != _tenantProvider.TenantId.Value)
            return false;

        location.IsActive = true;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ValidateLocationAccessAsync(int locationId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return false;

        return await _context.Locations
            .AnyAsync(l => l.LocationId == locationId &&
                          l.TenantId == _tenantProvider.TenantId.Value &&
                          l.IsActive == true);
    }

    public async Task<bool> SetPrimaryLocationAsync(int locationId)
    {
        var location = await _context.Locations.FindAsync(locationId);

        if (location == null)
            return false;

        // Tenant isolation
        if (_tenantProvider.TenantId.HasValue && location.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Location must be active to be set as primary
        if (location.IsActive != true)
        {
            throw new InvalidOperationException("Cannot set an inactive location as primary. Please reactivate the location first.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync();

        await DemoteOtherPrimariesAsync(location.TenantId, exceptLocationId: locationId);

        location.IsPrimary = true;
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        return true;
    }

    /// <summary>
    /// Clears the primary flag on the tenant's other branches and SAVES, before
    /// the caller marks the new primary.
    ///
    /// WHY A SAVE OF ITS OWN. UX_Locations_OnePrimaryPerTenant allows one
    /// primary per tenant and SQL Server checks it per statement. Changing both
    /// rows in one SaveChanges sends two UPDATEs in key order, so moving the
    /// primary to a LOWER LocationId set the new one first, briefly had two
    /// primaries, and failed with a 500: making branch 2 primary worked, making
    /// branch 1 primary again did not. Demote, save, then promote; the caller
    /// wraps both in one transaction so a failure leaves the old primary.
    /// </summary>
    private async Task DemoteOtherPrimariesAsync(int tenantId, int? exceptLocationId)
    {
        var others = await _context.Locations
            .Where(l => l.TenantId == tenantId && l.IsPrimary == true
                        && (exceptLocationId == null || l.LocationId != exceptLocationId))
            .ToListAsync();

        if (others.Count == 0) return;

        foreach (var l in others) l.IsPrimary = false;
        await _context.SaveChangesAsync();
    }

    public async Task<string> GetLocationTimezoneAsync(int locationId)
    {
        var location = await _context.Locations
            .Where(l => l.LocationId == locationId)
            .Select(l => new { l.TimeZoneId, l.TenantId })
            .FirstOrDefaultAsync();

        if (location == null)
            return TimezoneHelper.DefaultTimeZoneId;

        // Tenant isolation
        if (_tenantProvider.TenantId.HasValue && location.TenantId != _tenantProvider.TenantId.Value)
            return TimezoneHelper.DefaultTimeZoneId;

        return location.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
    }

    public List<TimezoneOption> GetAvailableTimezones()
    {
        return TimezoneHelper.GetCommonTimezones();
    }
}
