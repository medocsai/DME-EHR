using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EHR.Models;

// ============================================
// KIOSK SETTINGS DTOs
// ============================================

public class KioskSettingsDto
{
    public int LocationId { get; set; }
    public string LocationName { get; set; }
    public string KioskToken { get; set; }
    public string KioskUrl { get; set; }
    public bool IsEnabled { get; set; }
    public int SessionTimeoutMinutes { get; set; }
    public DateTime? TokenGeneratedAt { get; set; }
    public string TokenGeneratedByUserName { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public string LastAccessIpAddress { get; set; }
}

public class KioskSettingsUpdateDto
{
    public bool? IsEnabled { get; set; }

    // 0 = never timeout, otherwise 5-120 minutes (validation done in service layer if needed)
    public int? SessionTimeoutMinutes { get; set; }
}

public class KioskTokenRegenerateResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string NewToken { get; set; }
    public string NewKioskUrl { get; set; }
    public DateTime GeneratedAt { get; set; }
}

// ============================================
// KIOSK VALIDATION DTOs
// ============================================

public class KioskValidateTokenResponseDto
{
    public bool IsValid { get; set; }
    public string Message { get; set; }
    public string ClinicName { get; set; }
    public string ClinicLogoUrl { get; set; }
    public string LocationName { get; set; }
    public string LocationAddress { get; set; }
    public int SessionTimeoutMinutes { get; set; }
    /// <summary>ISSUE #7 FIX: Location's IANA timezone identifier for correct time display</summary>
    public string TimeZoneId { get; set; }
    /// <summary>ISSUE #7 FIX: Location's timezone abbreviation (e.g., "CT", "EST")</summary>
    public string TimeZoneAbbreviation { get; set; }
}

public class KioskVerifyPatientRequestDto
{
    // Identity verification (as of 2026-05): LastName + DateOfBirth + ZipCode.
    // SSN was removed. LastName / ZipCode are encrypted on Patient, so the
    // service narrows the candidate set by DOB first, then decrypts + compares.
    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100)]
    public string LastName { get; set; }

    [Required]
    public DateOnly DateOfBirth { get; set; }

    [Required(ErrorMessage = "ZIP code is required")]
    [StringLength(10, MinimumLength = 5, ErrorMessage = "Enter a valid ZIP code")]
    public string ZipCode { get; set; }
}

public class KioskVerifyPatientResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string SessionToken { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
    public KioskPatientInfoDto PatientInfo { get; set; }
    public KioskAppointmentInfoDto AppointmentInfo { get; set; }
    public bool HasExistingConsent { get; set; }

    /// <summary>
    /// True when the patient has already signed consent for today's appointment
    /// (typically via the patient portal before arriving). The kiosk skips the
    /// consent forms screen and shows a "Yes, I am Here" confirmation instead.
    /// Set on the same response as Success=true + a valid SessionToken.
    /// 2026-05.
    /// </summary>
    public bool AlreadyConsented { get; set; }
}

public class KioskPatientInfoDto
{
    public int PatientId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string FullName { get; set; }
    public DateOnly DateOfBirth { get; set; }
}

public class KioskAppointmentInfoDto
{
    public int AppointmentId { get; set; }
    public DateTime StartTime { get; set; }
    public string AppointmentType { get; set; }
    public string ProviderName { get; set; }
    public int? CareEpisodeId { get; set; }
}

// ============================================
// CONSENT FORM DTOs
// ============================================

public class KioskConsentFormsResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public List<KioskConsentFormDto> Forms { get; set; } = new();
    public KioskPatientInfoDto PatientInfo { get; set; }
    public KioskAppointmentInfoDto AppointmentInfo { get; set; }
}

public class KioskConsentFormDto
{
    public int TemplateId { get; set; }
    public string FormName { get; set; }
    public string RenderedHtml { get; set; }
    public List<KioskSignatureFieldDto> SignatureFields { get; set; } = new();
    public int DisplayOrder { get; set; }
}

