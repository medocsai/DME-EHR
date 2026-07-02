using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EHR.Services;

/// <summary>
/// Service for managing consent form templates.
/// Handles template CRUD, placeholder rendering, and sample data preview.
/// </summary>
public interface IConsentTemplateService
{
    Task<List<ConsentTemplateListDto>> GetTemplatesAsync(int? locationId = null, int? formType = null, bool includeInactive = false);
    Task<ConsentTemplateDetailDto> GetTemplateByIdAsync(int templateId);
    Task<ConsentFormTemplate> CreateTemplateAsync(ConsentTemplateCreateDto dto, int userId);
    Task<ConsentFormTemplate> UpdateTemplateAsync(int templateId, ConsentTemplateUpdateDto dto, int userId);
    Task<bool> DeleteTemplateAsync(int templateId, int userId);
    Task<ConsentTemplatePreviewDto> PreviewTemplateAsync(int templateId);
    Task<ConsentTemplatePreviewDto> PreviewTemplateContentAsync(string htmlContent);
    Task<List<KioskConsentFormDto>> GetRenderedFormsForPatientAsync(int patientId, int appointmentId, int? careEpisodeId);
    Dictionary<string, List<PlaceholderInfo>> GetAvailablePlaceholders();
}

public class ConsentTemplateService : IConsentTemplateService
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryption;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<ConsentTemplateService> _logger;

    public ConsentTemplateService(
        EhrDbContext context,
        EncryptionHelper encryption,
        ITenantProvider tenantProvider,
        ILogger<ConsentTemplateService> logger)
    {
        _context = context;
        _encryption = encryption;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    public async Task<List<ConsentTemplateListDto>> GetTemplatesAsync(int? locationId = null, int? formType = null, bool includeInactive = false)
    {
        var tenantId = _tenantProvider.TenantId;

        var query = _context.ConsentFormTemplates
            .Include(t => t.Location)
            .Include(t => t.CreatedByUser)
            .Where(t => t.TenantId == tenantId && !t.IsDeleted);

        if (!includeInactive)
            query = query.Where(t => t.IsActive);

        if (locationId.HasValue)
            query = query.Where(t => t.LocationId == null || t.LocationId == locationId.Value);

        if (formType.HasValue)
            query = query.Where(t => t.FormType == formType.Value);

        var templates = await query
            .OrderBy(t => t.DisplayOrder)
            .ThenBy(t => t.Name)
            .ToListAsync();

        return templates.Select(t => MapToListDto(t)).ToList();
    }

    public async Task<ConsentTemplateDetailDto> GetTemplateByIdAsync(int templateId)
    {
        var tenantId = _tenantProvider.TenantId;

        var template = await _context.ConsentFormTemplates
            .Include(t => t.Location)
            .Include(t => t.CreatedByUser)
            .Include(t => t.UpdatedByUser)
            .FirstOrDefaultAsync(t => t.ConsentFormTemplateId == templateId && t.TenantId == tenantId && !t.IsDeleted);

        if (template == null)
            return null;

        return MapToDetailDto(template);
    }

    public async Task<ConsentFormTemplate> CreateTemplateAsync(ConsentTemplateCreateDto dto, int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            throw new InvalidOperationException("Tenant context is required");

        // Validate location if specified
        if (dto.LocationId.HasValue)
        {
            var locationExists = await _context.Locations
                .AnyAsync(l => l.LocationId == dto.LocationId.Value && l.TenantId == tenantId);
            if (!locationExists)
                throw new InvalidOperationException("Invalid location specified");
        }

        // Get max display order
        var maxOrder = await _context.ConsentFormTemplates
            .Where(t => t.TenantId == tenantId && !t.IsDeleted)
            .MaxAsync(t => (int?)t.DisplayOrder) ?? -1;

        var template = new ConsentFormTemplate
        {
            TenantId = tenantId.Value,
            LocationId = dto.LocationId,
            Name = dto.Name,
            FormType = dto.FormType,
            Description = dto.Description,
            HtmlContent = dto.HtmlContent,
            DisplayOrder = dto.DisplayOrder > 0 ? dto.DisplayOrder : maxOrder + 1,
            IsActive = dto.IsActive,
            Version = 1,
            IsDeleted = false,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.ConsentFormTemplates.Add(template);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Consent template {TemplateId} created by user {UserId}", template.ConsentFormTemplateId, userId);

        return template;
    }

    public async Task<ConsentFormTemplate> UpdateTemplateAsync(int templateId, ConsentTemplateUpdateDto dto, int userId)
    {
        var tenantId = _tenantProvider.TenantId;

        var template = await _context.ConsentFormTemplates
            .FirstOrDefaultAsync(t => t.ConsentFormTemplateId == templateId && t.TenantId == tenantId && !t.IsDeleted);

        if (template == null)
            return null;

        // Track if content changed (for version increment)
        var contentChanged = dto.HtmlContent != null && dto.HtmlContent != template.HtmlContent;

        // Validate location if specified
        if (dto.LocationId.HasValue)
        {
            var locationExists = await _context.Locations
                .AnyAsync(l => l.LocationId == dto.LocationId.Value && l.TenantId == tenantId);
            if (!locationExists)
                throw new InvalidOperationException("Invalid location specified");
        }

        // Update fields
        if (!string.IsNullOrEmpty(dto.Name))
            template.Name = dto.Name;
        if (dto.FormType.HasValue)
            template.FormType = dto.FormType.Value;
        if (dto.Description != null)
            template.Description = dto.Description;
        if (dto.HtmlContent != null)
            template.HtmlContent = dto.HtmlContent;
        if (dto.LocationId.HasValue || dto.LocationId == null)
            template.LocationId = dto.LocationId;
        if (dto.DisplayOrder.HasValue)
            template.DisplayOrder = dto.DisplayOrder.Value;
        if (dto.IsActive.HasValue)
            template.IsActive = dto.IsActive.Value;

        // Increment version if content changed
        if (contentChanged)
            template.Version++;

        template.UpdatedByUserId = userId;
        template.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Consent template {TemplateId} updated by user {UserId} (version {Version})",
            templateId, userId, template.Version);

        return template;
    }

    public async Task<bool> DeleteTemplateAsync(int templateId, int userId)
    {
        var tenantId = _tenantProvider.TenantId;

        var template = await _context.ConsentFormTemplates
            .FirstOrDefaultAsync(t => t.ConsentFormTemplateId == templateId && t.TenantId == tenantId && !t.IsDeleted);

        if (template == null)
            return false;

        // Soft delete
        template.IsDeleted = true;
        template.IsActive = false;
        template.UpdatedByUserId = userId;
        template.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Consent template {TemplateId} deleted by user {UserId}", templateId, userId);

        return true;
    }

    public async Task<ConsentTemplatePreviewDto> PreviewTemplateAsync(int templateId)
    {
        var template = await _context.ConsentFormTemplates
            .FirstOrDefaultAsync(t => t.ConsentFormTemplateId == templateId && !t.IsDeleted);

        if (template == null)
            return null;

        return PreviewContent(template.HtmlContent);
    }

    public Task<ConsentTemplatePreviewDto> PreviewTemplateContentAsync(string htmlContent)
    {
        return Task.FromResult(PreviewContent(htmlContent));
    }

    public async Task<List<KioskConsentFormDto>> GetRenderedFormsForPatientAsync(int patientId, int appointmentId, int? careEpisodeId)
    {
        // ISSUE #2 FIX: Include PreferredLocation and Tenant for template variable resolution
        var patient = await _context.Patients
            .Include(p => p.PreferredLocation)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(p => p.PatientId == patientId);

        if (patient == null)
            return new List<KioskConsentFormDto>();

        var appointment = await _context.Appointments
            .Include(a => a.Provider)
            .Include(a => a.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

        if (appointment == null)
            return new List<KioskConsentFormDto>();

        CareEpisode careEpisode = null;
        if (careEpisodeId.HasValue)
        {
            careEpisode = await _context.CareEpisodes
                .Include(ce => ce.PrimaryProvider)
                .FirstOrDefaultAsync(ce => ce.CareEpisodeId == careEpisodeId.Value);
        }

        // Determine form type based on whether consent already exists for this care episode
        // Initial Consent (NewCareEpisode=0): First consent in this care episode
        // Ongoing Consent (ReturningVisit=1): Already has consent signed for this care episode
        var hasExistingConsentForCareEpisode = false;
        if (careEpisodeId.HasValue)
        {
            hasExistingConsentForCareEpisode = await _context.CareEpisodeConsents
                .AnyAsync(c => c.CareEpisodeId == careEpisodeId.Value);
        }

        var requiredFormType = hasExistingConsentForCareEpisode
            ? (int)ConsentFormType.ReturningVisit
            : (int)ConsentFormType.NewCareEpisode;

        _logger.LogInformation(
            "Patient {PatientId}, CareEpisode {CareEpisodeId}: Has existing consent = {HasConsent}. Using form type: {FormType}",
            patientId, careEpisodeId, hasExistingConsentForCareEpisode,
            hasExistingConsentForCareEpisode ? "ReturningVisit (Ongoing)" : "NewCareEpisode (Initial)");

        // Get active templates for this location
        // Include tenant-wide templates (LocationId = null) and location-specific templates
        var templates = await _context.ConsentFormTemplates
            .Where(t => t.TenantId == patient.TenantId
                && !t.IsDeleted
                && t.IsActive
                && (t.LocationId == null || t.LocationId == patient.PreferredLocationId)
                && t.FormType == requiredFormType)
            .OrderBy(t => t.DisplayOrder)
            .ThenBy(t => t.Name)
            .ToListAsync();

        // Render each template with patient data
        var forms = new List<KioskConsentFormDto>();
        var order = 0;

        foreach (var template in templates)
        {
            var rendered = RenderTemplate(template.HtmlContent, patient, appointment, careEpisode);
            var signatureFields = ExtractSignatureFields(template.HtmlContent);

            forms.Add(new KioskConsentFormDto
            {
                TemplateId = template.ConsentFormTemplateId,
                FormName = template.Name,
                RenderedHtml = rendered,
                SignatureFields = signatureFields,
                DisplayOrder = order++
            });
        }

        return forms;
    }

    public Dictionary<string, List<PlaceholderInfo>> GetAvailablePlaceholders()
    {
        return ConsentPlaceholders.GetAllPlaceholders();
    }

    #region Private Helper Methods

    private ConsentTemplateListDto MapToListDto(ConsentFormTemplate template)
    {
        return new ConsentTemplateListDto
        {
            ConsentFormTemplateId = template.ConsentFormTemplateId,
            Name = template.Name,
            FormType = template.FormType,
            FormTypeName = template.FormType == 0 ? "New Care Episode" : "Returning Visit",
            Description = template.Description,
            DisplayOrder = template.DisplayOrder,
            IsActive = template.IsActive,
            Version = template.Version,
            LocationName = template.Location?.Name,
            CreatedAt = template.CreatedAt,
            CreatedByUserName = GetUserName(template.CreatedByUser),
            UpdatedAt = template.UpdatedAt
        };
    }

    private ConsentTemplateDetailDto MapToDetailDto(ConsentFormTemplate template)
    {
        return new ConsentTemplateDetailDto
        {
            ConsentFormTemplateId = template.ConsentFormTemplateId,
            Name = template.Name,
            FormType = template.FormType,
            FormTypeName = template.FormType == 0 ? "New Care Episode" : "Returning Visit",
            Description = template.Description,
            DisplayOrder = template.DisplayOrder,
            IsActive = template.IsActive,
            Version = template.Version,
            LocationId = template.LocationId,
            LocationName = template.Location?.Name,
            HtmlContent = template.HtmlContent,
            CreatedAt = template.CreatedAt,
            CreatedByUserId = template.CreatedByUserId,
            CreatedByUserName = GetUserName(template.CreatedByUser),
            UpdatedAt = template.UpdatedAt,
            UpdatedByUserId = template.UpdatedByUserId,
            UpdatedByUserName = GetUserName(template.UpdatedByUser)
        };
    }

    private string GetUserName(User user)
    {
        if (user == null) return null;
        // User FirstName and LastName are stored as plain text (not PHI)
        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private ConsentTemplatePreviewDto PreviewContent(string htmlContent)
    {
        // Create sample patient data
        var sampleData = GetSamplePlaceholderValues();
        var renderedHtml = ReplacePlaceholders(htmlContent, sampleData);
        var signatureFields = ExtractSignatureFields(htmlContent);

        return new ConsentTemplatePreviewDto
        {
            RenderedHtml = renderedHtml,
            SignatureFields = signatureFields
        };
    }

    private string RenderTemplate(string htmlContent, Patient patient, Appointment appointment, CareEpisode careEpisode)
    {
        // ISSUE #8 FIX: Get location's timezone for correct date/time display
        var locationTimeZoneId = appointment.Location?.TimeZoneId ?? patient.PreferredLocation?.TimeZoneId ?? "America/Chicago";
        var nowInLocationTz = TimezoneHelper.ConvertFromUtc(DateTime.UtcNow, locationTimeZoneId);
        var appointmentTimeInLocationTz = TimezoneHelper.ConvertFromUtc(appointment.StartTime, locationTimeZoneId);

        // Get provider name
        var providerName = GetProviderName(appointment.Provider);

        var values = new Dictionary<string, string>
        {
            // Patient data
            [ConsentPlaceholders.PATIENT_FIRST_NAME] = Decrypt(patient.FirstName),
            [ConsentPlaceholders.PATIENT_LAST_NAME] = Decrypt(patient.LastName),
            [ConsentPlaceholders.PATIENT_FULL_NAME] = $"{Decrypt(patient.FirstName)} {Decrypt(patient.LastName)}",
            [ConsentPlaceholders.PATIENT_DOB] = patient.DateOfBirth.ToString("MM/dd/yyyy"),
            [ConsentPlaceholders.PATIENT_DOB_LONG] = patient.DateOfBirth.ToString("MMMM d, yyyy"),
            [ConsentPlaceholders.PATIENT_ADDRESS] = Decrypt(patient.Address) ?? "",
            [ConsentPlaceholders.PATIENT_CITY] = Decrypt(patient.City) ?? "",
            [ConsentPlaceholders.PATIENT_STATE] = Decrypt(patient.State) ?? "",
            [ConsentPlaceholders.PATIENT_ZIP] = Decrypt(patient.ZipCode) ?? "",
            [ConsentPlaceholders.PATIENT_FULL_ADDRESS] = FormatFullAddress(patient),
            [ConsentPlaceholders.PATIENT_PHONE] = FormatPhone(Decrypt(patient.Phone)),
            [ConsentPlaceholders.PATIENT_EMAIL] = Decrypt(patient.Email) ?? "",
            [ConsentPlaceholders.PATIENT_SSN_LAST4] = GetMaskedSsn(patient),
            [ConsentPlaceholders.EMERGENCY_CONTACT_NAME] = Decrypt(patient.EmergencyContactName) ?? "",
            [ConsentPlaceholders.EMERGENCY_CONTACT_PHONE] = FormatPhone(Decrypt(patient.EmergencyContactPhone)),
            [ConsentPlaceholders.EMERGENCY_CONTACT_RELATION] = Decrypt(patient.EmergencyContactRelation) ?? "",

            // Clinic/Location data - ISSUE #2 FIX: Use appointment location as primary, then patient's preferred location
            [ConsentPlaceholders.CLINIC_NAME] = appointment.Location?.Tenant?.Name ?? patient.PreferredLocation?.Tenant?.Name ?? "",
            [ConsentPlaceholders.CLINIC_ADDRESS] = appointment.Location?.Address ?? patient.PreferredLocation?.Address ?? "",
            [ConsentPlaceholders.CLINIC_CITY_STATE_ZIP] = FormatLocationCityStateZip(appointment.Location ?? patient.PreferredLocation),
            [ConsentPlaceholders.CLINIC_PHONE] = FormatPhone(appointment.Location?.Phone ?? patient.PreferredLocation?.Phone),
            [ConsentPlaceholders.CLINIC_FAX] = "",

            // ISSUE #8 FIX: Additional location/provider placeholders
            [ConsentPlaceholders.LOCATION_NAME] = appointment.Location?.Name ?? patient.PreferredLocation?.Name ?? "",
            [ConsentPlaceholders.PROVIDER_NAME] = providerName,

            // Date/Time - ISSUE #8 FIX: Use location's timezone, not server time
            [ConsentPlaceholders.CURRENT_DATE] = nowInLocationTz.ToString("MM/dd/yyyy"),
            [ConsentPlaceholders.CURRENT_DATE_LONG] = nowInLocationTz.ToString("MMMM d, yyyy"),
            [ConsentPlaceholders.CURRENT_TIME] = nowInLocationTz.ToString("h:mm tt"),
            [ConsentPlaceholders.CURRENT_DATETIME] = nowInLocationTz.ToString("MM/dd/yyyy h:mm tt"),

            // ISSUE #8 FIX: Additional date/time placeholders
            [ConsentPlaceholders.TODAY_DATE] = nowInLocationTz.ToString("MMMM d, yyyy"),
            [ConsentPlaceholders.APPOINTMENT_TIME] = appointmentTimeInLocationTz.ToString("h:mm tt"),
            [ConsentPlaceholders.APPOINTMENT_DATE] = appointmentTimeInLocationTz.ToString("MMMM d, yyyy"),

            // Care Episode data (may be null)
            [ConsentPlaceholders.CARE_EPISODE_START_DATE] = careEpisode?.StartDate.ToString("MM/dd/yyyy") ?? nowInLocationTz.ToString("MM/dd/yyyy"),
            [ConsentPlaceholders.DIAGNOSIS] = careEpisode?.PrimaryDiagnosisDescription ?? "To be determined",
            [ConsentPlaceholders.TREATING_PROVIDER] = providerName,
            [ConsentPlaceholders.REFERRING_PHYSICIAN] = ""
        };

        return ReplacePlaceholders(htmlContent, values);
    }

    private string ReplacePlaceholders(string content, Dictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        foreach (var kvp in values)
        {
            content = content.Replace(kvp.Key, kvp.Value ?? "");
        }

        // Replace signature placeholders with canvas elements
        // Note: The label is stored in Group 4 but not rendered in HTML since templates typically
        // include their own labels above the signature placeholder. The label is used for validation messages.
        content = Regex.Replace(content, ConsentPlaceholders.SIGNATURE_PATTERN, match =>
        {
            var fieldId = match.Groups[1].Value;
            var width = match.Groups[2].Value;
            var height = match.Groups[3].Value;
            // Group 4 is the label - not rendered here (used for validation messages only)
            // Group 5 is the optional "required" parameter - used for metadata, not rendered in HTML

            return $@"<div class=""signature-field"" data-field-id=""{fieldId}"" data-width=""{width}"" data-height=""{height}"">
                <div class=""signature-canvas-container"">
                    <canvas class=""signature-canvas"" width=""{width}"" height=""{height}"" data-field-id=""{fieldId}""></canvas>
                </div>
                <div class=""signature-actions"">
                    <button type=""button"" class=""btn btn-sm btn-outline-secondary clear-signature"" data-field-id=""{fieldId}"">Clear</button>
                </div>
            </div>";
        });

        return content;
    }

    private static List<KioskSignatureFieldDto> ExtractSignatureFields(string htmlContent)
    {
        var fields = new List<KioskSignatureFieldDto>();
        var matches = Regex.Matches(htmlContent, ConsentPlaceholders.SIGNATURE_PATTERN);

        foreach (Match match in matches)
        {
            // Group 5 is the optional "required" parameter (true/false)
            // Default to true if not specified
            var requiredParam = match.Groups[5].Success ? match.Groups[5].Value.Trim().ToLower() : "true";
            var isRequired = requiredParam != "false";

            fields.Add(new KioskSignatureFieldDto
            {
                FieldId = match.Groups[1].Value,
                Width = int.TryParse(match.Groups[2].Value, out var w) ? w : 400,
                Height = int.TryParse(match.Groups[3].Value, out var h) ? h : 150,
                Label = match.Groups[4].Value,
                IsRequired = isRequired
            });
        }

        return fields;
    }

    private Dictionary<string, string> GetSamplePlaceholderValues()
    {
        return new Dictionary<string, string>
        {
            [ConsentPlaceholders.PATIENT_FIRST_NAME] = "John",
            [ConsentPlaceholders.PATIENT_LAST_NAME] = "Doe",
            [ConsentPlaceholders.PATIENT_FULL_NAME] = "John Doe",
            [ConsentPlaceholders.PATIENT_DOB] = "01/15/1985",
            [ConsentPlaceholders.PATIENT_DOB_LONG] = "January 15, 1985",
            [ConsentPlaceholders.PATIENT_ADDRESS] = "123 Main Street",
            [ConsentPlaceholders.PATIENT_CITY] = "Springfield",
            [ConsentPlaceholders.PATIENT_STATE] = "IL",
            [ConsentPlaceholders.PATIENT_ZIP] = "62701",
            [ConsentPlaceholders.PATIENT_FULL_ADDRESS] = "123 Main Street, Springfield, IL 62701",
            [ConsentPlaceholders.PATIENT_PHONE] = "(555) 123-4567",
            [ConsentPlaceholders.PATIENT_EMAIL] = "john.doe@email.com",
            [ConsentPlaceholders.PATIENT_SSN_LAST4] = "XXX-XX-1234",
            [ConsentPlaceholders.EMERGENCY_CONTACT_NAME] = "Jane Doe",
            [ConsentPlaceholders.EMERGENCY_CONTACT_PHONE] = "(555) 987-6543",
            [ConsentPlaceholders.EMERGENCY_CONTACT_RELATION] = "Spouse",
            [ConsentPlaceholders.CARE_EPISODE_START_DATE] = DateTime.Today.ToString("MM/dd/yyyy"),
            [ConsentPlaceholders.DIAGNOSIS] = "Low Back Pain (M54.5)",
            [ConsentPlaceholders.TREATING_PROVIDER] = "Dr. Jane Smith, MD",
            [ConsentPlaceholders.REFERRING_PHYSICIAN] = "Dr. Robert Johnson, MD",
            [ConsentPlaceholders.CLINIC_NAME] = "Sample Internal Medicine Clinic",
            [ConsentPlaceholders.CLINIC_ADDRESS] = "456 Healthcare Drive",
            [ConsentPlaceholders.CLINIC_CITY_STATE_ZIP] = "Springfield, IL 62702",
            [ConsentPlaceholders.CLINIC_PHONE] = "(555) 987-6543",
            [ConsentPlaceholders.CLINIC_FAX] = "(555) 987-6544",
            [ConsentPlaceholders.CURRENT_DATE] = DateTime.Now.ToString("MM/dd/yyyy"),
            [ConsentPlaceholders.CURRENT_DATE_LONG] = DateTime.Now.ToString("MMMM d, yyyy"),
            [ConsentPlaceholders.CURRENT_TIME] = DateTime.Now.ToString("h:mm tt"),
            [ConsentPlaceholders.CURRENT_DATETIME] = DateTime.Now.ToString("MM/dd/yyyy h:mm tt")
        };
    }

    private string Decrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return _encryption.Decrypt(value) ?? value;
    }

    private string FormatFullAddress(Patient patient)
    {
        var parts = new List<string>();
        var address = Decrypt(patient.Address);
        var city = Decrypt(patient.City);
        var state = Decrypt(patient.State);
        var zip = Decrypt(patient.ZipCode);

        if (!string.IsNullOrWhiteSpace(address)) parts.Add(address);
        if (!string.IsNullOrWhiteSpace(city)) parts.Add(city);
        if (!string.IsNullOrWhiteSpace(state)) parts.Add(state);
        if (!string.IsNullOrWhiteSpace(zip)) parts.Add(zip);

        return string.Join(", ", parts);
    }

    private static string FormatLocationCityStateZip(Location location)
    {
        if (location == null) return "";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.City)) parts.Add(location.City);
        if (!string.IsNullOrWhiteSpace(location.State)) parts.Add(location.State);
        if (!string.IsNullOrWhiteSpace(location.ZipCode)) parts.Add(location.ZipCode);
        return string.Join(", ", parts);
    }

    private static string FormatPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var digits = Regex.Replace(phone, @"[^\d]", "");
        if (digits.Length == 10)
            return $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";
        return phone;
    }

    private string GetMaskedSsn(Patient patient)
    {
        // We only show last 4 for consent forms, masked format
        if (string.IsNullOrEmpty(patient.SsnLast4Hash))
            return "";

        // If we have encrypted SSN, decrypt and get last 4
        if (!string.IsNullOrEmpty(patient.SsnEncrypted))
        {
            var ssn = _encryption.Decrypt(patient.SsnEncrypted);
            if (!string.IsNullOrEmpty(ssn))
            {
                var digits = Regex.Replace(ssn, @"[^\d]", "");
                if (digits.Length >= 4)
                    return $"XXX-XX-{digits[^4..]}";
            }
        }

        return "XXX-XX-****";
    }

    private string GetProviderName(Provider provider)
    {
        if (provider == null) return "";
        var name = $"{provider.FirstName} {provider.LastName}";
        if (!string.IsNullOrEmpty(provider.Credentials))
            name += $", {provider.Credentials}";
        return name;
    }

    #endregion
}
