using EHR.Models;
using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Controller for managing therapist/provider unavailability and time-off requests.
/// Handles leave management, approval workflows, and calendar integration.
/// </summary>
[ApiController]
[Route("api/therapist-unavailability")]
[Authorize]
public class TherapistUnavailabilityController : ControllerBase
{
    private readonly ITherapistUnavailabilityService _unavailabilityService;

    public TherapistUnavailabilityController(ITherapistUnavailabilityService unavailabilityService)
    {
        _unavailabilityService = unavailabilityService;
    }

    /// <summary>
    /// Get all unavailability records (optionally filtered).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TherapistUnavailabilityListDto>>> GetUnavailabilities(
        [FromQuery] int? providerId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] bool includeUnapproved = false)
    {
        var result = await _unavailabilityService.GetUnavailabilitiesAsync(
            providerId, startDate, endDate, includeUnapproved);
        return Ok(result);
    }

    /// <summary>
    /// Get unavailability by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TherapistUnavailabilityListDto>> GetUnavailability(int id)
    {
        var result = await _unavailabilityService.GetByIdAsync(id);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Create new unavailability/leave request.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<TherapistUnavailability>> CreateUnavailability(
        [FromBody] TherapistUnavailabilityCreateDto dto)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var result = await _unavailabilityService.CreateAsync(dto, userId);
            return CreatedAtAction(nameof(GetUnavailability), new { id = result.UnavailabilityId }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update unavailability record.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<TherapistUnavailability>> UpdateUnavailability(
        int id,
        [FromBody] TherapistUnavailabilityUpdateDto dto)
    {
        try
        {
            var userRole = int.Parse(User.FindFirst("Role")?.Value ?? "99");
            var result = await _unavailabilityService.UpdateAsync(id, dto, userRole);
            if (result == null)
                return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete unavailability record.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> DeleteUnavailability(int id)
    {
        var result = await _unavailabilityService.DeleteAsync(id);
        if (!result)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Approve unavailability request.
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> ApproveUnavailability(int id)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var result = await _unavailabilityService.ApproveAsync(id, userId);
        if (!result)
            return NotFound();
        return Ok(new { message = "Unavailability approved" });
    }

    /// <summary>
    /// Check if a provider is available for a specific time slot.
    /// </summary>
    [HttpPost("check-availability")]
    public async Task<ActionResult<UnavailabilityCheckResult>> CheckAvailability(
        [FromBody] UnavailabilityCheckRequest request)
    {
        var result = await _unavailabilityService.CheckAvailabilityAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Get calendar events for unavailabilities (for calendar display).
    /// </summary>
    [HttpGet("calendar-events")]
    public async Task<ActionResult<List<object>>> GetCalendarEvents(
        [FromQuery] int? providerId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        var unavailabilities = await _unavailabilityService.GetUnavailabilitiesAsync(
            providerId, startDate, endDate, false);

        var events = unavailabilities.Select(u => new
        {
            id = $"unavail-{u.UnavailabilityId}",
            title = $"Unavailable: {u.ProviderName} ({u.TypeName})",
            start = u.IsFullDay
                ? u.StartDate.ToString("yyyy-MM-dd")
                : u.StartDate.ToDateTime(u.StartTime ?? TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss"),
            end = u.IsFullDay
                ? u.EndDate.AddDays(1).ToString("yyyy-MM-dd")
                : u.EndDate.ToDateTime(u.EndTime ?? new TimeOnly(23, 59, 59)).ToString("yyyy-MM-ddTHH:mm:ss"),
            allDay = u.IsFullDay,
            backgroundColor = "#9e9e9e",
            borderColor = "#616161",
            textColor = "#ffffff",
            display = "background",
            extendedProps = new
            {
                unavailabilityId = u.UnavailabilityId,
                providerId = u.ProviderId,
                providerName = u.ProviderName,
                type = u.Type,
                typeName = u.TypeName,
                reason = u.Reason,
                isUnavailability = true
            }
        });

        return Ok(events);
    }
}