public class KioskSignatureFieldDto
{
    public string FieldId { get; set; }
    public string Label { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsRequired { get; set; }
}

public class KioskSignatureSubmissionDto
{
    public string FieldId { get; set; }
    public string ImageData { get; set; } // Base64 encoded PNG
    public DateTime SignedAt { get; set; }
}

public class KioskFormProgressDto
{
    public int TemplateId { get; set; }
    public DateTime? ViewedAt { get; set; }
    public int ViewDurationSeconds { get; set; }
    public List<KioskSignatureSubmissionDto> Signatures { get; set; } = new();
}

public class KioskSubmitConsentRequestDto
{
    public List<KioskFormProgressDto> Forms { get; set; } = new();
    public bool ConfirmationChecked { get; set; }
}

// ============================================
// PORTAL CONSENT — patient signs at home (2026-05)
// ============================================
// Same shape as the kiosk submit but scoped by AppointmentId from the JWT-
// authenticated portal session. There is no KioskSession in this flow — the
// portal JWT proves the patient's identity, so the server only needs to
// confirm the appointment belongs to that patient (tenant-isolated).

public class PortalConsentSubmitRequestDto
{
    [System.ComponentModel.DataAnnotations.Required]
    public int AppointmentId { get; set; }

    public List<KioskFormProgressDto> Forms { get; set; } = new();
    public bool ConfirmationChecked { get; set; }
}

public class PortalConsentSubmitResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int? ConsentId { get; set; }
    public string AppointmentTime { get; set; }
    public string ProviderName { get; set; }
}

/// <summary>One row in the portal "consent awaiting your signature" list.</summary>
public class PortalAwaitingConsentDto
{
    public int AppointmentId { get; set; }
    public DateTime StartTime { get; set; }
    public string StartTimeFormatted { get; set; }
    public string DateFormatted { get; set; }
    public string ProviderName { get; set; }
    public string LocationName { get; set; }
    public string AppointmentType { get; set; }
    public bool IsTelehealth { get; set; }
}

public class KioskSubmitConsentResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int? ConsentId { get; set; }
    public string PatientFirstName { get; set; }
    public string AppointmentTime { get; set; }
    public string ProviderName { get; set; }

    /// <summary>
    /// Consent → intake handoff block. Non-null only when the patient has an
    /// IntakePortalToken and has not yet submitted intake. When set, the kiosk
    /// success screen renders a "Start/Continue Intake Form" button instead of
    /// auto-advancing to the next patient. See rules/technical/consent-to-intake-handoff.md.
    /// </summary>
    public KioskIntakeHandoffDto Intake { get; set; }

    /// <summary>
    /// Intake portal token for the patient — controller-only signal so
    /// KioskController can set the intake-verify cookie. Excluded from JSON
    /// serialization to keep the token out of the response payload (the URL
    /// in <see cref="KioskIntakeHandoffDto.Url"/> already carries it for the
    /// browser; the cookie is set as a Set-Cookie header by the controller).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? IntakeTokenForCookie { get; set; }

    /// <summary>
    /// Patient id for the audit log row (controller writes the audit). Excluded
    /// from JSON; the consent submit response does not expose PatientId.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? PatientIdForAudit { get; set; }

    /// <summary>
    /// Kiosk session id for the audit row's EntityId. Excluded from JSON.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? KioskSessionIdForAudit { get; set; }

    /// <summary>
    /// Tenant id for the audit row. Excluded from JSON.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? TenantIdForAudit { get; set; }
}

/// <summary>
/// Intake handoff payload returned with a successful consent submit when the
/// patient has not yet finished their intake form. The kiosk JS reads this to
/// decide whether to show the "Start/Continue Intake Form" button.
/// </summary>
public class KioskIntakeHandoffDto
{
    /// <summary>"not_started" or "partial". Drives the button label.</summary>
    public string State { get; set; }

    /// <summary>Wizard URL with `?return=kiosk` so the wizard exits back to /Kiosk?app=1.</summary>
    public string Url { get; set; }

    public int Completed { get; set; }
    public int Total { get; set; }
}

// ============================================
// CONSENT TEMPLATE DTOs (Admin)
// ============================================

