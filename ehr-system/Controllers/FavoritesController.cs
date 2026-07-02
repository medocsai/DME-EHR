using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Per-user (per-provider) favorite billing codes — starred from the Dx & CPT step.
/// All endpoints scope strictly by the JWT user (NameIdentifier claim) — providers
/// can never read or modify another provider's favorites list.
///
/// Endpoints:
///   GET    /api/favorites?type=ICD10        list current user's ICD favorites
///   GET    /api/favorites?type=CPT          list current user's CPT favorites
///   POST   /api/favorites                   add a favorite (idempotent)
///   DELETE /api/favorites?type=ICD10&code=E11.65   remove by code (no ID needed)
/// </summary>
[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoritesService _favoritesService;

    public FavoritesController(IFavoritesService favoritesService)
    {
        _favoritesService = favoritesService;
    }

    /// <summary>
    /// Resolve the current user's UserId from the JWT NameIdentifier claim.
    /// Returns null if the claim is missing or unparsable.
    /// </summary>
    private int? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : (int?)null;
    }

    /// <summary>
    /// List the current user's favorites for the given codeType ('ICD10' | 'CPT').
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProviderFavoriteCodeDto>>> GetFavorites([FromQuery] string type)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var list = await _favoritesService.GetFavoritesAsync(userId.Value, type);
            return Ok(list);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Add a favorite for the current user. Idempotent — returns existing if already favorited.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProviderFavoriteCodeDto>> AddFavorite([FromBody] AddFavoriteRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var dto = await _favoritesService.AddFavoriteAsync(userId.Value, request);
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Remove a favorite for the current user by codeType + code. Returns 204 if removed,
    /// 404 if not found.
    /// </summary>
    [HttpDelete]
    public async Task<ActionResult> RemoveFavorite([FromQuery] string type, [FromQuery] string code)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var removed = await _favoritesService.RemoveFavoriteAsync(userId.Value, type, code);
            if (!removed) return NotFound();
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
