using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Consent Form Templates API - Admin operations for managing consent templates.
/// </summary>
[ApiController]
[Route("api/consent-templates")]
[Authorize]
public class ConsentTemplatesController : ControllerBase
{
    private readonly IConsentTemplateService _templateService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ConsentTemplatesController> _logger;

    public ConsentTemplatesController(
        IConsentTemplateService templateService,
        IAuditService auditService,
        ILogger<ConsentTemplatesController> logger)
    {
        _templateService = templateService;
        _auditService = auditService;
        _logger = logger;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    private int GetRole() =>
        int.TryParse(User.FindFirst("Role")?.Value ?? User.FindFirstValue(ClaimTypes.Role), out var r) ? r : 999;

    /// <summary>
    /// Get all consent form templates for the tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ConsentTemplateListDto>>> GetTemplates(
        [FromQuery] int? locationId = null,
        [FromQuery] int? formType = null,
        [FromQuery] bool includeInactive = false)
    {
        var templates = await _templateService.GetTemplatesAsync(locationId, formType, includeInactive);
        return Ok(templates);
    }

    /// <summary>
    /// Get a specific template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConsentTemplateDetailDto>> GetTemplate(int id)
    {
        var template = await _templateService.GetTemplateByIdAsync(id);
        if (template == null)
        {
            return NotFound(new { message = "Template not found" });
        }

        return Ok(template);
    }

