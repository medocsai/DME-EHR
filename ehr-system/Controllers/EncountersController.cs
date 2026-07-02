using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/encounters")]
[Authorize]
[PhiAccessAudit(EntityType = "Encounter", IdRouteParam = "patientId")]
public class EncountersController : ControllerBase
{
    private readonly IEncounterService _encounterService;

    public EncountersController(IEncounterService encounterService)
    {
        _encounterService = encounterService;
    }

    [HttpGet]
    public async Task<ActionResult> GetEncounters(int patientId)
    {
        try
        {
            var encounters = await _encounterService.GetByPatientAsync(patientId);
            return Ok(encounters);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetEncounter(int patientId, int id)
    {
        var encounter = await _encounterService.GetByIdAsync(id);
        if (encounter == null) return NotFound();
        return Ok(encounter);
    }

    /// <summary>
    /// Convenience endpoint to get encounter by ID without needing patientId in URL.
    /// Used by the Encounter Workspace which only has the encounterId from the URL path.
    /// </summary>
    [HttpGet("~/api/encounters/{id}")]
    public async Task<ActionResult> GetEncounterDirect(int id)
    {
        var encounter = await _encounterService.GetByIdAsync(id);
        if (encounter == null) return NotFound();
        return Ok(encounter);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> CreateEncounter(int patientId, [FromBody] EncounterCreateDto dto)
    {
        try
        {
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
            var encounter = await _encounterService.CreateAsync(patientId, dto, userId);
            return CreatedAtAction(nameof(GetEncounter), new { patientId, id = encounter.EncounterId }, encounter);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateEncounter(int patientId, int id, [FromBody] EncounterUpdateDto dto)
    {
        var encounter = await _encounterService.UpdateAsync(id, dto);
        if (encounter == null) return NotFound();
        return Ok(encounter);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> DeleteEncounter(int patientId, int id)
    {
        var result = await _encounterService.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }

    [HttpGet("by-appointment/{appointmentId}")]
    public async Task<ActionResult> GetByAppointment(int patientId, int appointmentId)
    {
        var encounter = await _encounterService.GetByAppointmentIdAsync(appointmentId);
        if (encounter == null) return NotFound();
        return Ok(encounter);
    }
}
