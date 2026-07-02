using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EHR.Services;
using EHR.Services.Intake;
using EHR.Models;
using EHR.Models.Generated;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EHR.Controllers;

/// <summary>
/// Public Kiosk API - No authentication required.
/// Used by patient-facing kiosk tablets for consent signing.
/// </summary>
[ApiController]
[Route("api/kiosk")]
public class KioskController : ControllerBase
{
    private readonly IKioskService _kioskService;
    private readonly IConsentTemplateService _templateService;
    private readonly IConsentService _consentService;
    private readonly IPaymentService _paymentService;
    private readonly EhrDbContext _db;
    private readonly ILogger<KioskController> _logger;

    public KioskController(
        IKioskService kioskService,
        IConsentTemplateService templateService,
        IConsentService consentService,
        IPaymentService paymentService,
        EhrDbContext db,
        ILogger<KioskController> logger)
    {
        _kioskService = kioskService;
        _templateService = templateService;
        _consentService = consentService;
        _paymentService = paymentService;
        _db = db;
        _logger = logger;
    }

    private string GetClientIpAddress()
    {
        // Check for forwarded IP first (behind proxy/load balancer)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').First().Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private string GetUserAgent()
    {
        return Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown";
    }

    /// <summary>
    /// Validate a kiosk token and get clinic information.
    /// This is the first call when opening the kiosk URL.
    /// </summary>
    [HttpGet("validate/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskValidateTokenResponseDto>> ValidateToken(string token)
    {
        var ipAddress = GetClientIpAddress();
        var result = await _kioskService.ValidateKioskTokenAsync(token, ipAddress);

        if (!result.IsValid)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Verify patient identity using LastName, DateOfBirth, and ZipCode
    /// (SSN was removed from the kiosk identity check on 2026-05).
    /// Creates a session if verification succeeds.
    /// </summary>
    [HttpPost("verify/{kioskToken}")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskVerifyPatientResponseDto>> VerifyPatient(
        string kioskToken,
        [FromBody] KioskVerifyPatientRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new KioskVerifyPatientResponseDto
            {
                Success = false,
                Message = "Invalid input. Please check your entries."
            });
        }

        var ipAddress = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var result = await _kioskService.VerifyPatientAsync(kioskToken, request, ipAddress, userAgent);

        if (!result.Success)
        {
            // Don't return 400 for failed verification - it's not a bad request
            // Return 200 with success=false to distinguish from actual errors
            return Ok(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Get consent forms for verified session.
    /// Requires valid session token from verification step.
    /// </summary>
    [HttpGet("forms")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskConsentFormsResponseDto>> GetConsentForms(
        [FromHeader(Name = "X-Kiosk-Session")] string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return BadRequest(new KioskConsentFormsResponseDto
            {
                Success = false,
                Message = "Session token required"
            });
        }

        var session = await _kioskService.GetValidSessionAsync(sessionToken);
        if (session == null)
        {
            return Unauthorized(new KioskConsentFormsResponseDto
            {
                Success = false,
                Message = "Session expired or invalid. Please start over."
            });
        }

        // Update session activity
        await _kioskService.UpdateSessionActivityAsync(sessionToken);

        // Get rendered forms
        var forms = await _templateService.GetRenderedFormsForPatientAsync(
            session.PatientId,
            session.AppointmentId,
            session.CareEpisodeId);

        if (!forms.Any())
        {
            return Ok(new KioskConsentFormsResponseDto
            {
                Success = false,
                Message = "No consent forms configured. Please contact clinic staff."
            });
        }

        return Ok(new KioskConsentFormsResponseDto
        {
            Success = true,
            Forms = forms,
            PatientInfo = new KioskPatientInfoDto
            {
                PatientId = session.Patient.PatientId,
                FirstName = session.Patient.FirstName, // Will need decryption in real use
                LastName = session.Patient.LastName,
                DateOfBirth = session.Patient.DateOfBirth
            },
            AppointmentInfo = new KioskAppointmentInfoDto
            {
                AppointmentId = session.AppointmentId,
                StartTime = session.Appointment.StartTime,
                AppointmentType = GetAppointmentTypeName(session.Appointment.Type),
                ProviderName = session.Appointment.Provider != null ?
                    $"{session.Appointment.Provider.FirstName} {session.Appointment.Provider.LastName}" : "",
                CareEpisodeId = session.CareEpisodeId
            }
        });
    }

    /// <summary>
    /// Submit signed consent forms.
    /// </summary>
    [HttpPost("submit")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskSubmitConsentResponseDto>> SubmitConsent(
        [FromHeader(Name = "X-Kiosk-Session")] string sessionToken,
        [FromBody] KioskSubmitConsentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return BadRequest(new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Session token required"
            });
        }

        var ipAddress = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var result = await _consentService.SubmitConsentAsync(sessionToken, request, ipAddress, userAgent);

        if (!result.Success)
        {
            return Ok(result); // Return 200 even for failures to distinguish from server errors
        }

        // Consent → intake handoff: when the service signals a handoff applies,
        // set the intake-verify cookie (so the wizard skips its own DOB/SSN gate)
        // and write an audit row. Plain GUID cookie matches IntakeController's
        // existing scheme — see Services/Intake/IntakeVerifyCookie.cs and
        // rules/technical/consent-to-intake-handoff.md.
        if (result.Intake != null && result.IntakeTokenForCookie is Guid intakeToken)
        {
            Response.Cookies.Append(
                IntakeVerifyCookie.Name(intakeToken),
                IntakeVerifyCookie.Value(intakeToken),
                IntakeVerifyCookie.Options(Request.IsHttps, IntakeVerifyCookie.HandoffTtl));

            await WriteHandoffAuditAsync(result, ipAddress, userAgent, intakeToken);

            // Strip controller-only signals so they never reach the wire.
            result.IntakeTokenForCookie = null;
            result.PatientIdForAudit = null;
            result.KioskSessionIdForAudit = null;
            result.TenantIdForAudit = null;
        }

        return Ok(result);
    }

    /// <summary>
    /// "Yes, I am Here" — kiosk confirmation when the patient already signed
    /// consent (typically from the portal). Skips the consent forms flow, just
    /// transitions appointment Scheduled/Confirmed → CheckedIn + creates the
    /// Encounter + builds the same intake handoff as SubmitConsent.
    ///
    /// Returns the same response shape as SubmitConsent so the kiosk JS can
    /// reuse its post-submit success-screen handler (including the intake
    /// handoff button).
    /// </summary>
    [HttpPost("confirm-presence")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskSubmitConsentResponseDto>> ConfirmPresence(
        [FromHeader(Name = "X-Kiosk-Session")] string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return BadRequest(new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Session token required"
            });
        }

        var ipAddress = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var result = await _consentService.ConfirmKioskPresenceAsync(sessionToken);

        if (!result.Success)
        {
            return Ok(result);
        }

        // Same handoff-cookie issuance pattern as SubmitConsent — keep the two
        // success paths byte-identical from the front-end's perspective.
        if (result.Intake != null && result.IntakeTokenForCookie is Guid intakeToken)
        {
            Response.Cookies.Append(
                IntakeVerifyCookie.Name(intakeToken),
                IntakeVerifyCookie.Value(intakeToken),
                IntakeVerifyCookie.Options(Request.IsHttps, IntakeVerifyCookie.HandoffTtl));

            await WriteHandoffAuditAsync(result, ipAddress, userAgent, intakeToken);

            result.IntakeTokenForCookie = null;
            result.PatientIdForAudit = null;
            result.KioskSessionIdForAudit = null;
            result.TenantIdForAudit = null;
        }

        return Ok(result);
    }

    /// <summary>
    /// Writes one AuditLog row for the consent → intake handoff cookie issuance.
    /// Token is hashed (SHA-256) — never store the raw IntakePortalToken in
    /// audit, since it grants access without DOB/SSN.
    /// </summary>
    private async Task WriteHandoffAuditAsync(
        KioskSubmitConsentResponseDto result,
        string ipAddress,
        string userAgent,
        Guid intakeToken)
    {
        try
        {
            var newValues = JsonSerializer.Serialize(new
            {
                patientId = result.PatientIdForAudit,
                intakeTokenHash = HashToken(intakeToken),
                intakeState = result.Intake.State,
                completed = result.Intake.Completed,
                total = result.Intake.Total,
            });

            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = result.TenantIdForAudit,
                UserId = null,
                UserEmail = null,
                EntityType = "KioskSession",
                EntityId = result.KioskSessionIdForAudit,
                Action = "consent-to-intake-handoff-cookie-issued",
                NewValues = newValues,
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
                Timestamp = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit failure must not break the patient flow. Log + continue.
            _logger.LogWarning(ex, "Failed to write consent→intake handoff audit row");
        }
    }

    private static string HashToken(Guid token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.ToString("N")));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Update session activity (heartbeat).
    /// Call periodically to keep session alive.
    /// </summary>
    [HttpPost("heartbeat")]
    [AllowAnonymous]
    public async Task<ActionResult> Heartbeat(
        [FromHeader(Name = "X-Kiosk-Session")] string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return BadRequest(new { success = false, message = "Session token required" });
        }

        var session = await _kioskService.GetValidSessionAsync(sessionToken);
        if (session == null)
        {
            return Unauthorized(new { success = false, message = "Session expired" });
        }

        await _kioskService.UpdateSessionActivityAsync(sessionToken);
        return Ok(new { success = true, expiresAt = session.ExpiresAt });
    }

    /// <summary>
    /// Cancel/invalidate current session.
    /// </summary>
    [HttpPost("cancel")]
    [AllowAnonymous]
    public async Task<ActionResult> CancelSession(
        [FromHeader(Name = "X-Kiosk-Session")] string sessionToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            await _kioskService.InvalidateSessionAsync(sessionToken);
        }

        return Ok(new { success = true, message = "Session cancelled" });
    }

    /// <summary>
    /// Get patient outstanding balance for KIOSK display (soft notification).
    /// </summary>
    [HttpGet("balance")]
    [AllowAnonymous]
    public async Task<ActionResult> GetPatientBalance(
        [FromHeader(Name = "X-Kiosk-Session")] string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return BadRequest(new { success = false });

        var session = await _kioskService.GetValidSessionAsync(sessionToken);
        if (session == null)
            return Unauthorized(new { success = false });

        try
        {
            var tenantId = session.TenantId;
            var portalBalance = await _paymentService.GetPortalBalanceAsync(session.PatientId, tenantId);
            return Ok(new
            {
                Success = true,
                CurrentBalance = portalBalance.CurrentBalance,
                HasBalance = portalBalance.CurrentBalance > 0
            });
        }
        catch
        {
            return Ok(new { Success = true, CurrentBalance = 0m, HasBalance = false });
        }
    }

    private static string GetAppointmentTypeName(int type)
    {
        // Only 4 appointment types are used, return "Other" for any legacy types
        return type switch
        {
            0 => "New Patient Visit",
            1 => "Follow-Up Visit",
            2 => "Annual Physical",
            3 => "Wellness Exam",
            4 => "Consultation",
            5 => "Telehealth",
            6 => "Procedure Visit",
            7 => "Urgent Visit",
            8 => "Lab Review",
            9 => "Medication Review",
            10 => "New Longevity Patient",
            11 => "Follow-Up Longevity Patient",
            _ => "Other"
        };
    }
}

/// <summary>
/// Admin Kiosk Settings API - Requires authentication.
/// Used by clinic admins to configure kiosk settings.
/// </summary>
[ApiController]
[Route("api/kiosk-settings")]
[Authorize]
public class KioskSettingsController : ControllerBase
{
    private readonly IKioskService _kioskService;
    private readonly ILocationProvider _locationProvider;
    private readonly IAuditService _auditService;
    private readonly ILogger<KioskSettingsController> _logger;

