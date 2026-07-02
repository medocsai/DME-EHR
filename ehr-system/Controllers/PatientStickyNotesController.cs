using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Models;
using EHR.Services;
using System.Security.Claims;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/sticky-notes")]
[Authorize(Roles = "0,1,2,6,7")] // SuperAdmin, ClinicAdmin, Clinician, MA, Nurse
[PhiAccessAudit(EntityType = "PatientStickyNote", IdRouteParam = "patientId")]
public class PatientStickyNotesController : ControllerBase
{
    private readonly IPatientStickyNoteService _stickyNoteService;

    public PatientStickyNotesController(IPatientStickyNoteService stickyNoteService)
    {
        _stickyNoteService = stickyNoteService;
    }

    [HttpGet]
    public async Task<ActionResult<List<PatientStickyNoteListDto>>> GetStickyNotes(int patientId)
    {
        var notes = await _stickyNoteService.GetStickyNotesAsync(patientId);
        return Ok(notes);
    }

    [HttpPost]
    public async Task<ActionResult<PatientStickyNoteListDto>> CreateStickyNote(
        int patientId,
        [FromBody] PatientStickyNoteCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

        var result = await _stickyNoteService.CreateStickyNoteAsync(patientId, dto, userId, userName);
        if (result == null)
            return BadRequest(new { message = "Failed to create sticky note. Content is required." });

        return CreatedAtAction(nameof(GetStickyNotes), new { patientId }, result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteStickyNote(int patientId, int id)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var result = await _stickyNoteService.DeleteStickyNoteAsync(id, userId);
        if (!result)
            return NotFound(new { message = "Sticky note not found" });

        return Ok(new { message = "Sticky note deleted" });
    }
}
