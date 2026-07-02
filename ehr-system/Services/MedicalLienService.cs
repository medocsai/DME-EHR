using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EHR.Services;

/// <summary>
/// DTO for Medical Lien Form data
/// </summary>
public class MedicalLienFormDataDto
{
    public int PatientId { get; set; }
    public int ProviderId { get; set; }
    public int? LocationId { get; set; }

    // Patient Information
    public string PatientName { get; set; }
    public string PatientAddress { get; set; }
    public string PatientCity { get; set; }
    public string PatientState { get; set; }
    public string PatientZipCode { get; set; }
    public string PatientFullAddress { get; set; }
    public DateOnly? DateOfInjury { get; set; }

    // Attorney Information (from Insurance)
    public string AttorneyName { get; set; }
    public string AttorneyAddress { get; set; }
    public string AttorneyPhone { get; set; }
    public string AttorneyEmail { get; set; }

    // Provider/Therapist Information
    public string ProviderName { get; set; }
    public string ProviderCredentials { get; set; }
    public string ProviderSignatureImagePath { get; set; }
    public byte[] ProviderSignatureBytes { get; set; }

    // Clinic/Tenant Information
    public string ClinicName { get; set; }
    public string ClinicAddress { get; set; }
    public string ClinicCity { get; set; }
    public string ClinicState { get; set; }
    public string ClinicZipCode { get; set; }
    public string ClinicFullAddress { get; set; }
    public string ClinicPhone { get; set; }
    public string ClinicFax { get; set; }
    public string ClinicEmail { get; set; }
}

/// <summary>
/// Response DTO for Medical Lien Form generation
/// </summary>
public class MedicalLienFormResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public byte[] PdfData { get; set; }
    public string FileName { get; set; }
}

/// <summary>
/// DTO for provider list dropdown
/// </summary>
public class MedicalLienProviderDto
{
    public int ProviderId { get; set; }
    public string Name { get; set; }
    public string Credentials { get; set; }
    public bool HasSignature { get; set; }
}

/// <summary>
/// Service interface for Medical Lien Form generation
/// </summary>
public interface IMedicalLienService
{
    /// <summary>
    /// Get data for the Medical Lien Form preview
    /// </summary>
    Task<MedicalLienFormDataDto> GetMedicalLienFormDataAsync(int patientId, int providerId, int? locationId = null);

    /// <summary>
    /// Generate the Medical Lien Form PDF using the template for the clinic/location
    /// </summary>
    Task<MedicalLienFormResponseDto> GenerateMedicalLienPdfAsync(int patientId, int providerId, int locationId);

    /// <summary>
    /// Get list of providers for dropdown selection
    /// </summary>
    Task<List<MedicalLienProviderDto>> GetProvidersForLienFormAsync();
}

