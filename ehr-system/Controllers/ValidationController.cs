using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Models;
using EHR.Services;

namespace EHR.Controllers;

/// <summary>
/// Controller for patient profile validation operations.
/// Includes scheduled endpoint for bulk validation and individual patient validation.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ValidationController : ControllerBase
{
    private readonly IPatientValidationService _validationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ValidationController> _logger;

    public ValidationController(
        IPatientValidationService validationService,
        IConfiguration configuration,
        ILogger<ValidationController> logger)
    {
        _validationService = validationService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Scheduled endpoint for bulk patient validation.
    /// Called by external scheduler (Plesk) on a daily basis.
    /// URL: GET /validation/validate-patients-form
    /// Full URL: https://ptehr.medocs.ai/validation/validate-patients-form
    /// </summary>
    /// <remarks>
    /// Security: Validates API key from header or allows requests from localhost.
    /// Returns summary of validation results for logging purposes.
    /// </remarks>
    [HttpGet("validate-patients-form")]
    [AllowAnonymous] // Allows external scheduler to call without JWT
    public async Task<ActionResult<BulkValidationResultDto>> ValidatePatientsScheduled(
        [FromHeader(Name = "X-Validation-Api-Key")] string apiKey = null)
    {
        // Security check: Validate API key or require localhost
        var configuredApiKey = _configuration["Validation:ApiKey"];
        var isLocalhost = HttpContext.Connection.RemoteIpAddress?.ToString() == "127.0.0.1" ||
                          HttpContext.Connection.RemoteIpAddress?.ToString() == "::1";

        // Allow if: valid API key provided, or request is from localhost, or no API key is configured (dev mode)
        var hasValidApiKey = !string.IsNullOrEmpty(configuredApiKey) && configuredApiKey == apiKey;
        var noKeyConfigured = string.IsNullOrEmpty(configuredApiKey);

        if (!hasValidApiKey && !isLocalhost && !noKeyConfigured)
        {
            _logger.LogWarning("Unauthorized bulk validation attempt from IP: {IpAddress}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { message = "Invalid or missing API key" });
        }

        _logger.LogInformation("Starting scheduled bulk patient validation");

        try
        {
            // Run validation for all tenants (no tenant filter)
            var result = await _validationService.ValidateAllPatientsAsync(null);

            _logger.LogInformation(
                "Scheduled validation completed: {TotalPatients} patients checked, {Complete} complete, {Incomplete} incomplete, {Duration}ms",
                result.TotalPatientsChecked,
                result.CompleteProfilesCount,
                result.IncompleteProfilesCount,
                result.ProcessingTimeMs);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled bulk patient validation");
            return StatusCode(500, new BulkValidationResultDto
            {
                Success = false,
                Message = "Validation failed due to an internal error",
                ValidationTimestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Validates all patients for the current tenant.
    /// Requires admin authorization.
    /// </summary>
    [HttpPost("bulk-validate")]
    [Authorize(Roles = "0,1")] // Super Admin, Clinic Admin
    public async Task<ActionResult<BulkValidationResultDto>> BulkValidate()
    {
        try
        {
            var result = await _validationService.ValidateAllPatientsAsync();

            _logger.LogInformation(
                "Manual bulk validation completed by user: {TotalPatients} patients, {Complete} complete, {Incomplete} incomplete",
                result.TotalPatientsChecked,
                result.CompleteProfilesCount,
                result.IncompleteProfilesCount);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual bulk validation");
            return StatusCode(500, new BulkValidationResultDto
            {
                Success = false,
                Message = "Validation failed",
                ValidationTimestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Validates a single patient's profile.
    /// </summary>
    [HttpPost("patient/{patientId}")]
    [Authorize(Roles = "0,1,2,3")] // Super Admin, Clinic Admin, Clinician, Front Desk
    public async Task<ActionResult<PatientValidationStatusDto>> ValidatePatient(int patientId)
    {
        try
        {
            // Get current user ID from claims
            var userIdClaim = User.FindFirst("UserId")?.Value;
            int? userId = userIdClaim != null ? int.Parse(userIdClaim) : null;

            var result = await _validationService.ValidatePatientAsync(patientId, "Manual", userId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating patient {PatientId}", patientId);
            return StatusCode(500, new { message = "Validation failed" });
        }
    }

    /// <summary>
    /// Gets the current validation status for a patient.
    /// </summary>
    [HttpGet("patient/{patientId}")]
    [Authorize(Roles = "0,1,2,3")] // Super Admin, Clinic Admin, Clinician, Front Desk
    public async Task<ActionResult<PatientValidationStatusDto>> GetValidationStatus(int patientId)
    {
        try
        {
            var result = await _validationService.GetValidationStatusAsync(patientId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting validation status for patient {PatientId}", patientId);
            return StatusCode(500, new { message = "Failed to get validation status" });
        }
    }

    /// <summary>
    /// Gets a detailed validation report for a patient.
    /// </summary>
    [HttpGet("patient/{patientId}/report")]
    [Authorize(Roles = "0,1,2,3")] // Super Admin, Clinic Admin, Clinician, Front Desk
    public async Task<ActionResult<PatientValidationReportDto>> GetValidationReport(int patientId)
    {
        try
        {
            var result = await _validationService.GetValidationReportAsync(patientId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting validation report for patient {PatientId}", patientId);
            return StatusCode(500, new { message = "Failed to get validation report" });
        }
    }

    /// <summary>
    /// Gets validation summary for dashboard widget.
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Roles = "0,1,2,3")] // Super Admin, Clinic Admin, Clinician, Front Desk
    public async Task<ActionResult<ValidationSummaryDto>> GetValidationSummary()
    {
        try
        {
            var result = await _validationService.GetValidationSummaryAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting validation summary");
            return StatusCode(500, new { message = "Failed to get validation summary" });
        }
    }
}
