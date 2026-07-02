using EHR.Models;
using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EHR.Controllers;

/// <summary>
/// Controller for provider search and dropdown functionality.
/// Provides endpoints for populating provider selection dropdowns.
/// </summary>
[ApiController]
[Route("api/providers")]
[Authorize]
public class ProviderSearchController : ControllerBase
{
    private readonly IPatientSearchService _searchService;

    public ProviderSearchController(IPatientSearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// Get providers for dropdown selection.
    /// </summary>
    [HttpGet("dropdown")]
    public async Task<ActionResult<List<ProviderSearchResultDto>>> GetProvidersForDropdown(
        [FromQuery] bool activeOnly = true)
    {
        var result = await _searchService.GetProvidersForDropdownAsync(activeOnly);
        return Ok(result);
    }

    /// <summary>
    /// Get single provider for dropdown pre-population.
    /// </summary>
    [HttpGet("{id}/dropdown")]
    public async Task<ActionResult<ProviderSearchResultDto>> GetProviderForDropdown(int id)
    {
        var result = await _searchService.GetProviderForDropdownAsync(id);
        if (result == null)
            return NotFound();
        return Ok(result);
    }
}
