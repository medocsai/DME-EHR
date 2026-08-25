using EHR.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// API controller for telehealth operations.
/// Mixed anonymous (patient-facing) and authorized (clinical-facing) endpoints.
/// </summary>
[ApiController]
[Route("api/telehealth")]
public class TelehealthController : ControllerBase
{
    private readonly ITelehealthService _telehealthService;
    private readonly ILogger<TelehealthController> _logger;

    public TelehealthController(
        ITelehealthService telehealthService,
        ILogger<TelehealthController> logger)
    {
        _telehealthService = telehealthService;
        _logger = logger;
    }

    /// <summary>
    /// Validate a telehealth token and return appointment info for the patient join page.
    /// Public endpoint — no auth required.
    /// </summary>
    [HttpGet("validate/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<TelehealthJoinInfo>> ValidateToken(string token)
    {
        var result = await _telehealthService.ValidateTokenAsync(token);

        if (!result.IsValid)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Verify patient identity (LastName + DateOfBirth + ZipCode) before
    /// allowing into the waiting room. (2026-05: SSN removed from this flow.)
    /// Public endpoint — no auth required.
    /// </summary>
    [HttpPost("verify-identity/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<TelehealthVerifyIdentityResult>> VerifyIdentity(
        string token,
        [FromBody] TelehealthVerifyIdentityRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new TelehealthVerifyIdentityResult { Success = false, Message = "Please enter your last name, date of birth, and ZIP code." });

        var result = await _telehealthService.VerifyPatientIdentityAsync(
            token, request.LastName, request.DateOfBirth, request.ZipCode);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Patient joins the waiting room. Triggers auto check-in and SignalR notification.
    /// Public endpoint — no auth required.
    /// </summary>
    [HttpPost("join/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<TelehealthJoinResult>> JoinWaitingRoom(string token)
    {
        var result = await _telehealthService.PatientJoinWaitingRoomAsync(token);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Admit the patient into the video call. Only clinical staff can admit.
    /// Sends PatientAdmitted event to the patient's SignalR connection.
    /// </summary>
    [HttpPost("admit")]
    [Authorize(Roles = "0,1,2,6,7")] // SuperAdmin, ClinicAdmin, Clinician, MA, Nurse
    public async Task<ActionResult> AdmitPatient([FromBody] TelehealthAdmitDto dto)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var success = await _telehealthService.AdmitPatientAsync(dto.AppointmentId, userId);

            if (!success)
                return NotFound(new { message = "Appointment not found or no telehealth session" });

            return Ok(new { message = "Patient admitted to call" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to admit patient for appointment {AppointmentId}", dto?.AppointmentId);
            return this.ServerError(ex, "Server error admitting patient.");
        }
    }

    /// <summary>
    /// Manually send (or resend) the telehealth invite email to the patient.
    /// Used by front desk / admin from the appointment details view.
    /// </summary>
    [HttpPost("send-invite/{appointmentId}")]
    [Authorize(Roles = "0,1,2,3,6,7")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk, MA, Nurse
    public async Task<ActionResult> SendInvite(int appointmentId)
    {
        try
        {
            await _telehealthService.SendTelehealthInviteEmailAsync(appointmentId);
            return Ok(new { message = "Telehealth invite sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send telehealth invite for appointment {AppointmentId}", appointmentId);
            return BadRequest(new { message = "Failed to send invite email: " + ex.Message });
        }
    }

    /// <summary>
    /// Get Jitsi configuration for a telehealth appointment.
    /// Used by the encounter workspace to initialize the video panel.
    /// </summary>
    [HttpGet("config/{appointmentId}")]
    [Authorize]
    public async Task<ActionResult<TelehealthConfigDto>> GetConfig(int appointmentId)
    {
        // Get display name from current user claims
        // ClaimTypes.Name = "FirstName LastName" (set by AuthService)
        var displayName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Provider";

        // Check if user is a provider (clinical role)
        var role = User.FindFirst("Role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var isProvider = role == "0" || role == "2" || role == "6" || role == "7";

        var config = await _telehealthService.GetJitsiConfigAsync(appointmentId, displayName, isProvider);

        if (config == null)
            return NotFound(new { message = "Appointment not found" });

        return Ok(config);
    }
}
