using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;
using System.Security.Claims;
using Provider = EHR.Models.Generated.Provider;

namespace EHR.Controllers;

/// <summary>
/// Controller for provider/therapist management operations.
/// Handles CRUD operations for providers, signature management, and user account linking.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProvidersController : ControllerBase
{
    private readonly IProviderService _providerService;
    private readonly EhrDbContext _context;

    public ProvidersController(IProviderService providerService, EhrDbContext context)
    {
        _providerService = providerService;
        _context = context;
    }

    /// <summary>
    /// Get all providers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProviderListDto>>> GetProviders([FromQuery] bool? activeOnly = true)
    {
        var providers = await _providerService.GetProvidersAsync(activeOnly);
        return Ok(providers);
    }

    /// <summary>
    /// Get provider by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Provider>> GetProvider(int id)
    {
        var provider = await _providerService.GetProviderByIdAsync(id);
        if (provider == null) return NotFound();
        return Ok(provider);
    }

    /// <summary>
    /// Create a new provider.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<Provider>> CreateProvider([FromBody] ProviderCreateDto dto)
    {
        var provider = await _providerService.CreateProviderAsync(dto);
        return CreatedAtAction(nameof(GetProvider), new { id = provider.ProviderId }, provider);
    }

    /// <summary>
    /// Update an existing provider.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<Provider>> UpdateProvider(int id, [FromBody] ProviderUpdateDto dto)
    {
        var provider = await _providerService.UpdateProviderAsync(id, dto);
        if (provider == null) return NotFound();
        return Ok(provider);
    }

    /// <summary>
    /// Delete a provider.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> DeleteProvider(int id)
    {
        var result = await _providerService.DeleteProviderAsync(id);
        if (!result) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Get user account information for a provider.
    /// </summary>
    [HttpGet("{id}/user")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<ProviderUserInfoDto>> GetProviderUser(int id)
    {
        var userInfo = await _providerService.GetProviderUserAsync(id);
        if (userInfo == null) return NotFound();
        return Ok(userInfo);
    }

    /// <summary>
    /// Upload a signature image for a provider.
    /// </summary>
    [HttpPost("{id}/signature")]
    [RequestSizeLimit(5 * 1024 * 1024)] // 5MB limit for signature images
    [Authorize(Roles = "0,1,2")] // SuperAdmin, ClinicAdmin, Clinician
    public async Task<ActionResult<ProviderSignatureResponseDto>> UploadSignature(
        int id,
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProviderSignatureResponseDto
            {
                ProviderId = id,
                HasSignature = false,
                Message = "No file provided"
            });
        }

        var result = await _providerService.UploadSignatureAsync(id, file);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Get a provider's signature image.
    /// </summary>
    [HttpGet("{id}/signature")]
    public async Task<IActionResult> GetSignature(int id)
    {
        var result = await _providerService.GetSignatureAsync(id);
        if (result == null)
            return NotFound(new { message = "Signature not found" });

        var (stream, contentType) = result.Value;
        return File(stream, contentType);
    }

    /// <summary>
    /// Delete a provider's signature image.
    /// </summary>
    [HttpDelete("{id}/signature")]
    [Authorize(Roles = "0,1,2")] // SuperAdmin, ClinicAdmin, Clinician
    public async Task<IActionResult> DeleteSignature(int id)
    {
        var result = await _providerService.DeleteSignatureAsync(id);
        if (!result)
            return NotFound(new { message = "Provider or signature not found" });

        return Ok(new ProviderSignatureResponseDto
        {
            ProviderId = id,
            HasSignature = false,
            Message = "Signature deleted successfully"
        });
    }

    // ============================================
    // PROFILE PICTURE ENDPOINTS
    // ============================================

    /// <summary>
    /// Upload a profile picture for a provider.
    /// </summary>
    [HttpPost("{id}/profile-picture")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    [Authorize(Roles = "0,1,2")] // SuperAdmin, ClinicAdmin, Clinician
    public async Task<ActionResult<ProfilePictureResponseDto>> UploadProfilePicture(
        int id,
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProfilePictureResponseDto
            {
                EntityId = id,
                EntityType = "Provider",
                HasProfilePicture = false,
                Message = "No file provided"
            });
        }

        var result = await _providerService.UploadProfilePictureAsync(id, file);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Get a provider's profile picture.
    /// AllowAnonymous because img tags cannot send JWT Authorization headers.
    /// Tenant isolation is enforced in the service layer.
    /// </summary>
    [HttpGet("{id}/profile-picture")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProfilePicture(int id)
    {
        var result = await _providerService.GetProfilePictureAsync(id);
        if (result == null)
            return NotFound(new { message = "Profile picture not found" });

        var (stream, contentType) = result.Value;
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return File(stream, contentType);
    }

    /// <summary>
    /// Delete a provider's profile picture.
    /// </summary>
    [HttpDelete("{id}/profile-picture")]
    [Authorize(Roles = "0,1,2")] // SuperAdmin, ClinicAdmin, Clinician
    public async Task<IActionResult> DeleteProfilePicture(int id)
    {
        var result = await _providerService.DeleteProfilePictureAsync(id);
        if (!result)
            return NotFound(new { message = "Provider or profile picture not found" });

        return Ok(new ProfilePictureResponseDto
        {
            EntityId = id,
            EntityType = "Provider",
            HasProfilePicture = false,
            Message = "Profile picture deleted successfully"
        });
    }

    /// <summary>
    /// Get current provider's preferences (uses ProviderId from JWT claims).
    /// </summary>
    [HttpGet("my-preferences")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> GetMyPreferences()
    {
        var providerIdClaim = User.FindFirst("ProviderId")?.Value;
        if (string.IsNullOrEmpty(providerIdClaim) || !int.TryParse(providerIdClaim, out var providerId))
            return Ok(new { ShowResumePopup = false }); // Non-providers get default

        var provider = await _context.Providers
            .AsNoTracking()
            .Where(p => p.ProviderId == providerId)
            .Select(p => new { p.ShowResumePopup })
            .FirstOrDefaultAsync();

        if (provider == null) return Ok(new { ShowResumePopup = false });
        return Ok(provider);
    }

    /// <summary>
    /// Update current provider's preferences.
    /// </summary>
    [HttpPut("my-preferences")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> UpdateMyPreferences([FromBody] ProviderPreferencesDto dto)
    {
        var providerIdClaim = User.FindFirst("ProviderId")?.Value;
        if (string.IsNullOrEmpty(providerIdClaim) || !int.TryParse(providerIdClaim, out var providerId))
            return BadRequest(new { message = "No provider linked to this account" });

        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null) return NotFound();

        provider.ShowResumePopup = dto.ShowResumePopup;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { provider.ShowResumePopup });
    }
}
