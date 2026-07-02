using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientPortalAuthService
{
    /// <summary>
    /// Verify patient identity using Date of Birth and last 4 digits of SSN.
    /// Returns matching patients across all tenants.
    /// </summary>
    Task<PatientPortalVerifyResult> VerifyPatientAsync(DateOnly dateOfBirth, string ssnLast4);

    /// <summary>
    /// Generate a portal-specific JWT token for a verified patient.
    /// 4-hour expiry, contains PatientId, TenantId, LocationId claims.
    /// </summary>
    string GeneratePortalToken(Patient patient, Tenant tenant, Location? location = null);

    /// <summary>
    /// Get available locations for a tenant.
    /// </summary>
    Task<List<PortalLocationDto>> GetTenantLocationsAsync(int tenantId);
}

/// <summary>
/// Result of patient identity verification
/// </summary>
public class PatientPortalVerifyResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>If true, patient exists at multiple clinics and must choose one</summary>
    public bool RequiresClinicSelection { get; set; }

    /// <summary>Available clinics when RequiresClinicSelection is true</summary>
    public List<PortalClinicOption>? AvailableClinics { get; set; }

    /// <summary>The matched patient (single match) or null (multi-match or no match)</summary>
    public Patient? Patient { get; set; }

    /// <summary>The tenant for single-match scenarios</summary>
    public Tenant? Tenant { get; set; }

    /// <summary>Generated token for single-match auto-login</summary>
    public string? Token { get; set; }

    /// <summary>Patient display name</summary>
    public string? PatientName { get; set; }

    /// <summary>Patient ID</summary>
    public int? PatientId { get; set; }

    /// <summary>Token expiry</summary>
    public DateTime? TokenExpiry { get; set; }

    /// <summary>If true, tenant has multiple locations and patient must choose</summary>
    public bool RequiresLocationSelection { get; set; }

    /// <summary>Available locations when RequiresLocationSelection is true</summary>
    public List<PortalLocationDto>? AvailableLocations { get; set; }

    /// <summary>Selected tenant ID (for multi-step flow)</summary>
    public int? TenantId { get; set; }
}

public class PortalClinicOption
{
    public int TenantId { get; set; }
    public string ClinicName { get; set; } = string.Empty;
    public int PatientId { get; set; }
}

public class PortalLocationDto
{
    public int LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
}