public class ConsentTemplateListDto
{
    public int ConsentFormTemplateId { get; set; }
    public string Name { get; set; }
    public int FormType { get; set; }
    public string FormTypeName { get; set; }
    public string Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public string LocationName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserName { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ConsentTemplateDetailDto : ConsentTemplateListDto
{
    public int? LocationId { get; set; }
    public string HtmlContent { get; set; }
    public int CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }
    public string UpdatedByUserName { get; set; }
}

public class ConsentTemplateCreateDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; }

    [Required]
    [Range(0, 1)]
    public int FormType { get; set; }

    [StringLength(500)]
    public string Description { get; set; }

    [Required]
    public string HtmlContent { get; set; }

    public int? LocationId { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ConsentTemplateUpdateDto
{
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; }

    [Range(0, 1)]
    public int? FormType { get; set; }

    [StringLength(500)]
    public string Description { get; set; }

    public string HtmlContent { get; set; }

    public int? LocationId { get; set; }

    public int? DisplayOrder { get; set; }

    public bool? IsActive { get; set; }
}

public class ConsentTemplatePreviewDto
{
    public string RenderedHtml { get; set; }
    public List<KioskSignatureFieldDto> SignatureFields { get; set; } = new();
}

// ============================================
// CONSENT RECORD DTOs (Admin View)
// ============================================

public class PatientConsentHistoryDto
{
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public List<CareEpisodeConsentGroupDto> CareEpisodeConsents { get; set; } = new();
    public List<ConsentRecordDto> OrphanConsents { get; set; } = new();
}

public class CareEpisodeConsentGroupDto
{
    public int CareEpisodeId { get; set; }
    public string Diagnosis { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string ProviderName { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; }
    public List<ConsentRecordDto> Consents { get; set; } = new();
    public bool HasConsent => Consents != null && Consents.Count > 0;
}

public class ConsentRecordDto
{
    public int CareEpisodeConsentId { get; set; }
    public int? CareEpisodeId { get; set; }
    public int? AppointmentId { get; set; } // Nullable for manual uploads without appointment
    public DateTime AppointmentDate { get; set; }
    public string AppointmentType { get; set; }
    public int ConsentType { get; set; }
    public string ConsentTypeName { get; set; }
    public DateTime SignedAt { get; set; }
    public int FormCount { get; set; }
    public List<ConsentFormSummaryDto> Forms { get; set; } = new();
    public string LocationName { get; set; }
    public string IpAddress { get; set; }
}

public class ConsentFormSummaryDto
{
    public int CareEpisodeConsentFormId { get; set; }
    public string FormName { get; set; }
    public int SignatureCount { get; set; }
    public DateTime SignedAt { get; set; }
}

public class ConsentPdfResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public byte[] PdfData { get; set; }
    public string FileName { get; set; }
}

// ============================================
// CONSENT STATUS DTOs
// ============================================

public class PatientConsentStatusDto
{
    public int PatientId { get; set; }
    public string PatientName { get; set; }

    // Current care episode consent status
    public int? CurrentCareEpisodeId { get; set; }
    public bool CurrentCareEpisodeHasConsent { get; set; }
    public ConsentRecordDto CurrentCareEpisodeConsent { get; set; }

    // Upcoming appointment without consent
    public bool HasUpcomingAppointmentNeedingConsent { get; set; }
    public KioskAppointmentInfoDto UpcomingAppointment { get; set; }
}

// ============================================
// SIGNALR NOTIFICATION DTOs
// ============================================

public class ConsentNotificationDto
{
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public int AppointmentId { get; set; }
    public DateTime AppointmentTime { get; set; }
    public int FormCount { get; set; }
    public DateTime CompletedAt { get; set; }
    public string LocationName { get; set; }
}

// ============================================
// SSN UPDATE DTO
// ============================================

public class PatientSsnUpdateDto
{
    [Required]
    [StringLength(11, MinimumLength = 9)]
    [RegularExpression(@"^\d{3}-?\d{2}-?\d{4}$", ErrorMessage = "Invalid SSN format")]
    public string Ssn { get; set; }
}

// ============================================
// PLACEHOLDER REFERENCE
// ============================================

