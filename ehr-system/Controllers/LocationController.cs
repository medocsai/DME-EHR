using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services;
using EHR.Helpers;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Controller for managing clinic locations within a tenant.
/// Supports multi-location functionality for physical therapy clinics.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly ILocationService _locationService;
    private readonly IAuthService _authService;
    private readonly ITenantProvider _tenantProvider;
    private readonly EhrDbContext _context;

    public LocationsController(
        ILocationService locationService,
        IAuthService authService,
        ITenantProvider tenantProvider,
        EhrDbContext context)
    {
        _locationService = locationService;
        _authService = authService;
        _tenantProvider = tenantProvider;
        _context = context;
    }

    /// <summary>
    /// Get all locations for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<LocationListDto>>> GetLocations([FromQuery] bool includeInactive = false)
    {
        var locations = await _locationService.GetLocationsAsync(includeInactive);
        return Ok(locations);
    }

    /// <summary>
    /// Get locations for dropdown selection (simplified list)
    /// </summary>
    [HttpGet("dropdown")]
    public async Task<ActionResult<List<LocationDropdownDto>>> GetLocationsForDropdown()
    {
        var locations = await _locationService.GetLocationsForDropdownAsync();
        return Ok(locations);
    }

    /// <summary>
    /// Get a specific location by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<LocationDetailDto>> GetLocation(int id)
    {
        var location = await _locationService.GetLocationByIdAsync(id);

        if (location == null)
            return NotFound(new { message = "Location not found" });

        return Ok(location);
    }

    /// <summary>
    /// Create a new location (Admin only)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Location>> CreateLocation([FromBody] LocationCreateDto dto)
    {
        // Check if user is Admin or higher
        var roleClaim = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value;
        if (!int.TryParse(roleClaim, out var role) || role > 1) // 0 = SuperAdmin, 1 = ClinicAdmin
        {
            return Forbid();
        }

        try
        {
            var location = await _locationService.CreateLocationAsync(dto);
            return CreatedAtAction(nameof(GetLocation), new { id = location.LocationId }, location);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing location (Admin only)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Location>> UpdateLocation(int id, [FromBody] LocationUpdateDto dto)
    {
        // Check if user is Admin or higher
        var roleClaim = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value;
        if (!int.TryParse(roleClaim, out var role) || role > 1)
        {
            return Forbid();
        }

        try
        {
            var location = await _locationService.UpdateLocationAsync(id, dto);

            if (location == null)
                return NotFound(new { message = "Location not found" });

            return Ok(location);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Deactivate a location (Admin only)
    /// Cannot deactivate the primary location or locations with active patients
    /// </summary>
    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult> DeactivateLocation(int id)
    {
        // Check if user is Admin or higher
        var roleClaim = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value;
        if (!int.TryParse(roleClaim, out var role) || role > 1)
        {
            return Forbid();
        }

        try
        {
            var result = await _locationService.DeactivateLocationAsync(id);

            if (!result)
                return NotFound(new { message = "Location not found" });

            return Ok(new { message = "Location deactivated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Reactivate a deactivated location (Admin only)
    /// </summary>
    [HttpPost("{id}/reactivate")]
    public async Task<ActionResult> ReactivateLocation(int id)
    {
        // Check if user is Admin or higher
        var roleClaim = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value;
        if (!int.TryParse(roleClaim, out var role) || role > 1)
        {
            return Forbid();
        }

        var result = await _locationService.ReactivateLocationAsync(id);

        if (!result)
            return NotFound(new { message = "Location not found" });

        return Ok(new { message = "Location reactivated successfully" });
    }

    /// <summary>
    /// Set a location as the primary/default location (Admin only)
    /// </summary>
    [HttpPost("{id}/set-primary")]
    public async Task<ActionResult> SetPrimaryLocation(int id)
    {
        // Check if user is Admin or higher
        var roleClaim = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value;
        if (!int.TryParse(roleClaim, out var role) || role > 1)
        {
            return Forbid();
        }

        try
        {
            var result = await _locationService.SetPrimaryLocationAsync(id);

            if (!result)
                return NotFound(new { message = "Location not found" });

            return Ok(new { message = "Location set as primary successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Toggle (or set) the per-location Longevity feature flag.
    /// Body: { "enableLongevity": true/false }. Admin only.
    /// Used by the Settings page quick-toggle card so admins don't have to
    /// open the full Edit Location dialog to flip this one boolean.
    /// </summary>
    [HttpPost("{id}/longevity")]
    public async Task<ActionResult> ToggleLongevity(int id, [FromBody] ToggleLongevityDto dto)
    {
        var roleClaim = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value;
        if (!int.TryParse(roleClaim, out var role) || role > 1)
        {
            return Forbid();
        }

        var updateDto = new LocationUpdateDto { EnableLongevity = dto.EnableLongevity };
        var result = await _locationService.UpdateLocationAsync(id, updateDto);
        if (result == null)
            return NotFound(new { message = "Location not found" });

        return Ok(new { locationId = id, enableLongevity = dto.EnableLongevity });
    }

    public class ToggleLongevityDto
    {
        public bool EnableLongevity { get; set; }
    }

    /// <summary>
    /// Switch the user's current location context.
    /// This generates a new JWT token with the selected location and returns it.
    /// The frontend should update localStorage and use the new token for subsequent requests.
    /// </summary>
    [HttpPost("switch")]
    public async Task<ActionResult<SwitchLocationResponseDto>> SwitchLocation([FromBody] SwitchLocationDto dto)
    {
        // Validate that the location exists and belongs to the user's tenant
        var isValid = await _locationService.ValidateLocationAccessAsync(dto.LocationId);
        if (!isValid)
        {
            return BadRequest(new SwitchLocationResponseDto
            {
                Success = false,
                Message = "Invalid location or location not accessible"
            });
        }

        // Get the location details
        var location = await _context.Locations.FindAsync(dto.LocationId);
        if (location == null)
        {
            return BadRequest(new SwitchLocationResponseDto
            {
                Success = false,
                Message = "Location not found"
            });
        }

        // Get the current user
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
        {
            return Unauthorized();
        }

        // Generate a new token with the updated location
        var newToken = _authService.GenerateToken(user, user.Tenant, location);

        // Log the location switch
        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId,
            UserId = userId,
            UserEmail = user.Email,
            EntityType = "Location",
            EntityId = dto.LocationId,
            Action = "SwitchLocation",
            Timestamp = DateTime.UtcNow,
            Changes = $"User switched to location: {location.Name}"
        });
        await _context.SaveChangesAsync();

        var timeZoneId = location.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        return Ok(new SwitchLocationResponseDto
        {
            Success = true,
            LocationId = location.LocationId,
            LocationName = location.Name,
            Message = $"Successfully switched to {location.Name}",
            Token = newToken,
            TimeZoneId = timeZoneId,
            TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId)
        });
    }

    /// <summary>
    /// Get the current location context from the JWT token
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult> GetCurrentLocation()
    {
        var locationIdClaim = User.FindFirst("LocationId");
        var locationNameClaim = User.FindFirst("LocationName");

        if (locationIdClaim == null || !int.TryParse(locationIdClaim.Value, out var locationId))
        {
            return Ok(new { LocationId = (int?)null, LocationName = (string?)null, HasLocation = false });
        }

        // Get the location to include timezone info
        var location = await _context.Locations.FindAsync(locationId);
        var timeZoneId = location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        return Ok(new
        {
            LocationId = locationId,
            LocationName = locationNameClaim?.Value,
            HasLocation = true,
            TimeZoneId = timeZoneId,
            TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId)
        });
    }

    /// <summary>
    /// Get a list of available timezones for dropdown selection
    /// </summary>
    [HttpGet("timezones")]
    public ActionResult<List<TimezoneOption>> GetAvailableTimezones()
    {
        return Ok(_locationService.GetAvailableTimezones());
    }

    /// <summary>
    /// Get the timezone for a specific location
    /// </summary>
    [HttpGet("{id}/timezone")]
    public async Task<ActionResult> GetLocationTimezone(int id)
    {
        var timeZoneId = await _locationService.GetLocationTimezoneAsync(id);

        return Ok(new
        {
            LocationId = id,
            TimeZoneId = timeZoneId,
            TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId),
            TimeZoneDisplayName = TimezoneHelper.GetTimezoneDisplayName(timeZoneId),
            UtcOffset = TimezoneHelper.GetUtcOffset(timeZoneId)
        });
    }
}
