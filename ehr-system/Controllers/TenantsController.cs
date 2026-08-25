using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;
using Microsoft.AspNetCore.Authorization;
using EHR.Models;
using EHR.Services;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;

namespace EHR.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "0,1")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly IFileStorageService _storageService;
    private readonly FilePathBuilder _pathBuilder;
    private readonly MetadataBuilder _metadataBuilder;
    private readonly ILogger<TenantsController> _logger;

    private static readonly HashSet<string> AllowedLogoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".svg"
    };
    private const long MaxLogoSize = 2 * 1024 * 1024; // 2MB

    public TenantsController(
        ITenantService tenantService,
        IFileStorageService storageService,
        FilePathBuilder pathBuilder,
        MetadataBuilder metadataBuilder,
        ILogger<TenantsController> logger)
    {
        _tenantService = tenantService;
        _storageService = storageService;
        _pathBuilder = pathBuilder;
        _metadataBuilder = metadataBuilder;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<List<TenantListDto>>> GetTenants()
    {
        try
        {
            _logger.LogInformation("[Tenants] GET /api/tenants - fetching all tenants");
            var tenants = await _tenantService.GetAllTenantsAsync();
            _logger.LogInformation("[Tenants] GET /api/tenants - returning {Count} tenants", tenants.Count);
            return Ok(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] GET /api/tenants FAILED: {Message}", ex.Message);
            return this.ServerError(ex, "Failed to load tenants");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TenantDetailDto>> GetTenant(int id)
    {
        try
        {
            _logger.LogInformation("[Tenants] GET /api/tenants/{Id} - fetching tenant", id);
            var tenant = await _tenantService.GetTenantByIdAsync(id);
            if (tenant == null)
            {
                _logger.LogWarning("[Tenants] GET /api/tenants/{Id} - tenant not found", id);
                return NotFound();
            }

            var dto = MapToDetailDto(tenant);
            _logger.LogInformation("[Tenants] GET /api/tenants/{Id} - returning tenant '{Name}'", id, dto.Name);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] GET /api/tenants/{Id} FAILED: {Message}", id, ex.Message);
            return this.ServerError(ex, "Failed to load tenant");
        }
    }

    [HttpGet("subdomain/{subdomain}")]
    public async Task<ActionResult<TenantDetailDto>> GetTenantBySubdomain(string subdomain)
    {
        try
        {
            _logger.LogInformation("[Tenants] GET /api/tenants/subdomain/{Subdomain}", subdomain);
            var tenant = await _tenantService.GetTenantBySubdomainAsync(subdomain);
            if (tenant == null)
                return NotFound();

            return Ok(MapToDetailDto(tenant));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] GET /api/tenants/subdomain/{Subdomain} FAILED: {Message}", subdomain, ex.Message);
            return this.ServerError(ex, "Failed to load tenant by subdomain");
        }
    }

    [HttpPost]
    [Authorize(Roles = "0")]
    public async Task<ActionResult<TenantDetailDto>> CreateTenant([FromBody] TenantCreateDto dto)
    {
        try
        {
            _logger.LogInformation("[Tenants] POST /api/tenants - creating tenant '{Name}'", dto.Name);
            var tenant = await _tenantService.CreateTenantAsync(dto);
            var detailDto = MapToDetailDto(tenant);
            _logger.LogInformation("[Tenants] POST /api/tenants - created tenant Id={Id}, Name='{Name}'", tenant.TenantId, tenant.Name);
            return CreatedAtAction(nameof(GetTenant), new { id = tenant.TenantId }, detailDto);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[Tenants] POST /api/tenants - validation error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] POST /api/tenants FAILED: {Message}", ex.Message);
            return this.ServerError(ex, "Failed to create tenant");
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TenantDetailDto>> UpdateTenant(int id, [FromBody] TenantUpdateDto dto)
    {
        try
        {
            _logger.LogInformation("[Tenants] PUT /api/tenants/{Id} - updating tenant", id);
            var tenant = await _tenantService.UpdateTenantAsync(id, dto);
            if (tenant == null)
            {
                _logger.LogWarning("[Tenants] PUT /api/tenants/{Id} - tenant not found", id);
                return NotFound();
            }

            var detailDto = MapToDetailDto(tenant);
            _logger.LogInformation("[Tenants] PUT /api/tenants/{Id} - updated successfully", id);
            return Ok(detailDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] PUT /api/tenants/{Id} FAILED: {Message}", id, ex.Message);
            return this.ServerError(ex, "Failed to update tenant");
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0")]
    public async Task<ActionResult> DeleteTenant(int id)
    {
        try
        {
            var result = await _tenantService.DeleteTenantAsync(id);
            if (!result)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] DELETE /api/tenants/{Id} FAILED: {Message}", id, ex.Message);
            return this.ServerError(ex, $"Failed to delete tenant {id}");
        }
    }

    [HttpPost("{id}/status")]
    [Authorize(Roles = "0")]
    public async Task<ActionResult> UpdateTenantStatus(int id, [FromBody] UpdateStatusRequest request)
    {
        try
        {
            var result = await _tenantService.UpdateTenantStatusAsync(id, request.Status);
            if (!result)
                return NotFound();

            return Ok(new { message = "Status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tenants] POST /api/tenants/{Id}/status FAILED: {Message}", id, ex.Message);
            return this.ServerError(ex, $"Failed to update status");
        }
    }


    [HttpPost("{id}/logo")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult> UploadLogo(int id, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedLogoExtensions.Contains(extension))
            return BadRequest(new { message = $"File type '{extension}' is not allowed. Allowed: .jpg, .jpeg, .png, .webp, .svg" });

        if (file.Length > MaxLogoSize)
            return BadRequest(new { message = "File size exceeds maximum allowed size of 2MB" });

        var tenant = await _tenantService.GetTenantByIdAsync(id);
        if (tenant == null)
            return NotFound();

        // Delete existing logo if present
        if (!string.IsNullOrEmpty(tenant.LogoUrl))
        {
            await _storageService.DeleteFileAsync(tenant.LogoUrl);
        }

        var fileName = _pathBuilder.GenerateStorageFileName(file.FileName);
        var folderPath = _pathBuilder.BuildTenantLogoPath(id);
        var metadata = _metadataBuilder.BuildTenantLogoMetadata(id);

        using var stream = file.OpenReadStream();
        var result = await _storageService.UploadFileAsync(stream, fileName, folderPath, file.ContentType, metadata);

        if (!result.Success)
            return StatusCode(500, new { message = "Failed to upload logo" });

        // Update tenant record with the logo cloud path
        var updateDto = new TenantUpdateDto { LogoUrl = result.CloudPath };
        await _tenantService.UpdateTenantAsync(id, updateDto);

        return Ok(new { message = "Logo uploaded successfully", logoUrl = result.CloudPath });
    }

    /// <summary>
    /// Serve a tenant's logo image. AllowAnonymous because img tags cannot send JWT headers.
    /// </summary>
    [HttpGet("{id}/logo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLogo(int id)
    {
        var tenant = await _tenantService.GetTenantByIdAsync(id);
        if (tenant == null || string.IsNullOrEmpty(tenant.LogoUrl))
            return NotFound(new { message = "Logo not found" });

        var stream = await _storageService.DownloadFileAsync(tenant.LogoUrl);
        if (stream == null)
            return NotFound(new { message = "Logo file not found" });

        var ext = Path.GetExtension(tenant.LogoUrl).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(stream, contentType);
    }

    private static TenantDetailDto MapToDetailDto(Tenant tenant) => new()
    {
        TenantId = tenant.TenantId,
        Name = tenant.Name,
        Subdomain = tenant.Subdomain,
        Phone = tenant.Phone,
        Email = tenant.Email,
        Address = tenant.Address,
        City = tenant.City,
        State = tenant.State,
        ZipCode = tenant.ZipCode,
        TaxId = tenant.TaxId,
        Npi = tenant.Npi,
        LogoUrl = tenant.LogoUrl,
        Plan = tenant.Plan,
        Status = tenant.Status,
        MaxUsers = tenant.MaxUsers,
        MaxPatients = tenant.MaxPatients,
        SubscriptionStartDate = tenant.SubscriptionStartDate,
        SubscriptionEndDate = tenant.SubscriptionEndDate,
        CreatedAt = tenant.CreatedAt,
        Settings = tenant.Settings
    };

    [HttpDelete("{id}/logo")]
    public async Task<ActionResult> DeleteLogo(int id)
    {
        var tenant = await _tenantService.GetTenantByIdAsync(id);
        if (tenant == null)
            return NotFound();

        if (!string.IsNullOrEmpty(tenant.LogoUrl))
        {
            await _storageService.DeleteFileAsync(tenant.LogoUrl);
        }

        var updateDto = new TenantUpdateDto { LogoUrl = "" };
        await _tenantService.UpdateTenantAsync(id, updateDto);

        return Ok(new { message = "Logo deleted successfully" });
    }
}

public class UpdateStatusRequest
{
    public TenantStatus Status { get; set; }
}
