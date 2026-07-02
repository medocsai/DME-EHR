using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

/// <summary>
/// Controller for insurance prior authorization management.
/// Handles authorization tracking, approval workflow, and payer API integration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[PhiAccessAudit(EntityType = "InsuranceAuthorization")]
public class AuthorizationsController : ControllerBase
{
    private readonly IInsuranceAuthorizationService _authorizationService;

    public AuthorizationsController(IInsuranceAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Get all authorizations for an insurance record.
    /// </summary>
    [HttpGet("insurance/{insuranceId}")]
    public async Task<ActionResult<List<AuthorizationDto>>> GetAuthorizationsForInsurance(int insuranceId)
    {
        var authorizations = await _authorizationService.GetAuthorizationsForInsuranceAsync(insuranceId);
        return Ok(authorizations);
    }

    /// <summary>
    /// Get the current (most recent) authorization for an insurance record.
    /// </summary>
    [HttpGet("insurance/{insuranceId}/current")]
    public async Task<ActionResult<AuthorizationDto>> GetCurrentAuthorization(int insuranceId)
    {
        var authorization = await _authorizationService.GetCurrentAuthorizationAsync(insuranceId);
        if (authorization == null)
            return NotFound(new { message = "No authorization found for this insurance" });
        return Ok(authorization);
    }

    /// <summary>
    /// Get authorization history with summary information for an insurance record.
    /// </summary>
    [HttpGet("insurance/{insuranceId}/history")]
    public async Task<ActionResult<AuthorizationHistoryDto>> GetAuthorizationHistory(int insuranceId)
    {
        var history = await _authorizationService.GetAuthorizationHistoryAsync(insuranceId);
        if (history == null)
            return NotFound(new { message = "Insurance not found" });
        return Ok(history);
    }

    /// <summary>
    /// Get authorization by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AuthorizationDto>> GetAuthorization(int id)
    {
        var authorization = await _authorizationService.GetAuthorizationByIdAsync(id);
        if (authorization == null)
            return NotFound(new { message = "Authorization not found" });
        return Ok(authorization);
    }

    /// <summary>
    /// Create a new authorization record.
    /// Only available to Clinic Admin (Role 0, 1) and Front Desk (Role 3).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,3")]
    public async Task<ActionResult<AuthorizationDto>> CreateAuthorization([FromBody] AuthorizationCreateDto dto)
    {
        try
        {
            var authorization = await _authorizationService.CreateAuthorizationAsync(dto);
            return CreatedAtAction(nameof(GetAuthorization), new { id = authorization.AuthorizationId }, authorization);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing authorization record.
    /// Only available to Clinic Admin (Role 0, 1) and Front Desk (Role 3).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,3")]
    public async Task<ActionResult<AuthorizationDto>> UpdateAuthorization(int id, [FromBody] AuthorizationUpdateDto dto)
    {
        try
        {
            var authorization = await _authorizationService.UpdateAuthorizationAsync(id, dto);
            return Ok(authorization);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete an authorization record.
    /// Only available to Clinic Admin (Role 0, 1) and Front Desk (Role 3).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,3")]
    public async Task<ActionResult> DeleteAuthorization(int id)
    {
        var success = await _authorizationService.DeleteAuthorizationAsync(id);
        if (!success)
            return NotFound(new { message = "Authorization not found" });
        return Ok(new { message = "Authorization deleted successfully" });
    }

    /// <summary>
    /// Fetch authorization data from mock payer API.
    /// Only available to Clinic Admin (Role 0, 1) and Front Desk (Role 3).
    /// </summary>
    [HttpPost("insurance/{insuranceId}/fetch")]
    [Authorize(Roles = "0,1,3")]
    public async Task<ActionResult<MockAuthorizationFetchDto>> FetchMockAuthorization(int insuranceId)
    {
        var result = await _authorizationService.FetchMockAuthorizationAsync(insuranceId);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }
}
