using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Models;
using EHR.Services;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Care Notes HTTP surface.
///
/// Two route groups:
///   - /api/patients/{patientId}/care-notes
///       GET  -> all notes for a patient (any staff role)
///       POST -> create note (any staff role)
///   - /api/care-notes/...
///       PUT     /{id}                 -> edit (creator only)
///       DELETE  /{id}                 -> soft delete (creator only)
///       POST    /mark-patient-seen/{patientId}
///                                     -> auto-mark when clinician opens tab
///       GET     /unseen-count         -> top-nav bell count (clinician only)
///       GET     /unseen               -> top-nav bell dropdown (clinician only)
///
/// Authz:
///   Patient-scoped routes are open to staff who already touch patient data
///   (SuperAdmin, ClinicAdmin, Clinician, MA, Nurse, FrontDesk).
///   Notification routes (unseen-*) are restricted to Clinician role at the
///   frontend (clinician-only CSS) AND additionally require a non-null
///   ProviderId claim (server-side guard).
/// </summary>
[ApiController]
[Authorize]
public class CareNotesController : ControllerBase
{
    private readonly ICareNoteService _careNoteService;
    private readonly IAudioTranscriptionService _transcriptionService;

    public CareNotesController(
        ICareNoteService careNoteService,
        IAudioTranscriptionService transcriptionService)
    {
        _careNoteService = careNoteService;
        _transcriptionService = transcriptionService;
    }

    // ====================================================================
    // PATIENT-SCOPED
    // ====================================================================

    [HttpGet("api/patients/{patientId}/care-notes")]
    [Authorize(Roles = "0,1,2,3,4,6,7")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk, Biller, MA, Nurse
    public async Task<ActionResult<List<CareNoteDto>>> GetForPatient(int patientId)
    {
        var notes = await _careNoteService.GetForPatientAsync(patientId);

        // If the caller is a clinician with a ProviderId, opening this tab
        // also auto-marks any unseen notes addressed to them as seen.
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role == "2")
        {
            var providerIdStr = User.FindFirst("ProviderId")?.Value;
            if (int.TryParse(providerIdStr, out var providerId) && providerId > 0)
            {
                await _careNoteService.MarkPatientNotesSeenAsync(patientId, providerId);
            }
        }

        return Ok(notes);
    }

    [HttpPost("api/patients/{patientId}/care-notes")]
    [Authorize(Roles = "0,1,2,3,6,7")] // staff who can write a care note
    public async Task<ActionResult<CareNoteDto>> Create(int patientId, [FromBody] CareNoteCreateDto dto)
    {
        var (userId, userName) = GetCurrentUser();
        var result = await _careNoteService.CreateAsync(patientId, dto, userId, userName);
        if (result == null)
            return BadRequest(new { message = "Failed to create care note. Content is required." });
        return Ok(result);
    }

    // ====================================================================
    // NOTE-SCOPED
    // ====================================================================

