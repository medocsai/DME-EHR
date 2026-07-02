using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Controller for legacy note management (deprecated).
/// This controller handles the older Note entity. For new clinical documentation,
/// use the ClinicalNotesController instead.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotesController : ControllerBase
{
    private readonly INoteService _noteService;

    public NotesController(INoteService noteService)
    {
        _noteService = noteService;
    }

    /// <summary>
    /// Get notes with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<NoteListDto>>> GetNotes(
        [FromQuery] int? patientId,
        [FromQuery] int? providerId,
        [FromQuery] NoteStatus? status)
    {
        var notes = await _noteService.GetNotesAsync(patientId, providerId, status);
        return Ok(notes);
    }

    /// <summary>
    /// Get note by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Note>> GetNote(int id)
    {
        var note = await _noteService.GetNoteByIdAsync(id);
        if (note == null) return NotFound();
        return Ok(note);
    }

    /// <summary>
    /// Create a new note.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<Note>> CreateNote([FromBody] NoteCreateDto dto)
    {
        var note = await _noteService.CreateNoteAsync(dto);
        return CreatedAtAction(nameof(GetNote), new { id = note.NoteId }, note);
    }

    /// <summary>
    /// Update an existing note.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<Note>> UpdateNote(int id, [FromBody] NoteUpdateDto dto)
    {
        var note = await _noteService.UpdateNoteAsync(id, dto);
        if (note == null) return NotFound();
        return Ok(note);
    }

    /// <summary>
    /// Sign a note.
    /// </summary>
    [HttpPost("{id}/sign")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<Note>> SignNote(int id)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var note = await _noteService.SignNoteAsync(id, userId);
        if (note == null) return NotFound();
        return Ok(note);
    }
}
