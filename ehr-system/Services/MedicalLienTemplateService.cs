using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Models;

namespace EHR.Services;

/// <summary>
/// Interface for Medical Lien Template management operations.
/// </summary>
public interface IMedicalLienTemplateService
{
    /// <summary>
    /// Get all templates, optionally filtered by tenant
    /// </summary>
    Task<List<MedicalLienTemplateListDto>> GetTemplatesAsync(int? tenantId = null, bool activeOnly = true);

    /// <summary>
    /// Get a specific template by ID
    /// </summary>
    Task<MedicalLienTemplateDto?> GetByIdAsync(int templateId);

    /// <summary>
    /// Get the active template for a specific tenant and location
    /// </summary>
    Task<MedicalLienTemplateDto?> GetTemplateForLocationAsync(int tenantId, int locationId);

    /// <summary>
    /// Create a new template
    /// </summary>
    Task<MedicalLienTemplate> CreateAsync(MedicalLienTemplateCreateDto dto, int createdByUserId);

    /// <summary>
    /// Update an existing template
    /// </summary>
    Task<MedicalLienTemplate?> UpdateAsync(int templateId, MedicalLienTemplateUpdateDto dto);

    /// <summary>
    /// Delete a template
    /// </summary>
    Task<bool> DeleteAsync(int templateId);

    /// <summary>
    /// Get available placeholders for templates
    /// </summary>
    Dictionary<string, string> GetAvailablePlaceholders();

    /// <summary>
    /// Replace placeholders in template HTML with actual values
    /// </summary>
    string ReplacePlaceholders(string templateHtml, MedicalLienFormDataDto formData);
}

/// <summary>
/// Service for managing Medical Lien Form templates.
/// Templates are assigned to specific tenant + location combinations.
/// </summary>
public class MedicalLienTemplateService : IMedicalLienTemplateService
{
    private readonly EhrDbContext _context;
    private readonly ILogger<MedicalLienTemplateService> _logger;

