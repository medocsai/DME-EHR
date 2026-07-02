using EHR.Models;
using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EHR.Controllers;

/// <summary>
/// Controller for patient search and autocomplete functionality.
/// Provides optimized search endpoints for patient lookup in forms and dropdowns.
/// Uses blind index for HIPAA-compliant, scalable search.
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientSearchController : ControllerBase
{
    private readonly IPatientSearchService _searchService;

    public PatientSearchController(IPatientSearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// Search patients with autocomplete support.
    /// Returns paginated results with multiple identifying fields.
    /// Uses blind index for efficient HIPAA-compliant search.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<PatientSearchResponse>> SearchPatients(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int? preferredProviderId,
        [FromQuery] int? locationId,
        [FromQuery] int take = 20,
        [FromQuery] int skip = 0,
        [FromQuery] bool activeOnly = true)
    {
        var request = new PatientSearchRequest
        {
            Query = query ?? string.Empty,
            PreferredProviderId = preferredProviderId,
            LocationId = locationId,
            Take = take,
            Skip = skip,
            ActiveOnly = activeOnly
        };

        var result = await _searchService.SearchPatientsAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Get single patient for autocomplete pre-population.
    /// </summary>
    [HttpGet("{id}/autocomplete")]
    public async Task<ActionResult<PatientSearchResultDto>> GetPatientForAutocomplete(int id)
    {
        var result = await _searchService.GetPatientForAutocompleteAsync(id);
        if (result == null)
            return NotFound();
        return Ok(result);
    }
}
