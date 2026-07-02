using EHR.Helpers;
using EHR.Models;
using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Controller for managing clinical documentation (SOAP notes, evaluations, etc.).
/// Handles note creation, editing, signing, and care episode integration.
/// </summary>
[ApiController]
[Route("api/clinical-notes")]
[Authorize]
[PhiAccessAudit(EntityType = "ClinicalNote")]
public class ClinicalNotesController : ControllerBase
{
    private readonly IClinicalNoteService _noteService;
    private readonly ICareEpisodeExtractionService _extractionService;
    private readonly IEncounterContextService _encounterContextService;
    private readonly IConfiguration _config;

    public ClinicalNotesController(
        IClinicalNoteService noteService,
        ICareEpisodeExtractionService extractionService,
        IEncounterContextService encounterContextService,
        IConfiguration config)
    {
        _noteService = noteService;
        _extractionService = extractionService;
        _encounterContextService = encounterContextService;
        _config = config;
    }

    /// <summary>
    /// Get clinical notes with filtering (legacy - returns all results).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ClinicalNoteListDto>>> GetNotes(
        [FromQuery] int? patientId,
        [FromQuery] int? providerId,
        [FromQuery] int? appointmentId,
        [FromQuery] ClinicalNoteStatus? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        // If user is a clinician, only show their own notes
        var userRole = User.FindFirst("Role")?.Value;
        var userProviderId = User.FindFirst("ProviderId")?.Value;

        if (userRole == "2" && !string.IsNullOrEmpty(userProviderId))
        {
            providerId = int.Parse(userProviderId);
        }

