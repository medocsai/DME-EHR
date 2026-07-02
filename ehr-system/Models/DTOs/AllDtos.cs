// ============================================
// EHR DTOs - SCAFFOLD SAFE
// Updated to match actual database schema and service requirements
// Put this file in: Models/DTOs/AllDtos.cs
// ============================================

using EHR.Models.Generated;

namespace EHR.Models
{
    // ============================================
    // APPOINTMENT DTOs
    // ============================================
    public class AppointmentCreateDto
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? LocationId { get; set; }
        public int? CareEpisodeId { get; set; }
        public int Type { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Reason { get; set; }
        public string Notes { get; set; }
        public bool IsTelehealth { get; set; }
        public bool? IsRecurring { get; set; }
        public string RecurrencePattern { get; set; }

        /// <summary>
        /// For rescheduled appointments: ID of the original missed appointment.
        /// When set, the system will automatically update the original appointment
        /// to link back to this new rescheduled appointment.
        /// </summary>
        public int? RescheduledFromAppointmentId { get; set; }
    }

    public class AppointmentUpdateDto
    {
        public int? PatientId { get; set; }
        public int? ProviderId { get; set; }
        public int? LocationId { get; set; }
        public int? CareEpisodeId { get; set; }
        public int? Type { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Reason { get; set; }
        public string Notes { get; set; }
        public bool? IsTelehealth { get; set; }
        public int? Status { get; set; }
    }

    // ============================================
    // EXTEND APPOINTMENT (run-over support)
    // ============================================
    // Dedicated request/response for the "extend end time" action triggered
    // from the appointment detail modal. Kept separate from AppointmentUpdateDto
    // so it can carry its own validation (status whitelist, max +4h, conflict
    // check) without affecting unrelated update flows (notes/type/etc.).

    public class ExtendAppointmentRequestDto
    {
        /// <summary>UTC end time. Must be greater than the appointment's StartTime
        /// and no more than 4 hours later, and different from the current EndTime.</summary>
        public DateTime NewEndTime { get; set; }

        /// <summary>When true, skip the same-provider conflict check. Used after the
        /// user confirms the soft-overlap warning on the second submit.</summary>
        public bool Force { get; set; }
    }

    public class ExtendAppointmentConflictInfo
    {
        public int AppointmentId { get; set; }
        public string PatientName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string StartTimeFormatted { get; set; }
        public string EndTimeFormatted { get; set; }
    }

    public class ExtendAppointmentResultDto
    {
        public bool Success { get; set; }
        /// <summary>null on success. Otherwise one of:
        /// "NOT_FOUND" | "INVALID_STATUS" | "INVALID_TIME" | "CONFLICT".</summary>
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public ExtendAppointmentConflictInfo Conflict { get; set; }
        public DateTime? NewEndTime { get; set; }
    }

    // ============================================
    // TELEHEALTH DTOs
    // ============================================

    public class TelehealthJoinInfo
    {
        public int AppointmentId { get; set; }
        public string PatientName { get; set; }
        public string ProviderName { get; set; }
        public DateTime AppointmentTime { get; set; }
        public int AppointmentStatus { get; set; }
        public string ClinicName { get; set; }
        public bool IsValid { get; set; }
        public string Message { get; set; }
        /// <summary>Jitsi room name — so the patient joins the correct room.</summary>
        public string RoomName { get; set; }
        /// <summary>Jitsi domain (e.g., 8x8.vc).</summary>
        public string JitsiDomain { get; set; }
        /// <summary>JaaS JWT for patient-side authenticated Jitsi access.</summary>
        public string Jwt { get; set; }
        /// <summary>JaaS AppID for room name prefix and API script URL.</summary>
        public string AppId { get; set; }
    }

    public class TelehealthJoinResult
    {
        public bool Success { get; set; }
        public int AppointmentId { get; set; }
        public int AppointmentStatus { get; set; }
        public string Message { get; set; }
    }

    public class TelehealthConfigDto
    {
        public string JitsiDomain { get; set; }
        public string RoomName { get; set; }
        public string DisplayName { get; set; }
        public bool IsProvider { get; set; }
        /// <summary>JaaS JWT token for authenticated Jitsi access (no login screen).</summary>
        public string Jwt { get; set; }
        /// <summary>JaaS AppID — needed for room name prefix and API script URL.</summary>
        public string AppId { get; set; }
    }

    public class TelehealthAdmitDto
    {
        public int AppointmentId { get; set; }
    }

    public class TelehealthVerifyIdentityRequest
    {
        // Identity verification (2026-05): LastName + DateOfBirth + ZipCode.
        // SSN was removed across all patient validation flows (kiosk, portal
        // setup, tablet intake, and now telehealth).
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Last name is required")]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string LastName { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public DateOnly DateOfBirth { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "ZIP code is required")]
        [System.ComponentModel.DataAnnotations.StringLength(10, MinimumLength = 5)]
        public string ZipCode { get; set; }
    }

    public class TelehealthVerifyIdentityResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        /// <summary>Provider name to show after verification succeeds.</summary>
        public string ProviderName { get; set; }
        /// <summary>Appointment time formatted in location timezone.</summary>
        public string AppointmentTime { get; set; }
        public string PatientFirstName { get; set; }
    }

    public class AppointmentListDto
    {
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientMRN { get; set; } = string.Empty;
        public string PatientEmail { get; set; }
        public string PatientPhone { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public string ProviderColor { get; set; }
        public bool PatientHasProfilePicture { get; set; }
        public bool ProviderHasProfilePicture { get; set; }
        public int? LocationId { get; set; }
        public string LocationName { get; set; }
        public int Type { get; set; }
        public int Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Reason { get; set; }
        public bool IsTelehealth { get; set; }
        public string TelehealthToken { get; set; }
        public bool? InsuranceVerified { get; set; }
        public decimal? CopayDue { get; set; }
        public decimal? CopayCollected { get; set; }
        public bool HasNote { get; set; }
        // Note documentation status for dashboard display
        public int DocumentationStatus { get; set; }  // AppointmentDocumentationStatus enum
        public bool HasSignedNote { get; set; }
        // Cancellation tracking fields
        public string CancellationReason { get; set; }
        public int? CancelledByUserId { get; set; }
        public string CancelledByUserName { get; set; }
        public DateTime? CancelledAt { get; set; }
        // Missed appointments tracking
        public int? CareEpisodeId { get; set; }
        public int DaysMissed { get; set; }
        // Clinical notes for this appointment (used in Care Episode detail view)
        public List<AppointmentClinicalNoteDto> ClinicalNotes { get; set; } = new();
        // Patient sticky notes count (for dashboard badge)
        public int StickyNoteCount { get; set; }
        // Patient intake form status — see IntakeStatusHelper.cs / rules/technical/intake-status-indicator.md
        public EHR.Helpers.IntakeStatusDto IntakeStatus { get; set; }
        // Timezone information for display
        /// <summary>
        /// IANA timezone identifier for the appointment's location (e.g., "America/New_York")
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
        /// <summary>
        /// Start time formatted in location timezone with abbreviation (e.g., "9:00 AM EST")
        /// </summary>
        public string StartTimeFormatted { get; set; }
        /// <summary>
        /// End time formatted in location timezone with abbreviation (e.g., "9:45 AM EST")
        /// </summary>
        public string EndTimeFormatted { get; set; }
        /// <summary>
        /// Date formatted in location timezone (e.g., "Dec 18, 2025")
        /// </summary>
        public string DateFormatted { get; set; }

        // Created-by tracking (audit)
        public int? CreatedByUserId { get; set; }
        public int? CreatedByPatientId { get; set; }
        /// <summary>
        /// Display name: staff name or "Patient (Jane Doe)" for portal bookings
        /// </summary>
        public string CreatedByName { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Reschedule relationship tracking
        /// <summary>
        /// For missed appointments: ID of the new rescheduled appointment
        /// </summary>
        public int? RescheduledToAppointmentId { get; set; }
        /// <summary>
        /// For rescheduled appointments: ID of the original missed appointment
        /// </summary>
        public int? RescheduledFromAppointmentId { get; set; }
        /// <summary>
        /// For missed appointments: formatted info about the rescheduled appointment (e.g., "12/20/2025 10:00 AM with Dr. Smith")
        /// </summary>
        public string RescheduledToInfo { get; set; }
        /// <summary>
        /// For rescheduled appointments: formatted info about the original missed appointment
        /// </summary>
        public string RescheduledFromInfo { get; set; }
    }

    /// <summary>
    /// Simplified clinical note info for display in appointment lists
    /// </summary>
    public class AppointmentClinicalNoteDto
    {
        public int ClinicalNoteId { get; set; }
        public string TemplateName { get; set; }
        public int Type { get; set; }
        public string TypeName => GetTypeName(Type);
        public int Status { get; set; }
        public bool IsSigned => Status == 2 || Status == 4; // Signed or Finalized
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public DateOnly ServiceDate { get; set; }
        public object ProviderUserId { get; set; }

        private static string GetTypeName(int type) => type switch
        {
            0 => "History & Physical",
            1 => "SOAP Note",
            2 => "Office Visit Note",
            3 => "Progress Note",
            4 => "Consultation Note",
            5 => "Procedure Note",
            6 => "Annual Wellness Note",
            7 => "Phone Note",
            8 => "Referral Letter",
            9 => "Lab Review Note",
            _ => "Other"
        };
    }

    public class AppointmentCheckInDto
    {
        public decimal? CopayCollected { get; set; }
        public string Notes { get; set; }
    }

    public class ScheduleSlotDto
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsAvailable { get; set; }
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string ProviderColor { get; set; }
        public int? LocationId { get; set; }
        public string LocationName { get; set; }
        public string StartTimeFormatted { get; set; }
        public string EndTimeFormatted { get; set; }
        /// <summary>
        /// IANA timezone identifier for the location (e.g., "America/New_York")
        /// </summary>
        public string TimeZoneId { get; set; }
        /// <summary>
        /// User-friendly timezone abbreviation (e.g., "EST", "PST")
        /// </summary>
        public string TimeZoneAbbreviation { get; set; }
        /// <summary>
        /// Start time formatted with timezone (e.g., "9:00 AM EST")
        /// </summary>
        public string StartTimeWithTimezone { get; set; }
        /// <summary>
        /// End time formatted with timezone (e.g., "9:45 AM EST")
        /// </summary>
        public string EndTimeWithTimezone { get; set; }
    }

    // ============================================
    // PATIENT PORTAL BOOKING DTOs
    // ============================================

    /// <summary>
    /// Available time slot for patient portal booking (no provider info exposed)
    /// </summary>
    public class PortalAvailableSlotDto
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string StartTimeFormatted { get; set; }
        public string EndTimeFormatted { get; set; }
        public string TimeZoneAbbreviation { get; set; }
    }

