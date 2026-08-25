// ============================================================================
// DTOs for MEDOCS DME.
//
// Trimmed 2026-08-25 from 289 types to the 27 the product actually uses, when the
// clinical EHR this app was copied from was removed. Every type below is
// reachable from a live controller or service, or is referenced by one that is.
// Nothing here is speculative: if a DTO stops being used, delete it.
// ============================================================================

using EHR.Models.Generated;

namespace EHR.Models
{
    public class DashboardStatsDto
    {
        public int TodayAppointments { get; set; }
        public int CompletedToday { get; set; }
        public int PendingNotes { get; set; }
        public decimal TodayCollections { get; set; }
        public int ActivePatients { get; set; }
        public int TotalPatients { get; set; }
        public decimal OutstandingAR { get; set; }
        public int AuthorizationsExpiringSoon { get; set; }
        public int NoShowsToday { get; set; }
        public int ClaimsPending { get; set; }
    }

    public class TenantCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Subdomain { get; set; }  // Optional - auto-generated from Name if not provided
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string TaxId { get; set; }
        public string NPI { get; set; }
        public int Plan { get; set; } = 0;
        public string AdminEmail { get; set; } = string.Empty;
        public string AdminPassword { get; set; } = string.Empty;
        public string AdminFirstName { get; set; } = string.Empty;
        public string AdminLastName { get; set; } = string.Empty;
        /// <summary>
        /// Name for the initial/default location. If not provided, defaults to tenant name or "Main Office".
        /// </summary>
        public string? InitialLocationName { get; set; }
        /// <summary>
        /// IANA timezone identifier for the initial location (e.g., "America/Chicago", "America/Los_Angeles")
        /// Required when creating a tenant. Defaults to "America/Chicago" if not provided.
        /// </summary>
        public string InitialLocationTimeZoneId { get; set; } = "America/Chicago";
    }

    public class TenantListDto
    {
        public int TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public string Email { get; set; }
        public int Plan { get; set; }
        public int Status { get; set; }
        public int UserCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TenantUpdateDto
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string TaxId { get; set; }
        public string NPI { get; set; }
        public string LogoUrl { get; set; }
        public int? Plan { get; set; }
        public int? Status { get; set; }
    }

    public class TenantDetailDto
    {
        public int TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? TaxId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("NPI")]
        public string? Npi { get; set; }
        public string? LogoUrl { get; set; }
        public int? Plan { get; set; }
        public int? Status { get; set; }
        public int? MaxUsers { get; set; }
        public int? MaxPatients { get; set; }
        public DateTime? SubscriptionStartDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? Settings { get; set; }
    }

    public class UserLoginDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        /// <summary>
        /// Optional TenantId when user has selected a specific clinic from the tenant selection screen
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// Trusted device token from the __medocs_dt cookie (populated by controller). Used to skip OTP.
        /// </summary>
        public string DeviceToken { get; set; }

        /// <summary>
        /// User agent string (populated by controller). Stored on TrustedDevice for audit.
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Caller IP address (populated by controller from HttpContext). Used in
        /// audit log entries for failed-login forensics. Never trusted from
        /// the client — always overwritten server-side before AuthService sees it.
        /// </summary>
        public string IpAddress { get; set; }
    }

    public class VerifyOtpDto
    {
        public string Email { get; set; } = string.Empty;
        public string OtpCode { get; set; } = string.Empty;

        /// <summary>
        /// Optional TenantId when multi-tenant user selects a clinic after OTP verification.
        /// </summary>
        public int? TenantId { get; set; }

        /// <summary>
        /// If true, create a 15-day device trust token so the user can skip OTP on this device.
        /// </summary>
        public bool RememberDevice { get; set; }

        /// <summary>
        /// User agent string (populated by controller).
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Caller IP address (populated by controller from HttpContext).
        /// </summary>
        public string IpAddress { get; set; }
    }

    public class ResendOtpDto
    {
        public string Email { get; set; } = string.Empty;
    }

    public class UserLoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; }
        public DateTime? TokenExpiry { get; set; }
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int Role { get; set; }
        public int? TenantId { get; set; }
        public string TenantName { get; set; }
        public string TenantSubdomain { get; set; }
        public bool TenantHasLogo { get; set; }
        /// <summary>
        /// True if user belongs to multiple tenants and must select one
        /// </summary>
        public bool RequiresTenantSelection { get; set; }
        /// <summary>
        /// List of available tenants when user belongs to multiple clinics
        /// </summary>
        public List<TenantSelectionDto> AvailableTenants { get; set; }

        // ---- Multi-Location Support ----
        /// <summary>
        /// Current location ID the user is logged into (default location on first login)
        /// </summary>
        public int? LocationId { get; set; }
        /// <summary>
        /// Current location name
        /// </summary>
        public string LocationName { get; set; }
        /// <summary>
        /// IANA timezone identifier for the current location (e.g., "America/New_York")
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation for current location (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
        /// <summary>
        /// List of available locations for the user's tenant
        /// </summary>
        public List<LocationSelectionDto> AvailableLocations { get; set; }

        // ---- OTP Login Flow ----
        /// <summary>
        /// True when email+password are correct and an OTP was sent to the user's email.
        /// Client should show the OTP input step and call /api/auth/verify-otp.
        /// </summary>
        public bool RequiresOtpVerification { get; set; }

        /// <summary>
        /// Masked email (e.g., "j***n@medocs.ai") to display on the OTP step without leaking the full address.
        /// </summary>
        public string MaskedEmail { get; set; }

        /// <summary>
        /// Plain device-trust token returned on successful OTP verify when RememberDevice was true.
        /// The controller sets this as the __medocs_dt HttpOnly cookie and clears it before sending the response body.
        /// </summary>
        public string DeviceToken { get; set; }

        /// <summary>
        /// True when the user requested "Remember Device" at OTP step but login is waiting for tenant selection.
        /// Signals the client to pass RememberDevice=true on the second verify-otp call.
        /// </summary>
        public bool PendingDeviceTrust { get; set; }
    }

    public class TenantSelectionDto
    {
        public int TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UserListDto
    {
        public int UserId { get; set; }
        public int? TenantId { get; set; }
        public string TenantName { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public string Phone { get; set; }
        public int Role { get; set; }
        public string RoleName => GetRoleName(Role);
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? CreatedAt { get; set; }

        private static string GetRoleName(int role) => role switch
        {
            0 => "Super Admin",
            1 => "Clinic Admin",
            2 => "Clinician",
            3 => "Front Desk",
            4 => "Biller",
            _ => "Read Only"
        };
    }

    public class UserCreateDto
    {
        public int? TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; }
        public int Role { get; set; }
    }

    public class UserUpdateDto
    {
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Phone { get; set; }
        public int? Role { get; set; }
        public bool? IsActive { get; set; }
    }

    public class AdminResetPasswordDto
    {
        public int UserId { get; set; }
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AdminChangeEmailDto
    {
        public int UserId { get; set; }
        public string NewEmail { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequestDto
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class LocationListDto
    {
        public int LocationId { get; set; }
        public int TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string Phone { get; set; }
        public bool IsActive { get; set; }
        public bool IsPrimary { get; set; }
        /// <summary>
        /// IANA timezone identifier for this location (e.g., "America/New_York", "America/Los_Angeles")
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string FacilityNpi { get; set; }
        public string PlaceOfServiceCode { get; set; }
        /// <summary>
        /// Per-location feature flag: enables the Longevity section in patient intake
        /// and surfaces longevity data in the Patient Profile and Encounter intake panel.
        /// </summary>
        public bool EnableLongevity { get; set; }
    }

    public class LocationDropdownDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; }
        /// <summary>
        /// IANA timezone identifier for this location
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
    }

    public class LocationCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string Phone { get; set; }
        public bool IsPrimary { get; set; }
        /// <summary>
        /// IANA timezone identifier (e.g., "America/Chicago", "America/Los_Angeles")
        /// Required when creating a location. Defaults to "America/Chicago" if not provided.
        /// </summary>
        public string TimeZoneId { get; set; } = "America/Chicago";
        /// <summary>
        /// Per-location feature flag: Longevity section in patient intake.
        /// </summary>
        public bool EnableLongevity { get; set; }
    }

    public class LocationUpdateDto
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string Phone { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsPrimary { get; set; }
        /// <summary>
        /// IANA timezone identifier (e.g., "America/New_York", "America/Los_Angeles")
        /// </summary>
        public string TimeZoneId { get; set; }
        public string FacilityNpi { get; set; }
        public string PlaceOfServiceCode { get; set; }
        /// <summary>
        /// Per-location feature flag: enables the Longevity section in patient intake
        /// and surfaces longevity data in the Patient Profile and Encounter intake panel.
        /// </summary>
        public bool? EnableLongevity { get; set; }
    }

    public class LocationDetailDto : LocationListDto
    {
        public string TenantName { get; set; }
        /// <summary>
        /// Full timezone display name (e.g., "Eastern Standard Time")
        /// </summary>
        public string TimeZoneDisplayName { get; set; }
        /// <summary>
        /// Current UTC offset for the timezone (e.g., "-05:00")
        /// </summary>
        public string UtcOffset { get; set; }
    }

    public class LocationSelectionDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        /// <summary>
        /// IANA timezone identifier for this location
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
    }

    public class SwitchLocationDto
    {
        public int LocationId { get; set; }
    }

    public class SwitchLocationResponseDto
    {
        public bool Success { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; }
        public string Message { get; set; }
        /// <summary>
        /// New JWT token with updated location claim (optional - if using JWT for location)
        /// </summary>
        public string Token { get; set; }
        /// <summary>
        /// IANA timezone identifier for the new location
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
    }

}