    public KioskSettingsController(
        IKioskService kioskService,
        ILocationProvider locationProvider,
        IAuditService auditService,
        ILogger<KioskSettingsController> logger)
    {
        _kioskService = kioskService;
        _locationProvider = locationProvider;
        _auditService = auditService;
        _logger = logger;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    private int GetRole() =>
        int.TryParse(User.FindFirst("Role")?.Value ?? User.FindFirstValue(ClaimTypes.Role), out var r) ? r : 999;

    /// <summary>
    /// Get kiosk settings for a location
    /// </summary>
    [HttpGet("{locationId}")]
    public async Task<ActionResult<KioskSettingsDto>> GetKioskSettings(int locationId)
    {
        // Only admins can view kiosk settings
        if (GetRole() > 1)
        {
            return Forbid();
        }

        try
        {
            var settings = await _kioskService.GetKioskSettingsAsync(locationId);
            if (settings == null)
            {
                return NotFound(new { message = "Location not found" });
            }

            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting kiosk settings for location {LocationId}", locationId);
            var message = ex.InnerException?.Message ?? ex.Message;
            if (message.Contains("Invalid object name") || message.Contains("does not exist"))
            {
                return StatusCode(500, new { message = "Database tables not found. Please run the consent system migration script." });
            }
            return StatusCode(500, new { message = $"Error loading settings: {message}" });
        }
    }

    /// <summary>
    /// Update kiosk settings for a location
    /// </summary>
    [HttpPut("{locationId}")]
    public async Task<ActionResult<KioskSettingsDto>> UpdateKioskSettings(
        int locationId,
        [FromBody] KioskSettingsUpdateDto dto)
    {
        // Only admins can update kiosk settings
        if (GetRole() > 1)
        {
            return Forbid();
        }

        try
        {
            var settings = await _kioskService.CreateOrUpdateKioskSettingsAsync(locationId, dto, GetUserId());
            return Ok(settings);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error updating kiosk settings for location {LocationId}", locationId);
            var innerMessage = dbEx.InnerException?.Message ?? dbEx.Message;
            if (innerMessage.Contains("Invalid object name") || innerMessage.Contains("does not exist"))
            {
                return StatusCode(500, new { message = "Database tables not found. Please run the consent system migration script." });
            }
            return StatusCode(500, new { message = $"Database error: {innerMessage}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating kiosk settings for location {LocationId}", locationId);
            return StatusCode(500, new { message = $"Error saving settings: {ex.Message}" });
        }
    }

    /// <summary>
    /// Regenerate kiosk token (invalidates current link)
    /// </summary>
    [HttpPost("{locationId}/regenerate-token")]
    public async Task<ActionResult<KioskTokenRegenerateResponseDto>> RegenerateToken(int locationId)
    {
        // Only admins can regenerate tokens
        if (GetRole() > 1)
        {
            return Forbid();
        }

        try
        {
            var result = await _kioskService.RegenerateKioskTokenAsync(locationId, GetUserId());
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error regenerating kiosk token for location {LocationId}", locationId);
            var message = ex.InnerException?.Message ?? ex.Message;
            if (message.Contains("Invalid object name") || message.Contains("does not exist"))
            {
                return StatusCode(500, new { message = "Database tables not found. Please run the consent system migration script." });
            }
            return StatusCode(500, new { message = $"Error regenerating token: {message}" });
        }
    }

    /// <summary>
    /// Enable or disable kiosk
    /// </summary>
    [HttpPost("{locationId}/toggle")]
    public async Task<ActionResult> ToggleKiosk(int locationId, [FromBody] KioskToggleDto dto)
    {
        // Only admins can toggle kiosk
        if (GetRole() > 1)
        {
            return Forbid();
        }

        var success = await _kioskService.EnableKioskAsync(locationId, dto.IsEnabled, GetUserId());
        if (!success)
        {
            return NotFound(new { message = "Location not found" });
        }

        return Ok(new { success = true, isEnabled = dto.IsEnabled });
    }
}

public class KioskToggleDto
{
    public bool IsEnabled { get; set; }
}
