using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;
using EHR.Services;

namespace EHR.Controllers;

/// <summary>
/// Controller for Medical Lien Form functionality.
/// Handles PDF generation and provider listing for medical lien documents.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[PhiAccessAudit(EntityType = "MedicalLien")]
public class MedicalLienController : ControllerBase
{
    private readonly IMedicalLienService _medicalLienService;
    private readonly ILogger<MedicalLienController> _logger;

    public MedicalLienController(
        IMedicalLienService medicalLienService,
        ILogger<MedicalLienController> logger)
    {
        _medicalLienService = medicalLienService;
        _logger = logger;
    }

    /// <summary>
    /// Get list of providers available for the Medical Lien Form dropdown
    /// </summary>
    /// <returns>List of providers with signature status</returns>
    [HttpGet("providers")]
    [Authorize(Roles = "0,1,3")] // SuperAdmin, ClinicAdmin, FrontDesk
    public async Task<ActionResult<List<MedicalLienProviderDto>>> GetProviders()
    {
        try
        {
            var providers = await _medicalLienService.GetProvidersForLienFormAsync();
            return Ok(providers);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when getting providers for Medical Lien");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting providers for Medical Lien");
            return StatusCode(500, new { message = "An error occurred while retrieving providers" });
        }
    }

    /// <summary>
    /// Get Medical Lien Form data preview for a patient
    /// </summary>
    /// <param name="patientId">Patient ID</param>
    /// <param name="providerId">Provider ID for the form</param>
    /// <returns>Form data DTO</returns>
    [HttpGet("preview/{patientId}")]
    [Authorize(Roles = "0,1,3")] // SuperAdmin, ClinicAdmin, FrontDesk
    public async Task<ActionResult<MedicalLienFormDataDto>> GetFormPreview(int patientId, [FromQuery] int providerId)
    {
        if (providerId <= 0)
        {
            return BadRequest(new { message = "Provider ID is required" });
        }

        try
        {
            var formData = await _medicalLienService.GetMedicalLienFormDataAsync(patientId, providerId);
            return Ok(formData);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found when getting Medical Lien preview for patient {PatientId}", patientId);
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when getting Medical Lien preview for patient {PatientId}", patientId);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Medical Lien preview for patient {PatientId}", patientId);
            return StatusCode(500, new { message = "An error occurred while retrieving form data" });
        }
    }

    /// <summary>
    /// Generate and download the Medical Lien Form PDF
    /// </summary>
    /// <param name="patientId">Patient ID</param>
    /// <param name="providerId">Provider ID for the form</param>
    /// <param name="locationId">Location ID to determine which template to use</param>
    /// <returns>PDF file</returns>
    [HttpGet("generate/{patientId}")]
    [Authorize(Roles = "0,1,3")] // SuperAdmin, ClinicAdmin, FrontDesk
    public async Task<IActionResult> GeneratePdf(int patientId, [FromQuery] int providerId, [FromQuery] int locationId)
    {
        if (providerId <= 0)
        {
            return BadRequest(new { message = "Provider ID is required" });
        }

        if (locationId <= 0)
        {
            return BadRequest(new { message = "Location ID is required" });
        }

        try
        {
            var result = await _medicalLienService.GenerateMedicalLienPdfAsync(patientId, providerId, locationId);

            if (!result.Success)
            {
                return BadRequest(new { message = result.Message });
            }

            return File(result.PdfData, "application/pdf", result.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found when generating Medical Lien PDF for patient {PatientId}", patientId);
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when generating Medical Lien PDF for patient {PatientId}", patientId);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Medical Lien PDF for patient {PatientId}", patientId);
            return StatusCode(500, new { message = "An error occurred while generating the Medical Lien Form" });
        }
    }
}
