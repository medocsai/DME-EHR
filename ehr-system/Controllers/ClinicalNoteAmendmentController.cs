using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Handles amendment and addendum actions on signed clinical notes, plus
/// version history retrieval. Every endpoint re-validates authorization
/// server-side (role + original-signer + window). The client cannot bypass
/// these checks by crafting URLs.
/// </summary>
[ApiController]
[Route("api/clinical-notes")]
[Authorize]
public class ClinicalNoteAmendmentController : ControllerBase
{
    private readonly IClinicalNoteAmendmentService _svc;

    public ClinicalNoteAmendmentController(IClinicalNoteAmendmentService svc)
    {
        _svc = svc;
    }

    private int CurrentUserId => int.TryParse(User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    private int CurrentUserRole => int.TryParse(User.FindFirst("Role")?.Value, out var r) ? r : -1;

    /// <summary>Returns amendment/addendum status + mode + countdown info.</summary>
    [HttpGet("{noteId}/amendment-status")]
    public async Task<IActionResult> GetStatus(int noteId)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        var status = await _svc.GetStatusAsync(noteId, CurrentUserId, CurrentUserRole);
        return Ok(status);
    }

    /// <summary>Returns version list, addendums, and current (latest) HTML content.</summary>
    [HttpGet("{noteId}/history")]
    public async Task<IActionResult> GetHistory(int noteId)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        var history = await _svc.GetHistoryAsync(noteId, CurrentUserId, CurrentUserRole);
        return Ok(history);
    }

    /// <summary>Returns a specific version's full decrypted content. For "View" buttons.</summary>
    [HttpGet("{noteId}/version/{versionNumber}")]
    public async Task<IActionResult> GetVersion(int noteId, int versionNumber)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        if (versionNumber < 1) return BadRequest(new { error = "Version number must be >= 1." });
        var content = await _svc.GetVersionContentAsync(noteId, versionNumber);
        if (content == null) return NotFound();
        return Ok(content);
    }

    /// <summary>Creates a new amendment (new version) of a signed note.</summary>
    [HttpPost("{noteId}/amendment")]
    public async Task<IActionResult> CreateAmendment(int noteId, [FromBody] CreateAmendmentRequest req)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        if (req == null) return BadRequest(new { error = "Request body is required." });

        var (ok, error, version) = await _svc.CreateAmendmentAsync(noteId, req, CurrentUserId, CurrentUserRole);
        if (!ok) return Conflict(new { error });
        return Ok(new { versionNumber = version });
    }

    /// <summary>Creates a new addendum on a signed note. Always available for the original signer.</summary>
    [HttpPost("{noteId}/addendum")]
    public async Task<IActionResult> CreateAddendum(int noteId, [FromBody] CreateAddendumRequest req)
    {
        if (CurrentUserId <= 0) return Unauthorized();
        if (req == null) return BadRequest(new { error = "Request body is required." });

        var (ok, error, id) = await _svc.CreateAddendumAsync(noteId, req, CurrentUserId, CurrentUserRole);
        if (!ok) return Conflict(new { error });
        return Ok(new { addendumId = id });
    }
}
