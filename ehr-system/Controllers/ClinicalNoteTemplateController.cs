using EHR.Models;
using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Controller for managing clinical note templates.
/// Templates define the structure and content for SOAP notes, evaluations, and other documentation types.
/// </summary>
[ApiController]
[Route("api/clinical-note-templates")]
[Authorize]
public class ClinicalNoteTemplateController : ControllerBase
{
    private readonly IClinicalNoteTemplateService _templateService;

    public ClinicalNoteTemplateController(IClinicalNoteTemplateService templateService)
    {
        _templateService = templateService;
    }

    /// <summary>
    /// Get all templates.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ClinicalNoteTemplateListDto>>> GetTemplates(
        [FromQuery] bool activeOnly = true)
    {
        var result = await _templateService.GetTemplatesAsync(activeOnly);
        return Ok(result);
    }

    /// <summary>
    /// Get templates filtered by location.
    /// Uses tenant + location fallback: tenant-specific templates preferred, system templates as fallback.
    /// </summary>
    [HttpGet("for-location")]
    public async Task<ActionResult<List<ClinicalNoteTemplateDto>>> GetTemplatesForLocation(
        [FromQuery] int? locationId,
        [FromQuery] bool activeOnly = true)
    {
        var result = await _templateService.GetTemplatesForLocationAsync(locationId, activeOnly);
        return Ok(result);
    }

    /// <summary>
    /// Get template by ID with full HTML content.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ClinicalNoteTemplateDetailDto>> GetTemplate(int id)
    {
        var result = await _templateService.GetByIdAsync(id);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Create new template (Admin only).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<ClinicalNoteTemplate>> CreateTemplate(
        [FromBody] ClinicalNoteTemplateCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var result = await _templateService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetTemplate), new { id = result.TemplateId }, result);
    }

    /// <summary>
    /// Update template (Admin only).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<ClinicalNoteTemplate>> UpdateTemplate(
        int id,
        [FromBody] ClinicalNoteTemplateUpdateDto dto)
    {
        try
        {
            var result = await _templateService.UpdateAsync(id, dto);
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
    /// Delete template (Admin only).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> DeleteTemplate(int id)
    {
        try
        {
            var result = await _templateService.DeleteAsync(id);
            if (!result)
                return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Seed default templates (Admin only).
    /// </summary>
    [HttpPost("seed-defaults")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> SeedDefaults()
    {
        await _templateService.SeedDefaultTemplatesAsync();
        return Ok(new { message = "Default templates seeded" });
    }
}
