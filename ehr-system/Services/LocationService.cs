using Microsoft.EntityFrameworkCore;
using EHR.Data;
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
                PatientCount = l.Patients.Count(p => p.IsDeleted != true),
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
            .Include(l => l.Patients)
            .Include(l => l.Appointments)
            .Include(l => l.ProviderSchedules)
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
            PatientCount = location.Patients.Count(p => p.IsDeleted != true),
            ProviderCount = location.ProviderSchedules.Select(ps => ps.ProviderId).Distinct().Count(),
            AppointmentCount = location.Appointments.Count,
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

        // If this is set as primary, unset any existing primary location
        if (dto.IsPrimary)
        {
            var existingPrimary = await _context.Locations
                .Where(l => l.TenantId == tenantId && l.IsPrimary == true)
                .ToListAsync();

            foreach (var loc in existingPrimary)
            {
                loc.IsPrimary = false;
            }
        }

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

        // If setting as primary, unset any existing primary location
        if (dto.IsPrimary == true && location.IsPrimary != true)
        {
            var existingPrimary = await _context.Locations
                .Where(l => l.TenantId == location.TenantId && l.IsPrimary == true && l.LocationId != locationId)
                .ToListAsync();

            foreach (var loc in existingPrimary)
            {
                loc.IsPrimary = false;
            }
        }

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
        return location;
    }

    public async Task<bool> DeactivateLocationAsync(int locationId)
    {
        var location = await _context.Locations
            .Include(l => l.Patients)
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

        // Cannot deactivate if there are active patients
        var activePatientCount = location.Patients.Count(p => p.IsDeleted != true);
        if (activePatientCount > 0)
        {
            throw new InvalidOperationException($"Cannot deactivate location. There are {activePatientCount} active patients assigned to this location. Please reassign or discharge patients first.");
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

        // Unset any existing primary location
        var existingPrimary = await _context.Locations
            .Where(l => l.TenantId == location.TenantId && l.IsPrimary == true && l.LocationId != locationId)
            .ToListAsync();

        foreach (var loc in existingPrimary)
        {
            loc.IsPrimary = false;
        }

        location.IsPrimary = true;
        await _context.SaveChangesAsync();

        return true;
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