    /// <summary>
    /// Patient portal appointment booking request.
    /// ProviderId and Type are optional. When null, server uses random-assign and the
    /// historical default Type=FollowUpVisit, preserving the pre-wizard behavior.
    /// </summary>
    public class PortalBookAppointmentDto
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Reason { get; set; }
        public int? ProviderId { get; set; }
        public int? Type { get; set; }
    }

    /// <summary>
    /// Bookable provider as exposed to the patient portal. Strips fields the patient
    /// does not need (NPI, license details, Email/Phone, etc.).
    /// </summary>
    public class PortalProviderDto
    {
        public int ProviderId { get; set; }
        public string DisplayName { get; set; }
        public string Credentials { get; set; }
        public string Specialty { get; set; }
        public string Color { get; set; }
        public string Initials { get; set; }
        public bool HasPhoto { get; set; }
    }

    public class AppointmentConflictDto
    {
        public bool HasConflict { get; set; }
        public string ProviderName { get; set; }
        public string LocationName { get; set; }
        public string ConflictStart { get; set; }
        public string ConflictEnd { get; set; }
        public int? ConflictAppointmentId { get; set; }
    }

    /// <summary>
    /// Response for reschedule operation - returns info needed to create new appointment
    /// </summary>
    public class RescheduleAppointmentResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int OriginalAppointmentId { get; set; }
        /// <summary>True if the original was a future appointment and was cancelled</summary>
        public bool WasCancelled { get; set; }
        /// <summary>True if the original was a past/current appointment and was marked as missed</summary>
        public bool WasMarkedMissed { get; set; }
        // Fields needed to pre-fill the new appointment
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientMRN { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int Type { get; set; }
        public DateTime OriginalStartTime { get; set; }
        public DateTime OriginalEndTime { get; set; }
        public int? CareEpisodeId { get; set; }
        public string Reason { get; set; }
        public int? LocationId { get; set; }
    }

    // ============================================
    // ISSUE #6 FIX: RECURRING APPOINTMENT DTOs
    // ============================================

    /// <summary>
    /// Request to check availability for multiple dates (recurring appointments)
    /// </summary>
    public class RecurringAvailabilityCheckRequest
    {
        /// <summary>
        /// Provider ID. Use 0 or null for "Any Available Provider" mode (auto-assignment)
        /// </summary>
        public int ProviderId { get; set; }
        public int DurationMinutes { get; set; } = 30;
        /// <summary>
        /// List of proposed appointment date/times in UTC
        /// </summary>
        public List<DateTime> ProposedDates { get; set; } = new();
        /// <summary>
        /// When true with ProviderId=0, enables auto-assignment mode to find any available provider
        /// </summary>
        public bool AutoAssign { get; set; }
    }

    /// <summary>
    /// Response for batch availability check showing which dates are available
    /// </summary>
    public class RecurringAvailabilityCheckResponse
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int TotalRequested { get; set; }
        public int AvailableCount { get; set; }
        public int ConflictCount { get; set; }
        public List<RecurringDateAvailability> Dates { get; set; } = new();
    }

    /// <summary>
    /// Availability status for a single date in a recurring series
    /// </summary>
    public class RecurringDateAvailability
    {
        public DateTime ProposedDateTime { get; set; }
        public string FormattedDate { get; set; }
        public string FormattedTime { get; set; }
        public bool IsAvailable { get; set; }
        public string ConflictReason { get; set; } // e.g., "Existing appointment", "Time off", "Outside working hours"
        public int? ConflictingAppointmentId { get; set; }
        /// <summary>
        /// Provider ID assigned to this slot (used in auto-assignment mode)
        /// </summary>
        public int? AssignedProviderId { get; set; }
        /// <summary>
        /// Provider name assigned to this slot (used in auto-assignment mode)
        /// </summary>
        public string AssignedProviderName { get; set; }
    }

    /// <summary>
    /// Request to create a recurring appointment series
    /// </summary>
    public class RecurringAppointmentCreateRequest
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? LocationId { get; set; }
        public int Type { get; set; }
        public int DurationMinutes { get; set; } = 30;
        public string Notes { get; set; }
        public bool IsTelehealth { get; set; }
        /// <summary>
        /// Care Episode ID to link all appointments to
        /// </summary>
        public int? CareEpisodeId { get; set; }
        /// <summary>
        /// List of selected dates to create appointments for (filtered by user)
        /// </summary>
        public List<DateTime> SelectedDates { get; set; } = new();
        /// <summary>
        /// Pattern description for reference (e.g., "Weekly on Mon, Wed, Fri")
        /// </summary>
        public string RecurrencePattern { get; set; }
        /// <summary>
        /// Keep the same therapist for all appointments (default true)
        /// </summary>
        public bool SameTherapist { get; set; } = true;
        /// <summary>
        /// Send 24-hour reminder before each appointment
        /// </summary>
        public bool Reminder24h { get; set; } = true;
        /// <summary>
        /// Send 1-hour reminder before each appointment
        /// </summary>
        public bool Reminder1h { get; set; } = true;
    }

    /// <summary>
    /// Response after creating a recurring series
    /// </summary>
    public class RecurringAppointmentCreateResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int TotalCreated { get; set; }
        public int TotalFailed { get; set; }
        public string SeriesId { get; set; }
        public List<RecurringAppointmentResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Result for each appointment in a recurring series
    /// </summary>
    public class RecurringAppointmentResult
    {
        public DateTime RequestedDateTime { get; set; }
        public bool Created { get; set; }
        public int? AppointmentId { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Request to find available providers for multiple time slots (auto-assignment)
    /// </summary>
    public class AutoAssignProvidersRequest
    {
        public int DurationMinutes { get; set; } = 30;
        /// <summary>
        /// List of proposed appointment date/times in UTC
        /// </summary>
        public List<DateTime> ProposedDates { get; set; } = new();
        /// <summary>
        /// Optional: Preferred provider ID (will try to use this provider first if available)
        /// </summary>
        public int? PreferredProviderId { get; set; }
    }

    /// <summary>
    /// Response with auto-assigned providers for each time slot
    /// </summary>
    public class AutoAssignProvidersResponse
    {
        public int TotalRequested { get; set; }
        public int AvailableCount { get; set; }
        public int UnavailableCount { get; set; }
        public List<AutoAssignSlot> Slots { get; set; } = new();
    }

    /// <summary>
    /// Auto-assignment result for a single time slot
    /// </summary>
    public class AutoAssignSlot
    {
        public DateTime ProposedDateTime { get; set; }
        public string FormattedDate { get; set; }
        public string FormattedTime { get; set; }
        public bool IsAvailable { get; set; }
        public int? AssignedProviderId { get; set; }
        public string AssignedProviderName { get; set; }
        public string UnavailableReason { get; set; }
    }

    // ============================================
    // PATIENT DTOs
    // ============================================
    public class PatientCreateDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateOnly DateOfBirth { get; set; }
        public string Gender { get; set; }
        /// <summary>
        /// Full SSN (format: XXX-XX-XXXX or XXXXXXXXX). Will be encrypted and last 4 digits hashed for kiosk verification.
        /// </summary>
        public string Ssn { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string EmergencyContactName { get; set; }
        public string EmergencyContactPhone { get; set; }
        public string EmergencyContactAltPhone { get; set; }
        public string EmergencyContactRelation { get; set; }
        public int? PreferredProviderId { get; set; }
        /// <summary>
        /// Location ID for the patient. If not provided, defaults to current session location or tenant's default location.
        /// </summary>
        public int? PreferredLocationId { get; set; }
        /// <summary>
        /// Date of injury for medical lien and workers' compensation cases.
        /// </summary>
        public DateOnly? DateOfInjury { get; set; }
        public PatientInsuranceCreateDto? PrimaryInsurance { get; set; }
        public PatientInsuranceCreateDto? SecondaryInsurance { get; set; }
    }

    public class PatientUpdateDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string Gender { get; set; }
        /// <summary>
        /// Full SSN (format: XXX-XX-XXXX or XXXXXXXXX). Will be encrypted and last 4 digits hashed for kiosk verification.
        /// </summary>
        public string Ssn { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string EmergencyContactName { get; set; }
        public string EmergencyContactPhone { get; set; }
        public string EmergencyContactAltPhone { get; set; }
        public string EmergencyContactRelation { get; set; }
        public int? PreferredProviderId { get; set; }
        /// <summary>
        /// Date of injury for medical lien and workers' compensation cases.
        /// </summary>
        public DateOnly? DateOfInjury { get; set; }
        public PatientInsuranceCreateDto? PrimaryInsurance { get; set; }
        public PatientInsuranceCreateDto? SecondaryInsurance { get; set; }
    }

    public class PagedPatientResponse
    {
        public List<PatientListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class PatientListDto
    {
        public int PatientId { get; set; }
        public string MRN { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public DateOnly DateOfBirth { get; set; }
        public int Age => CalculateAge(DateOfBirth);
        public string Phone { get; set; }
        public string Email { get; set; }
        public int Status { get; set; }
        public string PrimaryInsurance { get; set; }
        public DateTime? LastVisit { get; set; }
        public bool IsArchived { get; set; }
        /// <summary>
        /// Profile completeness percentage (0-100). Null if not yet validated.
        /// </summary>
        public int? ProfileCompleteness { get; set; }
        /// <summary>
        /// Profile status: 1 = Incomplete, 2 = Complete. Null if not yet validated.
        /// </summary>
        public int? ProfileStatus { get; set; }
        public bool HasProfilePicture { get; set; }

        private static int CalculateAge(DateOnly dob)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var age = today.Year - dob.Year;
            if (dob > today.AddYears(-age)) age--;
            return age;
        }
    }

    public class PatientDetailDto
    {
        public int PatientId { get; set; }
        public string MRN { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public DateOnly DateOfBirth { get; set; }
        public int Age => CalculateAge(DateOfBirth);
        public string Gender { get; set; }
        /// <summary>
        /// SSN displayed as masked value (e.g., "***-**-1234") for security.
        /// To update SSN, send full SSN in PatientUpdateDto.
        /// </summary>
        public string Ssn { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string EmergencyContactName { get; set; }
        public string EmergencyContactPhone { get; set; }
        public string EmergencyContactAltPhone { get; set; }
        public string EmergencyContactRelation { get; set; }
        public int Status { get; set; }
        public int? PreferredProviderId { get; set; }
        public string PreferredProviderName { get; set; }
        /// <summary>
        /// Date of injury for medical lien and workers' compensation cases.
        /// </summary>
        public DateOnly? DateOfInjury { get; set; }
        public List<InsuranceDto>? Insurances { get; set; }
        public List<CareEpisodeDto>? CareEpisodes { get; set; }
        /// <summary>
        /// Validation status for the patient profile
        /// </summary>
        public PatientValidationStatusDto ValidationStatus { get; set; }
        public bool IsArchived { get; set; }
        public bool HasProfilePicture { get; set; }

        private static int CalculateAge(DateOnly dob)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var age = today.Year - dob.Year;
            if (dob > today.AddYears(-age)) age--;
            return age;
        }
    }

    public class PatientSearchResultDto
    {
        public int PatientId { get; set; }
        public string MRN { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public string DisplayName => $"{LastName}, {FirstName}";
        public DateOnly DateOfBirth { get; set; }
        public string DOBFormatted => DateOfBirth.ToString("MM/dd/yyyy");
        public int Age => CalculateAge(DateOfBirth);
        public string Phone { get; set; }
        public string PhoneLast4 => Phone?.Length >= 4 ? Phone[^4..] : Phone;
        public string Email { get; set; }
        public string EmailPartial => Email != null && Email.Contains('@') ? Email[0] + "***" + Email[(Email.IndexOf('@') - 1)..] : Email;
        public string Gender { get; set; }
        public int Status { get; set; }
        public string PrimaryInsurance { get; set; }
        public string City { get; set; }
        public bool IsArchived { get; set; }
        public string DisplayText => $"{LastName}, {FirstName} ({MRN})";
        public string DisplaySubText => $"DOB: {DOBFormatted} ({Age}y) | Ph: ...{PhoneLast4 ?? "N/A"} | {Gender ?? ""}";

        private static int CalculateAge(DateOnly dob)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var age = today.Year - dob.Year;
            if (dob > today.AddYears(-age)) age--;
            return age;
        }
    }

    public class PatientSearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public int? PreferredProviderId { get; set; }
        /// <summary>
        /// Filter by patient's preferred location. If null, searches across all locations.
        /// </summary>
        public int? LocationId { get; set; }
        public int Take { get; set; } = 20;
        public int Skip { get; set; } = 0;
        public bool ActiveOnly { get; set; } = true;
    }

    public class PatientSearchResponse
    {
        public List<PatientSearchResultDto> Results { get; set; } = new();
        public int TotalCount { get; set; }
        public bool HasMore { get; set; }
    }

    // ============================================
    // AUTHORIZATION DTOs
    // ============================================

    /// <summary>
    /// DTO for displaying authorization records in a list.
    /// VisitsUsed is calculated by the service by counting completed appointments for the patient.
    /// </summary>
    public class AuthorizationDto
    {
        public int AuthorizationId { get; set; }
        public int InsuranceId { get; set; }
        public string AuthorizationNumber { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public int AuthorizedVisits { get; set; }
        /// <summary>
        /// Calculated by service - count of completed appointments for the patient
        /// </summary>
        public int VisitsUsed { get; set; }
        /// <summary>
        /// Calculated: AuthorizedVisits - VisitsUsed
        /// </summary>
        public int VisitsRemaining { get; set; }
        public DateTime DateOfValidation { get; set; }
        public string Notes { get; set; }
        public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today);
        public bool IsCurrent { get; set; } // Set by service - most recent authorization
        /// <summary>
        /// IsActive = IsCurrent AND not expired (for frontend convenience)
        /// </summary>
        public bool IsActive => IsCurrent && !IsExpired;
        public string Status => IsExpired ? "Expired" : (IsCurrent ? "Active" : "Inactive");
        /// <summary>
        /// Validated on date formatted for display (alias for DateOfValidation)
        /// </summary>
        public DateTime ValidatedOn => DateOfValidation;
    }

    /// <summary>
    /// DTO for creating a new authorization record (internal use)
    /// Authorizations are created through the insurance validation process, not directly by users
    /// </summary>
    public class AuthorizationCreateDto
    {
        public int InsuranceId { get; set; }
        public string AuthorizationNumber { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public int AuthorizedVisits { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>
    /// DTO for displaying authorization history for an insurance record
    /// </summary>
    public class AuthorizationHistoryDto
    {
        public int InsuranceId { get; set; }
        public string InsuranceName { get; set; }
        public int TotalAuthorizations { get; set; }
        public AuthorizationDto CurrentAuthorization { get; set; }
        public List<AuthorizationDto> AllAuthorizations { get; set; } = new();
    }

    /// <summary>
    /// DTO for updating an existing authorization record
    /// </summary>
    public class AuthorizationUpdateDto
    {
        public string AuthorizationNumber { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public int AuthorizedVisits { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>
    /// DTO for mock authorization fetch response
    /// </summary>
    public class MockAuthorizationFetchDto
    {
        public bool Success { get; set; }
        public string AuthorizationNumber { get; set; }
        public int AuthorizedVisits { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string Notes { get; set; }
        public string ErrorMessage { get; set; }
    }

    // ============================================
    // INSURANCE DTOs
    // ============================================
    public class InsuranceDto
    {
        public int InsuranceId { get; set; }
        public int PatientId { get; set; }
        public string PayerName { get; set; } = string.Empty;
        public string PayerId { get; set; }
        public string PolicyNumber { get; set; } = string.Empty;
        public string GroupNumber { get; set; }
        public int Type { get; set; }
        public int? InsuranceCategory { get; set; }
        public string SubscriberName { get; set; }
        public string SubscriberFirstName { get; set; }
        public string SubscriberLastName { get; set; }
        public DateOnly? SubscriberDob { get; set; }
        public string SubscriberId { get; set; }
        public string SubscriberRelationship { get; set; }
        public decimal? Copay { get; set; }
        public decimal? Coinsurance { get; set; }
        public decimal? Deductible { get; set; }
        public decimal? DeductibleMet { get; set; }
        public decimal? DeductibleRemaining { get; set; }
        /// <summary>
        /// Number of visits allowed by the insurance plan (general coverage, not authorization-specific)
        /// </summary>
        public int? AllowedVisits { get; set; }
        public DateOnly? EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public DateOnly? EffectiveStartDate { get; set; }
        public DateOnly? EffectiveEndDate { get; set; }
        public int? EligibilityStatus { get; set; }
        public DateTime? LastVerifiedAt { get; set; }
        public string CoverageNotes { get; set; }
        public bool IsActive { get; set; }
        public string AttorneyName { get; set; }
        public string AttorneyPhone { get; set; }
        public string AttorneyEmail { get; set; }

        // Eligibility response fields (populated by verification)
        public string PlanName { get; set; }
        public bool? InNetwork { get; set; }
        public decimal? OutOfPocketMax { get; set; }

        // Authorization data from the most recent authorization record
        public AuthorizationDto CurrentAuthorization { get; set; }
        public List<AuthorizationDto> Authorizations { get; set; } = new();
    }

    public class PatientInsuranceCreateDto
    {
        public string PayerName { get; set; } = string.Empty;
        public string PayerId { get; set; }
        public string PolicyNumber { get; set; } = string.Empty;
        public string GroupNumber { get; set; }
        public int Type { get; set; } = 0;
        public int? InsuranceCategory { get; set; }
        public string SubscriberName { get; set; }
        public string SubscriberFirstName { get; set; }
        public string SubscriberLastName { get; set; }
        public string SubscriberId { get; set; }
        public DateOnly? SubscriberDob { get; set; }
        public string SubscriberRelationship { get; set; }
        public decimal? Copay { get; set; }
        public decimal? Coinsurance { get; set; }
        public decimal? Deductible { get; set; }
        public decimal? DeductibleTotal { get; set; }
        public DateOnly? EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public string AttorneyName { get; set; }
        public string AttorneyPhone { get; set; }
        public string AttorneyEmail { get; set; }

        // Insurance plan details - can be entered manually or from validation
        public int? AllowedVisits { get; set; }

        // Authorization fields - captured during insurance validation
        public string AuthorizationNumber { get; set; }
        public int? AuthorizedVisits { get; set; }
        public DateOnly? AuthorizationExpiry { get; set; }
    }

    public class InsuranceCreateDto : PatientInsuranceCreateDto
    {
        public int PatientId { get; set; }
    }

    public class InsuranceUpdateDto
    {
        public string PayerName { get; set; }
        public string PayerId { get; set; }
        public string PolicyNumber { get; set; }
        public string GroupNumber { get; set; }
        public int? Type { get; set; }
        public int? InsuranceCategory { get; set; }
        public string SubscriberName { get; set; }
        public string SubscriberFirstName { get; set; }
        public string SubscriberLastName { get; set; }
        public DateOnly? SubscriberDob { get; set; }
        public string SubscriberId { get; set; }
        public string SubscriberRelationship { get; set; }
        public decimal? Copay { get; set; }
        public decimal? Coinsurance { get; set; }
        public decimal? Deductible { get; set; }
        public decimal? DeductibleTotal { get; set; }
        public DateOnly? EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public bool? IsActive { get; set; }
        public string AttorneyName { get; set; }
        public string AttorneyPhone { get; set; }
        public string AttorneyEmail { get; set; }
        public int? AllowedVisits { get; set; }
    }

    public class InsuranceVerificationResult
    {
        // ── Response status ──
        public bool Success { get; set; }
        public bool IsEligible { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }

        // ── Plan identification ──
        public string PlanName { get; set; }
        public string PlanType { get; set; }           // "PPO", "HMO", "POS", "EPO"
        public string GroupName { get; set; }
        public string MemberId { get; set; }
        public bool? InNetwork { get; set; }

        // ── Primary benefits (in-network, used by existing code) ──
        public decimal? Copay { get; set; }
        public decimal? Coinsurance { get; set; }
        public decimal? Deductible { get; set; }
        public decimal? DeductibleMet { get; set; }
        public decimal? DeductibleTotal { get; set; }
        public decimal? OutOfPocketMax { get; set; }

        // ── In-Network benefits ──
        public decimal? CopayInNetwork { get; set; }
        public decimal? CoinsuranceInNetwork { get; set; }

        // ── Out-of-Network benefits ──
        public decimal? CopayOutOfNetwork { get; set; }
        public decimal? CoinsuranceOutOfNetwork { get; set; }

        // ── Individual Deductible ──
        public decimal? IndividualDeductible { get; set; }
        public decimal? IndividualDeductibleMet { get; set; }
        public decimal? IndividualDeductibleRemaining { get; set; }

        // ── Family Deductible ──
        public decimal? FamilyDeductible { get; set; }
        public decimal? FamilyDeductibleMet { get; set; }

        // ── Individual Out-of-Pocket ──
        public decimal? IndividualOopMax { get; set; }
        public decimal? IndividualOopMet { get; set; }
        public decimal? IndividualOopRemaining { get; set; }

        // ── Family Out-of-Pocket ──
        public decimal? FamilyOopMax { get; set; }
        public decimal? FamilyOopMet { get; set; }

        // ── Visit limits ──
        /// <summary>Number of visits allowed by the insurance plan (general coverage, not authorization-specific)</summary>
        public int? AllowedVisits { get; set; }
        public int? VisitsUsed { get; set; }
        public int? VisitsRemaining { get; set; }
        /// <summary>Deprecated: Use AllowedVisits for plan coverage.</summary>
        public int? AuthorizedVisits { get; set; }
        public int? RemainingVisits { get; set; }

        // ── Authorization ──
        public bool? RequiresPriorAuthorization { get; set; }
        public string AuthorizationNumber { get; set; }
        public DateTime AuthorizationExpiry { get; set; }

        // ── Coverage dates ──
        public DateOnly? CoverageEffectiveDate { get; set; }
        public DateOnly? CoverageTerminationDate { get; set; }
        public string BenefitPeriod { get; set; }      // "Calendar Year", "Plan Year"

        // ── Notes & timestamps ──
        public string CoverageNotes { get; set; }
        public DateOnly? VerificationDate { get; set; }
        public DateTime? VerifiedAt { get; set; }

        /// <summary>Raw OA JSON response. Not serialized to frontend — used only for DB storage.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string RawResponseJson { get; set; }

        /// <summary>Rich parsed eligibility details from raw OA response. Included in verify response so frontend can display immediately.</summary>
        public EligibilityDetailsDto EligibilityDetails { get; set; }
    }

    public class EligibilityDetailsDto
    {
        // ── Verification metadata ──
        public DateTime? VerifiedAt { get; set; }
        public string TransactionId { get; set; }

        // ── Plan info ──
        public string PlanName { get; set; }
        public string PlanType { get; set; }
        public string GroupNumber { get; set; }
        public string GroupName { get; set; }
        public string PayerName { get; set; }
        public string PayerId { get; set; }

        // ── Patient/Subscriber info ──
        public string PatientFirstName { get; set; }
        public string PatientLastName { get; set; }
        public string MemberId { get; set; }
        public string DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string Relationship { get; set; }
        public string Address { get; set; }

        // ── Coverage status ──
        public bool IsEligible { get; set; }
        public bool? InNetwork { get; set; }
        public string CoverageEffectiveDate { get; set; }
        public string CoverageTerminationDate { get; set; }
        public string BenefitPeriod { get; set; }

        // ── Benefits summary ──
        public BenefitAmounts InNetworkBenefits { get; set; }
        public BenefitAmounts OutOfNetworkBenefits { get; set; }

        // ── Visit limits ──
        public int? AllowedVisits { get; set; }
        public int? VisitsUsed { get; set; }
        public int? VisitsRemaining { get; set; }

        // ── Authorization ──
        public bool? RequiresPriorAuthorization { get; set; }

        // ── Service-specific benefits ──
        public List<ServiceBenefitGroup> ServiceBenefits { get; set; } = new();

        // ── Messages from payer ──
        public List<string> Disclaimers { get; set; } = new();
        public List<string> BenefitDescriptions { get; set; } = new();
        public List<string> Exclusions { get; set; } = new();
        public List<string> Limitations { get; set; } = new();
    }

    public class BenefitAmounts
    {
        public decimal? Copay { get; set; }
        public decimal? Coinsurance { get; set; }
        public decimal? IndividualDeductible { get; set; }
        public decimal? IndividualDeductibleMet { get; set; }
        public decimal? IndividualDeductibleRemaining { get; set; }
        public decimal? FamilyDeductible { get; set; }
        public decimal? FamilyDeductibleMet { get; set; }
        public decimal? IndividualOopMax { get; set; }
        public decimal? IndividualOopMet { get; set; }
        public decimal? IndividualOopRemaining { get; set; }
        public decimal? FamilyOopMax { get; set; }
        public decimal? FamilyOopMet { get; set; }
    }

    public class ServiceBenefitGroup
    {
        public string ServiceType { get; set; }
        public string ServiceTypeCode { get; set; }
        public List<ServiceBenefitItem> Items { get; set; } = new();
    }

    public class ServiceBenefitItem
    {
        public string BenefitType { get; set; }
        public string Network { get; set; }
        public string CoverageLevel { get; set; }
        public string TimePeriod { get; set; }
        public string MonetaryAmount { get; set; }
        public string Percent { get; set; }
        public string Quantity { get; set; }
        public bool? RequiresAuthorization { get; set; }
        public List<string> Messages { get; set; }
    }

    /// <summary>
    /// Request DTO for verifying insurance directly without a saved InsuranceId.
    /// Used on patient form before insurance is saved to database.
    /// </summary>
    public class InsuranceVerificationRequest
    {
        /// <summary>
        /// Optional: If provided, verification uses existing insurance record from database
        /// </summary>
        public int? InsuranceId { get; set; }

        /// <summary>
        /// Insurance type: 0 = Primary, 1 = Secondary
        /// Required if InsuranceId is not provided
        /// </summary>
        public int? Type { get; set; }

        /// <summary>
        /// Insurance company/payer name
        /// Required if InsuranceId is not provided
        /// </summary>
        public string PayerName { get; set; }

        /// <summary>
        /// Electronic payer ID (for Office Ally eligibility lookup)
        /// </summary>
        public string PayerId { get; set; }

        /// <summary>
        /// Member ID / Policy number
        /// Required if InsuranceId is not provided
        /// </summary>
        public string PolicyNumber { get; set; }

        /// <summary>
        /// Optional: Group number
        /// </summary>
        public string GroupNumber { get; set; }

        /// <summary>
        /// Optional: Subscriber ID
        /// </summary>
        public string SubscriberId { get; set; }

        /// <summary>
        /// Optional: Subscriber name
        /// </summary>
        public string SubscriberName { get; set; }

        /// <summary>
        /// Optional: Subscriber date of birth
        /// </summary>
        public DateOnly? SubscriberDob { get; set; }
    }

    // ============================================
    // CARE EPISODE DTOs
    // ============================================
    /// <summary>
    /// DTO for CareEpisode - represents a clinical treatment period.
    /// CareEpisode is purely clinical and has NO relationship to Insurance.
    /// VisitsUsed is calculated dynamically by counting completed appointments linked to this episode.
    /// </summary>
    public class CareEpisodeDto
    {
        public int CareEpisodeId { get; set; }
        public int PatientId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string PrimaryDiagnosis { get; set; }
        public string PrimaryDiagnosisCode { get; set; }
        public string PrimaryDiagnosisDescription { get; set; }
        public string DiagnosisNotes { get; set; }
        public string DiagnosisCodes { get; set; }
        public int? PrimaryProviderId { get; set; }
        public string PrimaryProviderName { get; set; }
        /// <summary>
        /// Calculated by service - count of completed appointments linked to this care episode
        /// </summary>
        public int VisitsUsed { get; set; }
        /// <summary>
        /// Calculated: ExpectedVisits - VisitsUsed
        /// </summary>
        public int VisitsRemaining { get; set; }
        /// <summary>
        /// Calculated by service - count of NoShow appointments linked to this care episode
        /// </summary>
        public int MissedVisits { get; set; }
        public decimal? CopayAmount { get; set; }
        public int? CopayVisits { get; set; }
        public string Goals { get; set; }
        public string PlanOfCare { get; set; }
        public string PhysicianName { get; set; }
        public int? ExpectedVisits { get; set; }
        public int? VisitFrequency { get; set; }
        public int Status { get; set; }
        public string StatusName => EnumHelper.GetCareEpisodeStatusName(Status);
        public string DischargeReason { get; set; }
        public int? CompletionMethod { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsOverdue => Status == 0 && EndDate.HasValue && EndDate.Value < DateOnly.FromDateTime(DateTime.Today);
        public bool HasLowVisits { get; set; }
    }

    /// <summary>
    /// List DTO for CareEpisode - represents a clinical treatment period.
    /// CareEpisode is purely clinical and has NO relationship to Insurance.
    /// Visit counts are calculated dynamically by counting appointments.
    /// </summary>
    public class CareEpisodeListDto
    {
        public int CareEpisodeId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string PrimaryDiagnosis { get; set; }
        public int? PrimaryProviderId { get; set; }
        public string PrimaryProviderName { get; set; }
        public int? ExpectedVisits { get; set; }
        /// <summary>
        /// Calculated by service - count of completed appointments linked to this care episode
        /// </summary>
        public int VisitsUsed { get; set; }
        /// <summary>
        /// Calculated: ExpectedVisits - VisitsUsed
        /// </summary>
        public int VisitsRemaining { get; set; }
        /// <summary>
        /// Calculated by service - count of NoShow appointments linked to this care episode
        /// </summary>
        public int MissedVisits { get; set; }
        public int Status { get; set; }
        public string StatusName => EnumHelper.GetCareEpisodeStatusName(Status);
        public bool IsOverdue => Status == 0 && EndDate.HasValue && EndDate.Value < DateOnly.FromDateTime(DateTime.Today);
        public bool HasLowVisits { get; set; }
        public bool IsExpiringSoon { get; set; }
    }

    public class CareEpisodeDetailDto : CareEpisodeDto
    {
        public List<AppointmentListDto> Appointments { get; set; } = new();
        public List<string> SecondaryDiagnoses { get; set; } = new();
        public List<EpisodeNoteDto> EpisodeNotes { get; set; } = new();
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string PatientName { get; set; }
    }

    public class EpisodeNoteDto
    {
        public int NoteId { get; set; }
        public string Content { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? CreatedByUserId { get; set; }
        public string CreatedByUserName { get; set; }
    }

    /// <summary>
    /// DTO for creating a new CareEpisode.
    /// CareEpisode is purely clinical - no Insurance fields are included.
    /// </summary>
    public class CareEpisodeCreateDto
    {
        public int PatientId { get; set; }
        public int? PrimaryProviderId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string PrimaryDiagnosisCode { get; set; }
        public string PrimaryDiagnosisDescription { get; set; }
        public string DiagnosisNotes { get; set; }
        public List<string>? SecondaryDiagnoses { get; set; }
        public decimal? CopayAmount { get; set; }
        public int? CopayVisits { get; set; }
        public List<string>? Goals { get; set; }
        public string PlanOfCare { get; set; }
        public string PhysicianName { get; set; }
        public int? ExpectedVisits { get; set; }
        public int? VisitFrequency { get; set; }
    }

    /// <summary>
    /// DTO for updating a CareEpisode.
    /// CareEpisode is purely clinical - no Insurance fields are included.
    /// </summary>
    public class CareEpisodeUpdateDto
    {
        public int? PrimaryProviderId { get; set; }
        public DateOnly? EndDate { get; set; }
        public string PrimaryDiagnosisCode { get; set; }
        public string PrimaryDiagnosisDescription { get; set; }
        public string DiagnosisNotes { get; set; }
        public List<string>? SecondaryDiagnoses { get; set; }
        public decimal? CopayAmount { get; set; }
        public int? CopayVisits { get; set; }
        public List<string>? Goals { get; set; }
        public string PlanOfCare { get; set; }
        public string PhysicianName { get; set; }
        public int? ExpectedVisits { get; set; }
        public int? VisitFrequency { get; set; }
        public int? Status { get; set; }
        public string DischargeReason { get; set; }
    }

    /// <summary>
    /// DTO for checking if a patient can create a care episode
    /// </summary>
    public class CareEpisodeEligibilityDto
    {
        public int PatientId { get; set; }
        public bool CanCreateEpisode { get; set; }
        public string Reason { get; set; }
        public CareEpisodeDto ActiveEpisode { get; set; }
    }

    /// <summary>
    /// DTO for marking a care episode as complete
    /// </summary>
    public class CareEpisodeCompleteDto
    {
        public int CareEpisodeId { get; set; }
        public string DischargeReason { get; set; }
    }

    /// <summary>
    /// DTO for dashboard care episode alerts.
    /// CareEpisode alerts are purely clinical (e.g., approaching expected visits, overdue).
    /// For insurance authorization alerts, see PatientAuthorizationAlertDto.
    /// </summary>
    public class CareEpisodeAlertDto
    {
        public int CareEpisodeId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string AlertType { get; set; } // LowVisits, Overdue, ExpiringSoon
        public string AlertMessage { get; set; }
        /// <summary>
        /// Remaining visits based on ExpectedVisits - completed appointments for this episode
        /// </summary>
        public int? VisitsRemaining { get; set; }
        public DateOnly? EndDate { get; set; }
        public string PrimaryDiagnosis { get; set; }
    }

    /// <summary>
    /// DTO for patient-level insurance authorization alerts.
    /// This is separate from CareEpisode - it tracks authorization status across ALL insurances for a patient.
    /// Used to alert admins when a patient needs re-authorization from their insurance company.
    /// </summary>
    public class PatientAuthorizationAlertDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string AlertType { get; set; } // LowAuthorizedVisits, AuthorizationExpiring, NoActiveAuthorization
        public string AlertMessage { get; set; }
        /// <summary>
        /// Total visits authorized across all insurances/authorizations for this patient
        /// </summary>
        public int TotalAuthorizedVisits { get; set; }
        /// <summary>
        /// Total completed appointments for this patient (calculated by counting completed appointments)
        /// </summary>
        public int TotalVisitsUsed { get; set; }
        /// <summary>
        /// TotalAuthorizedVisits - TotalVisitsUsed
        /// </summary>
        public int RemainingAuthorizedVisits { get; set; }
        /// <summary>
        /// Threshold below which the alert was triggered
        /// </summary>
        public int AlertThreshold { get; set; }
        /// <summary>
        /// Earliest expiring authorization date
        /// </summary>
        public DateOnly? NextExpiryDate { get; set; }
        /// <summary>
        /// List of insurance names for this patient
        /// </summary>
        public List<string> InsuranceNames { get; set; } = new();
    }

    /// <summary>
    /// DTO for Care Episodes that need appointments scheduled.
    /// Used for the "Require Schedule" dashboard card for Admin/Front Desk.
    /// </summary>
    public class RequireScheduleDto
    {
        public int CareEpisodeId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientMRN { get; set; }
        public string PatientPhone { get; set; }
        public string? PatientEmail { get; set; }
        public DateOnly? PatientDOB { get; set; }
        public string PrimaryDiagnosis { get; set; }
        public string ProviderName { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>
        /// Total expected visits for this care episode
        /// </summary>
        public int ExpectedVisits { get; set; }

        /// <summary>
        /// Number of appointments already scheduled for this care episode
        /// </summary>
        public int ScheduledAppointments { get; set; }

        /// <summary>
        /// Number of appointments still needed (ExpectedVisits - ScheduledAppointments)
        /// </summary>
        public int RequiredAppointments { get; set; }

        /// <summary>
        /// Visit frequency per week
        /// </summary>
        public int? VisitFrequency { get; set; }

        /// <summary>
        /// Care episode start date
        /// </summary>
        public DateOnly StartDate { get; set; }

        /// <summary>
        /// Care episode end date (if set)
        /// </summary>
        public DateOnly? EndDate { get; set; }

        /// <summary>
        /// When the care episode was created
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// DTO for insurance authorization validation response
    /// </summary>
    public class InsuranceAuthorizationDto
    {
        public bool IsValid { get; set; }
        public string AuthorizationNumber { get; set; }
        public int AuthorizedVisits { get; set; }
        public DateOnly AuthorizationStartDate { get; set; }
        public DateOnly AuthorizationEndDate { get; set; }
        public decimal? Copay { get; set; }
        public decimal? Deductible { get; set; }
        public decimal? DeductibleMet { get; set; }
        public decimal? Coinsurance { get; set; }
        public string CoverageNotes { get; set; }
        public string Message { get; set; }
        public DateTime VerifiedAt { get; set; }
    }

    /// <summary>
    /// DTO for system settings
    /// </summary>
    public class SystemSettingDto
    {
        public int SystemSettingId { get; set; }
        public string SettingKey { get; set; }
        public string SettingValue { get; set; }
        public string DataType { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string DefaultValue { get; set; }
    }

    public class SystemSettingUpdateDto
    {
        public string SettingValue { get; set; }
    }

    /// <summary>
    /// Dashboard widgets for Care Episode monitoring
    /// </summary>
    public class CareEpisodeDashboardDto
    {
        public List<CareEpisodeListDto> ActiveEpisodes { get; set; } = new();
        public List<CareEpisodeAlertDto> LowVisitsAlerts { get; set; } = new();
        public List<CareEpisodeAlertDto> OverdueEpisodes { get; set; } = new();
        public List<AppointmentListDto> MissedAppointments { get; set; } = new();
        public List<NoShowAlertDto> NoShowAlerts { get; set; } = new();
        public int TotalActiveEpisodes { get; set; }
        public int TotalOverdueEpisodes { get; set; }
        public int TotalLowVisitsAlerts { get; set; }
        public int TotalNoShowAlerts { get; set; }
        public int TotalMissedAppointments { get; set; }
    }

    /// <summary>
    /// DTO for no-show alerts on dashboard
    /// </summary>
    public class NoShowAlertDto
    {
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientEmail { get; set; }
        public string PatientPhone { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public DateTime ScheduledTime { get; set; }
        public int MinutesOverdue { get; set; }
        public int? CareEpisodeId { get; set; }
    }

    /// <summary>
    /// DTO for missed appointment with reschedule capability
    /// </summary>
    public class MissedAppointmentDto
    {
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientEmail { get; set; }
        public string PatientPhone { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public DateTime ScheduledTime { get; set; }
        public int Type { get; set; }
        public int DaysMissed { get; set; }
        public int? CareEpisodeId { get; set; }
    }

    // ============================================
    // PROVIDER DTOs
    // ============================================
    public class ProviderCreateDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Npi { get; set; } = string.Empty;
        public string Credentials { get; set; }
        public string LicenseNumber { get; set; }
        public string LicenseState { get; set; }
        public string Specialty { get; set; }
        public string Taxonomy { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Color { get; set; }
        public DateOnly? LicenseExpiry { get; set; }
        public int? DefaultAppointmentDuration { get; set; }

        // Password for auto-creating user account
        public string Password { get; set; }

        // Work schedule for the provider (duty hours)
        public List<ProviderScheduleDto> WorkSchedule { get; set; }
    }

    public class ProviderUpdateDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Npi { get; set; }
        public string Credentials { get; set; }
        public string LicenseNumber { get; set; }
        public string LicenseState { get; set; }
        public string Specialty { get; set; }
        public string Taxonomy { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Color { get; set; }
        public DateOnly? LicenseExpiry { get; set; }
        public bool? IsActive { get; set; }
        public int? DefaultAppointmentDuration { get; set; }

        // ISSUE #10 FIX: Work schedule for therapist-specific duty hours
        public List<ProviderScheduleDto> WorkSchedule { get; set; }

        // Password for creating user account if provider doesn't have one yet
        public string Password { get; set; }
    }

    // ISSUE #10 FIX: DTO for provider work schedule (duty hours)
    public class ProviderScheduleDto
    {
        public int DayOfWeek { get; set; } // 0 = Sunday, 1 = Monday, ..., 6 = Saturday
        public string StartTime { get; set; } // Format: "HH:mm" (e.g., "08:00")
        public string EndTime { get; set; } // Format: "HH:mm" (e.g., "17:00")
        public bool IsAvailable { get; set; } = true;
        public int? LocationId { get; set; }
    }

    public class ProviderListDto
    {
        public int ProviderId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        // Note: FullName and DisplayName can be set directly or will default to computed values
        private string _fullName;
        private string _displayName;
        public string FullName 
        { 
            get => _fullName ?? $"{FirstName} {LastName}";
            set => _fullName = value;
        }
        public string DisplayName
        {
            get => _displayName ?? $"{LastName}, {FirstName}";
            set => _displayName = value;
        }
        public string Npi { get; set; }
        public string Credentials { get; set; }
        public string Specialty { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Color { get; set; }
        public bool IsActive { get; set; }
        public int? CredentialStatus { get; set; }
        public DateTime? CredentialExpiry { get; set; }
        public bool HasSignature { get; set; }
        public bool HasProfilePicture { get; set; }
    }

    public class ProviderPreferencesDto
    {
        public bool ShowResumePopup { get; set; } = false;
    }

    public class ProviderDropdownDto
    {
        public int ProviderId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Credentials { get; set; }
        public string Specialty { get; set; }
        public string Color { get; set; }
        public bool IsActive { get; set; }
    }

    // DTO for provider's associated user account information
    public class ProviderUserInfoDto
    {
        public bool HasUser { get; set; }
        public int? UserId { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName => HasUser ? $"{FirstName} {LastName}".Trim() : null;
        public int? Role { get; set; }
        public string RoleName => Role.HasValue ? GetRoleName(Role.Value) : null;
        public bool? IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? CreatedAt { get; set; }

        private static string GetRoleName(int role) => role switch
        {
            0 => "Super Admin",
            1 => "Clinic Admin",
            2 => "Clinician",
            3 => "Front Desk",
            4 => "Biller",
            5 => "Read Only",
            _ => "User"
        };
    }

    // DTO for provider signature upload response
    public class ProviderSignatureResponseDto
    {
        public int ProviderId { get; set; }
        public bool HasSignature { get; set; }
        public string Message { get; set; }
    }

    // DTO for profile picture upload response (used for both patients and providers)
    public class ProfilePictureResponseDto
    {
        public int EntityId { get; set; }
        public string EntityType { get; set; }
        public bool HasProfilePicture { get; set; }
        public string Message { get; set; }
    }

    // ============================================
    // NOTE DTOs (for old Note entity)
    // ============================================
    public class NoteListDto
    {
        public int NoteId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public int? AppointmentId { get; set; }
        public int Type { get; set; }
        public int Status { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string Title { get; set; }
        public DateTime? SignedAt { get; set; }
        public bool HasAddendum { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NoteCreateDto
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? AppointmentId { get; set; }
        public int? CareEpisodeId { get; set; }
        public int Type { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string Title { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Subjective { get; set; }
        public string Objective { get; set; }
        public string Assessment { get; set; }
        public string Plan { get; set; }
        public string VitalSigns { get; set; }
        public string FunctionalTests { get; set; }
        public string Interventions { get; set; }
        public string PatientEducation { get; set; }
        public string HomeExerciseProgram { get; set; }
        public string CPTCodes { get; set; }
        public string ICDCodes { get; set; }
        public int? TotalMinutes { get; set; }
        public int? DirectMinutes { get; set; }
    }

    public class NoteUpdateDto
    {
        public string Title { get; set; }
        public string Content { get; set; }
        public string Subjective { get; set; }
        public string Objective { get; set; }
        public string Assessment { get; set; }
        public string Plan { get; set; }
        public string VitalSigns { get; set; }
        public string FunctionalTests { get; set; }
        public string Interventions { get; set; }
        public string PatientEducation { get; set; }
        public string HomeExerciseProgram { get; set; }
        public string CPTCodes { get; set; }
        public string ICDCodes { get; set; }
        public int? TotalMinutes { get; set; }
        public int? DirectMinutes { get; set; }
        public int? Status { get; set; }
    }

    // ============================================
    // CLINICAL NOTE DTOs
    // ============================================
    public class ClinicalNoteCreateDto
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? AppointmentId { get; set; }
        public int? EncounterId { get; set; }
        public int? TemplateId { get; set; }
        public int Type { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string HtmlContent { get; set; } = string.Empty;
    }

    public class ClinicalNoteUpdateDto
    {
        public string HtmlContent { get; set; }
    }

    public class ClinicalNoteListDto
    {
        public int ClinicalNoteId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientMRN { get; set; } = string.Empty;
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public int? AppointmentId { get; set; }
        public int? EncounterId { get; set; }
        public int Type { get; set; }
        public string TypeName => GetTypeName(Type);
        public int Status { get; set; }
        public string StatusName => GetStatusName(Status);
        public DateOnly ServiceDate { get; set; }
        public string TemplateName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTime? SignedAt { get; set; }

        private static string GetTypeName(int type) => type switch
        {
            0 => "History & Physical",
            1 => "SOAP Note",
            2 => "Office Visit Note",
            3 => "Progress Note",
            4 => "Consultation Note",
            5 => "Procedure Note",
            6 => "Annual Wellness Note",
            7 => "Phone Note",
            8 => "Referral Letter",
            9 => "Lab Review Note",
            _ => "Other"
        };

        private static string GetStatusName(int status) => status switch
        {
            0 => "Draft",
            1 => "Pending Signature",
            2 => "Signed",
            _ => "Unknown"
        };
    }

    public class ClinicalNoteDetailDto : ClinicalNoteListDto
    {
        public string HtmlContent { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public DateTime? SignedAt { get; set; }
        public int? SignedByUserId { get; set; }
        public string SignedByName { get; set; }
    }

    public class ClinicalNotePagedResponse
    {
        public List<ClinicalNoteListDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // ============================================
    // ENCOUNTER CONTEXT & TEMPLATE PREFILL DTOs
    // ============================================

    /// <summary>
    /// Request to pre-fill a clinical note template with encounter data using Gemini AI.
    /// </summary>
    public class PrefillTemplateRequest
    {
        public int TemplateId { get; set; }
        public int? EncounterId { get; set; }
        public int PatientId { get; set; }
        public int? AppointmentId { get; set; }
    }

    /// <summary>
    /// Response from the template pre-fill endpoint.
    /// </summary>
    public class PrefillTemplateResponse
    {
        public bool Success { get; set; }
        public string PrefilledHtml { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public bool FellBackToRawTemplate { get; set; }
    }

    /// <summary>
    /// All encounter context data gathered for a patient visit.
    /// Used for template pre-filling and Scribe context enhancement.
    /// </summary>
    public class EncounterContextDto
    {
        // Encounter-level data
        public string? ChiefComplaint { get; set; }
        public string? HistoryOfPresentIllness { get; set; }
        public string? ReviewOfSystems { get; set; }
        public string? PhysicalExam { get; set; }
        public string? Assessment { get; set; }
        public string? Plan { get; set; }

        // Vitals for this encounter
        public EncounterVitalsDto? Vitals { get; set; }

        // Patient-level history (CURRENT — clinically active)
        public List<string> ActiveAllergies { get; set; } = new();
        public List<string> ActiveMedications { get; set; } = new();
        public List<string> ActiveProblems { get; set; } = new();
        public List<string> FamilyHistory { get; set; } = new();
        public List<string> SocialHistory { get; set; } = new();

        // Patient-level history (HISTORICAL — context only)
        // Per rules/technical/history-review-soft-delete.md (section 12), the AI
        // receives Active items as "current" plus a clearly-labeled HISTORICAL
        // block for treatment progression context. Reason text is included so the
        // model can judge relevance to the current chief complaint.
        // Cutoffs: All inactive allergies; medications discontinued/completed in
        // last 24 months; all resolved/inactive problems.
        public List<string> InactiveAllergies { get; set; } = new();
        public List<string> DiscontinuedMedications { get; set; } = new();
        public List<string> ResolvedProblems { get; set; } = new();

        /// <summary>
        /// Build a text summary of all encounter context for Gemini prompts.
        /// Uses [CURRENT] / [HISTORICAL] section headers with explicit usage
        /// instructions so the model uses Active items in the note's "current"
        /// sections and Historical items only as context.
        /// </summary>
        public string ToPromptText()
        {
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrWhiteSpace(ChiefComplaint))
                sb.AppendLine($"Chief Complaint: {ChiefComplaint}");
            if (!string.IsNullOrWhiteSpace(HistoryOfPresentIllness))
                sb.AppendLine($"History of Present Illness: {HistoryOfPresentIllness}");

            if (Vitals != null)
            {
                sb.AppendLine($"\nVITALS (recorded this visit):");
                if (Vitals.BloodPressure != null) sb.AppendLine($"- Blood Pressure: {Vitals.BloodPressure}");
                if (Vitals.HeartRate.HasValue) sb.AppendLine($"- Heart Rate: {Vitals.HeartRate} bpm");
                if (Vitals.Temperature.HasValue) sb.AppendLine($"- Temperature: {Vitals.Temperature}°F");
                if (Vitals.RespiratoryRate.HasValue) sb.AppendLine($"- Respiratory Rate: {Vitals.RespiratoryRate}/min");
                if (Vitals.SpO2.HasValue) sb.AppendLine($"- SpO2: {Vitals.SpO2}%");
                if (Vitals.Weight.HasValue) sb.AppendLine($"- Weight: {Vitals.Weight} lbs");
                if (Vitals.Height.HasValue) sb.AppendLine($"- Height: {Vitals.Height} inches");
                if (Vitals.Bmi.HasValue) sb.AppendLine($"- BMI: {Vitals.Bmi}");
            }

            // CURRENT block — clinically active items. Use these in the note's
            // current medications / problems / allergies sections.
            var hasCurrent = ActiveAllergies.Count > 0 || ActiveMedications.Count > 0 ||
                             ActiveProblems.Count > 0 || FamilyHistory.Count > 0 || SocialHistory.Count > 0;
            if (hasCurrent)
            {
                sb.AppendLine();
                sb.AppendLine("[CURRENT - ACTIVE STATE. List these in note's current medications/problems/allergies sections.]");
                if (ActiveAllergies.Count > 0)
                    sb.AppendLine($"\nAllergies:\n{string.Join("\n", ActiveAllergies.Select(a => $"- {a}"))}");
                if (ActiveMedications.Count > 0)
                    sb.AppendLine($"\nMedications:\n{string.Join("\n", ActiveMedications.Select(m => $"- {m}"))}");
                if (ActiveProblems.Count > 0)
                    sb.AppendLine($"\nProblems:\n{string.Join("\n", ActiveProblems.Select(p => $"- {p}"))}");
                if (FamilyHistory.Count > 0)
                    sb.AppendLine($"\nFamily History:\n{string.Join("\n", FamilyHistory.Select(f => $"- {f}"))}");
                if (SocialHistory.Count > 0)
                    sb.AppendLine($"\nSocial History:\n{string.Join("\n", SocialHistory.Select(s => $"- {s}"))}");
            }

            // HISTORICAL block — context only.
            var hasHistorical = InactiveAllergies.Count > 0 || DiscontinuedMedications.Count > 0 || ResolvedProblems.Count > 0;
            if (hasHistorical)
            {
                sb.AppendLine();
                sb.AppendLine("[HISTORICAL - INACTIVE / DISCONTINUED / RESOLVED. Context only.");
                sb.AppendLine("Do NOT list as current. Reference only if relevant to chief complaint");
                sb.AppendLine("or to explain treatment progression. Reason field explains why state changed.]");
                if (InactiveAllergies.Count > 0)
                    sb.AppendLine($"\nInactive Allergies:\n{string.Join("\n", InactiveAllergies.Select(a => $"- {a}"))}");
                if (DiscontinuedMedications.Count > 0)
                    sb.AppendLine($"\nDiscontinued / Completed Medications (last 24 months):\n{string.Join("\n", DiscontinuedMedications.Select(m => $"- {m}"))}");
                if (ResolvedProblems.Count > 0)
                    sb.AppendLine($"\nResolved / Inactive Problems:\n{string.Join("\n", ResolvedProblems.Select(p => $"- {p}"))}");
            }

            if (!string.IsNullOrWhiteSpace(ReviewOfSystems))
                sb.AppendLine($"\nREVIEW OF SYSTEMS: {ReviewOfSystems}");
            if (!string.IsNullOrWhiteSpace(PhysicalExam))
                sb.AppendLine($"\nPHYSICAL EXAM: {PhysicalExam}");

            return sb.ToString();
        }

        /// <summary>
        /// Check if there's any meaningful data to pre-fill.
        /// </summary>
        public bool HasData =>
            !string.IsNullOrWhiteSpace(ChiefComplaint) ||
            !string.IsNullOrWhiteSpace(HistoryOfPresentIllness) ||
            Vitals != null ||
            ActiveAllergies.Count > 0 ||
            ActiveMedications.Count > 0 ||
            ActiveProblems.Count > 0 ||
            FamilyHistory.Count > 0 ||
            SocialHistory.Count > 0 ||
            InactiveAllergies.Count > 0 ||
            DiscontinuedMedications.Count > 0 ||
            ResolvedProblems.Count > 0;
    }

    /// <summary>
    /// Vitals data for an encounter context.
    /// </summary>
    public class EncounterVitalsDto
    {
        public string? BloodPressure { get; set; }
        public int? HeartRate { get; set; }
        public int? RespiratoryRate { get; set; }
        public decimal? Temperature { get; set; }
        public decimal? SpO2 { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Height { get; set; }
        public decimal? Bmi { get; set; }
    }

    // ============================================
    // CLINICAL NOTE TEMPLATE DTOs
    // ============================================
    public class ClinicalNoteTemplateDto
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? TenantId { get; set; }
        public string TenantName { get; set; }
        public int? LocationId { get; set; }
        public string LocationName { get; set; }
        public string HtmlContent { get; set; } = string.Empty;
        public bool IsSystemTemplate { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }

    public class ClinicalNoteTemplateCreateDto
    {
        public int? TenantId { get; set; }
        public int? LocationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string HtmlContent { get; set; } = string.Empty;
        public int SortOrder { get; set; } = 0;
        public bool IsActive { get; set; } = true;
    }

    public class ClinicalNoteTemplateUpdateDto
    {
        public string Name { get; set; }
        public int? TenantId { get; set; }
        public int? LocationId { get; set; }
        public string HtmlContent { get; set; }
        public int? SortOrder { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ClinicalNoteTemplateListDto
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? TenantId { get; set; }
        public string TenantName { get; set; }
        public int? LocationId { get; set; }
        public string LocationName { get; set; }
        public bool IsSystemTemplate { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }

    public class ClinicalNoteTemplateDetailDto : ClinicalNoteTemplateListDto
    {
        public string HtmlContent { get; set; } = string.Empty;
    }

    // ============================================
    // THERAPIST UNAVAILABILITY DTOs
    // ============================================
    public class TherapistUnavailabilityCreateDto
    {
        public int ProviderId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int Type { get; set; }
        public string Reason { get; set; }
        public bool IsFullDay { get; set; } = true;
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string RecurrencePattern { get; set; }
    }

    public class TherapistUnavailabilityUpdateDto
    {
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public int? Type { get; set; }
        public string Reason { get; set; }
        public bool? IsFullDay { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string RecurrencePattern { get; set; }
    }

    public class TherapistUnavailabilityListDto
    {
        public int UnavailabilityId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public string ProviderColor { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int Type { get; set; }
        public string TypeName => GetTypeName(Type);
        public string Reason { get; set; }
        public bool IsFullDay { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public bool IsApproved { get; set; }
        public string ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }

        private static string GetTypeName(int type) => type switch
        {
            0 => "Vacation",
            1 => "Sick Leave",
            2 => "Training",
            3 => "Personal Leave",
            4 => "Holiday",
            5 => "Conference",
            6 => "Admin Time",
            _ => "Other"
        };
    }

    public class UnavailabilityCheckRequest
    {
        public int ProviderId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class UnavailabilityCheckResult
    {
        public bool IsAvailable { get; set; }
        public string ConflictReason { get; set; }
        public TherapistUnavailabilityListDto? ConflictingUnavailability { get; set; }
        public List<TherapistUnavailabilityListDto>? Conflicts { get; set; }
    }

    // ============================================
    // BILLING DTOs
    // ============================================
    public class ChargeDto
    {
        public int ChargeId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public int? AppointmentId { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string CptCode { get; set; } = string.Empty;
        public string Description { get; set; }
        public int Units { get; set; }
        public decimal ChargeAmount { get; set; }
        public decimal? AllowedAmount { get; set; }
        public decimal? PaidAmount { get; set; }
        public int Status { get; set; }
    }

    public class ChargeCreateDto
    {
        public int PatientId { get; set; }
        public int? AppointmentId { get; set; }
        public int? NoteId { get; set; }
        public int? ProviderId { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string CptCode { get; set; } = string.Empty;
        public string CPTCode { get; set; }
        public string CPTDescription { get; set; }
        public int Units { get; set; } = 1;
        public decimal ChargeAmount { get; set; }
        public string Modifier1 { get; set; }
        public string Modifier2 { get; set; }
        public string Modifiers { get; set; }
        public string DiagnosisCodes { get; set; }
        public string ICDCodes { get; set; }
    }

    public class ChargeListDto
    {
        public int ChargeId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string ProviderName { get; set; }
        public int? AppointmentId { get; set; }
        public int? ClaimId { get; set; }
        public DateOnly ServiceDate { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("CPTCode")]
        public string CptCode { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CPTDescription")]
        public string CPTDescription { get; set; }
        public string Description { get; set; }
        public string Modifiers { get; set; }
        public int Units { get; set; }
        public decimal ChargeAmount { get; set; }
        public decimal? AllowedAmount { get; set; }
        public decimal? PaidAmount { get; set; }
        public decimal? Balance { get; set; }
        public int Status { get; set; }
    }

    public class ClaimDto
    {
        public int ClaimId { get; set; }
        public string ClaimNumber { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PayerName { get; set; } = string.Empty;
        public DateOnly ServiceDateFrom { get; set; }
        public DateOnly? ServiceDateTo { get; set; }
        public decimal TotalCharged { get; set; }
        public decimal? TotalAllowed { get; set; }
        public decimal TotalPaid { get; set; }
        public int Status { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }

    public class ClaimListDto
    {
        public int ClaimId { get; set; }
        public string ClaimNumber { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public string PayerName { get; set; } = string.Empty;
        public DateOnly ServiceDateFrom { get; set; }
        public DateOnly? ServiceDateTo { get; set; }
        public decimal TotalCharged { get; set; }
        public decimal? TotalAllowed { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Balance { get; set; }
        public int DaysInAR { get; set; }
        public string DenialReason { get; set; }
        public int Status { get; set; }
        public DateTime? SubmittedAt { get; set; }

        // Amendment-window submit lock (populated server-side from the claim's
        // linked encounter's CheckOutTime + ClinicalNotes:AmendmentWindowHours).
        // UI uses these to render a "Locked" pill + countdown + to gray out the
        // Submit button while the provider's 24h amendment window is still open.
        public bool IsSubmitLocked { get; set; }
        public DateTime? AmendmentWindowEndsAt { get; set; }   // UTC
    }

    public class BillingPagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public decimal TotalBilled { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int PendingCount { get; set; }
        public int BilledCount { get; set; }
    }

    public class ClaimCreateDto
    {
        public int PatientId { get; set; }
        public int InsuranceId { get; set; }
        public List<int> ChargeIds { get; set; } = new();
        public DateOnly ServiceDateFrom { get; set; }
        public DateOnly? ServiceDateTo { get; set; }
    }

    public class PaymentDto
    {
        public int PaymentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public int? ClaimId { get; set; }
        public int Type { get; set; }
        public string TypeName => GetTypeName(Type);
        public int Method { get; set; }
        public string MethodName => GetMethodName(Method);
        public decimal Amount { get; set; }
        public DateOnly PaymentDate { get; set; }
        public string ReferenceNumber { get; set; }
        public int Status { get; set; }

        private static string GetTypeName(int type) => type switch
        {
            0 => "Copay",
            1 => "Coinsurance",
            2 => "Deductible",
            3 => "Self Pay",
            4 => "Insurance Payment",
            5 => "Refund",
            _ => "Other"
        };

        private static string GetMethodName(int method) => method switch
        {
            0 => "Cash",
            1 => "Check",
            2 => "Credit Card",
            3 => "Debit Card",
            4 => "EFT",
            5 => "ERA",
            _ => "Other"
        };
    }

    public class PaymentListDto
    {
        public int PaymentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public int? ClaimId { get; set; }
        public string TransactionId { get; set; }
        public string PayerName { get; set; }
        public int Type { get; set; }
        public string TypeName => GetTypeName(Type);
        public int Method { get; set; }
        public string MethodName => GetMethodName(Method);
        public decimal Amount { get; set; }
        public DateOnly PaymentDate { get; set; }
        public string ReferenceNumber { get; set; }
        public int Status { get; set; }
        public bool IsRefund { get; set; }

        private static string GetTypeName(int type) => type switch
        {
            0 => "Copay",
            1 => "Coinsurance",
            2 => "Deductible",
            3 => "Self Pay",
            4 => "Insurance Payment",
            5 => "Refund",
            _ => "Other"
        };

        private static string GetMethodName(int method) => method switch
        {
            0 => "Cash",
            1 => "Check",
            2 => "Credit Card",
            3 => "Debit Card",
            4 => "EFT",
            5 => "ERA",
            _ => "Other"
        };
    }

    public class PaymentCreateDto
    {
        public int PatientId { get; set; }
        public int? ClaimId { get; set; }
        public int? AppointmentId { get; set; }
        public int Type { get; set; }
        public int Method { get; set; }
        public decimal Amount { get; set; }
        public DateOnly PaymentDate { get; set; }
        public string TransactionId { get; set; }
        public string CheckNumber { get; set; }
        public DateOnly? CheckDate { get; set; }
        public string CardReference { get; set; }
        public string PayerName { get; set; }
        public string ReferenceNumber { get; set; }
        public string Notes { get; set; }
    }

    public class PatientBalanceDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public decimal TotalCharges { get; set; }
        public decimal TotalPayments { get; set; }
        public decimal TotalAdjustments { get; set; }
        public decimal InsurancePayments { get; set; }
        public decimal PatientPayments { get; set; }
        public decimal Adjustments { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal Balance { get; set; }
        public decimal InsuranceBalance { get; set; }
        public decimal PatientBalance { get; set; }
    }

    // ============================================
    // COPAY / INSTALLMENT / PORTAL BILLING DTOs
    // ============================================

    /// <summary>
    /// Portal balance summary — calculated real-time, no stored balance field.
    /// </summary>
    public class PortalBalanceDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public decimal TotalCharges { get; set; }
        public decimal InsurancePaid { get; set; }
        public decimal Adjustments { get; set; }
        public decimal PatientPaid { get; set; }
        /// <summary>TotalCharges - InsurancePaid - Adjustments - PatientPaid</summary>
        public decimal CurrentBalance { get; set; }
        public bool HasActivePlan { get; set; }
    }

    /// <summary>
    /// Simple transaction row for patient portal — Date, Description, Amount only.
    /// </summary>
    public class PatientTransactionDto
    {
        public DateTime Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Method { get; set; } = string.Empty;
    }

    /// <summary>
    /// Full ledger row for staff/biller — all charges + payments with calculated running balance.
    /// </summary>
    public class PatientLedgerEntryDto
    {
        public DateTime Date { get; set; }
        /// <summary>Charge=1, Payment=2, Adjustment=3, InsurancePayment=4</summary>
        public int EntryType { get; set; }
        public string EntryTypeName { get; set; } = string.Empty;
        public string CptCode { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal? ChargeAmount { get; set; }
        public decimal? PaymentAmount { get; set; }
        public decimal? AdjustmentAmount { get; set; }
        public string PayerName { get; set; }
        public string PaymentMethod { get; set; }
        /// <summary>Calculated on-the-fly during query, not stored.</summary>
        public decimal RunningBalance { get; set; }
    }

    /// <summary>
    /// Create a Stripe PaymentIntent for patient portal online payment.
    /// </summary>
    public class StripePaymentCreateDto
    {
        public decimal Amount { get; set; }
        /// <summary>Full=0, FirstInstallment=1, Installment=2</summary>
        public int PaymentType { get; set; }
        public string ReturnUrl { get; set; }
    }

    /// <summary>
    /// Create an installment plan for a patient.
    /// </summary>
    public class InstallmentPlanCreateDto
    {
        public decimal TotalAmount { get; set; }
        public int NumberOfInstallments { get; set; }
        public string StripePaymentMethodId { get; set; }

        /// <summary>
        /// Location where the plan is created. Determines which Stripe Connect account
        /// will receive the recurring charges. Required for all installment plans.
        /// </summary>
        public int LocationId { get; set; }
    }

    /// <summary>
    /// Installment plan summary with details.
    /// </summary>
    public class InstallmentPlanDto
    {
        public int PlanId { get; set; }
        public decimal TotalAmount { get; set; }
        public int NumberOfInstallments { get; set; }
        public int Status { get; set; }
        public string StatusName => Status switch
        {
            0 => "Active",
            1 => "Completed",
            2 => "Cancelled",
            3 => "Defaulted",
            _ => "Unknown"
        };
        public decimal AmountPaid { get; set; }
        public decimal AmountRemaining { get; set; }
        public DateOnly? NextDueDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<InstallmentDetailDto> Details { get; set; } = new();
    }

    /// <summary>
    /// Individual installment detail row.
    /// </summary>
    public class InstallmentDetailDto
    {
        public int DetailId { get; set; }
        public int InstallmentNumber { get; set; }
        public DateOnly DueDate { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
        public string StatusName => Status switch
        {
            0 => "Pending",
            1 => "Paid",
            2 => "Failed",
            3 => "Delinquent",
            _ => "Unknown"
        };
        public DateTime? PaidAt { get; set; }
    }

    // ============================================
    // CALENDAR DTOs
    // ============================================
    public class CalendarEventDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string BackgroundColor { get; set; }
        public string BorderColor { get; set; }
        public string TextColor { get; set; }
        public string Display { get; set; } = "auto";
        public bool AllDay { get; set; }
        public Dictionary<string, object>? ExtendedProps { get; set; }
    }

    // ============================================
    // DASHBOARD DTOs
    // ============================================
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

    public class DashboardAppointmentsDto
    {
        public List<AppointmentListDto> Appointments { get; set; } = new();
        public int WaitingForCheckInCount { get; set; }
        public int MissingNotesCount { get; set; }
        public int RequiresSignatureCount { get; set; }
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// DTO for dashboard "Missing Notes" alert items.
    /// Represents appointments where patient checked in but no clinical note exists.
    /// </summary>
    public class MissingNotesItemDto
    {
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientMRN { get; set; } = string.Empty;
        public string? PatientPhone { get; set; }
        public string? PatientEmail { get; set; }
        public DateTime AppointmentDate { get; set; }
        public int Type { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        /// <summary>Number of days since the appointment</summary>
        public int DaysSince { get; set; }
    }

    /// <summary>
    /// DTO for dashboard "Need Signature" alert items.
    /// Represents clinical notes that exist but are not yet signed (Draft or PendingSignature status).
    /// </summary>
    public class MissingSignatureItemDto
    {
        public int ClinicalNoteId { get; set; }
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientMRN { get; set; } = string.Empty;
        public string? PatientPhone { get; set; }
        public string? PatientEmail { get; set; }
        public DateTime AppointmentDate { get; set; }
        public string? TemplateName { get; set; }
        public int Type { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
        /// <summary>Number of days since the note was created</summary>
        public int DaysSince { get; set; }
    }

    public class ARAgingDto
    {
        public decimal Current { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Days91To120 { get; set; }
        public decimal Over120Days { get; set; }
        // Total can be set directly or computed
        private decimal? _total;
        public decimal Total 
        { 
            get => _total ?? (Current + Days31To60 + Days61To90 + Days91To120 + Over120Days);
            set => _total = value;
        }
    }

    // ============================================
    // CMS 1500 / UB04 CLAIM DETAIL DTOs
    // ============================================

    /// <summary>
    /// Full CMS 1500 / UB04 claim detail for form view
    /// </summary>
    public class ClaimDetailDto
    {
        // Core identifiers
        public int ClaimId { get; set; }
        public string ClaimNumber { get; set; }
        public int? Type { get; set; }
        public int? Status { get; set; }
        public DateOnly ServiceDateFrom { get; set; }
        public DateOnly ServiceDateTo { get; set; }
        public DateTime? SubmittedAt { get; set; }

        // Amendment-window submit lock (same as ClaimListDto). Drives the
        // "Locked until {time}" countdown + disabled Submit-to-Office-Ally
        // button on the claim detail modal.
        public bool IsSubmitLocked { get; set; }
        public DateTime? AmendmentWindowEndsAt { get; set; }   // UTC
        public DateTime? CreatedAt { get; set; }

        // FK IDs
        public int PatientId { get; set; }
        public int? InsuranceId { get; set; }
        public int? ClinicalNoteId { get; set; }
        public int? AppointmentId { get; set; }
        public int? CareEpisodeId { get; set; }
        public int? AuthorizationId { get; set; }
        public int? ProviderId { get; set; }
        public int? LocationId { get; set; }

        // Patient demographics (decrypted)
        public string PatientFirstName { get; set; }
        public string PatientLastName { get; set; }
        public string PatientAddress { get; set; }
        public string PatientCity { get; set; }
        public string PatientState { get; set; }
        public string PatientZip { get; set; }
        public string PatientPhone { get; set; }
        public DateOnly? PatientDob { get; set; }
        public string PatientGender { get; set; }
        public string PatientMrn { get; set; }

        // Insurance info
        public string PayerName { get; set; }
        public string PayerId { get; set; }
        public int? InsuranceTypeCode { get; set; }

        // Insured snapshot (Box 4-11)
        public string InsuredName { get; set; }
        public string InsuredAddress { get; set; }
        public string InsuredCity { get; set; }
        public string InsuredState { get; set; }
        public string InsuredZip { get; set; }
        public DateOnly? InsuredDob { get; set; }
        public string InsuredGender { get; set; }
        public string InsuredPolicyNumber { get; set; }
        public string InsuredGroupNumber { get; set; }
        public string SubscriberRelationship { get; set; }

        // Signatures (Box 12/13)
        public bool? PatientSignatureOnFile { get; set; }
        public bool? InsuredSignatureOnFile { get; set; }

        // Referring Provider (Box 17)
        public string ReferringProviderName { get; set; }
        public string ReferringProviderNpi { get; set; }

        // Diagnosis Codes (Box 21) — JSON array
        public string DiagnosisCodes { get; set; }

        // Prior Auth (Box 23)
        public string PriorAuthorizationNumber { get; set; }

        // Place of Service (Box 24b)
        public string PlaceOfServiceCode { get; set; }

        // Federal Tax ID (Box 25)
        public string FederalTaxId { get; set; }

        // Patient Account Number (Box 26)
        public string PatientAccountNumber { get; set; }

        // Accept Assignment (Box 27)
        public bool? AcceptAssignment { get; set; }

        // Amount Paid (Box 29)
        public decimal? AmountPaid { get; set; }

        // Rendering Provider (Box 31)
        public string RenderingProviderName { get; set; }
        public string RenderingProviderNpi { get; set; }

        // Facility (Box 32)
        public string FacilityName { get; set; }
        public string FacilityAddress { get; set; }
        public string FacilityNpi { get; set; }

        // Billing Provider (Box 33)
        public string BillingProviderName { get; set; }
        public string BillingProviderAddress { get; set; }
        public string BillingProviderNpi { get; set; }
        public string BillingProviderTaxonomy { get; set; }

        // UB04-specific
        public string TypeOfBill { get; set; }
        public DateOnly? AdmissionDate { get; set; }
        public int? AdmissionType { get; set; }
        public string PatientDischargeStatus { get; set; }
        public string ConditionCodes { get; set; }
        public string ValueCodes { get; set; }
        public string OccurrenceCodes { get; set; }

        // Financial totals
        public decimal TotalCharged { get; set; }
        public decimal? TotalAllowed { get; set; }
        public decimal? TotalPaid { get; set; }
        public decimal? TotalAdjustment { get; set; }
        public decimal? PatientResponsibility { get; set; }

        // Notes & denial
        public string Notes { get; set; }
        public string DenialReasonCode { get; set; }
        public string DenialReason { get; set; }

        // EDI
        public string Edidata { get; set; }
        public string ResponseData { get; set; }

        // Charge lines (Box 24)
        public List<ChargeLineDto> ChargeLines { get; set; } = new();
    }

    /// <summary>
    /// Single charge/service line (CMS 1500 Box 24 row)
    /// </summary>
    public class ChargeLineDto
    {
        public int ChargeId { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string PlaceOfServiceCode { get; set; }
        public string CptCode { get; set; }
        public string CptDescription { get; set; }
        public string Modifier1 { get; set; }
        public string Modifier2 { get; set; }
        public string Modifier3 { get; set; }
        public string Modifier4 { get; set; }
        public string IcdPointers { get; set; }
        public decimal ChargeAmount { get; set; }
        public int? Units { get; set; }
        public string RenderingProviderNpi { get; set; }
        public string RevenueCode { get; set; }
        public int? Status { get; set; }
        public decimal? AllowedAmount { get; set; }
        public decimal? PaidAmount { get; set; }
        public decimal? AdjustmentAmount { get; set; }
    }

    /// <summary>
    /// Biller edits to CMS 1500 / UB04 claim fields
    /// </summary>
    public class ClaimUpdateDto
    {
        public int? Type { get; set; }
        public int? InsuranceTypeCode { get; set; }
        public bool? PatientSignatureOnFile { get; set; }
        public bool? InsuredSignatureOnFile { get; set; }
        public string ReferringProviderName { get; set; }
        public string ReferringProviderNpi { get; set; }
        public string DiagnosisCodes { get; set; }
        public string PriorAuthorizationNumber { get; set; }
        public string PlaceOfServiceCode { get; set; }
        public string FederalTaxId { get; set; }
        public string PatientAccountNumber { get; set; }
        public bool? AcceptAssignment { get; set; }
        public decimal? AmountPaid { get; set; }
        public string RenderingProviderName { get; set; }
        public string RenderingProviderNpi { get; set; }
        public string FacilityName { get; set; }
        public string FacilityAddress { get; set; }
        public string FacilityNpi { get; set; }
        public string BillingProviderName { get; set; }
        public string BillingProviderAddress { get; set; }
        public string BillingProviderNpi { get; set; }
        public string BillingProviderTaxonomy { get; set; }
        public string InsuredName { get; set; }
        public string InsuredAddress { get; set; }
        public string InsuredCity { get; set; }
        public string InsuredState { get; set; }
        public string InsuredZip { get; set; }
        public DateOnly? InsuredDob { get; set; }
        public string InsuredGender { get; set; }
        public string InsuredPolicyNumber { get; set; }
        public string InsuredGroupNumber { get; set; }
        public string SubscriberRelationship { get; set; }
        public string Notes { get; set; }
        // UB04
        public string TypeOfBill { get; set; }
        public DateOnly? AdmissionDate { get; set; }
        public int? AdmissionType { get; set; }
        public string PatientDischargeStatus { get; set; }
        public string ConditionCodes { get; set; }
        public string ValueCodes { get; set; }
        public string OccurrenceCodes { get; set; }
    }

    /// <summary>
    /// Add/edit a CPT charge line on a claim
    /// </summary>
    public class ClaimChargeCreateDto
    {
        public int ClaimId { get; set; }
        public DateOnly ServiceDate { get; set; }
        public string CptCode { get; set; }
        public string CptDescription { get; set; }
        public int? Units { get; set; }
        public decimal ChargeAmount { get; set; }
        public string Modifier1 { get; set; }
        public string Modifier2 { get; set; }
        public string Modifier3 { get; set; }
        public string Modifier4 { get; set; }
        public string IcdPointers { get; set; }
        public string PlaceOfServiceCode { get; set; }
        public string RevenueCode { get; set; }
    }

    // ============================================
    // CPT SUGGESTION DTOs
    // ============================================

    public class CptSuggestionRequest
    {
        public int ClaimId { get; set; }
        public string DiagnosisCodes { get; set; }
        public string NoteContent { get; set; }
        /// <summary>
        /// Optional. AppointmentType int from EHR.Models.AppointmentType enum.
        /// When set, the Gemini prompt receives "APPOINTMENT TYPE: {name}" and
        /// a context-aware instruction (especially relevant for Telehealth,
        /// where modifier 95 / POS 02-10 matter, and for Longevity visits).
        /// </summary>
        public int? AppointmentType { get; set; }
    }

    public class CptSuggestionDto
    {
        public string CptCode { get; set; }
        public string Description { get; set; }
        public int Units { get; set; } = 1;
        public string Rationale { get; set; }
        public decimal? DefaultRate { get; set; }
    }

    public class CptSuggestionResponse
    {
        public bool Success { get; set; }
        public List<CptSuggestionDto> Suggestions { get; set; } = new();
        public string ErrorMessage { get; set; }
    }

    // --- ICD-10 AI Suggestions (mirrors CPT pattern) ---
    public class IcdSuggestionRequest
    {
        public string NoteContent { get; set; }
        /// <summary>
        /// Optional. AppointmentType int from EHR.Models.AppointmentType enum.
        /// When set, the Gemini prompt receives "APPOINTMENT TYPE: {name}" so
        /// it can bias toward type-appropriate diagnoses (e.g., preventive
        /// Z-codes for Annual Physical, lifestyle/age codes for Longevity).
        /// </summary>
        public int? AppointmentType { get; set; }
    }

    public class IcdSuggestionDto
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public int Confidence { get; set; } = 50;   // 0-100, used for High/Medium/Low badge
        public string Rationale { get; set; }
    }

    public class IcdSuggestionResponse
    {
        public bool Success { get; set; }
        public List<IcdSuggestionDto> Suggestions { get; set; } = new();
        public string ErrorMessage { get; set; }
    }

    // ============================================
    // PROVIDER FAVORITE CODES (Dx & CPT step)
    // Per-user favorites — strict isolation, no clinic-wide visibility.
    // Units NOT stored — provider sets them inline on the visit row.
    // ============================================

    public class ProviderFavoriteCodeDto
    {
        public int Id { get; set; }
        public string CodeType { get; set; }    // "ICD10" or "CPT"
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class AddFavoriteRequest
    {
        public string CodeType { get; set; }    // "ICD10" or "CPT"
        public string Code { get; set; }
        public string Description { get; set; }
    }

    // ============================================
    // CPT CHECKOUT DTOs
    // ============================================

    public class CheckoutWithCptRequest
    {
        public List<CheckoutCptItem> CptCodes { get; set; } = new();
    }

    public class CheckoutCptItem
    {
        public string CptCode { get; set; }
        public string Description { get; set; }
        public int Units { get; set; } = 1;
    }

    // ============================================
    // AUTH DTOs
    // ============================================
    public class LoginDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Subdomain { get; set; }
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int Role { get; set; }
        public int? TenantId { get; set; }
        public string TenantName { get; set; }
        public string TenantSubdomain { get; set; }
        public int? ProviderId { get; set; }
    }

    // ============================================
    // TENANT DTOs
    // ============================================
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
        public int PatientCount { get; set; }
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

    /// <summary>
    /// Full tenant details for editing (avoids returning raw entity with navigation properties)
    /// </summary>
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

    // ============================================
    // AUTH DTOs (additional)
    // ============================================
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

    /// <summary>
    /// OTP verification DTO — second step of staff login after email+password.
    /// </summary>
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

    /// <summary>
    /// Resend OTP DTO — re-send the verification code if it was lost.
    /// </summary>
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
        public int? ProviderId { get; set; }
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

    /// <summary>
    /// DTO for tenant selection when a user belongs to multiple clinics
    /// </summary>
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

    // ============================================
    // ADDITIONAL DTOs
    // ============================================
    public class ProviderSearchResultDto
    {
        public int ProviderId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Specialty { get; set; }
        public string Color { get; set; }
        public bool IsActive { get; set; }
    }

    public class SignClinicalNoteRequest
    {
        public string SignatureData { get; set; }
    }

    // ============================================
    // USER MANAGEMENT DTOs (Super Admin)
    // ============================================
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
        public int? ProviderId { get; set; }
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
        public int? ProviderId { get; set; }
    }

    public class UserUpdateDto
    {
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Phone { get; set; }
        public int? Role { get; set; }
        public int? ProviderId { get; set; }
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

    // ============================================
    // FORGOT PASSWORD DTOs
    // ============================================
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

    // ============================================
    // PATIENT DOCUMENT DTOs
    // ============================================

    /// <summary>
    /// Document category enumeration
    /// </summary>
    public enum DocumentCategory
    {
        InsuranceCard = 0,
        ID = 1,
        Referral = 2,
        MedicalRecord = 3,
        Consent = 4,
        Other = 5
    }

    public class PatientDocumentDto
    {
        public int DocumentId { get; set; }
        public int PatientId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int Category { get; set; }
        public string CategoryName => GetCategoryName(Category);
        public string Description { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string UploadedByName { get; set; }

        private static string GetCategoryName(int category) => category switch
        {
            0 => "Insurance Card",
            1 => "ID Document",
            2 => "Referral",
            3 => "Medical Record",
            4 => "Consent Form",
            _ => "Other"
        };
    }

    public class PatientDocumentListDto
    {
        public int DocumentId { get; set; }
        public int PatientId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileSizeFormatted => FormatFileSize(FileSize);
        public int Category { get; set; }
        public string CategoryName => GetCategoryName(Category);
        public string Description { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string UploadedByName { get; set; }
        public bool IsPatientUploaded { get; set; }

        private static string GetCategoryName(int category) => category switch
        {
            0 => "Insurance Card",
            1 => "ID Document",
            2 => "Referral",
            3 => "Medical Record",
            4 => "Consent Form",
            _ => "Other"
        };

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public class PatientDocumentUploadDto
    {
        public int PatientId { get; set; }
        public int Category { get; set; } = 5; // Default to Other
        public string Description { get; set; }
    }

    public class PatientDocumentUploadResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int? DocumentId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
    }

    // ============================================
    // REPORT DTOs
    // ============================================

    /// <summary>
    /// Request DTO for the Monthly Patient Visit Grid report
    /// </summary>
    public class MonthlyVisitGridRequestDto
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public int? ProviderId { get; set; }
    }

    /// <summary>
    /// Response DTO for the Monthly Patient Visit Grid report
    /// </summary>
    public class MonthlyVisitGridResponseDto
    {
        public string AgencyName { get; set; }
        public string Discipline { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public int DaysInMonth { get; set; }
        public List<PatientVisitGridRowDto> Rows { get; set; } = new();
        public int TotalVisits { get; set; }
        public int TotalEvaluations { get; set; }
        public int TotalInterimEvals { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Single row in the Monthly Patient Visit Grid
    /// </summary>
    public class PatientVisitGridRowDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public int ProviderId { get; set; }
        public string TherapistName { get; set; }
        public string TherapistCode { get; set; }  // Initials or short code
        public string Insurance { get; set; }       // Insurance code/name
        public int? AuthorizedVisits { get; set; }  // A.V. column
        public string PCP { get; set; }             // Primary Care Physician
        public string Frequency { get; set; }       // e.g., "3x6" (3 times per week for 6 weeks)
        public string CertPeriod { get; set; }      // Certification Period

        // Day visits - key is day of month (1-31), value is visit code (V, E, I, D, A, C, F, H, N, R)
        public Dictionary<int, List<DayVisitDto>> DayVisits { get; set; } = new();

        // Summary counts
        public int MonthlyVisitCount { get; set; }    // M column - count of V codes
        public int EvaluationCount { get; set; }      // E column - count of E codes
        public int InterimEvalCount { get; set; }     // I column - count of I codes
        public int? RemainingVisits { get; set; }     // RV column - Authorized minus used
        public int? ProjectedVisits { get; set; }     // PV column
    }

    /// <summary>
    /// Visit details for a specific day cell
    /// </summary>
    public class DayVisitDto
    {
        public int AppointmentId { get; set; }
        public string Code { get; set; }      // V, E, I, D, A, C, F, H, N, R
        public int Type { get; set; }         // Appointment type enum value
        public int Status { get; set; }       // Appointment status enum value
        public DateTime StartTime { get; set; }
        public bool HasNote { get; set; }
        public int NoteStatus { get; set; }   // Note status if exists
    }

    // ============================================
    // ANALYTICAL REPORTS DTOs
    // ============================================

    public class AnalyticalReportRequestDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int? ProviderId { get; set; }
        public int? LocationId { get; set; }
        public int? InsuranceId { get; set; }
    }

    public class ReportKpiDto
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string PreviousValue { get; set; }
        public decimal? ChangePercent { get; set; }
    }

    // --- Encounter Completion ---
    public class EncounterCompletionReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<EncounterCompletionChartDto> ChartData { get; set; } = new();
        public List<EncounterCompletionRowDto> Rows { get; set; } = new();
    }
    public class EncounterCompletionChartDto
    {
        public string ProviderName { get; set; }
        public int Signed { get; set; }
        public int Open { get; set; }
        public int Draft { get; set; }
    }
    public class EncounterCompletionRowDto
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int TotalEncounters { get; set; }
        public int Signed { get; set; }
        public int Open { get; set; }
        public int Draft { get; set; }
        public int Amended { get; set; }
        public decimal CompletionRate { get; set; }
        public decimal AvgDaysToSign { get; set; }
    }

    // --- Orders Tracking ---
    public class OrdersTrackingReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<OrdersTrackingChartDto> ChartData { get; set; } = new();
        public List<OrdersTrackingRowDto> Rows { get; set; } = new();
    }
    public class OrdersTrackingChartDto
    {
        public string OrderType { get; set; }
        public int Pending { get; set; }
        public int Sent { get; set; }
        public int ResultsReceived { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
    }
    public class OrdersTrackingRowDto
    {
        public string OrderType { get; set; }
        public int TotalOrdered { get; set; }
        public int Pending { get; set; }
        public int Sent { get; set; }
        public int ResultsReceived { get; set; }
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public decimal AvgTurnaroundDays { get; set; }
    }

    // --- Prescription Analytics ---
    public class PrescriptionAnalyticsReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<PrescriptionAnalyticsChartDto> ChartData { get; set; } = new();
        public List<PrescriptionAnalyticsRowDto> Rows { get; set; } = new();
    }
    public class PrescriptionAnalyticsChartDto
    {
        public string DrugName { get; set; }
        public int Count { get; set; }
    }
    public class PrescriptionAnalyticsRowDto
    {
        public string DrugName { get; set; }
        public string GenericName { get; set; }
        public int TimesPrescribed { get; set; }
        public decimal AvgQuantity { get; set; }
        public decimal AvgRefills { get; set; }
        public int ControlledCount { get; set; }
        public string TopPrescriber { get; set; }
    }

    // --- Chronic Disease Panel ---
    public class ChronicDiseaseReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<ChronicDiseaseChartDto> ChartData { get; set; } = new();
        public List<ChronicDiseaseRowDto> Rows { get; set; } = new();
    }
    public class ChronicDiseaseChartDto
    {
        public string IcdCode { get; set; }
        public string Description { get; set; }
        public int ActivePatientCount { get; set; }
    }
    public class ChronicDiseaseRowDto
    {
        public string IcdCode { get; set; }
        public string Description { get; set; }
        public int ActivePatientCount { get; set; }
        public int ResolvedCount { get; set; }
        public decimal AvgDurationDays { get; set; }
    }

    // --- Visit Volume ---
    public class VisitVolumeReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<VisitVolumeChartDto> ChartData { get; set; } = new();
        public List<VisitVolumeRowDto> Rows { get; set; } = new();
    }
    public class VisitVolumeChartDto
    {
        public string Label { get; set; }
        public int Completed { get; set; }
        public int NoShow { get; set; }
        public int Cancelled { get; set; }
    }
    public class VisitVolumeRowDto
    {
        public string Period { get; set; }
        public int TotalScheduled { get; set; }
        public int Completed { get; set; }
        public int NoShow { get; set; }
        public int Cancelled { get; set; }
        public decimal CompletionRate { get; set; }
        public int NewPatient { get; set; }
        public int Telehealth { get; set; }
    }

    // --- No-Show Rate ---
    public class NoShowRateReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<NoShowByDayDto> ByDayOfWeek { get; set; } = new();
        public List<NoShowRateRowDto> Rows { get; set; } = new();
    }
    public class NoShowByDayDto
    {
        public string DayOfWeek { get; set; }
        public int Total { get; set; }
        public int NoShows { get; set; }
        public decimal Rate { get; set; }
    }
    public class NoShowRateRowDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public int NoShowCount { get; set; }
        public int CancellationCount { get; set; }
        public int TotalAppointments { get; set; }
        public decimal Rate { get; set; }
        public DateTime? LastNoShow { get; set; }
        public string Insurance { get; set; }
    }

    // --- Revenue & Claims ---
    public class RevenueClaimsReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<RevenueClaimsChartDto> ChartData { get; set; } = new();
        public List<RevenueClaimsRowDto> Rows { get; set; } = new();
    }
    public class RevenueClaimsChartDto
    {
        public string Label { get; set; }
        public decimal TotalBilled { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalDenied { get; set; }
    }
    public class RevenueClaimsRowDto
    {
        public string Period { get; set; }
        public int TotalClaims { get; set; }
        public decimal TotalBilled { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalDenied { get; set; }
        public decimal TotalPending { get; set; }
        public decimal CollectionRate { get; set; }
        public decimal AvgDaysToPayment { get; set; }
    }

    // --- Payer Mix ---
    public class PayerMixReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<PayerMixChartDto> ChartData { get; set; } = new();
        public List<PayerMixRowDto> Rows { get; set; } = new();
    }
    public class PayerMixChartDto
    {
        public string PayerName { get; set; }
        public int VisitCount { get; set; }
        public decimal Percentage { get; set; }
    }
    public class PayerMixRowDto
    {
        public string PayerName { get; set; }
        public int ActivePatients { get; set; }
        public int TotalVisits { get; set; }
        public decimal TotalBilled { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal AvgReimbursement { get; set; }
        public decimal Percentage { get; set; }
    }

    // --- Provider Productivity ---
    public class ProviderProductivityReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<ProviderProductivityChartDto> ChartData { get; set; } = new();
        public List<ProviderProductivityRowDto> Rows { get; set; } = new();
    }
    public class ProviderProductivityChartDto
    {
        public string ProviderName { get; set; }
        public int CompletedVisits { get; set; }
        public int EncountersSigned { get; set; }
        public int NoShows { get; set; }
    }
    public class ProviderProductivityRowDto
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string Specialty { get; set; }
        public int TotalScheduled { get; set; }
        public int Completed { get; set; }
        public int NoShows { get; set; }
        public decimal CompletionRate { get; set; }
        public int EncountersCreated { get; set; }
        public int EncountersSigned { get; set; }
        public int PrescriptionsWritten { get; set; }
        public int OrdersPlaced { get; set; }
        public int ActivePatients { get; set; }
    }

    // --- Patient Panel ---
    public class PatientPanelReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<PatientPanelChartDto> ChartData { get; set; } = new();
        public List<PatientPanelRowDto> Rows { get; set; } = new();
    }
    public class PatientPanelChartDto
    {
        public string AgeGroup { get; set; }
        public int TotalPatients { get; set; }
    }
    public class PatientPanelRowDto
    {
        public string AgeGroup { get; set; }
        public int TotalPatients { get; set; }
        public int SeenInPeriod { get; set; }
        public int NotSeenIn90Days { get; set; }
        public decimal AvgVisitsPerPatient { get; set; }
    }

    // --- Copay Collection ---
    public class CopayCollectionReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<CopayCollectionChartDto> ChartData { get; set; } = new();
        public List<CopayCollectionRowDto> Rows { get; set; } = new();
    }
    public class CopayCollectionChartDto
    {
        public string Label { get; set; }
        public decimal TotalOwed { get; set; }
        public decimal TotalCollected { get; set; }
    }
    public class CopayCollectionRowDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public decimal TotalCharges { get; set; }
        public decimal InsurancePaid { get; set; }
        public decimal PatientPaid { get; set; }
        public decimal Adjustments { get; set; }
        public decimal Balance { get; set; }
        public bool HasInstallmentPlan { get; set; }
        public string LastPaymentDate { get; set; }
    }

    // --- A/R Aging ---
    public class ArAgingReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<ArAgingChartDto> ChartData { get; set; } = new();
        public List<ArAgingRowDto> Rows { get; set; } = new();
    }
    public class ArAgingChartDto
    {
        public string Bucket { get; set; }
        public decimal Amount { get; set; }
        public int ClaimCount { get; set; }
    }
    public class ArAgingRowDto
    {
        public string PayerName { get; set; }
        public decimal Current { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Over90 { get; set; }
        public decimal Total { get; set; }
        public int ClaimCount { get; set; }
    }

    // --- Payment Analysis ---
    public class PaymentAnalysisReportDto
    {
        public List<ReportKpiDto> Kpis { get; set; } = new();
        public List<PaymentAnalysisChartDto> ChartData { get; set; } = new();
        public List<PaymentAnalysisRowDto> Rows { get; set; } = new();
    }
    public class PaymentAnalysisChartDto
    {
        public string Label { get; set; }
        public decimal Amount { get; set; }
    }
    public class PaymentAnalysisRowDto
    {
        public string PaymentType { get; set; }
        public string PaymentMethod { get; set; }
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AvgAmount { get; set; }
        public decimal Percentage { get; set; }
    }

    // ============================================
    // PATIENT VALIDATION DTOs
    // ============================================

    /// <summary>
    /// Validation status for a patient profile
    /// </summary>
    public class PatientValidationStatusDto
    {
        public int PatientId { get; set; }
        public bool IsComplete { get; set; }
        public int MissingFieldsCount { get; set; }
        /// <summary>
        /// List of section names with missing information
        /// e.g., ["Emergency Contact", "Insurance"]
        /// </summary>
        public List<string> MissingSections { get; set; } = new();
        /// <summary>
        /// Detailed missing fields by section
        /// </summary>
        public Dictionary<string, List<string>> MissingFieldsDetails { get; set; } = new();
        /// <summary>
        /// The first section with missing information (for click-to-fix navigation)
        /// </summary>
        public string FirstIncompleteSection { get; set; }
        /// <summary>
        /// User-friendly message describing validation status
        /// </summary>
        public string StatusMessage { get; set; }
        public DateTime? LastValidatedAt { get; set; }
    }

    /// <summary>
    /// Result from bulk validation endpoint
    /// </summary>
    public class BulkValidationResultDto
    {
        public bool Success { get; set; }
        public int TotalPatientsChecked { get; set; }
        public int CompleteProfilesCount { get; set; }
        public int IncompleteProfilesCount { get; set; }
        public DateTime ValidationTimestamp { get; set; }
        public string Message { get; set; }
        /// <summary>
        /// Processing duration in milliseconds
        /// </summary>
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// Summary of validation status for dashboard widget
    /// </summary>
    public class ValidationSummaryDto
    {
        public int TotalPatients { get; set; }
        public int CompleteProfiles { get; set; }
        public int IncompleteProfiles { get; set; }
        public decimal CompletionPercentage => TotalPatients > 0
            ? Math.Round((decimal)CompleteProfiles / TotalPatients * 100, 1)
            : 100;
        public DateTime? LastBulkValidationAt { get; set; }
    }

    /// <summary>
    /// Request to validate a specific patient
    /// </summary>
    public class PatientValidationRequestDto
    {
        public int PatientId { get; set; }
        /// <summary>
        /// Source of the validation (Create, Update, Manual)
        /// </summary>
        public string Source { get; set; } = "Manual";
    }

    /// <summary>
    /// Missing field information with user-friendly label
    /// </summary>
    public class MissingFieldDto
    {
        public string FieldName { get; set; }
        public string FriendlyLabel { get; set; }
        public string Section { get; set; }
    }

    /// <summary>
    /// Validation section with missing fields
    /// </summary>
    public class ValidationSectionDto
    {
        public string SectionName { get; set; }
        public string FriendlyName { get; set; }
        public bool IsComplete { get; set; }
        public List<MissingFieldDto> MissingFields { get; set; } = new();
    }

    /// <summary>
    /// Complete validation report for a patient
    /// </summary>
    public class PatientValidationReportDto
    {
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public bool IsComplete { get; set; }
        public List<ValidationSectionDto> Sections { get; set; } = new();
        public string OverallStatus { get; set; }
        public DateTime ValidatedAt { get; set; }
    }

    // ============================================
    // LOCATION DTOs (Multi-Location System)
    // ============================================

    /// <summary>
    /// DTO for listing locations in dropdowns and lists
    /// </summary>
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
        public int PatientCount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string FacilityNpi { get; set; }
        public string PlaceOfServiceCode { get; set; }
        /// <summary>
        /// Per-location feature flag: enables the Longevity section in patient intake
        /// and surfaces longevity data in the Patient Profile and Encounter intake panel.
        /// </summary>
        public bool EnableLongevity { get; set; }
    }

    /// <summary>
    /// DTO for location dropdown selection
    /// </summary>
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

    /// <summary>
    /// DTO for creating a new location
    /// </summary>
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

    /// <summary>
    /// DTO for updating a location
    /// </summary>
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

    /// <summary>
    /// DTO for location detail view
    /// </summary>
    public class LocationDetailDto : LocationListDto
    {
        public string TenantName { get; set; }
        public int ProviderCount { get; set; }
        public int AppointmentCount { get; set; }
        /// <summary>
        /// Full timezone display name (e.g., "Eastern Standard Time")
        /// </summary>
        public string TimeZoneDisplayName { get; set; }
        /// <summary>
        /// Current UTC offset for the timezone (e.g., "-05:00")
        /// </summary>
        public string UtcOffset { get; set; }
    }

    /// <summary>
    /// DTO for location selection in user login response
    /// </summary>
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

    /// <summary>
    /// DTO for switching location
    /// </summary>
    public class SwitchLocationDto
    {
        public int LocationId { get; set; }
    }

    /// <summary>
    /// Response DTO after switching location
    /// </summary>
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

    // ============================================
    // MEDICAL LIEN TEMPLATE DTOs
    // ============================================

    /// <summary>
    /// DTO for Medical Lien Template list display
    /// </summary>
    public class MedicalLienTemplateListDto
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; }
        public int TenantId { get; set; }
        public string TenantName { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for Medical Lien Template with full content
    /// </summary>
    public class MedicalLienTemplateDto
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; }
        public int TenantId { get; set; }
        public string TenantName { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; }
        public string HtmlContent { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for creating a new Medical Lien Template
    /// </summary>
    public class MedicalLienTemplateCreateDto
    {
        public int TenantId { get; set; }
        public int LocationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; }
        public string HtmlContent { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// DTO for updating a Medical Lien Template
    /// </summary>
    public class MedicalLienTemplateUpdateDto
    {
        public int? TenantId { get; set; }
        public int? LocationId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string HtmlContent { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>
    /// Supported placeholders for Medical Lien Form templates
    /// </summary>
    public static class MedicalLienPlaceholders
    {
        public const string PatientName = "{{PatientName}}";
        public const string PatientAddress = "{{PatientAddress}}";
        public const string DateOfInjury = "{{DateOfInjury}}";
        public const string AttorneyName = "{{AttorneyName}}";
        public const string AttorneyAddress = "{{AttorneyAddress}}";
        public const string AttorneyPhone = "{{AttorneyPhone}}";
        public const string AttorneyEmail = "{{AttorneyEmail}}";
        public const string ProviderName = "{{ProviderName}}";
        public const string ProviderSignature = "{{ProviderSignature}}";
        public const string ClinicName = "{{ClinicName}}";
        public const string ClinicAddress = "{{ClinicAddress}}";
        public const string ClinicPhone = "{{ClinicPhone}}";
        public const string ClinicEmail = "{{ClinicEmail}}";
        public const string TodayDate = "{{TodayDate}}";

        /// <summary>
        /// Get all available placeholders with descriptions for UI display
        /// </summary>
        public static Dictionary<string, string> GetAllPlaceholders()
        {
            return new Dictionary<string, string>
            {
                { PatientName, "Patient's full name" },
                { PatientAddress, "Patient's full address" },
                { DateOfInjury, "Date of injury from patient record" },
                { AttorneyName, "Attorney name from insurance section" },
                { AttorneyAddress, "Attorney address from insurance section" },
                { AttorneyPhone, "Attorney phone from insurance section" },
                { AttorneyEmail, "Attorney email from insurance section" },
                { ProviderName, "Selected provider/therapist name" },
                { ProviderSignature, "Provider's signature image (or placeholder)" },
                { ClinicName, "Clinic/tenant name" },
                { ClinicAddress, "Clinic's full address" },
                { ClinicPhone, "Clinic's phone number" },
                { ClinicEmail, "Clinic's email address" },
                { TodayDate, "Current date when form is generated" }
            };
        }
    }

    // ============================================
    // INTERNAL MESSAGING SYSTEM DTOs
    // ============================================

    /// <summary>
    /// User info for messaging (simplified user data)
    /// </summary>
    public class MessagingUserDto
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}";
        public string Email { get; set; }
        public int? Role { get; set; }
        public string RoleName => Role switch
        {
            0 => "Super Admin",
            1 => "Clinic Admin",
            2 => "Clinician",
            3 => "Admin",
            _ => "User"
        };
        public bool IsOnline { get; set; }
        public DateTime? LastActiveAt { get; set; }
        /// <summary>
        /// Avatar initials (first letter of first and last name)
        /// </summary>
        public string Initials => $"{(FirstName?.Length > 0 ? FirstName[0] : ' ')}{(LastName?.Length > 0 ? LastName[0] : ' ')}".ToUpper();
    }

    /// <summary>
    /// Conversation list item for displaying conversation list
    /// </summary>
    public class ConversationListDto
    {
        public int ConversationId { get; set; }
        /// <summary>
        /// The other user in the conversation (not the current user)
        /// </summary>
        public MessagingUserDto OtherUser { get; set; }
        /// <summary>
        /// Preview of the last message (decrypted, truncated)
        /// </summary>
        public string LastMessagePreview { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public int? LastMessageSenderId { get; set; }
        /// <summary>
        /// Number of unread messages for the current user
        /// </summary>
        public int UnreadCount { get; set; }
        /// <summary>
        /// Type of the last message (Text, VoiceNote, File)
        /// </summary>
        public int? LastMessageType { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Message DTO for displaying in chat window
    /// </summary>
    public class MessageDto
    {
        public int MessageId { get; set; }
        public int ConversationId { get; set; }
        public int SenderId { get; set; }
        public string SenderName { get; set; }
        public int RecipientId { get; set; }
        /// <summary>
        /// Decrypted message text content
        /// </summary>
        public string MessageText { get; set; }
        /// <summary>
        /// Type of message: 0=Text, 1=VoiceNote, 2=File
        /// </summary>
        public int MessageType { get; set; }
        public string MessageTypeName => MessageType switch
        {
            0 => "Text",
            1 => "VoiceNote",
            2 => "File",
            _ => "Unknown"
        };
        /// <summary>
        /// Signed URL for downloading the file (voice note or attachment)
        /// </summary>
        public string FileUrl { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        /// <summary>
        /// Formatted file size (e.g., "1.5 MB")
        /// </summary>
        public string FileSizeFormatted => FormatFileSize(FileSize);
        public string FileMimeType { get; set; }
        /// <summary>
        /// Duration in seconds for voice notes
        /// </summary>
        public decimal? FileDurationSeconds { get; set; }
        /// <summary>
        /// Formatted duration for voice notes (e.g., "1:23")
        /// </summary>
        public string DurationFormatted => FormatDuration(FileDurationSeconds);
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// Formatted timestamp (e.g., "2:30 PM" for today, "Yesterday" for yesterday, "Jan 15" for older)
        /// </summary>
        public string CreatedAtFormatted { get; set; }
        /// <summary>
        /// Whether the current user is the sender
        /// </summary>
        public bool IsMine { get; set; }

        private static string FormatFileSize(long? bytes)
        {
            if (!bytes.HasValue || bytes.Value == 0) return "";
            var sizes = new[] { "B", "KB", "MB", "GB" };
            var order = 0;
            double size = bytes.Value;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private static string FormatDuration(decimal? seconds)
        {
            if (!seconds.HasValue) return "";
            var totalSeconds = (int)seconds.Value;
            var minutes = totalSeconds / 60;
            var secs = totalSeconds % 60;
            return $"{minutes}:{secs:D2}";
        }
    }

    /// <summary>
    /// DTO for sending a new text message
    /// </summary>
    public class SendMessageDto
    {
        /// <summary>
        /// User ID of the recipient
        /// </summary>
        public int RecipientId { get; set; }
        /// <summary>
        /// The message text content (will be encrypted)
        /// </summary>
        public string MessageText { get; set; }
    }

    /// <summary>
    /// DTO for uploading a voice note
    /// </summary>
    public class SendVoiceNoteDto
    {
        /// <summary>
        /// User ID of the recipient
        /// </summary>
        public int RecipientId { get; set; }
        /// <summary>
        /// Duration of the voice note in seconds
        /// </summary>
        public decimal DurationSeconds { get; set; }
        /// <summary>
        /// Optional caption for the voice note
        /// </summary>
        public string Caption { get; set; }
    }

    /// <summary>
    /// DTO for uploading a file attachment
    /// </summary>
    public class SendFileAttachmentDto
    {
        /// <summary>
        /// User ID of the recipient
        /// </summary>
        public int RecipientId { get; set; }
        /// <summary>
        /// Optional caption/description for the file
        /// </summary>
        public string Caption { get; set; }
    }

    /// <summary>
    /// Response after sending a message (returned to sender)
    /// </summary>
    public class MessageSentResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int? MessageId { get; set; }
        public int? ConversationId { get; set; }
        public DateTime? CreatedAt { get; set; }
        /// <summary>
        /// Signed URL for accessing the file (for voice notes and attachments)
        /// </summary>
        public string FileUrl { get; set; }
    }

    /// <summary>
    /// Real-time notification sent via SignalR when a new message is received
    /// </summary>
    public class NewMessageNotification
    {
        public int MessageId { get; set; }
        public int ConversationId { get; set; }
        public int SenderId { get; set; }
        public string SenderName { get; set; }
        public string MessagePreview { get; set; }
        public int MessageType { get; set; }
        public string FileName { get; set; }
        public decimal? DurationSeconds { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Typing indicator notification sent via SignalR
    /// </summary>
    public class TypingIndicatorNotification
    {
        public int ConversationId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public bool IsTyping { get; set; }
    }

    /// <summary>
    /// Read receipt notification sent via SignalR
    /// </summary>
    public class MessageReadNotification
    {
        public int ConversationId { get; set; }
        public int MessageId { get; set; }
        public int ReadByUserId { get; set; }
        public DateTime ReadAt { get; set; }
    }

    /// <summary>
    /// User presence (online/offline status) notification
    /// </summary>
    public class UserPresenceNotification
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastActiveAt { get; set; }
    }

    /// <summary>
    /// Summary of unread messages for badge display
    /// </summary>
    public class UnreadMessagesSummaryDto
    {
        /// <summary>
        /// Total unread messages across all conversations
        /// </summary>
        public int TotalUnreadCount { get; set; }
        /// <summary>
        /// Number of conversations with unread messages
        /// </summary>
        public int UnreadConversationsCount { get; set; }
    }

    /// <summary>
    /// Request for loading message history with pagination
    /// </summary>
    public class MessageHistoryRequestDto
    {
        /// <summary>
        /// Load messages before this message ID (for infinite scroll up)
        /// </summary>
        public int? BeforeMessageId { get; set; }
        /// <summary>
        /// Load messages after this message ID (for infinite scroll down)
        /// </summary>
        public int? AfterMessageId { get; set; }
        /// <summary>
        /// Number of messages to load (default 50)
        /// </summary>
        public int PageSize { get; set; } = 50;
    }

    /// <summary>
    /// Response with paginated message history
    /// </summary>
    public class MessageHistoryResponseDto
    {
        public List<MessageDto> Messages { get; set; } = new();
        public bool HasMoreOlder { get; set; }
        public bool HasMoreNewer { get; set; }
        public int? OldestMessageId { get; set; }
        public int? NewestMessageId { get; set; }
    }

    /// <summary>
    /// Search messages request
    /// </summary>
    public class SearchMessagesRequestDto
    {
        /// <summary>
        /// Search query text
        /// </summary>
        public string Query { get; set; }
        /// <summary>
        /// Optional: limit search to specific conversation
        /// </summary>
        public int? ConversationId { get; set; }
        /// <summary>
        /// Maximum results (default 20)
        /// </summary>
        public int MaxResults { get; set; } = 20;
    }

    /// <summary>
    /// Search result item
    /// </summary>
    public class MessageSearchResultDto
    {
        public int MessageId { get; set; }
        public int ConversationId { get; set; }
        public string OtherUserName { get; set; }
        public string MessagePreview { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>
        /// Highlighted match snippet
        /// </summary>
        public string HighlightedText { get; set; }
    }

    // ============================================
    // INTERNAL MEDICINE DTOs
    // ============================================

    // --- Encounter ---
    public class EncounterDto
    {
        public int EncounterId { get; set; }
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        /// <summary>True when the provider has a profile picture uploaded.
        /// Lets the UI render a real avatar instead of just initials.</summary>
        public bool ProviderHasProfilePicture { get; set; }
        public int? AppointmentId { get; set; }
        public DateOnly EncounterDate { get; set; }
        public string ChiefComplaint { get; set; }
        public string HistoryOfPresentIllness { get; set; }
        public string ReviewOfSystems { get; set; }
        public string PhysicalExam { get; set; }
        public string Assessment { get; set; }
        public string Plan { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SignedAt { get; set; }
        // Related data counts for visit history display
        public int ClinicalNoteCount { get; set; }
        public string ClinicalNoteStatus { get; set; }
        public int VitalsCount { get; set; }
        public string AppointmentType { get; set; }
        // Telehealth fields from associated appointment
        public bool? IsTelehealth { get; set; }
        public string TelehealthUrl { get; set; }
        public string CptSelections { get; set; }
        public string IcdSelections { get; set; }

        /// <summary>
        /// Patient-friendly visit summary (decrypted plaintext when populated).
        /// Generated by EncounterSummaryManager on Close Encounter / Amendment / Addendum.
        /// Stored encrypted at rest; decrypted by EncounterService before this DTO is returned.
        /// Spec: rules/technical/encounter-summary.md
        /// </summary>
        public string SummaryText { get; set; }
    }

    /// <summary>
    /// Patient-friendly visit summary derived from signed clinical notes
    /// (PHI scrubbed) and persisted on the Encounter. Generated at Close
    /// Encounter and re-generated on amendment / addendum.
    /// Spec: rules/technical/encounter-summary.md
    /// </summary>
    public class EncounterSummaryDto
    {
        public int EncounterId { get; set; }
        public string SummaryText { get; set; }
        public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);
    }

    public class EncounterCreateDto
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? AppointmentId { get; set; }
        public DateOnly EncounterDate { get; set; }
        public string ChiefComplaint { get; set; }
    }

    public class EncounterUpdateDto
    {
        public string ChiefComplaint { get; set; }
        public string HistoryOfPresentIllness { get; set; }
        public string ReviewOfSystems { get; set; }
        public string PhysicalExam { get; set; }
        public string Assessment { get; set; }
        public string Plan { get; set; }
        public string CptSelections { get; set; }
        public string IcdSelections { get; set; }
    }

    // --- Patient Problem ---
    public class PatientProblemDto
    {
        public int PatientProblemId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public string IcdCode { get; set; }
        public string Description { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public DateOnly? OnsetDate { get; set; }
        public DateOnly? ResolvedDate { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PatientProblemCreateDto
    {
        public int? EncounterId { get; set; }
        public string IcdCode { get; set; }
        public string Description { get; set; }
        public DateOnly? OnsetDate { get; set; }
        public string Notes { get; set; }
    }

    public class PatientProblemUpdateDto
    {
        public string IcdCode { get; set; }
        public string Description { get; set; }
        public int? Status { get; set; }
        public DateOnly? OnsetDate { get; set; }
        public DateOnly? ResolvedDate { get; set; }
        public string Notes { get; set; }
    }

    // --- Patient Allergy ---
    public class PatientAllergyDto
    {
        public int PatientAllergyId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public string AllergenName { get; set; }
        public int Type { get; set; }
        public string TypeName { get; set; }
        public string Reaction { get; set; }
        public int Severity { get; set; }
        public string SeverityName { get; set; }
        public DateOnly? OnsetDate { get; set; }
        public bool IsActive { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PatientAllergyCreateDto
    {
        public int? EncounterId { get; set; }
        public string AllergenName { get; set; }
        public int Type { get; set; }
        public string Reaction { get; set; }
        public int Severity { get; set; }
        public DateOnly? OnsetDate { get; set; }
        public string Notes { get; set; }
    }

    public class PatientAllergyUpdateDto
    {
        public string AllergenName { get; set; }
        public int? Type { get; set; }
        public string Reaction { get; set; }
        public int? Severity { get; set; }
        public DateOnly? OnsetDate { get; set; }
        public bool? IsActive { get; set; }
        public string Notes { get; set; }
    }

    // --- Patient Medication ---
    public class PatientMedicationDto
    {
        public int PatientMedicationId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public string DrugName { get; set; }
        public string Dosage { get; set; }
        public string Form { get; set; }
        public string Route { get; set; }
        public string Frequency { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public int? PrescribedByProviderId { get; set; }
        public string PrescribedByProviderName { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PatientMedicationCreateDto
    {
        public int? EncounterId { get; set; }
        public string DrugName { get; set; }
        public string Dosage { get; set; }
        public string Form { get; set; }
        public string Route { get; set; }
        public string Frequency { get; set; }
        public DateOnly? StartDate { get; set; }
        public int? PrescribedByProviderId { get; set; }
        public string Notes { get; set; }
    }

    public class PatientMedicationUpdateDto
    {
        public string DrugName { get; set; }
        public string Dosage { get; set; }
        public string Form { get; set; }
        public string Route { get; set; }
        public string Frequency { get; set; }
        public int? Status { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string Notes { get; set; }
    }

    // --- Patient Vital ---
    public class PatientVitalDto
    {
        public int PatientVitalId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public DateTime RecordedAt { get; set; }
        public int? SystolicBp { get; set; }
        public int? DiastolicBp { get; set; }
        public string BloodPressure { get; set; } // formatted "120/80"
        public int? HeartRate { get; set; }
        public int? RespiratoryRate { get; set; }
        public decimal? Temperature { get; set; }
        public decimal? SpO2 { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Height { get; set; }
        public decimal? Bmi { get; set; }
        public string Notes { get; set; }
    }

    public class PatientVitalCreateDto
    {
        public int? EncounterId { get; set; }
        public int? SystolicBp { get; set; }
        public int? DiastolicBp { get; set; }
        public int? HeartRate { get; set; }
        public int? RespiratoryRate { get; set; }
        public decimal? Temperature { get; set; }
        public decimal? SpO2 { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Height { get; set; }
        public string Notes { get; set; }
    }

    // --- Patient Immunization ---
    public class PatientImmunizationDto
    {
        public int PatientImmunizationId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public string VaccineName { get; set; }
        public string CvxCode { get; set; }
        public DateOnly AdministeredDate { get; set; }
        public string LotNumber { get; set; }
        public string Manufacturer { get; set; }
        public string Site { get; set; }
        public string AdministeredByProviderName { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PatientImmunizationCreateDto
    {
        public int? EncounterId { get; set; }
        public string VaccineName { get; set; }
        public string CvxCode { get; set; }
        public DateOnly AdministeredDate { get; set; }
        public string LotNumber { get; set; }
        public string Manufacturer { get; set; }
        public string Site { get; set; }
        public int? AdministeredByProviderId { get; set; }
        public string Notes { get; set; }
    }

    // --- Patient Family History ---
    public class PatientFamilyHistoryDto
    {
        public int PatientFamilyHistoryId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public string Relation { get; set; }
        public string Condition { get; set; }
        public int? AgeAtOnset { get; set; }
        public bool? IsDeceased { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PatientFamilyHistoryCreateDto
    {
        public int? EncounterId { get; set; }
        public string Relation { get; set; }
        public string Condition { get; set; }
        public int? AgeAtOnset { get; set; }
        public bool? IsDeceased { get; set; }
        public string Notes { get; set; }
    }

    // --- Patient Social History ---
    public class PatientSocialHistoryDto
    {
        public int PatientSocialHistoryId { get; set; }
        public int PatientId { get; set; }
        public int? EncounterId { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PatientSocialHistoryCreateDto
    {
        public int? EncounterId { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
    }

    // --- Treatment Plan ---
    public class TreatmentPlanDto
    {
        public int TreatmentPlanId { get; set; }
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string ConditionName { get; set; }
        public string IcdCode { get; set; }
        public string Goals { get; set; }
        public int? FollowUpIntervalDays { get; set; }
        public DateOnly? NextFollowUp { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TreatmentPlanCreateDto
    {
        public int ProviderId { get; set; }
        public string ConditionName { get; set; }
        public string IcdCode { get; set; }
        public string Goals { get; set; }
        public int? FollowUpIntervalDays { get; set; }
        public DateOnly? NextFollowUp { get; set; }
        public string Notes { get; set; }
    }

    public class TreatmentPlanUpdateDto
    {
        public string ConditionName { get; set; }
        public string IcdCode { get; set; }
        public string Goals { get; set; }
        public int? FollowUpIntervalDays { get; set; }
        public DateOnly? NextFollowUp { get; set; }
        public int? Status { get; set; }
        public string Notes { get; set; }
    }

    // ============================================
    // E-PRESCRIBING DTOs
    // ============================================

    // --- Prescription ---
    public class PrescriptionDto
    {
        public int PrescriptionId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? EncounterId { get; set; }
        public string DrugName { get; set; }
        public string GenericName { get; set; }
        public string NDCCode { get; set; }
        public string RxNormCode { get; set; }
        public string Strength { get; set; }
        public int DosageForm { get; set; }
        public string DosageFormName { get; set; }
        public decimal Quantity { get; set; }
        public int DaysSupply { get; set; }
        public string DoseAmount { get; set; }
        public string DoseUnit { get; set; }
        public int Route { get; set; }
        public string RouteName { get; set; }
        public int Frequency { get; set; }
        public string FrequencyName { get; set; }
        public string DirectionsFreeText { get; set; }
        public int Refills { get; set; }
        public bool DAW { get; set; }
        public string PharmacyName { get; set; }
        public string PharmacyPhone { get; set; }
        public string PharmacyAddress { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public bool IsControlledSubstance { get; set; }
        public int? DEASchedule { get; set; }
        public string DiagnosisCode { get; set; }
        public DateOnly PrescribedDate { get; set; }
        public DateOnly? ExpirationDate { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PrescriptionCreateDto
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? EncounterId { get; set; }
        public string DrugName { get; set; }
        public string GenericName { get; set; }
        public string NDCCode { get; set; }
        public string RxNormCode { get; set; }
        public string Strength { get; set; }
        public int DosageForm { get; set; }
        public decimal Quantity { get; set; }
        public int DaysSupply { get; set; }
        public string DoseAmount { get; set; }
        public string DoseUnit { get; set; }
        public int Route { get; set; }
        public int Frequency { get; set; }
        public string DirectionsFreeText { get; set; }
        public int Refills { get; set; }
        public bool DAW { get; set; }
        public int? PharmacyId { get; set; }
        public string PharmacyName { get; set; }
        public string PharmacyPhone { get; set; }
        public string PharmacyAddress { get; set; }
        public int Status { get; set; }
        public bool IsControlledSubstance { get; set; }
        public int? DEASchedule { get; set; }
        public string DiagnosisCode { get; set; }
        public string Notes { get; set; }
    }

    public class PrescriptionUpdateDto
    {
        // Status-only update (for Sign & Send)
        public int? Status { get; set; }

        // Full draft edit fields (all nullable so partial updates work)
        public int? PatientId { get; set; }
        public int? ProviderId { get; set; }
        public string DrugName { get; set; }
        public string GenericName { get; set; }
        public string NDCCode { get; set; }
        public string Strength { get; set; }
        public int? DosageForm { get; set; }
        public decimal? Quantity { get; set; }
        public int? DaysSupply { get; set; }
        public string DoseAmount { get; set; }
        public string DoseUnit { get; set; }
        public int? Route { get; set; }
        public int? Frequency { get; set; }
        public string DirectionsFreeText { get; set; }
        public int? Refills { get; set; }
        public bool? DAW { get; set; }
        public string PharmacyName { get; set; }
        public string PharmacyPhone { get; set; }
        public string PharmacyAddress { get; set; }
        public bool? IsControlledSubstance { get; set; }
        public int? DEASchedule { get; set; }
        public string DiagnosisCode { get; set; }
        public string Notes { get; set; }
    }

    // --- Pharmacy ---
    public class PharmacyDto
    {
        public int PharmacyId { get; set; }
        public string Name { get; set; }
        public string NCPDP { get; set; }
        public string NPI { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public string FullAddress { get; set; }
        public bool IsActive { get; set; }
    }

    // --- Drug Database ---
    public class DrugSearchResultDto
    {
        public int DrugId { get; set; }
        public string NDCCode { get; set; }
        public string BrandName { get; set; }
        public string GenericName { get; set; }
        public string Strength { get; set; }
        public int DosageForm { get; set; }
        public string DosageFormName { get; set; }
        public int Route { get; set; }
        public string RouteName { get; set; }
        public int? DEASchedule { get; set; }
        public string CommonDirections { get; set; }
        public string Warnings { get; set; }
        public string DisplayName { get; set; }
    }

    // ============================================
    // ORDERS MODULE DTOs
    // ============================================

    public class OrderDto
    {
        public int OrderId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientMRN { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? EncounterId { get; set; }
        public int OrderType { get; set; }
        public string OrderTypeName { get; set; }
        public int Status { get; set; }
        public string StatusName { get; set; }
        public int Priority { get; set; }
        public string PriorityName { get; set; }
        public DateOnly OrderDate { get; set; }
        public string DiagnosisCode { get; set; }
        public string ClinicalIndication { get; set; }
        public string Notes { get; set; }

        // Lab
        public string LabPanelName { get; set; }
        public bool? FastingRequired { get; set; }
        public string SpecimenType { get; set; }

        // Imaging
        public int? Modality { get; set; }
        public string ModalityName { get; set; }
        public string BodyPart { get; set; }
        public bool? ContrastRequired { get; set; }
        public string ImagingFacility { get; set; }

        // Referral
        public string ReferralSpecialty { get; set; }
        public string ReferredToProvider { get; set; }
        public string ReferredToFacility { get; set; }
        public string ReferredToPhone { get; set; }
        public string ReferredToFax { get; set; }
        public string ReferralReason { get; set; }
        public int? ReferralUrgency { get; set; }
        public string ReferralUrgencyName { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public List<OrderResultDto> Results { get; set; }
    }

    public class OrderCreateDto
    {
        public int PatientId { get; set; }
        public int ProviderId { get; set; }
        public int? EncounterId { get; set; }
        public int OrderType { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
        public string DiagnosisCode { get; set; }
        public string ClinicalIndication { get; set; }
        public string Notes { get; set; }

        // Lab
        public string LabPanelName { get; set; }
        public bool? FastingRequired { get; set; }
        public string SpecimenType { get; set; }

        // Imaging
        public int? Modality { get; set; }
        public string BodyPart { get; set; }
        public bool? ContrastRequired { get; set; }
        public string ImagingFacility { get; set; }

        // Referral
        public string ReferralSpecialty { get; set; }
        public string ReferredToProvider { get; set; }
        public string ReferredToFacility { get; set; }
        public string ReferredToPhone { get; set; }
        public string ReferredToFax { get; set; }
        public string ReferralReason { get; set; }
        public int? ReferralUrgency { get; set; }
    }

    public class OrderUpdateDto
    {
        public int? Status { get; set; }
        public int? Priority { get; set; }
        public string DiagnosisCode { get; set; }
        public string ClinicalIndication { get; set; }
        public string Notes { get; set; }

        // Lab
        public string LabPanelName { get; set; }
        public bool? FastingRequired { get; set; }
        public string SpecimenType { get; set; }

        // Imaging
        public int? Modality { get; set; }
        public string BodyPart { get; set; }
        public bool? ContrastRequired { get; set; }
        public string ImagingFacility { get; set; }

        // Referral
        public string ReferralSpecialty { get; set; }
        public string ReferredToProvider { get; set; }
        public string ReferredToFacility { get; set; }
        public string ReferredToPhone { get; set; }
        public string ReferredToFax { get; set; }
        public string ReferralReason { get; set; }
        public int? ReferralUrgency { get; set; }
    }

    public class OrderResultDto
    {
        public int OrderResultId { get; set; }
        public int OrderId { get; set; }
        public string TestName { get; set; }
        public string ResultValue { get; set; }
        public string ResultUnit { get; set; }
        public string ReferenceRange { get; set; }
        public bool? IsAbnormal { get; set; }
        public string FindingsText { get; set; }
        public DateTime? ResultDate { get; set; }
    }

    /// <summary>
    /// One-shot payload for the lab-order print template. Joins order + patient
    /// + provider + tenant + active location and pre-formats all date/time
    /// strings in the location's IANA timezone (per the strict timezone rule).
    /// </summary>
    public class LabOrderPrintDto
    {
        // Order
        public int OrderId { get; set; }
        public string AccessionNumber { get; set; }         // computed: LAB-{TenantId}-{OrderId:D7}
        public string OrderDateFormatted { get; set; }      // "09-Feb-2026"
        public string ReportedDateFormatted { get; set; }   // "10-Feb-2026 09:42 CST"  (CompletedAt; null until completed)
        public string PriorityName { get; set; }
        public string StatusName { get; set; }
        public string DiagnosisCode { get; set; }
        public string DiagnosisDescription { get; set; }
        public string ClinicalIndication { get; set; }
        public string LabPanelName { get; set; }
        public string SpecimenType { get; set; }
        public bool? FastingRequired { get; set; }
        public string Notes { get; set; }
        public List<LabResultPrintDto> Results { get; set; } = new();

        // Patient
        public string PatientNameFormal { get; set; }       // "WILLIAMS, ROBERT"
        public string PatientMrn { get; set; }
        public string PatientDobFormatted { get; set; }     // "14-May-1962"
        public int PatientAge { get; set; }
        public string PatientGender { get; set; }
        public string PatientAddressLine { get; set; }      // "2200 W. Diversey Pkwy, Chicago, IL 60647"
        public string PatientPhone { get; set; }

        // Ordering Provider
        public string ProviderName { get; set; }            // "Sarah Johnson, MD"
        public string ProviderNpi { get; set; }
        public string ProviderCredentials { get; set; }
        public string ProviderSpecialty { get; set; }

        // Clinic (tenant + location)
        public int TenantId { get; set; }                   // for logo URL: /api/tenants/{id}/logo
        public bool HasLogo { get; set; }
        public string ClinicName { get; set; }
        public string ClinicTagline { get; set; }           // location name e.g. "North Loop Clinic", or empty
        public string ClinicAddressLine { get; set; }       // "123 N. Michigan Avenue, Suite 500"
        public string ClinicCityStateZip { get; set; }      // "Chicago, IL 60601"
        public string ClinicPhone { get; set; }
        public string ClinicEmail { get; set; }
        public string ClinicNpi { get; set; }               // tenant NPI (only if set)
        public string ClinicTaxId { get; set; }             // tenant Tax ID (only if set)
        public string TimeZoneAbbreviation { get; set; }    // "CST", "EST" — for display
        public string TimeZoneId { get; set; }              // IANA, e.g. "America/Chicago"

        // Audit
        public string PrintedByName { get; set; }
        public string PrintedAtFormatted { get; set; }      // "15-May-2026 09:38 PM CST"
        public string VerifiedAtFormatted { get; set; }     // "10-Feb-2026 09:42 AM CST" (CompletedAt); null if not completed
    }

    public class LabResultPrintDto
    {
        public string TestName { get; set; }
        public string ResultValue { get; set; }
        public string ResultUnit { get; set; }
        public string ReferenceRange { get; set; }
        public bool IsAbnormal { get; set; }
        public string FlagText { get; set; }                 // "H · Abnormal" / "Normal"
    }

    public class OrderResultCreateDto
    {
        public int OrderId { get; set; }
        public List<OrderResultItemDto> Results { get; set; }
    }

    public class OrderResultItemDto
    {
        public string TestName { get; set; }
        public string ResultValue { get; set; }
        public string ResultUnit { get; set; }
        public string ReferenceRange { get; set; }
        public bool? IsAbnormal { get; set; }
        public string FindingsText { get; set; }
    }

    public class LabTestCatalogDto
    {
        public int LabTestId { get; set; }
        public string PanelName { get; set; }
        public string TestName { get; set; }
        public string TestCode { get; set; }
        public string Unit { get; set; }
        public string ReferenceRange { get; set; }
        public string SpecimenType { get; set; }
        public int DisplayOrder { get; set; }
    }

    public class OrdersPendingCountDto
    {
        public int PendingLabResults { get; set; }
        public int PendingImagingResults { get; set; }
        public int PendingReferrals { get; set; }
        public int TotalPending { get; set; }
    }

    // ============================================
    // PATIENT STICKY NOTES DTOs
    // ============================================
    public class PatientStickyNoteCreateDto
    {
        public string Content { get; set; } = string.Empty;
    }

    public class PatientStickyNoteListDto
    {
        public int PatientStickyNoteId { get; set; }
        public int PatientId { get; set; }
        public string Content { get; set; } = string.Empty;
        public int CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // ============================================
    // CARE NOTES DTOs
    // Structured staff -> provider communication.
    // ============================================
    public class CareNoteCreateDto
    {
        public string Content { get; set; } = string.Empty;
        public int? ForProviderId { get; set; }
    }

    public class CareNoteUpdateDto
    {
        public string Content { get; set; } = string.Empty;
        public int? ForProviderId { get; set; }
    }

    /// <summary>
    /// Full note as shown in the patient profile Care Notes tab.
    /// </summary>
    public class CareNoteDto
    {
        public int CareNoteId { get; set; }
        public int PatientId { get; set; }
        public string Content { get; set; } = string.Empty;

        public int? ForProviderId { get; set; }
        public string? ForProviderName { get; set; }

        public int CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public bool IsEdited { get; set; }
        public int? EditedByUserId { get; set; }
        public string? EditedByName { get; set; }
        public DateTime? EditedAt { get; set; }

        public int? SeenByProviderId { get; set; }
        public DateTime? SeenAt { get; set; }
    }

    /// <summary>
    /// Single row in the top-nav unseen dropdown (clinician only).
    /// Includes patient name + first ~120 chars of content as preview.
    /// </summary>
    public class CareNoteUnseenDto
    {
        public int CareNoteId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientMrn { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

}
