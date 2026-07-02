using EHR.Models.Generated;
using EHR.Models;
using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Controller for managing Medical Lien Form templates.
/// Templates are HTML-based with placeholders and are assigned to specific clinics and locations.
/// Super Admin only - used to configure templates for different clinics/locations.
/// </summary>
[ApiController]
[Route("api/medical-lien-templates")]
[Authorize(Roles = "0")] // Super Admin only
public class MedicalLienTemplateController : ControllerBase
{
    private readonly IMedicalLienTemplateService _templateService;
    private readonly ILogger<MedicalLienTemplateController> _logger;

    public MedicalLienTemplateController(
        IMedicalLienTemplateService templateService,
        ILogger<MedicalLienTemplateController> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    /// <summary>
    /// Get all Medical Lien templates.
    /// </summary>
    /// <param name="tenantId">Optional tenant ID to filter templates</param>
    /// <param name="activeOnly">If true, only return active templates (default: false for admin view)</param>
    /// <returns>List of templates</returns>
    [HttpGet]
    public async Task<ActionResult<List<MedicalLienTemplateListDto>>> GetTemplates(
        [FromQuery] int? tenantId = null,
        [FromQuery] bool activeOnly = false)
    {
        try
        {
            var result = await _templateService.GetTemplatesAsync(tenantId, activeOnly);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Medical Lien templates");
            return StatusCode(500, new { message = "An error occurred while retrieving templates" });
        }
    }

    /// <summary>
    /// Get a specific template by ID with full HTML content.
    /// </summary>
    /// <param name="id">Template ID</param>
    /// <returns>Template details including HTML content</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<MedicalLienTemplateDto>> GetTemplate(int id)
    {
        try
        {
            var result = await _templateService.GetByIdAsync(id);
            if (result == null)
            {
                return NotFound(new { message = "Template not found" });
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Medical Lien template {TemplateId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the template" });
        }
    }

    /// <summary>
    /// Get available placeholders for templates.
    /// </summary>
    /// <returns>Dictionary of placeholders and their descriptions</returns>
    [HttpGet("placeholders")]
    public ActionResult<Dictionary<string, string>> GetPlaceholders()
    {
        var placeholders = _templateService.GetAvailablePlaceholders();
        return Ok(placeholders);
    }

    /// <summary>
    /// Create a new Medical Lien template.
    /// </summary>
    /// <param name="dto">Template creation data</param>
    /// <returns>Created template</returns>
    [HttpPost]
    public async Task<ActionResult<MedicalLienTemplate>> CreateTemplate([FromBody] MedicalLienTemplateCreateDto dto)
    {
        try
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var result = await _templateService.CreateAsync(dto, userId);
            return CreatedAtAction(nameof(GetTemplate), new { id = result.TemplateId }, result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when creating Medical Lien template");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Medical Lien template");
            return StatusCode(500, new { message = "An error occurred while creating the template" });
        }
    }

    /// <summary>
    /// Update an existing Medical Lien template.
    /// </summary>
    /// <param name="id">Template ID</param>
    /// <param name="dto">Updated template data</param>
    /// <returns>Updated template</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<MedicalLienTemplate>> UpdateTemplate(int id, [FromBody] MedicalLienTemplateUpdateDto dto)
    {
        try
        {
            var result = await _templateService.UpdateAsync(id, dto);
            if (result == null)
            {
                return NotFound(new { message = "Template not found" });
            }
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation when updating Medical Lien template {TemplateId}", id);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Medical Lien template {TemplateId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the template" });
        }
    }

    /// <summary>
    /// Delete a Medical Lien template.
    /// </summary>
    /// <param name="id">Template ID</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTemplate(int id)
    {
        try
        {
            var result = await _templateService.DeleteAsync(id);
            if (!result)
            {
                return NotFound(new { message = "Template not found" });
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Medical Lien template {TemplateId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the template" });
        }
    }
}