        var result = await _noteService.GetNotesAsync(
            patientId, providerId, appointmentId, startDate, endDate, status.HasValue ? (int)status.Value : null);
        return Ok(result);
    }

    /// <summary>
    /// Get clinical notes with filtering and pagination.
    /// Search parameter searches patient name, patient MRN, and provider name.
    /// </summary>
    [HttpGet("paged")]
    public async Task<ActionResult<ClinicalNotePagedResponse>> GetNotesPaged(
        [FromQuery] string? search,
        [FromQuery] int? patientId,
        [FromQuery] int? providerId,
        [FromQuery] int? appointmentId,
        [FromQuery] ClinicalNoteStatus? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        // If user is a clinician, only show their own notes
        var userRole = User.FindFirst("Role")?.Value;
        var userProviderId = User.FindFirst("ProviderId")?.Value;

        if (userRole == "2" && !string.IsNullOrEmpty(userProviderId))
        {
            providerId = int.Parse(userProviderId);
        }

        var result = await _noteService.GetNotesPagedAsync(
            search, patientId, providerId, appointmentId, startDate, endDate,
            status.HasValue ? (int)status.Value : null, page, pageSize);
        return Ok(result);
    }

    /// <summary>
    /// Get clinical note by ID with decrypted content.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ClinicalNoteDetailDto>> GetNote(int id)
    {
        var result = await _noteService.GetDetailAsync(id);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Create new clinical note.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<ClinicalNote>> CreateNote([FromBody] ClinicalNoteCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var result = await _noteService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetNote), new { id = result.ClinicalNoteId }, result);
    }

    /// <summary>
    /// Update clinical note.
    /// Note: Signing is handled separately via /sign or /sign-with-care-episode endpoints
    /// to ensure proper validation (Initial Evaluation checks, Care Episode creation, etc.)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<ClinicalNote>> UpdateNote(
        int id,
        [FromBody] ClinicalNoteUpdateDto dto)
    {
        try
        {
            var result = await _noteService.UpdateAsync(id, dto);
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
    /// Sign clinical note.
    /// </summary>
    [HttpPost("{id}/sign")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<ClinicalNote>> SignNote(
        int id,
        [FromBody] SignClinicalNoteRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var result = await _noteService.SignNoteAsync(id, userId, request.SignatureData);
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
    /// Validate Initial Evaluation clinical note before signing.
    /// Extracts care episode data and checks for missing fields and insurance mismatches.
    /// </summary>
    [HttpPost("{id}/validate-initial-evaluation")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<ValidateInitialEvaluationResponse>> ValidateInitialEvaluation(int id)
    {
        try
        {
            var result = await _extractionService.ValidateInitialEvaluationAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Sign clinical note and create care episode if it's an Initial Evaluation.
    /// Accepts pre-validated extraction data from /validate-initial-evaluation to avoid double Gemini calls.
    /// </summary>
    [HttpPost("{id}/sign-with-care-episode")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<SignWithCareEpisodeResponse>> SignWithCareEpisode(
        int id,
        [FromBody] SignWithCareEpisodeRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var result = await _extractionService.SignAndCreateCareEpisodeAsync(id, userId, request);

            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete clinical note (draft only, creator or admin can delete).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> DeleteNote(int id)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            var (success, errorMessage) = await _noteService.DeleteAsync(id, userId, userIp);

            if (!success)
            {
                if (errorMessage?.Contains("not found") == true)
                    return NotFound(new { message = errorMessage });
                return BadRequest(new { message = errorMessage });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get notes for a specific appointment (for "Create Clinical Note" button).
    /// </summary>
    [HttpGet("by-appointment/{appointmentId}")]
    public async Task<ActionResult<List<ClinicalNoteListDto>>> GetNotesByAppointment(int appointmentId)
    {
        var result = await _noteService.GetNotesAsync(appointmentId: appointmentId);
        return Ok(result);
    }

    /// <summary>
    /// Check if a note exists for an appointment.
    /// </summary>
    [HttpGet("exists/{appointmentId}")]
    public async Task<ActionResult<object>> CheckNoteExists(int appointmentId)
    {
        var notes = await _noteService.GetNotesAsync(appointmentId: appointmentId);
        return Ok(new
        {
            exists = notes.Any(),
            noteId = notes.FirstOrDefault()?.ClinicalNoteId,
            status = notes.FirstOrDefault()?.Status
        });
    }

    /// <summary>
    /// Get the last signed note for a patient by the current provider.
    /// Used for the "Duplicate from Last" feature in Write Report.
    /// Only returns notes from Follow-Up appointments (Type = 1) for Daily Progress Note duplication.
    /// </summary>
    [HttpGet("last-signed")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<ClinicalNoteDetailDto>> GetLastSignedNote(
        [FromQuery] int patientId,
        [FromQuery] int? providerId,
        [FromQuery] int? templateId,
        [FromQuery] int? appointmentType = 1) // Default to Follow-Up (1) for Daily Progress Notes
    {
        // If providerId not specified, use current user's provider ID
        var userProviderId = User.FindFirst("ProviderId")?.Value;
        var effectiveProviderId = providerId ?? (string.IsNullOrEmpty(userProviderId) ? 0 : int.Parse(userProviderId));

        if (effectiveProviderId == 0)
        {
            return BadRequest(new { message = "Provider ID is required" });
        }

        var result = await _noteService.GetLastSignedNoteAsync(patientId, effectiveProviderId, templateId, appointmentType);

        if (result == null)
        {
            return NotFound(new { message = "No signed notes found for this patient" });
        }

        return Ok(result);
    }

    /// <summary>
    /// Pre-fill a clinical note template with encounter context data (vitals, CC/HPI, history) using Gemini AI.
    /// Called when a doctor selects a template during note creation — returns the template HTML
    /// with encounter data intelligently merged into the appropriate sections.
    /// Falls back to raw template if Gemini fails or no encounter data exists.
    /// </summary>
    [HttpPost("prefill-template")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<PrefillTemplateResponse>> PrefillTemplate([FromBody] PrefillTemplateRequest request)
    {
        if (request.TemplateId <= 0 || request.PatientId <= 0)
        {
            return BadRequest(new { message = "TemplateId and PatientId are required" });
        }

        var result = await _encounterContextService.PrefillTemplateAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Get encounter context data for a patient/encounter.
    /// Used by Scribe and other features that need encounter context.
    /// </summary>
    [HttpGet("encounter-context")]
    public async Task<ActionResult<EncounterContextDto>> GetEncounterContext(
        [FromQuery] int patientId,
        [FromQuery] int? encounterId)
    {
        if (patientId <= 0)
        {
            return BadRequest(new { message = "PatientId is required" });
        }

        var result = await _encounterContextService.GetEncounterContextAsync(patientId, encounterId);
        return Ok(result);
    }
}