public static class ConsentPlaceholders
{
    // Patient data placeholders
    public const string PATIENT_FULL_NAME = "{{PATIENT_FULL_NAME}}";
    public const string PATIENT_FIRST_NAME = "{{PATIENT_FIRST_NAME}}";
    public const string PATIENT_LAST_NAME = "{{PATIENT_LAST_NAME}}";
    public const string PATIENT_DOB = "{{PATIENT_DOB}}";
    public const string PATIENT_DOB_LONG = "{{PATIENT_DOB_LONG}}";
    public const string PATIENT_ADDRESS = "{{PATIENT_ADDRESS}}";
    public const string PATIENT_CITY = "{{PATIENT_CITY}}";
    public const string PATIENT_STATE = "{{PATIENT_STATE}}";
    public const string PATIENT_ZIP = "{{PATIENT_ZIP}}";
    public const string PATIENT_FULL_ADDRESS = "{{PATIENT_FULL_ADDRESS}}";
    public const string PATIENT_PHONE = "{{PATIENT_PHONE}}";
    public const string PATIENT_EMAIL = "{{PATIENT_EMAIL}}";
    public const string PATIENT_SSN_LAST4 = "{{PATIENT_SSN_LAST4}}";

    // Emergency contact placeholders
    public const string EMERGENCY_CONTACT_NAME = "{{EMERGENCY_CONTACT_NAME}}";
    public const string EMERGENCY_CONTACT_PHONE = "{{EMERGENCY_CONTACT_PHONE}}";
    public const string EMERGENCY_CONTACT_RELATION = "{{EMERGENCY_CONTACT_RELATION}}";

    // Care episode data placeholders
    public const string CARE_EPISODE_START_DATE = "{{CARE_EPISODE_START_DATE}}";
    public const string DIAGNOSIS = "{{DIAGNOSIS}}";
    public const string TREATING_PROVIDER = "{{TREATING_PROVIDER}}";
    public const string REFERRING_PHYSICIAN = "{{REFERRING_PHYSICIAN}}";

    // Clinic data placeholders
    public const string CLINIC_NAME = "{{CLINIC_NAME}}";
    public const string CLINIC_ADDRESS = "{{CLINIC_ADDRESS}}";
    public const string CLINIC_CITY_STATE_ZIP = "{{CLINIC_CITY_STATE_ZIP}}";
    public const string CLINIC_PHONE = "{{CLINIC_PHONE}}";
    public const string CLINIC_FAX = "{{CLINIC_FAX}}";

    // ISSUE #8 FIX: Additional location/provider placeholders for better compatibility
    public const string LOCATION_NAME = "{{LOCATION_NAME}}";
    public const string PROVIDER_NAME = "{{PROVIDER_NAME}}";

    // Date/time placeholders
    public const string CURRENT_DATE = "{{CURRENT_DATE}}";
    public const string CURRENT_DATE_LONG = "{{CURRENT_DATE_LONG}}";
    public const string CURRENT_TIME = "{{CURRENT_TIME}}";
    public const string CURRENT_DATETIME = "{{CURRENT_DATETIME}}";

    // ISSUE #8 FIX: Additional date/time placeholders for better compatibility
    public const string TODAY_DATE = "{{TODAY_DATE}}";
    public const string APPOINTMENT_TIME = "{{APPOINTMENT_TIME}}";
    public const string APPOINTMENT_DATE = "{{APPOINTMENT_DATE}}";

    // Signature placeholder pattern: {{SIGNATURE:field_id:width:height:label:required}}
    // Format: {{SIGNATURE:field_id:width:height:Label Text:required}}
    // - field_id: Unique identifier (e.g., patient_sig, representative_sig)
    // - width: Canvas width in pixels (e.g., 400)
    // - height: Canvas height in pixels (e.g., 150)
    // - Label Text: Display label shown above signature field (e.g., Patient Signature)
    // - required: Optional - "true" or "false" (defaults to true if omitted)
    //
    // Examples:
    //   {{SIGNATURE:patient_sig:400:150:Patient Signature:true}}
    //   {{SIGNATURE:representative_sig:400:150:Patient Representative:false}}
    //   {{SIGNATURE:witness_sig:300:100:Witness Signature}}  (required defaults to true)
    public const string SIGNATURE_PATTERN = @"\{\{SIGNATURE:([^:]+):(\d+):(\d+):([^:}]+)(?::([^}]+))?\}\}";

