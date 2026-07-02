using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Controllers;

/// <summary>
/// Controller for appointment scheduling and management.
/// Handles CRUD operations, check-in/check-out workflow, recurring appointments, and availability checks.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[PhiAccessAudit(EntityType = "Appointment")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentsController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    /// <summary>CheckOutAsync 
    /// Get appointments with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AppointmentListDto>>> GetAppointments(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? providerId,
        [FromQuery] int? patientId,
        [FromQuery] AppointmentStatus? status,
        [FromQuery] int? locationId)
    {
        try
        {
            var appointments = await _appointmentService.GetAppointmentsAsync(startDate, endDate, providerId, patientId, status, locationId);
            return Ok(appointments);
        }
        catch (Exception ex) { }
        return BadRequest();
    }

    /// <summary>
    /// Get appointment by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AppointmentListDto>> GetAppointment(int id)
    {
        var appointment = await _appointmentService.GetAppointmentByIdAsync(id);
        if (appointment == null) return NotFound();
        return Ok(appointment);
    }

    /// <summary>
    /// Create a new appointment.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult<Appointment>> CreateAppointment([FromBody] AppointmentCreateDto dto)
    {
        try
        {
            var userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
            var appointment = await _appointmentService.CreateAppointmentAsync(dto, createdByUserId: userId);
            return CreatedAtAction(nameof(GetAppointment), new { id = appointment.AppointmentId }, appointment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing appointment.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult<Appointment>> UpdateAppointment(int id, [FromBody] AppointmentUpdateDto dto)
    {
        var appointment = await _appointmentService.UpdateAppointmentAsync(id, dto);
        if (appointment == null) return NotFound();
        return Ok(appointment);
    }

    /// <summary>
    /// Extend an appointment's end time (run-over support). Validates status,
    /// time bounds, and same-provider overlap. On overlap returns 409 with a
    /// conflict body so the UI can show "Extend Anyway"; resending with
    /// Force=true bypasses the conflict check.
    /// </summary>
    [HttpPost("{id}/extend")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult<ExtendAppointmentResultDto>> ExtendAppointment(int id, [FromBody] ExtendAppointmentRequestDto dto)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        int? changedByUserId = userIdClaim != null ? int.Parse(userIdClaim) : null;

        var result = await _appointmentService.ExtendAppointmentAsync(id, dto, changedByUserId);

        if (result.Success) return Ok(result);

        return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(result),
            "CONFLICT" => Conflict(result),
            // INVALID_STATUS / INVALID_TIME / anything else with Success=false
            _ => BadRequest(result)
        };
    }

    /// <summary>
    /// Cancel an appointment.
    /// </summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult> CancelAppointment(int id, [FromBody] CancelRequest? request = null)
    {
        // Get the user ID from the JWT token
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        int? cancelledByUserId = userIdClaim != null ? int.Parse(userIdClaim) : null;

        try
        {
            var result = await _appointmentService.CancelAppointmentAsync(id, request?.Reason, cancelledByUserId);
            if (!result) return NotFound();
            return Ok(new { message = "Appointment cancelled" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Marks an appointment as Missed (status 8). Used when rescheduling past appointments
    /// that were never checked in. This preserves the no-show history for reporting
    /// while removing it from the dashboard's active appointment queues.
    /// </summary>
    [HttpPost("{id}/mark-missed")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult> MarkAsMissed(int id, [FromBody] MarkAsMissedRequest? request = null)
    {
        // Get the user ID from the JWT token
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        int? markedByUserId = userIdClaim != null ? int.Parse(userIdClaim) : null;

        try
        {
            var result = await _appointmentService.MarkAsMissedAsync(id, request?.Reason, markedByUserId, request?.RescheduledToAppointmentId);
            if (!result) return NotFound();
            return Ok(new { message = "Appointment marked as missed" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reschedule an appointment. Based on the appointment date:
    /// - Future appointments: Cancels the original and returns info to create a new one
    /// - Past/current appointments: Marks as missed and returns info to create a new one
    /// The client must then create the new appointment with RescheduledFromAppointmentId set.
    /// </summary>
    [HttpPost("{id}/reschedule")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult<RescheduleAppointmentResponse>> RescheduleAppointment(int id)
    {
        // Get the user ID from the JWT token
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        int? rescheduledByUserId = userIdClaim != null ? int.Parse(userIdClaim) : null;

        var result = await _appointmentService.RescheduleAppointmentAsync(id, rescheduledByUserId);

        if (!result.Success)
            return BadRequest(new { error = result.Message });

        return Ok(result);
    }

    /// <summary>
    /// Reinstate a cancelled appointment.
    /// </summary>
    [HttpPost("{id}/reinstate")]
    [Authorize(Roles = "0,1")] // Only SuperAdmin and ClinicAdmin can reinstate
    public async Task<ActionResult> ReinstateAppointment(int id)
    {
        var result = await _appointmentService.ReinstateAppointmentAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Appointment reinstated" });
    }

    /// <summary>
    /// Check in a patient for their appointment.
    /// </summary>
    [HttpPost("{id}/checkin")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult<Appointment>> CheckIn(int id, [FromBody] AppointmentCheckInDto dto)
    {
        var appointment = await _appointmentService.CheckInAsync(id, dto);
        if (appointment == null) return NotFound();
        return Ok(appointment);
    }

    /// <summary>
    /// Check out a patient from their appointment.
    /// </summary>
    [HttpPost("{id}/checkout")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult<Appointment>> CheckOut(int id)
    {
        try
        {
            var appointment = await _appointmentService.CheckOutAsync(id);
            if (appointment == null) return NotFound();
            return Ok(appointment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Get AI-suggested CPT codes for an appointment's clinical notes.
    /// </summary>
    [HttpPost("{id}/suggest-cpt")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult<CptSuggestionResponse>> SuggestCptForCheckout(int id)
    {
        try
        {
            var result = await _appointmentService.SuggestCptForCheckoutAsync(id);
            return Ok(result);
        }
        catch (Exception)
        {
            return StatusCode(500, new CptSuggestionResponse
            {
                Success = false,
                ErrorMessage = "CPT suggestion service unavailable"
            });
        }
    }

    /// <summary>
    /// Get AI-suggested ICD-10 diagnosis codes for an appointment's clinical notes.
    /// </summary>
    [HttpPost("{id}/suggest-icd")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult<IcdSuggestionResponse>> SuggestIcdForCheckout(int id)
    {
        try
        {
            var result = await _appointmentService.SuggestIcdForCheckoutAsync(id);
            return Ok(result);
        }
        catch (Exception)
        {
            return StatusCode(500, new IcdSuggestionResponse
            {
                Success = false,
                ErrorMessage = "ICD-10 suggestion service unavailable"
            });
        }
    }

    /// <summary>
    /// Check out with CPT codes - creates charges and closes encounter.
    /// </summary>
    [HttpPost("{id}/checkout-with-cpt")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult<Appointment>> CheckOutWithCpt(int id, [FromBody] CheckoutWithCptRequest request)
    {
        try
        {
            var appointment = await _appointmentService.CheckOutWithCptAsync(id, request);
            if (appointment == null) return NotFound();
            return Ok(appointment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Start visit - transition appointment from CheckedIn to InProgress.
    /// </summary>
    [HttpPost("{id}/start-visit")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult<Appointment>> StartVisit(int id)
    {
        var appointment = await _appointmentService.StartVisitAsync(id);
        if (appointment == null) return NotFound();
        return Ok(appointment);
    }

    /// <summary>
    /// Get available time slots for a provider on a specific date.
    /// If providerId is not specified or 0, returns slots for all providers.
    /// </summary>
    [HttpGet("slots")]
    public async Task<ActionResult<List<ScheduleSlotDto>>> GetAvailableSlots(
        [FromQuery] int? providerId,
        [FromQuery] DateTime date,
        [FromQuery] int durationMinutes = 30)
    {
        var slots = await _appointmentService.GetAvailableSlotsAsync(providerId ?? 0, date, durationMinutes);
        return Ok(slots);
    }

    /// <summary>
    /// Check for scheduling conflicts.
    /// </summary>
    [HttpGet("check-conflict")]
    public async Task<ActionResult> CheckConflict(
        [FromQuery] int providerId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var conflict = await _appointmentService.CheckForConflictAsync(providerId, startTime, endTime);
        return Ok(conflict);
    }

    /// <summary>
    /// Check availability for multiple dates (recurring appointments).
    /// </summary>
    [HttpPost("check-recurring-availability")]
    public async Task<ActionResult<RecurringAvailabilityCheckResponse>> CheckRecurringAvailability(
        [FromBody] RecurringAvailabilityCheckRequest request)
    {
        var result = await _appointmentService.CheckRecurringAvailabilityAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Create a recurring appointment series.
    /// </summary>
    [HttpPost("recurring")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult<RecurringAppointmentCreateResponse>> CreateRecurringAppointments(
        [FromBody] RecurringAppointmentCreateRequest request)
    {
        try
        {
            var result = await _appointmentService.CreateRecurringAppointmentsAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Auto-assign available providers for multiple time slots.
    /// Used when "Any Available Provider" is selected in recurring appointments.
    /// </summary>
    [HttpPost("auto-assign-providers")]
    public async Task<ActionResult<AutoAssignProvidersResponse>> AutoAssignProviders(
        [FromBody] AutoAssignProvidersRequest request)
    {
        var result = await _appointmentService.AutoAssignProvidersAsync(request);
        return Ok(result);
    }
}

/// <summary>
/// Request model for cancelling an appointment.
/// </summary>
public class CancelRequest
{
    public string? Reason { get; set; }
}

/// <summary>
/// Request model for marking an appointment as missed.
/// </summary>
public class MarkAsMissedRequest
{
    public string? Reason { get; set; }
    public int? RescheduledToAppointmentId { get; set; }
}

/// <summary>
/// Request model for rescheduling an appointment.
/// </summary>
public class RescheduleRequest
{
    // Currently no additional parameters needed - the service determines
    // whether to cancel or mark as missed based on the appointment date
}