/// <summary>
/// Service for generating Medical Lien Forms.
/// Handles PDF generation with patient, attorney, provider, and clinic information.
/// Uses HTML templates assigned to specific clinics and locations.
/// </summary>
public class MedicalLienService : IMedicalLienService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryption;
    private readonly IFileStorageService _fileStorage;
    private readonly IMedicalLienTemplateService _templateService;
    private readonly IHtmlToPdfService _htmlToPdfService;
    private readonly ILogger<MedicalLienService> _logger;

    public MedicalLienService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryption,
        IFileStorageService fileStorage,
        IMedicalLienTemplateService templateService,
        IHtmlToPdfService htmlToPdfService,
        ILogger<MedicalLienService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryption = encryption;
        _fileStorage = fileStorage;
        _templateService = templateService;
        _htmlToPdfService = htmlToPdfService;
        _logger = logger;
    }

    /// <summary>
    /// Get list of providers for dropdown selection
    /// </summary>
    public async Task<List<MedicalLienProviderDto>> GetProvidersForLienFormAsync()
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            throw new InvalidOperationException("Tenant ID is required");
        }

        var providers = await _context.Providers
            .Where(p => p.TenantId == _tenantProvider.TenantId.Value && p.IsActive == true)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Select(p => new MedicalLienProviderDto
            {
                ProviderId = p.ProviderId,
                Name = $"{p.FirstName} {p.LastName}",
                Credentials = p.Credentials,
                HasSignature = !string.IsNullOrEmpty(p.SignatureImagePath)
            })
            .ToListAsync();

        return providers;
    }

    /// <summary>
    /// Get data for the Medical Lien Form preview
    /// </summary>
    public async Task<MedicalLienFormDataDto> GetMedicalLienFormDataAsync(int patientId, int providerId, int? locationId = null)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            throw new InvalidOperationException("Tenant ID is required");
        }

        // Get patient with insurance information
        // AsNoTracking: read-only form preview; Patient/Insurance decrypted for DTO.
        var patient = await _context.Patients
            .AsNoTracking()
            .Include(p => p.Insurances)
            .Include(p => p.Tenant)
            .Where(p => p.PatientId == patientId &&
                       p.TenantId == _tenantProvider.TenantId.Value &&
                       p.IsDeleted != true)
            .FirstOrDefaultAsync();

        if (patient == null)
        {
            throw new KeyNotFoundException($"Patient with ID {patientId} not found");
        }

        // Decrypt patient PHI fields
        _encryption.DecryptEntity(patient);

        // Get provider
        var provider = await _context.Providers
            .Where(p => p.ProviderId == providerId &&
                       p.TenantId == _tenantProvider.TenantId.Value &&
                       p.IsActive == true)
            .FirstOrDefaultAsync();

        if (provider == null)
        {
            throw new KeyNotFoundException($"Provider with ID {providerId} not found");
        }

        // Get tenant (clinic) information
        var tenant = await _context.Tenants
            .Where(t => t.TenantId == _tenantProvider.TenantId.Value)
            .FirstOrDefaultAsync();

        // Get primary insurance with attorney information
        var primaryInsurance = patient.Insurances
            .FirstOrDefault(i => i.IsActive == true &&
                                (i.Type == 0 || // Primary
                                 i.InsuranceCategory == 0 || // Workers Comp
                                 i.InsuranceCategory == 1)); // Personal Injury

        // Decrypt insurance PHI fields if exists
        if (primaryInsurance != null)
        {
            _encryption.DecryptEntity(primaryInsurance);
        }

        // Build patient full address
        var patientAddressParts = new List<string>();
        if (!string.IsNullOrEmpty(patient.Address)) patientAddressParts.Add(patient.Address);
        var patientCityStateZip = BuildCityStateZip(patient.City, patient.State, patient.ZipCode);
        if (!string.IsNullOrEmpty(patientCityStateZip)) patientAddressParts.Add(patientCityStateZip);
        var patientFullAddress = string.Join(", ", patientAddressParts);

        // Build clinic full address
        var clinicAddressParts = new List<string>();
        if (!string.IsNullOrEmpty(tenant?.Address)) clinicAddressParts.Add(tenant.Address);
        var clinicCityStateZip = BuildCityStateZip(tenant?.City, tenant?.State, tenant?.ZipCode);
        if (!string.IsNullOrEmpty(clinicCityStateZip)) clinicAddressParts.Add(clinicCityStateZip);
        var clinicFullAddress = string.Join(", ", clinicAddressParts);

        // Get provider signature bytes if available
        byte[] signatureBytes = null;
        if (!string.IsNullOrEmpty(provider.SignatureImagePath))
        {
            try
            {
                signatureBytes = await _fileStorage.DownloadBytesAsync(provider.SignatureImagePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve provider signature for provider {ProviderId}", providerId);
            }
        }

        return new MedicalLienFormDataDto
        {
            PatientId = patient.PatientId,
            ProviderId = provider.ProviderId,
            LocationId = locationId,

            // Patient Information
            PatientName = $"{patient.FirstName} {patient.LastName}".ToUpper(),
            PatientAddress = patient.Address,
            PatientCity = patient.City,
            PatientState = patient.State,
            PatientZipCode = patient.ZipCode,
            PatientFullAddress = patientFullAddress.ToUpper(),
            DateOfInjury = patient.DateOfInjury,

            // Attorney Information
            AttorneyName = primaryInsurance?.AttorneyName,
            AttorneyPhone = primaryInsurance?.AttorneyPhone,
            AttorneyEmail = primaryInsurance?.AttorneyEmail,
            AttorneyAddress = null, // Note: Attorney address field not in Insurance model

            // Provider Information
            ProviderName = $"{provider.FirstName} {provider.LastName}",
            ProviderCredentials = provider.Credentials,
            ProviderSignatureImagePath = provider.SignatureImagePath,
            ProviderSignatureBytes = signatureBytes,

            // Clinic Information
            ClinicName = tenant?.Name ?? "Healthcare Clinic",
            ClinicAddress = tenant?.Address,
            ClinicCity = tenant?.City,
            ClinicState = tenant?.State,
            ClinicZipCode = tenant?.ZipCode,
            ClinicFullAddress = clinicFullAddress,
            ClinicPhone = tenant?.Phone,
            ClinicFax = null, // Note: Fax field not in Tenant model
            ClinicEmail = tenant?.Email
        };
    }

    /// <summary>
    /// Generate the Medical Lien Form PDF using the template for the clinic/location.
    /// Uses PuppeteerSharp to render HTML exactly as it appears in the browser.
    /// </summary>
    public async Task<MedicalLienFormResponseDto> GenerateMedicalLienPdfAsync(int patientId, int providerId, int locationId)
    {
        try
        {
            if (!_tenantProvider.TenantId.HasValue)
            {
                throw new InvalidOperationException("Tenant ID is required");
            }

            var tenantId = _tenantProvider.TenantId.Value;

            // Get the template for this clinic/location
            var template = await _templateService.GetTemplateForLocationAsync(tenantId, locationId);

            if (template == null)
            {
                _logger.LogWarning("No Medical Lien template found for Tenant {TenantId} Location {LocationId}", tenantId, locationId);
                return new MedicalLienFormResponseDto
                {
                    Success = false,
                    Message = "No Medical Lien Form template configured. Please contact software support at contact@rehabdox.com"
                };
            }

            // Get form data
            var formData = await GetMedicalLienFormDataAsync(patientId, providerId, locationId);

            // Replace placeholders in template HTML
            var processedHtml = _templateService.ReplacePlaceholders(template.HtmlContent, formData);

            // Wrap the HTML in a complete document with print-friendly styling
            var fullHtml = WrapHtmlForPdf(processedHtml);

            // Generate PDF using PuppeteerSharp (renders HTML exactly as in browser)
            var pdfBytes = await _htmlToPdfService.GeneratePdfFromHtmlAsync(fullHtml);

            // Generate filename
            var sanitizedPatientName = SanitizeFileName(formData.PatientName);
            var dateStr = DateTime.Now.ToString("yyyyMMdd");
            var fileName = $"MedicalLien_{sanitizedPatientName}_{dateStr}.pdf";

            return new MedicalLienFormResponseDto
            {
                Success = true,
                Message = "Medical Lien Form generated successfully",
                PdfData = pdfBytes,
                FileName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Medical Lien PDF for patient {PatientId}", patientId);
            return new MedicalLienFormResponseDto
            {
                Success = false,
                Message = $"Failed to generate Medical Lien Form: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Wraps the template HTML in a complete HTML document with print-friendly styling.
    /// </summary>
    private static string WrapHtmlForPdf(string htmlContent)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{
            font-family: 'Times New Roman', Times, serif;
            font-size: 11pt;
            line-height: 1.4;
            margin: 0;
            padding: 20px;
        }}
        table {{
            width: 100%;
            border-collapse: collapse;
        }}
        td, th {{
            vertical-align: top;
            padding: 2px 5px;
        }}
        @media print {{
            body {{
                margin: 0;
                padding: 0;
            }}
        }}
    </style>
</head>
<body>
{htmlContent}
</body>
</html>";
    }

    /// <summary>
    /// Build city, state, zip string
    /// </summary>
    private static string BuildCityStateZip(string city, string state, string zipCode)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(city)) parts.Add(city);
        if (!string.IsNullOrEmpty(state)) parts.Add(state);

        var cityState = string.Join(", ", parts);

        if (!string.IsNullOrEmpty(zipCode))
        {
            return string.IsNullOrEmpty(cityState) ? zipCode : $"{cityState} {zipCode}";
        }

        return cityState;
    }

    /// <summary>
    /// Sanitize filename by removing invalid characters
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "Unknown";

        // Remove invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());

        // Replace spaces with underscores
        sanitized = sanitized.Replace(" ", "_");

        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }
}