    /// <summary>
    /// Get all available placeholders grouped by category
    /// </summary>
    public static Dictionary<string, List<PlaceholderInfo>> GetAllPlaceholders()
    {
        return new Dictionary<string, List<PlaceholderInfo>>
        {
            ["Patient Information"] = new()
            {
                new PlaceholderInfo(PATIENT_FULL_NAME, "Patient's full name"),
                new PlaceholderInfo(PATIENT_FIRST_NAME, "Patient's first name"),
                new PlaceholderInfo(PATIENT_LAST_NAME, "Patient's last name"),
                new PlaceholderInfo(PATIENT_DOB, "Date of birth (MM/DD/YYYY)"),
                new PlaceholderInfo(PATIENT_DOB_LONG, "Date of birth (Month DD, YYYY)"),
                new PlaceholderInfo(PATIENT_ADDRESS, "Street address"),
                new PlaceholderInfo(PATIENT_CITY, "City"),
                new PlaceholderInfo(PATIENT_STATE, "State"),
                new PlaceholderInfo(PATIENT_ZIP, "ZIP code"),
                new PlaceholderInfo(PATIENT_FULL_ADDRESS, "Full address (street, city, state, zip)"),
                new PlaceholderInfo(PATIENT_PHONE, "Phone number"),
                new PlaceholderInfo(PATIENT_EMAIL, "Email address"),
                new PlaceholderInfo(PATIENT_SSN_LAST4, "Last 4 digits of SSN (XXX-XX-####)"),
                new PlaceholderInfo(EMERGENCY_CONTACT_NAME, "Emergency contact name"),
                new PlaceholderInfo(EMERGENCY_CONTACT_PHONE, "Emergency contact phone"),
                new PlaceholderInfo(EMERGENCY_CONTACT_RELATION, "Emergency contact relationship"),
            },
            ["Care Episode"] = new()
            {
                new PlaceholderInfo(CARE_EPISODE_START_DATE, "Start date of care episode"),
                new PlaceholderInfo(DIAGNOSIS, "Primary diagnosis"),
                new PlaceholderInfo(TREATING_PROVIDER, "Treating provider name"),
                new PlaceholderInfo(REFERRING_PHYSICIAN, "Referring physician (if any)"),
            },
            ["Clinic Information"] = new()
            {
                new PlaceholderInfo(CLINIC_NAME, "Clinic/practice name"),
                new PlaceholderInfo(CLINIC_ADDRESS, "Clinic street address"),
                new PlaceholderInfo(CLINIC_CITY_STATE_ZIP, "Clinic city, state, ZIP"),
                new PlaceholderInfo(CLINIC_PHONE, "Clinic phone number"),
                new PlaceholderInfo(CLINIC_FAX, "Clinic fax number"),
            },
            ["Date & Time"] = new()
            {
                new PlaceholderInfo(CURRENT_DATE, "Current date (MM/DD/YYYY)"),
                new PlaceholderInfo(CURRENT_DATE_LONG, "Current date (Month DD, YYYY)"),
                new PlaceholderInfo(CURRENT_TIME, "Current time"),
                new PlaceholderInfo(CURRENT_DATETIME, "Current date and time"),
            }
        };
    }
}

public class PlaceholderInfo
{
    public string Placeholder { get; set; }
    public string Description { get; set; }

    public PlaceholderInfo(string placeholder, string description)
    {
        Placeholder = placeholder;
        Description = description;
    }
}

// ============================================
// MANUAL CONSENT UPLOAD DTOs
// ============================================

/// <summary>
/// Request DTO for manually uploading a consent form PDF.
/// Used by clinic admin or front desk staff to upload paper-based consent forms.
/// </summary>
public class ManualConsentUploadRequestDto
{
    /// <summary>Patient ID for which the consent is being uploaded</summary>
    [Required]
    public int PatientId { get; set; }

    /// <summary>Optional appointment ID if consent was collected during a specific appointment</summary>
    public int? AppointmentId { get; set; }

    /// <summary>Optional care episode ID to link the consent to</summary>
    public int? CareEpisodeId { get; set; }

    /// <summary>
    /// Consent type: 0 = New Care Episode, 1 = Returning Visit
    /// </summary>
    [Range(0, 1)]
    public int ConsentType { get; set; }

    /// <summary>Date when the consent was signed (defaults to upload date if not provided)</summary>
    public DateTime? SignedAt { get; set; }

    /// <summary>Optional notes about the manually uploaded consent</summary>
    [StringLength(500)]
    public string Notes { get; set; }
}

/// <summary>
/// Response DTO for manual consent upload operation.
/// </summary>
public class ManualConsentUploadResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int? ConsentId { get; set; }
    public DateTime? UploadedAt { get; set; }
}
