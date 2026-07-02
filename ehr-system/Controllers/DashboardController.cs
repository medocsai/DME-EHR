using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;
using EHR.Helpers;

namespace EHR.Controllers;

/// <summary>
/// Controller for dashboard data and metrics.
/// Provides statistics, alerts, and appointment queues for the main dashboard.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ITenantProvider _tenantProvider;
    private readonly IAppointmentService _appointmentService;
    private readonly ILocationService _locationService;
    private readonly IDashboardService _dashboardService;

    public DashboardController(
        ITenantService tenantService,
        ITenantProvider tenantProvider,
        IAppointmentService appointmentService,
        ILocationService locationService,
        IDashboardService dashboardService)
    {
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _appointmentService = appointmentService;
        _locationService = locationService;
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Get dashboard statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats([FromQuery] int? providerId, [FromQuery] int? locationId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return BadRequest(new { message = "Tenant context required" });

        var stats = await _tenantService.GetTenantStatsAsync(_tenantProvider.TenantId.Value, providerId, locationId);
        return Ok(stats);
    }

    /// <summary>
    /// Gets appointments needing attention for the dashboard.
    /// Returns: 1) Today's appointments waiting for check-in
    ///          2) Checked-in appointments with missing notes
    ///          3) Checked-in appointments with notes requiring signature
    /// Excludes: Fully complete appointments (checked in + notes + signed) and cancelled appointments
    /// Uses location's timezone for "today" calculation.
    /// </summary>
    [HttpGet("appointments")]
    public async Task<ActionResult<DashboardAppointmentsDto>> GetDashboardAppointments([FromQuery] int? providerId, [FromQuery] int? locationId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return BadRequest(new { message = "Tenant context required" });

        // Get location's timezone for "today" calculation
        string? locationTimeZoneId = null;
        if (locationId.HasValue)
        {
            locationTimeZoneId = await _locationService.GetLocationTimezoneAsync(locationId.Value);
        }
        // Default to Central Time (America/Chicago) if no location specified
        locationTimeZoneId ??= "America/Chicago";

        // Get today's date in the location's timezone
        var nowUtc = DateTime.UtcNow;
        var locationNow = TimezoneHelper.ConvertFromUtc(nowUtc, locationTimeZoneId);
        var todayInLocationTz = locationNow.Date;

        // Convert location's today start/end to UTC for database queries
        var todayStartUtc = TimezoneHelper.GetStartOfDayUtc(DateOnly.FromDateTime(todayInLocationTz), locationTimeZoneId);
        var tomorrowStartUtc = TimezoneHelper.GetStartOfDayUtc(DateOnly.FromDateTime(todayInLocationTz.AddDays(1)), locationTimeZoneId);

        // Get today's appointments (for check-in queue) using location-aware timezone dates
        var todaysAppointments = await _appointmentService.GetAppointmentsAsync(
            startDate: todayStartUtc,
            endDate: tomorrowStartUtc,
            providerId: providerId,
            locationId: locationId);

        // Get past appointments with incomplete documentation (last 90 days)
        var pastStartUtc = TimezoneHelper.GetStartOfDayUtc(DateOnly.FromDateTime(todayInLocationTz.AddDays(-90)), locationTimeZoneId);
        var pastAppointments = await _appointmentService.GetAppointmentsAsync(
            startDate: pastStartUtc,
            endDate: todayStartUtc,
            providerId: providerId,
            locationId: locationId);

        // Filter and categorize appointments
        // WaitingForCheckIn: Only TODAY's appointments that are Scheduled or Confirmed
        var waitingForCheckIn = todaysAppointments
            .Where(a => a.Status == (int)AppointmentStatus.Scheduled || a.Status == (int)AppointmentStatus.Confirmed)
            .OrderBy(a => a.StartTime)
            .ToList();

        // InProgress: checked-in appointments not yet completed (today + past)
        var inProgressAppointments = todaysAppointments
            .Concat(pastAppointments)
            .Where(a => a.DocumentationStatus == (int)AppointmentDocumentationStatus.InProgress)
            .OrderBy(a => a.StartTime)
            .ToList();

        // "Today's Appointments" list should ONLY contain today's appointments
        // Filter out inactive statuses: NoShow (5), Cancelled (6), Rescheduled (7), Missed (8)
        var todayOnlyAppointments = todaysAppointments
            .Where(a => a.Status != (int)AppointmentStatus.NoShow
                && a.Status != (int)AppointmentStatus.Cancelled
                && a.Status != (int)AppointmentStatus.Rescheduled
                && a.Status != (int)AppointmentStatus.Missed)
            .OrderBy(a => a.Status == (int)AppointmentStatus.Completed ? 1 : 0)
            .ThenBy(a => a.StartTime)
            .ToList();

        return Ok(new DashboardAppointmentsDto
        {
            Appointments = todayOnlyAppointments,
            WaitingForCheckInCount = waitingForCheckIn.Count,
            MissingNotesCount = inProgressAppointments.Count,
            RequiresSignatureCount = 0,
            TotalCount = todayOnlyAppointments.Count
        });
    }

    /// <summary>
    /// Gets appointments where patient checked in but no clinical note exists.
    /// Used by the "Missing Notes" dashboard alert widget.
    /// </summary>
    [HttpGet("missing-notes")]
    public async Task<ActionResult<List<MissingNotesItemDto>>> GetMissingNotes(
        [FromQuery] int? providerId,
        [FromQuery] int? locationId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return BadRequest(new { message = "Tenant context required" });

        var items = await _dashboardService.GetMissingNotesAsync(providerId, locationId);
        return Ok(items);
    }

    /// <summary>
    /// Gets clinical notes that need signature (Draft or PendingSignature status).
    /// Used by the "Need Signature" dashboard alert widget.
    /// </summary>
    [HttpGet("missing-signature")]
    public async Task<ActionResult<List<MissingSignatureItemDto>>> GetMissingSignatures(
        [FromQuery] int? providerId,
        [FromQuery] int? locationId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return BadRequest(new { message = "Tenant context required" });

        var items = await _dashboardService.GetMissingSignaturesAsync(providerId, locationId);
        return Ok(items);
    }
}