    /// <summary>
    /// Create a new consent form template
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1")] // SuperAdmin, ClinicAdmin
    public async Task<ActionResult<ConsentTemplateDetailDto>> CreateTemplate(
        [FromBody] ConsentTemplateCreateDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var template = await _templateService.CreateTemplateAsync(dto, GetUserId());
            var detailDto = await _templateService.GetTemplateByIdAsync(template.ConsentFormTemplateId);

            await _auditService.LogAccessAsync(
                GetUserId(),
                User.FindFirst(ClaimTypes.Email)?.Value,
                "Create",
                "ConsentFormTemplate",
                template.ConsentFormTemplateId
            );

            return CreatedAtAction(nameof(GetTemplate), new { id = template.ConsentFormTemplateId }, detailDto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing consent form template
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1")] // SuperAdmin, ClinicAdmin
    public async Task<ActionResult<ConsentTemplateDetailDto>> UpdateTemplate(
        int id,
        [FromBody] ConsentTemplateUpdateDto dto)
    {
        try
        {
            var template = await _templateService.UpdateTemplateAsync(id, dto, GetUserId());
            if (template == null)
            {
                return NotFound(new { message = "Template not found" });
            }

            var detailDto = await _templateService.GetTemplateByIdAsync(template.ConsentFormTemplateId);

            await _auditService.LogAccessAsync(
                GetUserId(),
                User.FindFirst(ClaimTypes.Email)?.Value,
                "Update",
                "ConsentFormTemplate",
                id
            );

            return Ok(detailDto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a consent form template (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1")] // SuperAdmin, ClinicAdmin
    public async Task<ActionResult> DeleteTemplate(int id)
    {
        var result = await _templateService.DeleteTemplateAsync(id, GetUserId());
        if (!result)
        {
            return NotFound(new { message = "Template not found" });
        }

        await _auditService.LogAccessAsync(
            GetUserId(),
            User.FindFirst(ClaimTypes.Email)?.Value,
            "Delete",
            "ConsentFormTemplate",
            id
        );

        return Ok(new { message = "Template deleted successfully" });
    }

    /// <summary>
    /// Preview a template with sample data
    /// </summary>
    [HttpGet("{id}/preview")]
    public async Task<ActionResult<ConsentTemplatePreviewDto>> PreviewTemplate(int id)
    {
        var preview = await _templateService.PreviewTemplateAsync(id);
        if (preview == null)
        {
            return NotFound(new { message = "Template not found" });
        }

        return Ok(preview);
    }

    /// <summary>
    /// Preview template content (for editor preview without saving)
    /// </summary>
    [HttpPost("preview-content")]
    public async Task<ActionResult<ConsentTemplatePreviewDto>> PreviewContent(
        [FromBody] ConsentTemplatePreviewContentDto dto)
    {
        if (string.IsNullOrEmpty(dto?.HtmlContent))
        {
            return BadRequest(new { message = "HTML content is required" });
        }

        var preview = await _templateService.PreviewTemplateContentAsync(dto.HtmlContent);
        return Ok(preview);
    }

    /// <summary>
    /// Get available placeholders for template editor
    /// </summary>
    [HttpGet("placeholders")]
    public ActionResult<Dictionary<string, List<PlaceholderInfo>>> GetPlaceholders()
    {
        var placeholders = _templateService.GetAvailablePlaceholders();
        return Ok(placeholders);
    }
}

public class ConsentTemplatePreviewContentDto
{
    public string HtmlContent { get; set; }
}

/// <summary>
/// Patient Consent Records API - Admin operations for viewing consent records.
/// </summary>
[ApiController]
[Route("api/consents")]
[Authorize]
public class ConsentsController : ControllerBase
{
    private readonly IConsentService _consentService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ConsentsController> _logger;

    public ConsentsController(
        IConsentService consentService,
        IAuditService auditService,
        ILogger<ConsentsController> logger)
    {
        _consentService = consentService;
        _auditService = auditService;
        _logger = logger;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    /// <summary>
    /// Get consent history for a patient
    /// </summary>
    [HttpGet("patient/{patientId}")]
    public async Task<ActionResult<PatientConsentHistoryDto>> GetPatientConsentHistory(int patientId)
    {
        var history = await _consentService.GetPatientConsentHistoryAsync(patientId);
        if (history == null)
        {
            return NotFound(new { message = "Patient not found" });
        }

        await _auditService.LogAccessAsync(
            GetUserId(),
            null,
            "Read",
            "PatientConsentHistory",
            patientId
        );

        return Ok(history);
    }

    /// <summary>
    /// Get consent status for a patient (current/upcoming)
    /// </summary>
    [HttpGet("patient/{patientId}/status")]
    public async Task<ActionResult<PatientConsentStatusDto>> GetPatientConsentStatus(int patientId)
    {
        var status = await _consentService.GetPatientConsentStatusAsync(patientId);
        if (status == null)
        {
            return NotFound(new { message = "Patient not found" });
        }

        return Ok(status);
    }

    /// <summary>
    /// Get consent for a specific care episode
    /// </summary>
    [HttpGet("care-episode/{careEpisodeId}")]
    public async Task<ActionResult<ConsentRecordDto>> GetConsentForCareEpisode(int careEpisodeId)
    {
        var consent = await _consentService.GetConsentForCareEpisodeAsync(careEpisodeId);
        if (consent == null)
        {
            return NotFound(new { message = "No consent found for this care episode" });
        }

        await _auditService.LogAccessAsync(
            GetUserId(),
            null,
            "Read",
            "CareEpisodeConsent",
            consent.CareEpisodeConsentId
        );

        return Ok(consent);
    }

    /// <summary>
    /// Get consent for a specific appointment
    /// </summary>
    [HttpGet("appointment/{appointmentId}")]
    public async Task<ActionResult<ConsentRecordDto>> GetConsentForAppointment(int appointmentId)
    {
        var consent = await _consentService.GetConsentForAppointmentAsync(appointmentId);
        if (consent == null)
        {
            return NotFound(new { message = "No consent found for this appointment" });
        }

        await _auditService.LogAccessAsync(
            GetUserId(),
            null,
            "Read",
            "CareEpisodeConsent",
            consent.CareEpisodeConsentId
        );

        return Ok(consent);
    }

    /// <summary>
    /// Get a specific consent record by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConsentRecordDto>> GetConsent(int id)
    {
        var consent = await _consentService.GetConsentByIdAsync(id);
        if (consent == null)
        {
            return NotFound(new { message = "Consent not found" });
        }

        await _auditService.LogAccessAsync(
            GetUserId(),
            null,
            "Read",
            "CareEpisodeConsent",
            id
        );

        return Ok(consent);
    }

    /// <summary>
    /// View consent PDF inline (in browser)
    /// </summary>
    [HttpGet("{id}/view-pdf")]
    public async Task<IActionResult> ViewConsentPdf(int id)
    {
        var result = await _consentService.GetConsentPdfAsync(id);
        if (result == null)
        {
            return NotFound(new { message = "Consent PDF not found" });
        }

        var (pdfData, fileName) = result.Value;

        await _auditService.LogAccessAsync(
            GetUserId(),
            null,
            "View",
            "CareEpisodeConsentPdf",
            id
        );

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
        return File(pdfData, "application/pdf");
    }

    /// <summary>
    /// Download consent PDF
    /// </summary>
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> DownloadConsentPdf(int id)
    {
        var result = await _consentService.GetConsentPdfAsync(id);
        if (result == null)
        {
            return NotFound(new { message = "Consent PDF not found" });
        }

        var (pdfData, fileName) = result.Value;

        await _auditService.LogAccessAsync(
            GetUserId(),
            null,
            "Download",
            "CareEpisodeConsentPdf",
            id
        );

        return File(pdfData, "application/pdf", fileName);
    }

    /// <summary>
    /// Upload a manual consent form PDF.
    /// Used by clinic admin or front desk staff for paper-based consent collection.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = "0,1,2")] // SuperAdmin, ClinicAdmin, FrontDesk
    [RequestSizeLimit(10_485_760)] // 10MB limit
    public async Task<ActionResult<ManualConsentUploadResponseDto>> UploadManualConsent(
        [FromForm] int patientId,
        [FromForm] int? appointmentId,
        [FromForm] int? careEpisodeId,
        [FromForm] int consentType,
        [FromForm] DateTime? signedAt,
        [FromForm] string notes,
        IFormFile file)
    {
        // Validate file is provided
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ManualConsentUploadResponseDto
            {
                Success = false,
                Message = "No file provided"
            });
        }

        // Validate file type (PDF only)
        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ManualConsentUploadResponseDto
            {
                Success = false,
                Message = "Only PDF files are allowed"
            });
        }

        var request = new ManualConsentUploadRequestDto
        {
            PatientId = patientId,
            AppointmentId = appointmentId,
            CareEpisodeId = careEpisodeId,
            ConsentType = consentType,
            SignedAt = signedAt,
            Notes = notes
        };

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var userId = GetUserId();

        using var stream = file.OpenReadStream();
        var result = await _consentService.UploadManualConsentAsync(
            request,
            stream,
            file.FileName,
            userId,
            ipAddress);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        await _auditService.LogAccessAsync(
            userId,
            User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            "Upload",
            "CareEpisodeConsent",
            result.ConsentId ?? 0
        );

        return Ok(result);
    }
}