    public MedicalLienTemplateService(
        EhrDbContext context,
        ILogger<MedicalLienTemplateService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<MedicalLienTemplateListDto>> GetTemplatesAsync(int? tenantId = null, bool activeOnly = true)
    {
        var query = _context.MedicalLienTemplates
            .Include(t => t.Tenant)
            .Include(t => t.Location)
            .AsQueryable();

        if (tenantId.HasValue)
        {
            query = query.Where(t => t.TenantId == tenantId.Value);
        }

        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query
            .OrderBy(t => t.Tenant.Name)
            .ThenBy(t => t.Location.Name)
            .Select(t => new MedicalLienTemplateListDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                Description = t.Description,
                TenantId = t.TenantId,
                TenantName = t.Tenant != null ? t.Tenant.Name : "Unknown",
                LocationId = t.LocationId,
                LocationName = t.Location != null ? t.Location.Name : "Unknown",
                IsActive = t.IsActive,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<MedicalLienTemplateDto?> GetByIdAsync(int templateId)
    {
        return await _context.MedicalLienTemplates
            .Include(t => t.Tenant)
            .Include(t => t.Location)
            .Where(t => t.TemplateId == templateId)
            .Select(t => new MedicalLienTemplateDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                Description = t.Description,
                TenantId = t.TenantId,
                TenantName = t.Tenant != null ? t.Tenant.Name : "Unknown",
                LocationId = t.LocationId,
                LocationName = t.Location != null ? t.Location.Name : "Unknown",
                HtmlContent = t.HtmlContent,
                IsActive = t.IsActive,
                CreatedByUserId = t.CreatedByUserId,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<MedicalLienTemplateDto?> GetTemplateForLocationAsync(int tenantId, int locationId)
    {
        return await _context.MedicalLienTemplates
            .Include(t => t.Tenant)
            .Include(t => t.Location)
            .Where(t => t.TenantId == tenantId && t.LocationId == locationId && t.IsActive)
            .Select(t => new MedicalLienTemplateDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                Description = t.Description,
                TenantId = t.TenantId,
                TenantName = t.Tenant != null ? t.Tenant.Name : "Unknown",
                LocationId = t.LocationId,
                LocationName = t.Location != null ? t.Location.Name : "Unknown",
                HtmlContent = t.HtmlContent,
                IsActive = t.IsActive,
                CreatedByUserId = t.CreatedByUserId,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<MedicalLienTemplate> CreateAsync(MedicalLienTemplateCreateDto dto, int createdByUserId)
    {
        // Validate tenant exists
        var tenantExists = await _context.Tenants.AnyAsync(t => t.TenantId == dto.TenantId && t.IsDeleted != true);
        if (!tenantExists)
        {
            throw new InvalidOperationException($"Tenant with ID {dto.TenantId} not found");
        }

        // Validate location exists and belongs to tenant
        var locationExists = await _context.Locations
            .AnyAsync(l => l.LocationId == dto.LocationId && l.TenantId == dto.TenantId && l.IsActive == true);
        if (!locationExists)
        {
            throw new InvalidOperationException($"Location with ID {dto.LocationId} not found or does not belong to the specified tenant");
        }

        // Check if an active template already exists for this tenant + location
        var existingTemplate = await _context.MedicalLienTemplates
            .AnyAsync(t => t.TenantId == dto.TenantId && t.LocationId == dto.LocationId && t.IsActive);

        if (existingTemplate && dto.IsActive)
        {
            throw new InvalidOperationException($"An active Medical Lien template already exists for this clinic and location. Please deactivate or delete the existing template first.");
        }

        var template = new MedicalLienTemplate
        {
            TenantId = dto.TenantId,
            LocationId = dto.LocationId,
            Name = dto.Name,
            Description = dto.Description,
            HtmlContent = dto.HtmlContent,
            IsActive = dto.IsActive,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.MedicalLienTemplates.Add(template);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created Medical Lien Template {TemplateId} for Tenant {TenantId} Location {LocationId}",
            template.TemplateId, dto.TenantId, dto.LocationId);

        return template;
    }

    public async Task<MedicalLienTemplate?> UpdateAsync(int templateId, MedicalLienTemplateUpdateDto dto)
    {
        var template = await _context.MedicalLienTemplates.FindAsync(templateId);
        if (template == null)
        {
            return null;
        }

        // If changing tenant/location, validate they exist
        if (dto.TenantId.HasValue && dto.TenantId.Value != template.TenantId)
        {
            var tenantExists = await _context.Tenants.AnyAsync(t => t.TenantId == dto.TenantId.Value && t.IsDeleted != true);
            if (!tenantExists)
            {
                throw new InvalidOperationException($"Tenant with ID {dto.TenantId} not found");
            }
            template.TenantId = dto.TenantId.Value;
        }

        if (dto.LocationId.HasValue && dto.LocationId.Value != template.LocationId)
        {
            var tenantId = dto.TenantId ?? template.TenantId;
            var locationExists = await _context.Locations
                .AnyAsync(l => l.LocationId == dto.LocationId.Value && l.TenantId == tenantId && l.IsActive == true);
            if (!locationExists)
            {
                throw new InvalidOperationException($"Location with ID {dto.LocationId} not found or does not belong to the specified tenant");
            }
            template.LocationId = dto.LocationId.Value;
        }

        // Check for duplicate if tenant/location changed and IsActive is true
        var newTenantId = dto.TenantId ?? template.TenantId;
        var newLocationId = dto.LocationId ?? template.LocationId;
        var newIsActive = dto.IsActive ?? template.IsActive;

        if (newIsActive)
        {
            var duplicateExists = await _context.MedicalLienTemplates
                .AnyAsync(t => t.TenantId == newTenantId && t.LocationId == newLocationId && t.IsActive && t.TemplateId != templateId);

            if (duplicateExists)
            {
                throw new InvalidOperationException($"An active Medical Lien template already exists for this clinic and location.");
            }
        }

        if (!string.IsNullOrEmpty(dto.Name))
            template.Name = dto.Name;

        if (dto.Description != null)
            template.Description = dto.Description;

        if (!string.IsNullOrEmpty(dto.HtmlContent))
            template.HtmlContent = dto.HtmlContent;

        if (dto.IsActive.HasValue)
            template.IsActive = dto.IsActive.Value;

        template.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated Medical Lien Template {TemplateId}", templateId);

        return template;
    }

    public async Task<bool> DeleteAsync(int templateId)
    {
        var template = await _context.MedicalLienTemplates.FindAsync(templateId);
        if (template == null)
        {
            return false;
        }

        _context.MedicalLienTemplates.Remove(template);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted Medical Lien Template {TemplateId}", templateId);

        return true;
    }

    public Dictionary<string, string> GetAvailablePlaceholders()
    {
        return MedicalLienPlaceholders.GetAllPlaceholders();
    }

    public string ReplacePlaceholders(string templateHtml, MedicalLienFormDataDto formData)
    {
        if (string.IsNullOrEmpty(templateHtml))
            return string.Empty;

        var result = templateHtml;

        // Patient Information
        result = result.Replace(MedicalLienPlaceholders.PatientName, formData.PatientName ?? "");
        result = result.Replace(MedicalLienPlaceholders.PatientAddress, formData.PatientAddress ?? "");
        result = result.Replace(MedicalLienPlaceholders.DateOfInjury,
            formData.DateOfInjury?.ToString("MM/dd/yyyy") ?? "Not specified");

        // Attorney Information
        result = result.Replace(MedicalLienPlaceholders.AttorneyName, formData.AttorneyName ?? "");
        result = result.Replace(MedicalLienPlaceholders.AttorneyAddress, formData.AttorneyAddress ?? "");
        result = result.Replace(MedicalLienPlaceholders.AttorneyPhone, formData.AttorneyPhone ?? "");
        result = result.Replace(MedicalLienPlaceholders.AttorneyEmail, formData.AttorneyEmail ?? "");

        // Provider Information
        result = result.Replace(MedicalLienPlaceholders.ProviderName, formData.ProviderName ?? "");

        // Provider Signature - embed as base64 image if available
        if (formData.ProviderSignatureBytes != null && formData.ProviderSignatureBytes.Length > 0)
        {
            var signatureBase64 = Convert.ToBase64String(formData.ProviderSignatureBytes);
            var signatureImg = $"<img src=\"data:image/png;base64,{signatureBase64}\" alt=\"Provider Signature\" style=\"max-height: 60px;\" />";
            result = result.Replace(MedicalLienPlaceholders.ProviderSignature, signatureImg);
        }
        else
        {
            result = result.Replace(MedicalLienPlaceholders.ProviderSignature, "<span style=\"border-bottom: 1px solid #000; display: inline-block; width: 200px;\">&nbsp;</span>");
        }

        // Clinic Information
        result = result.Replace(MedicalLienPlaceholders.ClinicName, formData.ClinicName ?? "");
        result = result.Replace(MedicalLienPlaceholders.ClinicAddress, formData.ClinicAddress ?? "");
        result = result.Replace(MedicalLienPlaceholders.ClinicPhone, formData.ClinicPhone ?? "");
        result = result.Replace(MedicalLienPlaceholders.ClinicEmail, formData.ClinicEmail ?? "");

        // Date
        result = result.Replace(MedicalLienPlaceholders.TodayDate, DateTime.Today.ToString("MM/dd/yyyy"));

        return result;
    }
}