    [HttpPut("api/care-notes/{id}")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult<CareNoteDto>> Update(int id, [FromBody] CareNoteUpdateDto dto)
    {
        var (userId, userName) = GetCurrentUser();
        var result = await _careNoteService.UpdateAsync(id, dto, userId, userName);
        if (result == null)
            return BadRequest(new { message = "Care note not found, or you are not the author." });
        return Ok(result);
    }

    [HttpDelete("api/care-notes/{id}")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    public async Task<ActionResult> Delete(int id)
    {
        var (userId, userName) = GetCurrentUser();
        var ok = await _careNoteService.SoftDeleteAsync(id, userId, userName);
        if (!ok)
            return BadRequest(new { message = "Care note not found, or you are not the author." });
        return Ok(new { message = "Care note deleted" });
    }

    /// <summary>
    /// Explicit "mark all this patient's notes seen" for the calling clinician.
    /// GET on the patient-scoped collection already does this implicitly, but
    /// we expose this so the frontend can call it on demand (e.g. clicking
    /// a notification dropdown row jumps straight to the patient tab and we
    /// want the unseen badge to drop immediately).
    /// </summary>
    [HttpPost("api/care-notes/mark-patient-seen/{patientId}")]
    [Authorize(Roles = "2")]
    public async Task<ActionResult> MarkPatientSeen(int patientId)
    {
        var providerIdStr = User.FindFirst("ProviderId")?.Value;
        if (!int.TryParse(providerIdStr, out var providerId) || providerId <= 0)
            return Forbid();

        var n = await _careNoteService.MarkPatientNotesSeenAsync(patientId, providerId);
        return Ok(new { markedSeen = n });
    }

    // ====================================================================
    // PROVIDER NOTIFICATION (Clinician role only)
    // ====================================================================

    [HttpGet("api/care-notes/unseen-count")]
    [Authorize(Roles = "2")]
    public async Task<ActionResult<object>> UnseenCount()
    {
        var providerIdStr = User.FindFirst("ProviderId")?.Value;
        if (!int.TryParse(providerIdStr, out var providerId) || providerId <= 0)
            return Ok(new { count = 0 });

        var count = await _careNoteService.GetUnseenCountForProviderAsync(providerId);
        return Ok(new { count });
    }

    [HttpGet("api/care-notes/unseen")]
    [Authorize(Roles = "2")]
    public async Task<ActionResult<List<CareNoteUnseenDto>>> Unseen()
    {
        var providerIdStr = User.FindFirst("ProviderId")?.Value;
        if (!int.TryParse(providerIdStr, out var providerId) || providerId <= 0)
            return Ok(new List<CareNoteUnseenDto>());

        var rows = await _careNoteService.GetUnseenForProviderAsync(providerId);
        return Ok(rows);
    }

    // ====================================================================
    // CHUNKED TRANSCRIPTION (live mic dictation, no polish, no rewriting)
    //
    // Frontend sends short audio chunks (silence-detected, ~3-10s each) plus
    // the running transcript so far as `previousText`. Server returns the
    // raw transcribed text for THIS chunk. Frontend appends it to the
    // textarea so the user sees text appearing as they speak.
    //
    // No polish, no AI rewriting. The nurse's words land in the textarea
    // exactly as spoken. previousText is sent only so Gemini can keep
    // medical terminology consistent across chunks (it does NOT rewrite
    // earlier text).
    // ====================================================================
    [HttpPost("api/care-notes/transcribe-chunk")]
    [Authorize(Roles = "0,1,2,3,6,7")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB cap per chunk
    public async Task<ActionResult> TranscribeChunk(
        [FromForm] IFormFile audio,
        [FromForm] int? patientId = null,
        [FromForm] string? previousText = null)
    {
        if (audio == null || audio.Length == 0)
            return BadRequest(new { message = "No audio chunk uploaded." });

        var mime = string.IsNullOrWhiteSpace(audio.ContentType) ? "audio/webm" : audio.ContentType.ToLowerInvariant();

        var tempPath = Path.Combine(Path.GetTempPath(),
            $"carenote_chunk_{Guid.NewGuid():N}{Path.GetExtension(audio.FileName ?? ".webm")}");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                await audio.CopyToAsync(stream);
            }

            var result = await _transcriptionService.TranscribeChunkAsync(
                audioFilePath: tempPath,
                mimeType: mime,
                previousText: previousText,
                patientId: patientId);

            if (!result.Success)
                return StatusCode(500, new { message = result.ErrorMessage ?? "Transcription failed." });

            return Ok(new { text = result.Text });
        }
        finally
        {
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); } catch { /* swallow */ }
        }
    }

    // ====================================================================
    // helpers
    // ====================================================================
    private (int userId, string userName) GetCurrentUser()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var firstName = User.FindFirst("FirstName")?.Value;
        var lastName = User.FindFirst("LastName")?.Value;
        var fullName = ($"{firstName} {lastName}").Trim();
        if (string.IsNullOrWhiteSpace(fullName))
            fullName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
        return (userId, fullName);
    }
}
