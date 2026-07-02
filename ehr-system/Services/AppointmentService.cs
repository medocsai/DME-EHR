using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Hubs;

namespace EHR.Services;

public interface IAppointmentService
{
    Task<List<AppointmentListDto>> GetAppointmentsAsync(DateTime? startDate = null, DateTime? endDate = null,
        int? providerId = null, int? patientId = null, AppointmentStatus? status = null, int? locationId = null);
    Task<AppointmentListDto?> GetAppointmentByIdAsync(int appointmentId);
    Task<Appointment> CreateAppointmentAsync(AppointmentCreateDto dto, int? createdByUserId = null);
    // Patient portal booking
    Task<List<PortalAvailableSlotDto>> GetPortalAvailableSlotsAsync(int tenantId, int? locationId, DateTime date, int durationMinutes = 30, int? providerId = null);
    Task<List<PortalProviderDto>> GetPortalBookableProvidersAsync(int tenantId, int? locationId);
    Task<Appointment> BookPortalAppointmentAsync(int patientId, int tenantId, int? locationId, PortalBookAppointmentDto dto);
    Task<Appointment?> UpdateAppointmentAsync(int appointmentId, AppointmentUpdateDto dto);
    /// <summary>
    /// Extend an in-progress / scheduled appointment's EndTime. Validates status,
    /// time bounds, and (unless Force=true) overlap with the same provider's other
    /// active appointments. See rules/technical/appointment-extend.md (TODO doc).
    /// </summary>
    Task<ExtendAppointmentResultDto> ExtendAppointmentAsync(int appointmentId, ExtendAppointmentRequestDto dto, int? changedByUserId = null);
    Task<bool> CancelAppointmentAsync(int appointmentId, string? reason = null, int? cancelledByUserId = null);
    Task<bool> MarkAsMissedAsync(int appointmentId, string? reason = null, int? markedByUserId = null, int? rescheduledToAppointmentId = null);
    Task<bool> ReinstateAppointmentAsync(int appointmentId);
    Task<Appointment?> CheckInAsync(int appointmentId, AppointmentCheckInDto dto);
    Task<Appointment?> CheckOutAsync(int appointmentId);
    Task<CptSuggestionResponse> SuggestCptForCheckoutAsync(int appointmentId);
    Task<IcdSuggestionResponse> SuggestIcdForCheckoutAsync(int appointmentId);
    Task<Appointment?> CheckOutWithCptAsync(int appointmentId, CheckoutWithCptRequest request);
    Task<Appointment?> StartVisitAsync(int appointmentId);
    Task<List<ScheduleSlotDto>> GetAvailableSlotsAsync(int providerId, DateTime date, int durationMinutes = 30);
    Task<AppointmentConflictDto> CheckForConflictAsync(int providerId, DateTime startTime, DateTime endTime);
    // ISSUE #6 FIX: Recurring appointment methods
    Task<RecurringAvailabilityCheckResponse> CheckRecurringAvailabilityAsync(RecurringAvailabilityCheckRequest request);
    Task<RecurringAppointmentCreateResponse> CreateRecurringAppointmentsAsync(RecurringAppointmentCreateRequest request);
    /// <summary>
    /// Find available providers for multiple time slots (auto-assignment mode)
    /// </summary>
    Task<AutoAssignProvidersResponse> AutoAssignProvidersAsync(AutoAssignProvidersRequest request);
    // Reschedule appointment (for all statuses)
    Task<RescheduleAppointmentResponse> RescheduleAppointmentAsync(int appointmentId, int? rescheduledByUserId = null);
}

public class AppointmentService : IAppointmentService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EHR.Helpers.EncryptionHelper _encryptionHelper;
    private readonly IScheduleNotificationService _scheduleNotificationService;
    private readonly ITelehealthService _telehealthService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly EHR.Helpers.IIntakeStatusHelper _intakeStatusHelper;

    public AppointmentService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EHR.Helpers.EncryptionHelper encryptionHelper,
        IScheduleNotificationService scheduleNotificationService,
        ITelehealthService telehealthService,
        IServiceScopeFactory serviceScopeFactory,
        IEmailService emailService,
        IConfiguration config,
        EHR.Helpers.IIntakeStatusHelper intakeStatusHelper)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryptionHelper = encryptionHelper;
        _scheduleNotificationService = scheduleNotificationService;
        _telehealthService = telehealthService;
        _serviceScopeFactory = serviceScopeFactory;
        _emailService = emailService;
        _config = config;
        _intakeStatusHelper = intakeStatusHelper;
    }
    
    public async Task<List<AppointmentListDto>> GetAppointmentsAsync(DateTime? startDate = null, DateTime? endDate = null,
        int? providerId = null, int? patientId = null, AppointmentStatus? status = null, int? locationId = null)
    {
        var query = _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .Include(a => a.ClinicalNotes)
            .AsQueryable();

        // CRITICAL: Tenant data isolation - filter by TenantId
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        // LOCATION FILTERING: Filter by explicit locationId if provided, otherwise by provider context
        // ISSUE #2 FIX: Support explicit locationId parameter for dashboard timezone-aware queries
        if (locationId.HasValue)
        {
            query = query.Where(a => a.Patient.PreferredLocationId == locationId.Value);
        }
        else if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(a => a.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        if (startDate.HasValue)
            query = query.Where(a => a.StartTime >= startDate.Value);
        
        if (endDate.HasValue)
            query = query.Where(a => a.StartTime <= endDate.Value);
        
        if (providerId.HasValue)
            query = query.Where(a => a.ProviderId == providerId.Value);
        
        if (patientId.HasValue)
            query = query.Where(a => a.PatientId == patientId.Value);
        
        if (status.HasValue)
            query = query.Where(a => a.Status == (int)status.Value);
        
        var appointments = await query
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        // Decrypt PHI fields for Patient and Provider
        foreach (var a in appointments)
        {
            if (a.Patient != null)
                _encryptionHelper.DecryptEntity(a.Patient);
            if (a.Provider != null)
                _encryptionHelper.DecryptEntity(a.Provider);
        }

        // Get user names for cancelled appointments
        var cancelledByUserIds = appointments.Where(a => a.CancelledByUserId.HasValue).Select(a => a.CancelledByUserId.Value).Distinct().ToList();
        var cancelledByUsers = cancelledByUserIds.Any()
            ? await _context.Users.Where(u => cancelledByUserIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FirstName + " " + u.LastName)
            : new Dictionary<int, string>();

        // Get user/patient names for created-by tracking
        var createdByUserIds = appointments.Where(a => a.CreatedByUserId.HasValue).Select(a => a.CreatedByUserId.Value).Distinct().ToList();
        var createdByUsers = createdByUserIds.Any()
            ? await _context.Users.Where(u => createdByUserIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FirstName + " " + u.LastName)
            : new Dictionary<int, string>();
        var createdByPatientIds = appointments.Where(a => a.CreatedByPatientId.HasValue).Select(a => a.CreatedByPatientId.Value).Distinct().ToList();
        var createdByPatients = new Dictionary<int, string>();
        if (createdByPatientIds.Any())
        {
            var patients = await _context.Patients.Where(p => createdByPatientIds.Contains(p.PatientId)).ToListAsync();
            foreach (var p in patients)
            {
                _encryptionHelper.DecryptEntity(p);
                createdByPatients[p.PatientId] = "Patient: " + p.FirstName + " " + p.LastName;
            }
        }

        // Get reschedule relationship info
        var rescheduledToIds = appointments.Where(a => a.RescheduledToAppointmentId.HasValue).Select(a => a.RescheduledToAppointmentId.Value).Distinct().ToList();
        var rescheduledFromIds = appointments.Where(a => a.RescheduledFromAppointmentId.HasValue).Select(a => a.RescheduledFromAppointmentId.Value).Distinct().ToList();
        var relatedAppointmentIds = rescheduledToIds.Concat(rescheduledFromIds).Distinct().ToList();

        var relatedAppointments = relatedAppointmentIds.Any()
            ? await _context.Appointments
                .Include(a => a.Provider)
                .Include(a => a.Location)  // TIMEZONE FIX: Include Location for timezone conversion
                .Where(a => relatedAppointmentIds.Contains(a.AppointmentId))
                .ToDictionaryAsync(a => a.AppointmentId, a => a)
            : new Dictionary<int, Appointment>();

        // Decrypt provider names for related appointments
        foreach (var apt in relatedAppointments.Values)
        {
            if (apt.Provider != null)
                _encryptionHelper.DecryptEntity(apt.Provider);
        }

        // Get current location's timezone as fallback for appointments without LocationId
        string? currentLocationTimeZoneId = null;
        if (_locationProvider.LocationId.HasValue)
        {
            var currentLocation = await _context.Locations
                .Where(l => l.LocationId == _locationProvider.LocationId.Value)
                .Select(l => l.TimeZoneId)
                .FirstOrDefaultAsync();
            currentLocationTimeZoneId = currentLocation;
        }

        // Get sticky note counts for all patients in this batch (for dashboard badge)
        var patientIds = appointments.Select(a => a.PatientId).Distinct().ToList();
        var stickyNoteCounts = new Dictionary<int, int>();
        if (patientIds.Any() && _tenantProvider.TenantId.HasValue)
        {
            stickyNoteCounts = await _context.PatientStickyNotes
                .Where(s => patientIds.Contains(s.PatientId) && !s.IsDeleted
                    && s.TenantId == _tenantProvider.TenantId.Value)
                .GroupBy(s => s.PatientId)
                .Select(g => new { PatientId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PatientId, x => x.Count);
        }

        // Get intake form status for all patients in this batch (for chip on appointments lists)
        var intakeStatuses = await _intakeStatusHelper.GetForPatientsAsync(patientIds);

        return appointments.Select(a => {
            var hasNote = a.ClinicalNotes != null && a.ClinicalNotes.Any();
            // Check if ANY note is signed (for HasSignedNote field)
            var hasSignedNote = hasNote && a.ClinicalNotes.Any(n =>
                n.Status == (int)NoteStatus.Signed ||
                n.Status == (int)NoteStatus.Finalized ||
                n.Status == (int)NoteStatus.Amended);
            // Check if ALL notes are signed (for Complete status - appointment only complete when ALL notes signed)
            var allNotesSigned = hasNote && a.ClinicalNotes.All(n =>
                n.Status == (int)NoteStatus.Signed ||
                n.Status == (int)NoteStatus.Finalized ||
                n.Status == (int)NoteStatus.Amended);

            // Calculate documentation status
            // Only applicable for checked-in appointments (Status >= 2 and not Cancelled/Missed)
            int documentationStatus;
            var isCheckedIn = (a.Status ?? 0) >= (int)AppointmentStatus.CheckedIn &&
                              (a.Status ?? 0) != (int)AppointmentStatus.Cancelled &&
                              (a.Status ?? 0) != (int)AppointmentStatus.Missed;

            if (!isCheckedIn)
            {
                documentationStatus = (int)AppointmentDocumentationStatus.NotApplicable;
            }
            else if ((a.Status ?? 0) == (int)AppointmentStatus.Completed)
            {
                documentationStatus = (int)AppointmentDocumentationStatus.Complete;
            }
            else
            {
                documentationStatus = (int)AppointmentDocumentationStatus.InProgress;
            }

            // Get timezone information from location, fallback to current user's location timezone
            var timeZoneId = a.Location?.TimeZoneId ?? currentLocationTimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
            var timeZoneAbbr = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId, a.StartTime);

            return new AppointmentListDto
            {
                AppointmentId = a.AppointmentId,
                PatientId = a.PatientId,
                PatientName = a.Patient != null ? a.Patient.FirstName + " " + a.Patient.LastName : "",
                PatientMRN = a.Patient?.Mrn ?? "",
                ProviderId = a.ProviderId,
                ProviderName = a.Provider != null ? a.Provider.LastName + ", " + a.Provider.FirstName : "",
                ProviderColor = a.Provider?.Color,
                PatientHasProfilePicture = !string.IsNullOrEmpty(a.Patient?.ProfilePicturePath),
                ProviderHasProfilePicture = !string.IsNullOrEmpty(a.Provider?.ProfilePicturePath),
                LocationId = a.LocationId,
                LocationName = a.Location?.Name,
                Type = a.Type,
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                Status = a.Status ?? 0,
                Reason = a.Reason,
                IsTelehealth = a.IsTelehealth ?? false,
                InsuranceVerified = a.InsuranceVerified,
                CopayDue = a.CopayDue,
                CopayCollected = a.CopayCollected,
                HasNote = hasNote,
                DocumentationStatus = documentationStatus,
                HasSignedNote = hasSignedNote,
                CancellationReason = a.CancellationReason,
                CancelledByUserId = a.CancelledByUserId,
                CancelledByUserName = a.CancelledByUserId.HasValue && cancelledByUsers.ContainsKey(a.CancelledByUserId.Value)
                    ? cancelledByUsers[a.CancelledByUserId.Value] : null,
                CancelledAt = a.CancelledAt,
                // Timezone information
                TimeZoneId = timeZoneId,
                TimeZoneAbbreviation = timeZoneAbbr,
                StartTimeFormatted = TimezoneHelper.FormatTimeWithTimezone(a.StartTime, timeZoneId),
                EndTimeFormatted = TimezoneHelper.FormatTimeWithTimezone(a.EndTime, timeZoneId),
                DateFormatted = TimezoneHelper.FormatDateWithTimezone(a.StartTime, timeZoneId),
                // Reschedule relationship tracking
                RescheduledToAppointmentId = a.RescheduledToAppointmentId,
                RescheduledFromAppointmentId = a.RescheduledFromAppointmentId,
                RescheduledToInfo = a.RescheduledToAppointmentId.HasValue && relatedAppointments.ContainsKey(a.RescheduledToAppointmentId.Value)
                    ? FormatRescheduleInfo(relatedAppointments[a.RescheduledToAppointmentId.Value], currentLocationTimeZoneId)
                    : null,
                RescheduledFromInfo = a.RescheduledFromAppointmentId.HasValue && relatedAppointments.ContainsKey(a.RescheduledFromAppointmentId.Value)
                    ? FormatRescheduleInfo(relatedAppointments[a.RescheduledFromAppointmentId.Value], currentLocationTimeZoneId)
                    : null,
                // Patient sticky notes count for dashboard badge
                StickyNoteCount = stickyNoteCounts.GetValueOrDefault(a.PatientId, 0),
                // Patient intake form status for chip across Dashboard / Appointments / Calendar
                IntakeStatus = intakeStatuses.GetValueOrDefault(a.PatientId,
                    new EHR.Helpers.IntakeStatusDto { Status = (int)EHR.Helpers.IntakeStatusValue.NotSubmitted }),
                // Telehealth token for join link
                TelehealthToken = a.TelehealthToken,
                // Created-by tracking
                CreatedByUserId = a.CreatedByUserId,
                CreatedByPatientId = a.CreatedByPatientId,
                CreatedByName = a.CreatedByUserId.HasValue && createdByUsers.ContainsKey(a.CreatedByUserId.Value)
                    ? createdByUsers[a.CreatedByUserId.Value]
                    : a.CreatedByPatientId.HasValue && createdByPatients.ContainsKey(a.CreatedByPatientId.Value)
                        ? createdByPatients[a.CreatedByPatientId.Value]
                        : null,
                CreatedAt = a.CreatedAt
            };
        }).ToList();
    }

    private static string FormatRescheduleInfo(Appointment appointment, string? fallbackTimeZoneId = null)
    {
        var providerName = appointment.Provider != null
            ? $"{appointment.Provider.FirstName} {appointment.Provider.LastName}"
            : "Unknown Provider";
        // TIMEZONE FIX: Use location timezone, fallback to current user's location timezone
        var timeZoneId = appointment.Location?.TimeZoneId ?? fallbackTimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
        var localDateTime = TimezoneHelper.ConvertFromUtc(appointment.StartTime, timeZoneId);
        var dateTime = localDateTime.ToString("MM/dd/yyyy h:mm tt");
        return $"{dateTime} with {providerName}";
    }

    public async Task<AppointmentListDto?> GetAppointmentByIdAsync(int appointmentId)
    {
        var query = _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .Include(a => a.ClinicalNotes)
            .Where(a => a.AppointmentId == appointmentId);

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        var a = await query.FirstOrDefaultAsync();

        if (a == null)
            return null;

        // Decrypt PHI fields for Patient and Provider
        if (a.Patient != null)
            _encryptionHelper.DecryptEntity(a.Patient);
        if (a.Provider != null)
            _encryptionHelper.DecryptEntity(a.Provider);

        // Get cancelled by user name if applicable
        string? cancelledByUserName = null;
        if (a.CancelledByUserId.HasValue)
        {
            var cancelledByUser = await _context.Users.FindAsync(a.CancelledByUserId.Value);
            if (cancelledByUser != null)
                cancelledByUserName = cancelledByUser.FirstName + " " + cancelledByUser.LastName;
        }

        // Get created-by name (staff or patient)
        string? createdByName = null;
        if (a.CreatedByUserId.HasValue)
        {
            var createdByUser = await _context.Users.FindAsync(a.CreatedByUserId.Value);
            if (createdByUser != null)
                createdByName = createdByUser.FirstName + " " + createdByUser.LastName;
        }
        else if (a.CreatedByPatientId.HasValue)
        {
            var createdByPatient = await _context.Patients.FindAsync(a.CreatedByPatientId.Value);
            if (createdByPatient != null)
            {
                _encryptionHelper.DecryptEntity(createdByPatient);
                createdByName = "Patient: " + createdByPatient.FirstName + " " + createdByPatient.LastName;
            }
        }

        // Get current location's timezone as fallback for appointments without LocationId
        string? currentLocationTimeZoneId = null;
        if (_locationProvider.LocationId.HasValue)
        {
            currentLocationTimeZoneId = await _context.Locations
                .Where(l => l.LocationId == _locationProvider.LocationId.Value)
                .Select(l => l.TimeZoneId)
                .FirstOrDefaultAsync();
        }

        // Get reschedule relationship info
        string? rescheduledToInfo = null;
        string? rescheduledFromInfo = null;
        if (a.RescheduledToAppointmentId.HasValue)
        {
            var rescheduledTo = await _context.Appointments
                .Include(apt => apt.Provider)
                .Include(apt => apt.Location)  // TIMEZONE FIX: Include Location for timezone conversion
                .FirstOrDefaultAsync(apt => apt.AppointmentId == a.RescheduledToAppointmentId.Value);
            if (rescheduledTo != null)
            {
                if (rescheduledTo.Provider != null)
                    _encryptionHelper.DecryptEntity(rescheduledTo.Provider);
                rescheduledToInfo = FormatRescheduleInfo(rescheduledTo, currentLocationTimeZoneId);
            }
        }
        if (a.RescheduledFromAppointmentId.HasValue)
        {
            var rescheduledFrom = await _context.Appointments
                .Include(apt => apt.Provider)
                .Include(apt => apt.Location)  // TIMEZONE FIX: Include Location for timezone conversion
                .FirstOrDefaultAsync(apt => apt.AppointmentId == a.RescheduledFromAppointmentId.Value);
            if (rescheduledFrom != null)
            {
                if (rescheduledFrom.Provider != null)
                    _encryptionHelper.DecryptEntity(rescheduledFrom.Provider);
                rescheduledFromInfo = FormatRescheduleInfo(rescheduledFrom, currentLocationTimeZoneId);
            }
        }

        // Calculate documentation status
        var hasNote = a.ClinicalNotes != null && a.ClinicalNotes.Any();
        // Check if ANY note is signed (for HasSignedNote field)
        var hasSignedNote = hasNote && a.ClinicalNotes.Any(n =>
            n.Status == (int)NoteStatus.Signed ||
            n.Status == (int)NoteStatus.Finalized ||
            n.Status == (int)NoteStatus.Amended);
        // Check if ALL notes are signed (for Complete status - appointment only complete when ALL notes signed)
        var allNotesSigned = hasNote && a.ClinicalNotes.All(n =>
            n.Status == (int)NoteStatus.Signed ||
            n.Status == (int)NoteStatus.Finalized ||
            n.Status == (int)NoteStatus.Amended);

        int documentationStatus;
        var isCheckedIn = (a.Status ?? 0) >= (int)AppointmentStatus.CheckedIn &&
                          (a.Status ?? 0) != (int)AppointmentStatus.Cancelled;

        if (!isCheckedIn)
        {
            documentationStatus = (int)AppointmentDocumentationStatus.NotApplicable;
        }
        else if ((a.Status ?? 0) == (int)AppointmentStatus.Completed)
        {
            documentationStatus = (int)AppointmentDocumentationStatus.Complete;
        }
        else
        {
            documentationStatus = (int)AppointmentDocumentationStatus.InProgress;
        }

        // Get timezone information from location, fallback to current user's location timezone
        var timeZoneId = a.Location?.TimeZoneId ?? currentLocationTimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
        var timeZoneAbbr = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId, a.StartTime);

        return new AppointmentListDto
        {
            AppointmentId = a.AppointmentId,
            PatientId = a.PatientId,
            PatientName = a.Patient != null ? a.Patient.FirstName + " " + a.Patient.LastName : "",
            PatientMRN = a.Patient?.Mrn ?? "",
            ProviderId = a.ProviderId,
            ProviderName = a.Provider != null ? a.Provider.LastName + ", " + a.Provider.FirstName : "",
            ProviderColor = a.Provider?.Color,
            PatientHasProfilePicture = !string.IsNullOrEmpty(a.Patient?.ProfilePicturePath),
            ProviderHasProfilePicture = !string.IsNullOrEmpty(a.Provider?.ProfilePicturePath),
            LocationId = a.LocationId,
            LocationName = a.Location?.Name,
            Type = a.Type,
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            Status = a.Status ?? 0,
            Reason = a.Reason,
            IsTelehealth = a.IsTelehealth ?? false,
            InsuranceVerified = a.InsuranceVerified,
            CopayDue = a.CopayDue,
            CopayCollected = a.CopayCollected,
            HasNote = hasNote,
            DocumentationStatus = documentationStatus,
            HasSignedNote = hasSignedNote,
            CancellationReason = a.CancellationReason,
            CancelledByUserId = a.CancelledByUserId,
            CancelledByUserName = cancelledByUserName,
            CancelledAt = a.CancelledAt,
            // Timezone information
            TimeZoneId = timeZoneId,
            TimeZoneAbbreviation = timeZoneAbbr,
            StartTimeFormatted = TimezoneHelper.FormatTimeWithTimezone(a.StartTime, timeZoneId),
            EndTimeFormatted = TimezoneHelper.FormatTimeWithTimezone(a.EndTime, timeZoneId),
            DateFormatted = TimezoneHelper.FormatDateWithTimezone(a.StartTime, timeZoneId),
            // Reschedule relationship tracking
            RescheduledToAppointmentId = a.RescheduledToAppointmentId,
            RescheduledFromAppointmentId = a.RescheduledFromAppointmentId,
            RescheduledToInfo = rescheduledToInfo,
            RescheduledFromInfo = rescheduledFromInfo,
            // Telehealth token for join link
            TelehealthToken = a.TelehealthToken,
            // Created-by tracking
            CreatedByUserId = a.CreatedByUserId,
            CreatedByPatientId = a.CreatedByPatientId,
            CreatedByName = createdByName,
            CreatedAt = a.CreatedAt
        };
    }

    public async Task<Appointment> CreateAppointmentAsync(AppointmentCreateDto dto, int? createdByUserId = null)
    {
        // Check for conflicts within tenant
        var conflictQuery = _context.Appointments
            .Where(a => a.ProviderId == dto.ProviderId &&
                       a.Status != (int)AppointmentStatus.Cancelled &&
                       ((a.StartTime <= dto.StartTime && a.EndTime > dto.StartTime) ||
                        (a.StartTime < dto.EndTime && a.EndTime >= dto.EndTime) ||
                        (a.StartTime >= dto.StartTime && a.EndTime <= dto.EndTime)));

        // CRITICAL: Tenant data isolation for conflict check
        if (_tenantProvider.TenantId.HasValue)
        {
            conflictQuery = conflictQuery.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        var conflicts = await conflictQuery.AnyAsync();

        if (conflicts)
            throw new InvalidOperationException("Time slot conflicts with existing appointment");

        // If no location explicitly provided, fall back to patient's preferred location
        if (!dto.LocationId.HasValue)
        {
            var patient = await _context.Patients.FindAsync(dto.PatientId);
            dto.LocationId = patient?.PreferredLocationId;
        }

        var appointment = new Appointment
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = dto.PatientId,
            ProviderId = dto.ProviderId,
            LocationId = dto.LocationId,
            Type = dto.Type,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Status = (int)AppointmentStatus.Scheduled,
            Reason = dto.Reason,
            Notes = dto.Notes,
            IsTelehealth = dto.IsTelehealth,
            IsRecurring = dto.IsRecurring,
            RecurrencePattern = dto.RecurrencePattern,
            RescheduledFromAppointmentId = dto.RescheduledFromAppointmentId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId
        };

        // Get copay from patient's primary insurance
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.PatientId == dto.PatientId &&
                                     i.Type == (int)InsuranceType.Primary &&
                                     i.IsActive == true);
        if (insurance != null)
        {
            appointment.CopayDue = insurance.Copay;
            appointment.InsuranceVerified = insurance.LastVerifiedAt.HasValue &&
                                           insurance.LastVerifiedAt.Value > DateTime.UtcNow.AddDays(-30);
        }

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        // Send SignalR notification for created appointment
        await SendAppointmentChangeNotificationAsync(appointment, "created");

        // Generate telehealth session and send invite email for telehealth appointments
        if (appointment.Type == (int)AppointmentType.Telehealth || appointment.IsTelehealth == true)
        {
            try
            {
                await _telehealthService.GenerateTelehealthSessionAsync(appointment.AppointmentId);
                // Fire-and-forget email — don't block appointment creation
                _ = _telehealthService.SendTelehealthInviteEmailAsync(appointment.AppointmentId);
            }
            catch (Exception ex)
            {
                // Log but don't fail appointment creation if telehealth setup fails
                Console.WriteLine($"[AppointmentService] Telehealth setup warning: {ex.Message}");
            }
        }

        // Send appointment confirmation email with portal link (fire-and-forget)
        var apptPatientId = appointment.PatientId;
        var apptProviderId = appointment.ProviderId;
        var apptLocationId = appointment.LocationId;
        var apptTenantId = appointment.TenantId;
        var apptStartTime = appointment.StartTime;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var scopedContext = scope.ServiceProvider.GetRequiredService<EhrDbContext>();
                var scopedEmailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var scopedEncryption = scope.ServiceProvider.GetRequiredService<EncryptionHelper>();

                var patient = await scopedContext.Patients.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PatientId == apptPatientId);
                if (patient == null) return;

                scopedEncryption.DecryptEntity(patient);
                if (string.IsNullOrWhiteSpace(patient.Email)) return;

                var providerEntity = await scopedContext.Providers.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProviderId == apptProviderId);
                if (providerEntity != null)
                    scopedEncryption.DecryptEntity(providerEntity);
                var location = await scopedContext.Locations.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.LocationId == apptLocationId);

                var providerName = providerEntity != null
                    ? $"{providerEntity.FirstName} {providerEntity.LastName}"
                    : "your provider";
                var locationName = location?.Name;
                var timeZoneId = location?.TimeZoneId ?? Helpers.TimezoneHelper.DefaultTimeZoneId;
                var localTime = Helpers.TimezoneHelper.ConvertFromUtc(apptStartTime, timeZoneId);
                var formattedDate = localTime.ToString("MMMM d, yyyy");
                var formattedTime = Helpers.TimezoneHelper.FormatTimeWithTimezone(apptStartTime, timeZoneId);

                // Generate portal link using location's portal code (no token needed)
                var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
                var portalCode = location?.PortalCode ?? "";
                var portalUrl = $"{baseUrl}/Portal/{portalCode}";
                var firstName = patient.FirstName ?? "Patient";

                var locationHtml = !string.IsNullOrEmpty(locationName)
                    ? $"<br><strong>Location:</strong> {locationName}"
                    : "";

                var emailBody = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family:Arial,sans-serif; line-height:1.6; color:#333; margin:0; padding:0;'>
    <div style='max-width:600px; margin:0 auto;'>
        <div style='background-color:#1B72BE; color:white; padding:20px; text-align:center;'>
            <h1 style='margin:0; font-size:24px;'>MEDOCS</h1>
        </div>
        <div style='padding:24px; background-color:#f9fafb;'>
            <h2 style='color:#1B72BE; margin-top:0;'>Appointment Confirmed</h2>
            <p>Hello {firstName},</p>
            <p>Your appointment has been scheduled:</p>
            <div style='background-color:#e3f2fd; border:1px solid #90caf9; padding:15px; border-radius:4px; margin:15px 0;'>
                <strong>Date:</strong> {formattedDate}<br>
                <strong>Time:</strong> {formattedTime}<br>
                <strong>Provider:</strong> {providerName}
                {locationHtml}
            </div>
            <p>If you need to reschedule or cancel, please contact our office at least 24 hours in advance.</p>
            <p style='text-align:center;'>
                <a href='{portalUrl}' style='display:inline-block; padding:12px 24px; background-color:#1B72BE; color:white; text-decoration:none; border-radius:6px; font-weight:bold;'>Access Patient Portal</a>
            </p>
            <p>Best regards,<br>Your Healthcare Team</p>
        </div>
        <div style='padding:16px; text-align:center; font-size:12px; color:#6b7280;'>
            <p>This is an automated message from MEDOCS. Please do not reply.</p>
            <p>&copy; MEDOCS LLC</p>
        </div>
    </div>
</body>
</html>";

                await scopedEmailService.SendEmailAsync(patient.Email, $"Appointment Confirmed - {formattedDate} at {formattedTime}", emailBody, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppointmentService] Appointment email warning: {ex.Message}");
            }
        });

        // If this appointment is rescheduled from another appointment,
        // update the original appointment with the link to this new one
        if (dto.RescheduledFromAppointmentId.HasValue)
        {
            var originalAppointment = await _context.Appointments.FindAsync(dto.RescheduledFromAppointmentId.Value);
            if (originalAppointment != null &&
                (!_tenantProvider.TenantId.HasValue || originalAppointment.TenantId == _tenantProvider.TenantId.Value))
            {
                var status = originalAppointment.Status ?? 0;

                // Check if appointment is in a valid state for reschedule linking:
                // - Cancelled (status 6) - used for rescheduling future appointments
                // - Missed (status 8) - used for rescheduling past appointments
                // - Or "calculated missed" (Status 0/1 Scheduled/Confirmed with past date)
                var isCancelled = status == (int)AppointmentStatus.Cancelled;
                var isMissed = status == (int)AppointmentStatus.Missed ||
                              ((status == (int)AppointmentStatus.Scheduled || status == (int)AppointmentStatus.Confirmed) &&
                               originalAppointment.StartTime < DateTime.UtcNow);

                if (isCancelled || isMissed)
                {
                    originalAppointment.RescheduledToAppointmentId = appointment.AppointmentId;
                    originalAppointment.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
        }

        return appointment;
    }
    
    public async Task<Appointment?> UpdateAppointmentAsync(int appointmentId, AppointmentUpdateDto dto)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return null;

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return null;

        if (dto.ProviderId.HasValue) appointment.ProviderId = dto.ProviderId.Value;
        if (dto.LocationId.HasValue) appointment.LocationId = dto.LocationId.Value;
        if (dto.Type.HasValue) appointment.Type = dto.Type.Value;
        if (dto.StartTime.HasValue) appointment.StartTime = dto.StartTime.Value;
        if (dto.EndTime.HasValue) appointment.EndTime = dto.EndTime.Value;
        if (dto.Status.HasValue) appointment.Status = dto.Status.Value;
        if (dto.Reason != null) appointment.Reason = dto.Reason;
        if (dto.Notes != null) appointment.Notes = dto.Notes;
        if (dto.IsTelehealth.HasValue) appointment.IsTelehealth = dto.IsTelehealth.Value;
        
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Send SignalR notification for updated appointment
        await SendAppointmentChangeNotificationAsync(appointment, "updated");

        return appointment;
    }

    /// <summary>
    /// Extend an appointment's EndTime when the session runs over. Validates:
    ///   1. Tenant isolation + appointment exists.
    ///   2. Status is one of (Scheduled, Confirmed, CheckedIn, InProgress) — we don't
    ///      extend Completed / Cancelled / NoShow / Rescheduled / Missed.
    ///   3. NewEndTime > StartTime, ≤ StartTime + 4h, and ≠ current EndTime.
    ///   4. Unless Force=true, the new range must not overlap another active
    ///      appointment of the same provider (excluding this appointment).
    /// Active = Scheduled / Confirmed / CheckedIn / InProgress (tighter than the
    /// create-path conflict check, which also lets historical Completed rows in).
    /// </summary>
    public async Task<ExtendAppointmentResultDto> ExtendAppointmentAsync(int appointmentId, ExtendAppointmentRequestDto dto, int? changedByUserId = null)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
        {
            return new ExtendAppointmentResultDto
            {
                Success = false,
                ErrorCode = "NOT_FOUND",
                Message = "Appointment not found."
            };
        }

        // CRITICAL: Tenant data isolation — refuse any cross-tenant access.
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
        {
            return new ExtendAppointmentResultDto
            {
                Success = false,
                ErrorCode = "NOT_FOUND",
                Message = "Appointment not found."
            };
        }

        // Status whitelist: only extend visits that are still in motion.
        var status = appointment.Status;
        var statusAllowed =
            status == (int)AppointmentStatus.Scheduled ||
            status == (int)AppointmentStatus.Confirmed ||
            status == (int)AppointmentStatus.CheckedIn ||
            status == (int)AppointmentStatus.InProgress;
        if (!statusAllowed)
        {
            return new ExtendAppointmentResultDto
            {
                Success = false,
                ErrorCode = "INVALID_STATUS",
                Message = "This appointment can't be extended in its current status."
            };
        }

        // Time validation — defensive; the UI enforces the same caps but we never
        // trust the client.
        var newEnd = dto.NewEndTime;
        if (newEnd <= appointment.StartTime)
        {
            return new ExtendAppointmentResultDto
            {
                Success = false,
                ErrorCode = "INVALID_TIME",
                Message = "New end time must be after the appointment start time."
            };
        }
        if (newEnd > appointment.StartTime.AddHours(4))
        {
            return new ExtendAppointmentResultDto
            {
                Success = false,
                ErrorCode = "INVALID_TIME",
                Message = "Appointments can't run more than 4 hours past the start time."
            };
        }
        if (newEnd == appointment.EndTime)
        {
            return new ExtendAppointmentResultDto
            {
                Success = false,
                ErrorCode = "INVALID_TIME",
                Message = "New end time is the same as the current end time."
            };
        }

        // Conflict check (skipped when the caller forces — i.e., the user has
        // already seen and dismissed the overlap warning).
        if (!dto.Force)
        {
            var startTime = appointment.StartTime;
            var conflictQuery = _context.Appointments
                .Include(a => a.Patient)
                .Where(a => a.AppointmentId != appointmentId
                    && a.ProviderId == appointment.ProviderId
                    && (a.Status == (int)AppointmentStatus.Scheduled
                        || a.Status == (int)AppointmentStatus.Confirmed
                        || a.Status == (int)AppointmentStatus.CheckedIn
                        || a.Status == (int)AppointmentStatus.InProgress)
                    // Standard half-open overlap: two ranges overlap iff
                    // a.Start < newEnd AND a.End > startTime.
                    && a.StartTime < newEnd
                    && a.EndTime > startTime);

            if (_tenantProvider.TenantId.HasValue)
            {
                conflictQuery = conflictQuery.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
            }

            var conflict = await conflictQuery.OrderBy(a => a.StartTime).FirstOrDefaultAsync();
            if (conflict != null)
            {
                // Decrypt the conflicting patient name so the warning text is
                // human-readable. Allowed: the caller already has clinic-side
                // PHI access (controller is [Authorize] role 0/1/2/3).
                string conflictPatientName = "another patient";
                if (conflict.Patient != null)
                {
                    var first = _encryptionHelper.Decrypt(conflict.Patient.FirstName) ?? "";
                    var last = _encryptionHelper.Decrypt(conflict.Patient.LastName) ?? "";
                    var combined = $"{first} {last}".Trim();
                    if (!string.IsNullOrEmpty(combined)) conflictPatientName = combined;
                }

                // Use the appointment's location timezone for the user-facing
                // time strings. Falls back to America/Chicago (the existing default).
                string timeZoneId = "America/Chicago";
                if (appointment.LocationId.HasValue)
                {
                    var locTz = await _context.Locations
                        .Where(l => l.LocationId == appointment.LocationId.Value)
                        .Select(l => l.TimeZoneId)
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(locTz)) timeZoneId = locTz;
                }

                return new ExtendAppointmentResultDto
                {
                    Success = false,
                    ErrorCode = "CONFLICT",
                    Message = $"Extending to this time overlaps with {conflictPatientName}'s appointment.",
                    Conflict = new ExtendAppointmentConflictInfo
                    {
                        AppointmentId = conflict.AppointmentId,
                        PatientName = conflictPatientName,
                        StartTime = conflict.StartTime,
                        EndTime = conflict.EndTime,
                        StartTimeFormatted = TimezoneHelper.FormatTimeWithTimezone(conflict.StartTime, timeZoneId),
                        EndTimeFormatted = TimezoneHelper.FormatTimeWithTimezone(conflict.EndTime, timeZoneId)
                    }
                };
            }
        }

        // All checks passed — persist the new EndTime and notify subscribers.
        appointment.EndTime = newEnd;
        appointment.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await SendAppointmentChangeNotificationAsync(appointment, "extended", changedByUserId);

        return new ExtendAppointmentResultDto
        {
            Success = true,
            NewEndTime = appointment.EndTime
        };
    }

    public async Task<bool> CancelAppointmentAsync(int appointmentId, string? reason = null, int? cancelledByUserId = null)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return false;

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Prevent cancellation of checked-in appointments (Status >= 2: CheckedIn, InProgress, Completed)
        // NoShow (5) is allowed to be cancelled, but CheckedIn (2), InProgress (3), Completed (4) are not
        var currentStatus = appointment.Status ?? 0;
        if (currentStatus >= (int)AppointmentStatus.CheckedIn &&
            currentStatus <= (int)AppointmentStatus.Completed)
        {
            throw new InvalidOperationException("Cannot cancel appointment. Patient has already checked in.");
        }

        appointment.Status = (int)AppointmentStatus.Cancelled;
        appointment.CancellationReason = reason;
        appointment.CancelledByUserId = cancelledByUserId;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Send SignalR notification for cancelled appointment
        await SendAppointmentChangeNotificationAsync(appointment, "cancelled", cancelledByUserId);

        return true;
    }

    /// <summary>
    /// Marks an appointment as Missed. Used when rescheduling a past appointment that was never checked in.
    /// Unlike Cancelled, Missed indicates the patient did not show up and the appointment date has passed.
    /// This preserves the no-show history for reporting while removing it from active dashboard queues.
    /// </summary>
    public async Task<bool> MarkAsMissedAsync(int appointmentId, string? reason = null, int? markedByUserId = null, int? rescheduledToAppointmentId = null)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return false;

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Only Scheduled or Confirmed appointments can be marked as Missed
        // (they must not have been checked in)
        var currentStatus = appointment.Status ?? 0;
        if (currentStatus != (int)AppointmentStatus.Scheduled &&
            currentStatus != (int)AppointmentStatus.Confirmed)
        {
            throw new InvalidOperationException("Only Scheduled or Confirmed appointments can be marked as Missed.");
        }

        // Prevent re-rescheduling an already rescheduled appointment
        if (appointment.RescheduledToAppointmentId.HasValue)
        {
            throw new InvalidOperationException("This appointment has already been rescheduled.");
        }

        appointment.Status = (int)AppointmentStatus.Missed;
        appointment.CancellationReason = reason;  // Reuse the field to store the "missed" reason (e.g., "Rescheduled to...")
        appointment.CancelledByUserId = markedByUserId;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.RescheduledToAppointmentId = rescheduledToAppointmentId;
        appointment.UpdatedAt = DateTime.UtcNow;

        // If there's a rescheduled appointment, update its RescheduledFromAppointmentId
        if (rescheduledToAppointmentId.HasValue)
        {
            var newAppointment = await _context.Appointments.FindAsync(rescheduledToAppointmentId.Value);
            if (newAppointment != null)
            {
                newAppointment.RescheduledFromAppointmentId = appointmentId;
                newAppointment.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        // Send SignalR notification for missed appointment
        await SendAppointmentChangeNotificationAsync(appointment, "missed", markedByUserId);

        return true;
    }

    public async Task<bool> ReinstateAppointmentAsync(int appointmentId)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return false;

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Only cancelled appointments can be reinstated
        if (appointment.Status != (int)AppointmentStatus.Cancelled)
            return false;

        // Restore to Scheduled status and clear cancellation info
        appointment.Status = (int)AppointmentStatus.Scheduled;
        appointment.CancellationReason = null;
        appointment.CancelledByUserId = null;
        appointment.CancelledAt = null;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Send SignalR notification for reinstated appointment
        await SendAppointmentChangeNotificationAsync(appointment, "reinstated");

        return true;
    }

    /// <summary>
    /// Prepares an appointment for rescheduling by updating its status based on date logic:
    /// - Future appointments: Set to Cancelled
    /// - Current/past appointments: Set to Missed
    /// Returns info needed to create the new rescheduled appointment.
    /// </summary>
    public async Task<RescheduleAppointmentResponse> RescheduleAppointmentAsync(int appointmentId, int? rescheduledByUserId = null)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

        if (appointment == null)
            return new RescheduleAppointmentResponse { Success = false, Message = "Appointment not found" };

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return new RescheduleAppointmentResponse { Success = false, Message = "Appointment not found" };

        // Prevent rescheduling if already rescheduled
        if (appointment.RescheduledToAppointmentId.HasValue)
            return new RescheduleAppointmentResponse { Success = false, Message = "This appointment has already been rescheduled" };

        // Get the appointment's location timezone to determine if it's in the future or past
        string timeZoneId = appointment.Location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
        var localNow = TimezoneHelper.ConvertFromUtc(DateTime.UtcNow, timeZoneId);
        var appointmentLocalTime = TimezoneHelper.ConvertFromUtc(appointment.StartTime, timeZoneId);

        // Determine if appointment is in the future (compare dates in local timezone)
        var isFutureAppointment = appointmentLocalTime.Date > localNow.Date;
        var isToday = appointmentLocalTime.Date == localNow.Date;

        // For today's appointments, check if the time has passed
        var isFutureTime = isToday && appointmentLocalTime > localNow;

        // Appointment is considered "future" if it's a future date OR it's today but time hasn't passed yet
        var shouldCancel = isFutureAppointment || isFutureTime;

        // Determine which statuses can be rescheduled
        var currentStatus = appointment.Status ?? 0;

        // Cannot reschedule checked-in, in-progress, or completed appointments
        if (currentStatus >= (int)AppointmentStatus.CheckedIn &&
            currentStatus <= (int)AppointmentStatus.Completed)
        {
            return new RescheduleAppointmentResponse
            {
                Success = false,
                Message = "Cannot reschedule an appointment where the patient has already checked in"
            };
        }

        // Determine the new status and reason based on timing
        if (shouldCancel)
        {
            // Future appointment: Cancel it
            appointment.Status = (int)AppointmentStatus.Cancelled;
            appointment.CancellationReason = "Rescheduled to a new date/time";
        }
        else
        {
            // Past/current appointment: Mark as Missed
            appointment.Status = (int)AppointmentStatus.Missed;
            appointment.CancellationReason = "Rescheduled - original date has passed";
        }

        appointment.CancelledByUserId = rescheduledByUserId;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Send SignalR notification for rescheduled appointment
        await SendAppointmentChangeNotificationAsync(appointment, "rescheduled", rescheduledByUserId);

        // Decrypt patient and provider names for response
        if (appointment.Patient != null)
            _encryptionHelper.DecryptEntity(appointment.Patient);
        if (appointment.Provider != null)
            _encryptionHelper.DecryptEntity(appointment.Provider);

        return new RescheduleAppointmentResponse
        {
            Success = true,
            OriginalAppointmentId = appointmentId,
            WasCancelled = shouldCancel,
            WasMarkedMissed = !shouldCancel,
            Message = shouldCancel
                ? "Appointment cancelled for rescheduling"
                : "Appointment marked as missed for rescheduling",
            PatientId = appointment.PatientId,
            PatientName = appointment.Patient != null
                ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}"
                : "",
            PatientMRN = appointment.Patient?.Mrn ?? "",
            ProviderId = appointment.ProviderId,
            ProviderName = appointment.Provider != null
                ? $"{appointment.Provider.LastName}, {appointment.Provider.FirstName}"
                : "",
            Type = appointment.Type,
            OriginalStartTime = appointment.StartTime,
            OriginalEndTime = appointment.EndTime,
            Reason = appointment.Reason,
            LocationId = appointment.LocationId
        };
    }

    public async Task<Appointment?> CheckInAsync(int appointmentId, AppointmentCheckInDto dto)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return null;

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Only update if not already checked in
        var wasNotCheckedIn = appointment.Status == (int)AppointmentStatus.Scheduled ||
                              appointment.Status == (int)AppointmentStatus.Confirmed;

        appointment.Status = (int)AppointmentStatus.CheckedIn;
        appointment.CheckInTime = DateTime.UtcNow;
        if (dto.CopayCollected.HasValue)
            appointment.CopayCollected = dto.CopayCollected.Value;
        appointment.UpdatedAt = DateTime.UtcNow;

        // NOTE: Payment record is created by the frontend (GlobalBridge.js) with correct payment method
        // Do NOT create a duplicate payment here — the frontend POST /api/payments handles it

        // Auto-create Encounter when patient is checked in (if none exists for this appointment)
        if (wasNotCheckedIn)
        {
            var existingEncounter = await _context.Encounters
                .AnyAsync(e => e.AppointmentId == appointmentId);
            if (!existingEncounter)
            {
                var encounter = new Encounter
                {
                    TenantId = appointment.TenantId,
                    PatientId = appointment.PatientId,
                    ProviderId = appointment.ProviderId,
                    AppointmentId = appointment.AppointmentId,
                    EncounterDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    ChiefComplaint = appointment.Reason ?? "",
                    Status = 0, // Open
                    CreatedByUserId = 0,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Encounters.Add(encounter);
            }
        }

        await _context.SaveChangesAsync();

        // Send SignalR notification for checked-in appointment
        await SendAppointmentChangeNotificationAsync(appointment, "checkedIn");

        return appointment;
    }

    public async Task<Appointment?> CheckOutAsync(int appointmentId)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return null;

        // CRITICAL: Tenant data isolation - verify appointment belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Validate: at least one clinical note must exist and all must be signed
        var encounter = await _context.Encounters
            .FirstOrDefaultAsync(e => e.AppointmentId == appointmentId && e.TenantId == appointment.TenantId);

        if (encounter != null)
        {
            var notes = await _context.ClinicalNotes
                .Where(n => (n.EncounterId == encounter.EncounterId || n.AppointmentId == appointmentId) && n.TenantId == appointment.TenantId)
                .ToListAsync();

            // Fix orphaned notes: backfill EncounterId where missing
            var orphanedNotes = notes.Where(n => n.EncounterId == null || n.EncounterId == 0).ToList();
            foreach (var orphan in orphanedNotes)
            {
                orphan.EncounterId = encounter.EncounterId;
            }

            if (notes.Count == 0)
                throw new InvalidOperationException("Cannot close encounter: at least one clinical note is required.");

            var unsignedNotes = notes.Where(n => n.Status != 2).ToList(); // Status 2 = Signed
            if (unsignedNotes.Count > 0)
                throw new InvalidOperationException($"Cannot close encounter: {unsignedNotes.Count} unsigned note(s). All notes must be signed before closing.");
        }

        appointment.Status = (int)AppointmentStatus.Completed;
        appointment.CheckOutTime = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        // Update associated Encounter status on checkout
        if (encounter != null && encounter.Status == 0) // Open -> Signed
        {
            encounter.Status = 1; // Signed
            encounter.SignedAt = DateTime.UtcNow;
            encounter.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Auto-create draft billing claim on encounter completion — FIRE AND FORGET
        var tenantId = _tenantProvider.TenantId;
        if (tenantId.HasValue)
        {
            var aptId = appointmentId;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var tp = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
                    tp.TenantId = tenantId;
                    var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();
                    var db = scope.ServiceProvider.GetRequiredService<EhrDbContext>();

                    // Find the clinical note for this appointment (signed preferred, any note as fallback)
                    var note = await db.ClinicalNotes
                        .Where(n => n.AppointmentId == aptId && n.TenantId == tenantId.Value)
                        .OrderByDescending(n => n.Status) // Signed (2) first, then Draft (0)
                        .ThenByDescending(n => n.SignedAt)
                        .FirstOrDefaultAsync();

                    if (note != null)
                    {
                        await billingService.AutoCreateDraftClaimAsync(note.ClinicalNoteId);
                        System.Diagnostics.Debug.WriteLine($"Auto-claim created for note {note.ClinicalNoteId} (apt {aptId})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"No clinical note found for appointment {aptId} — skipping claim creation");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BILLING] Auto-claim creation FAILED for appointment {aptId}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Background auto-claim creation failed for appointment {aptId}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        // Encounter Summary — fire-and-forget. Generates patient-friendly visit summary
        // from signed clinical notes (PHI scrubbed) and saves to Encounter.SummaryText.
        // Spec: rules/technical/encounter-summary.md
        if (encounter != null && tenantId.HasValue)
        {
            var encId = encounter.EncounterId;
            var tId = tenantId.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var tp = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
                    tp.TenantId = tId;
                    var summaryManager = scope.ServiceProvider.GetRequiredService<IEncounterSummaryManager>();
                    await summaryManager.GenerateSummaryAsync(encId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SUMMARY] Encounter summary generation FAILED for encounter {encId}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Background encounter summary failed for encounter {encId}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        return appointment;
    }

    public async Task<CptSuggestionResponse> SuggestCptForCheckoutAsync(int appointmentId)
    {
        var appointment = await _context.Appointments
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
        if (appointment == null)
            return new CptSuggestionResponse { Success = false, ErrorMessage = "Appointment not found" };

        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return new CptSuggestionResponse { Success = false, ErrorMessage = "Appointment not found" };

        var noteContent = await BuildAggregatedNoteContentAsync(appointmentId, appointment.TenantId);
        if (string.IsNullOrWhiteSpace(noteContent))
            return new CptSuggestionResponse { Success = false, ErrorMessage = "No readable clinical note content found" };

        // Diagnosis codes — read from the encounter's ICD-10 selections (Internal Medicine source of truth).
        // Falls back to empty string if no encounter / selections yet — AI will then suggest CPT from note alone.
        var diagnosisCodes = await GetIcdSelectionsAsCommaListAsync(appointmentId, appointment.TenantId);

        using var scope = _serviceScopeFactory.CreateScope();
        var tp = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        tp.TenantId = _tenantProvider.TenantId;
        var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

        return await billingService.SuggestCptCodesAsync(new CptSuggestionRequest
        {
            NoteContent = noteContent,
            DiagnosisCodes = diagnosisCodes,
            // Pass appointment.Type so Gemini can bias toward type-appropriate
            // CPT codes — telehealth modifiers, preventive E/M, longevity time
            // codes, etc. See BillingService.SuggestCptCodesAsync prompt.
            AppointmentType = appointment.Type
        });
    }

    public async Task<IcdSuggestionResponse> SuggestIcdForCheckoutAsync(int appointmentId)
    {
        var appointment = await _context.Appointments
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
        if (appointment == null)
            return new IcdSuggestionResponse { Success = false, ErrorMessage = "Appointment not found" };

        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return new IcdSuggestionResponse { Success = false, ErrorMessage = "Appointment not found" };

        var noteContent = await BuildAggregatedNoteContentAsync(appointmentId, appointment.TenantId);
        if (string.IsNullOrWhiteSpace(noteContent))
            return new IcdSuggestionResponse { Success = false, ErrorMessage = "No readable clinical note content found" };

        using var scope = _serviceScopeFactory.CreateScope();
        var tp = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        tp.TenantId = _tenantProvider.TenantId;
        var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

        return await billingService.SuggestIcdCodesAsync(new IcdSuggestionRequest
        {
            NoteContent = noteContent,
            // Pass appointment.Type so Gemini can bias toward type-appropriate
            // diagnosis codes (preventive Z-codes for wellness, lifestyle codes
            // for longevity, etc.). See BillingService.SuggestIcdCodesAsync.
            AppointmentType = appointment.Type
        });
    }

    /// <summary>
    /// Aggregates and decrypts all clinical notes for an appointment into a single plain-text string
    /// suitable for AI prompt input (HTML stripped, whitespace collapsed, capped at 4000 chars).
    /// PHI is scrubbed (patient name, DOB, MRN, phone, email; provider name; regex
    /// fallbacks for phone/SSN/MRN format/email) before the text is returned, so
    /// every downstream Gemini call site receives a sanitized string.
    /// Spec: rules/technical/encounter-summary.md
    /// </summary>
    private async Task<string> BuildAggregatedNoteContentAsync(int appointmentId, int tenantId)
    {
        var notes = await _context.ClinicalNotes
            .Where(n => n.AppointmentId == appointmentId && n.TenantId == tenantId)
            .OrderBy(n => n.Type)
            .ToListAsync();

        if (notes.Count == 0) return null;

        var contentParts = new List<string>();
        foreach (var note in notes)
        {
            try
            {
                var decrypted = _encryptionHelper.Decrypt(note.HtmlContent);
                if (!string.IsNullOrEmpty(decrypted))
                    contentParts.Add($"--- Note (Type {note.Type}, Date {note.ServiceDate}) ---\n{decrypted}");
            }
            catch { /* skip unreadable notes */ }
        }

        var noteContent = string.Join("\n\n", contentParts);
        noteContent = System.Text.RegularExpressions.Regex.Replace(noteContent, "<[^>]+>", " ");
        noteContent = System.Text.RegularExpressions.Regex.Replace(noteContent, @"\s+", " ").Trim();
        if (noteContent.Length > 4000) noteContent = noteContent[..4000];

        // PHI scrub before returning. Build PhiContext from the appointment's
        // patient + provider so the scrubber knows which exact strings to redact.
        if (!string.IsNullOrWhiteSpace(noteContent))
        {
            var appt = await _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Provider)
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId && a.TenantId == tenantId);
            var phi = EHR.Helpers.PhiContext.Build(appt?.Patient, appt?.Provider, _encryptionHelper);
            noteContent = EHR.Helpers.ClinicalNotePHIScrubber.Scrub(noteContent, phi);
        }

        return noteContent;
    }

    /// <summary>
    /// Reads Encounter.IcdSelections (JSON array of {code, description, ...}) for the appointment's
    /// encounter and returns codes as a comma-separated string for the CPT-suggestion AI prompt.
    /// Returns empty string when no encounter or no selections yet.
    /// </summary>
    private async Task<string> GetIcdSelectionsAsCommaListAsync(int appointmentId, int tenantId)
    {
        var encounter = await _context.Encounters
            .Where(e => e.AppointmentId == appointmentId && e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();

        if (encounter == null || string.IsNullOrWhiteSpace(encounter.IcdSelections))
            return string.Empty;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(encounter.IcdSelections);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return string.Empty;
            var codes = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    item.TryGetProperty("code", out var codeProp) &&
                    codeProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var code = codeProp.GetString();
                    if (!string.IsNullOrWhiteSpace(code)) codes.Add(code.Trim());
                }
                else if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var code = item.GetString();
                    if (!string.IsNullOrWhiteSpace(code)) codes.Add(code.Trim());
                }
            }
            return string.Join(", ", codes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<Appointment?> CheckOutWithCptAsync(int appointmentId, CheckoutWithCptRequest request)
    {
        if (request.CptCodes == null || request.CptCodes.Count == 0)
            throw new InvalidOperationException("At least one CPT code is required to close the encounter.");

        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return null;

        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return null;

        var tenantId = _tenantProvider.TenantId!.Value;

        // Find encounter
        var encounter = await _context.Encounters
            .FirstOrDefaultAsync(e => e.AppointmentId == appointmentId && e.TenantId == tenantId);

        // Find the first signed clinical note (for charge linkage)
        var note = await _context.ClinicalNotes
            .Where(n => n.AppointmentId == appointmentId && n.TenantId == tenantId)
            .OrderByDescending(n => n.Status)
            .ThenByDescending(n => n.SignedAt)
            .FirstOrDefaultAsync();

        // Duplicate prevention: skip charge creation if charges already exist
        var existingCharges = await _context.Charges
            .AnyAsync(c => c.AppointmentId == appointmentId && c.TenantId == tenantId);

        if (!existingCharges)
        {
            // Convert UTC to location timezone for correct service date
            var location = appointment.LocationId.HasValue
                ? await _context.Locations.FindAsync(appointment.LocationId.Value)
                : null;
            // TimezoneHelper.ConvertFromUtc falls back to DefaultTimeZoneId when TimeZoneId is null/empty
            var serviceDate = DateOnly.FromDateTime(
                TimezoneHelper.ConvertFromUtc(appointment.StartTime, location?.TimeZoneId));
            var providerId = note?.ProviderId ?? encounter?.ProviderId ?? 0;
            var patientId = encounter?.PatientId ?? appointment.PatientId;

            foreach (var item in request.CptCodes)
            {
                _context.Charges.Add(new Charge
                {
                    TenantId = tenantId,
                    PatientId = patientId,
                    AppointmentId = appointmentId,
                    ClinicalNoteId = note?.ClinicalNoteId,
                    ProviderId = providerId,
                    ServiceDate = serviceDate,
                    Cptcode = item.CptCode,
                    Cptdescription = item.Description,
                    Units = item.Units,
                    ChargeAmount = 0,
                    Status = 0, // Pending
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        // Run standard checkout (status update + auto-claim creation)
        return await CheckOutAsync(appointmentId);
    }

    public async Task<Appointment?> StartVisitAsync(int appointmentId)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return null;

        if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // For telehealth: allow Scheduled/Confirmed/CheckedIn → InProgress
        // (Clinical person may start visit before patient joins the waiting room)
        if (appointment.Type == (int)AppointmentType.Telehealth || appointment.IsTelehealth == true)
        {
            if (appointment.Status != (int)AppointmentStatus.Scheduled &&
                appointment.Status != (int)AppointmentStatus.Confirmed &&
                appointment.Status != (int)AppointmentStatus.CheckedIn)
                return null;

            // Auto-create encounter if skipping CheckedIn (Scheduled/Confirmed → InProgress)
            if (appointment.Status == (int)AppointmentStatus.Scheduled ||
                appointment.Status == (int)AppointmentStatus.Confirmed)
            {
                var existingEncounter = await _context.Encounters
                    .AnyAsync(e => e.AppointmentId == appointmentId);
                if (!existingEncounter)
                {
                    var encounter = new Encounter
                    {
                        TenantId = appointment.TenantId,
                        PatientId = appointment.PatientId,
                        ProviderId = appointment.ProviderId,
                        AppointmentId = appointment.AppointmentId,
                        EncounterDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        ChiefComplaint = appointment.Reason ?? "",
                        Status = 0, // Open
                        CreatedByUserId = 0,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Encounters.Add(encounter);
                }
            }
        }
        else
        {
            // Standard in-person: only CheckedIn → InProgress
            if (appointment.Status != (int)AppointmentStatus.CheckedIn)
                return null;
        }

        appointment.Status = (int)AppointmentStatus.InProgress;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await SendAppointmentChangeNotificationAsync(appointment, "inProgress");

        return appointment;
    }
    
    public async Task<List<ScheduleSlotDto>> GetAvailableSlotsAsync(int providerId, DateTime date, int durationMinutes = 30)
    {
        var providerQuery = _context.Providers
            .Include(p => p.ProviderSchedules)
                .ThenInclude(ps => ps.Location)
            .Where(p => p.ProviderId == providerId);

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue)
        {
            providerQuery = providerQuery.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        var provider = await providerQuery.FirstOrDefaultAsync();

        if (provider == null)
            return new List<ScheduleSlotDto>();

        // TIMEZONE FIX: Get location timezone FIRST before determining the local date
        int? locationId = null;
        string locationTimeZoneId = TimezoneHelper.DefaultTimeZoneId;
        string locationName = "Default";

        // Try to get timezone from current location context first
        if (_locationProvider.LocationId.HasValue)
        {
            var currentLocation = await _context.Locations.FindAsync(_locationProvider.LocationId.Value);
            if (currentLocation != null)
            {
                locationId = currentLocation.LocationId;
                locationName = currentLocation.Name;
                locationTimeZoneId = currentLocation.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
            }
        }

        // The date parameter comes from the frontend in UTC
        // We need to interpret it in the location's timezone to find the correct local date
        var localDate = TimezoneHelper.ConvertFromUtc(date, locationTimeZoneId).Date;

        // TIMEZONE FIX: Use the LOCAL date's day of week, not the UTC date's day of week
        var dayOfWeek = (int)localDate.DayOfWeek;
        var schedule = provider.ProviderSchedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek && s.IsAvailable == true);

        TimeOnly startTime;
        TimeOnly endTime;

        if (schedule == null)
        {
            // DUTY DAYS FIX: Check if provider has any schedules configured
            var hasAnySchedule = provider.ProviderSchedules.Any();
            if (hasAnySchedule)
            {
                // Provider has duty days configured but is NOT available on this day
                // Return empty list - no slots should be shown
                return new List<ScheduleSlotDto>();
            }
            // No schedules configured at all - use default hours but skip weekends
            if (dayOfWeek == 0 || dayOfWeek == 6) // Sunday = 0, Saturday = 6
            {
                return new List<ScheduleSlotDto>();
            }
            // Default schedule for weekdays: 8 AM - 5 PM
            startTime = new TimeOnly(8, 0);
            endTime = new TimeOnly(17, 0);
        }
        else
        {
            startTime = schedule.StartTime;
            endTime = schedule.EndTime;
            // If schedule has a location, use its timezone (may override the context location)
            if (schedule.LocationId.HasValue && schedule.Location != null)
            {
                locationId = schedule.LocationId;
                locationName = schedule.Location.Name ?? "Default";
                locationTimeZoneId = schedule.Location.TimeZoneId ?? locationTimeZoneId;
                // Recalculate localDate if timezone changed
                localDate = TimezoneHelper.ConvertFromUtc(date, locationTimeZoneId).Date;
            }
        }

        // Get timezone abbreviation
        var timezoneAbbr = TimezoneHelper.GetTimezoneAbbreviation(locationTimeZoneId);

        // Create start and end times in the location's timezone, then convert to UTC for storage/comparison
        var localStartOfDay = localDate.Add(startTime.ToTimeSpan());
        var localEndOfDay = localDate.Add(endTime.ToTimeSpan());

        // Convert local times to UTC for database queries
        var startOfDayUtc = TimezoneHelper.ConvertToUtc(localStartOfDay, locationTimeZoneId);
        var endOfDayUtc = TimezoneHelper.ConvertToUtc(localEndOfDay, locationTimeZoneId);

        // Get existing appointments for the day (appointments are stored in UTC)
        var existingApptQuery = _context.Appointments
            .Where(a => a.ProviderId == providerId &&
                       a.StartTime >= startOfDayUtc &&
                       a.StartTime < endOfDayUtc.AddDays(1) &&
                       a.Status != (int)AppointmentStatus.Cancelled);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            existingApptQuery = existingApptQuery.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        var existingAppointments = await existingApptQuery.OrderBy(a => a.StartTime).ToListAsync();

        // Get provider time-off/unavailability for the day
        var localDateOnly = DateOnly.FromDateTime(localDate);
        var timeOffQuery = _context.TherapistUnavailabilities
            .Where(u => u.ProviderId == providerId &&
                       u.IsApproved == true &&
                       u.StartDate <= localDateOnly &&
                       u.EndDate >= localDateOnly);

        if (_tenantProvider.TenantId.HasValue)
        {
            timeOffQuery = timeOffQuery.Where(u => u.TenantId == _tenantProvider.TenantId.Value);
        }

        var timeOffRecords = await timeOffQuery.ToListAsync();

        // Helper function to check if a time slot overlaps with time-off
        bool IsInTimeOff(DateTime slotStartUtc, DateTime slotEndUtc)
        {
            foreach (var timeOff in timeOffRecords)
            {
                // Full day time-off - block entire day
                if (timeOff.IsFullDay)
                {
                    return true;
                }

                // Partial day time-off
                // If IsFullDay is false, we have a partial day time-off
                if (!timeOff.IsFullDay)
                {
                    // If times are not specified, treat as full day block for safety
                    if (!timeOff.StartTime.HasValue || !timeOff.EndTime.HasValue)
                    {
                        return true;
                    }

                    // Convert time-off times to local DateTime for the appointment date
                    var timeOffDate = localDate; // Use the appointment date (at midnight)
                    var timeOffStartLocal = timeOffDate.Add(timeOff.StartTime.Value.ToTimeSpan());
                    var timeOffEndLocal = timeOffDate.Add(timeOff.EndTime.Value.ToTimeSpan());

                    // Convert to UTC for comparison
                    var timeOffStartUtc = TimezoneHelper.ConvertToUtc(timeOffStartLocal, locationTimeZoneId);
                    var timeOffEndUtc = TimezoneHelper.ConvertToUtc(timeOffEndLocal, locationTimeZoneId);

                    // Check for overlap: slot overlaps with time-off if:
                    // slot starts before time-off ends AND slot ends after time-off starts
                    if (slotStartUtc < timeOffEndUtc && slotEndUtc > timeOffStartUtc)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        var slots = new List<ScheduleSlotDto>();
        var currentLocalTime = localStartOfDay;
        var slotDuration = TimeSpan.FromMinutes(durationMinutes);

        while (currentLocalTime.Add(slotDuration) <= localEndOfDay)
        {
            var slotLocalEnd = currentLocalTime.Add(slotDuration);

            // Convert slot times to UTC for conflict checking
            var slotStartUtc = TimezoneHelper.ConvertToUtc(currentLocalTime, locationTimeZoneId);
            var slotEndUtc = TimezoneHelper.ConvertToUtc(slotLocalEnd, locationTimeZoneId);

            // ISSUE #9 FIX: Check both existing appointments AND time-off records
            var hasAppointmentConflict = existingAppointments.Any(a =>
                (a.StartTime <= slotStartUtc && a.EndTime > slotStartUtc) ||
                (a.StartTime < slotEndUtc && a.EndTime >= slotEndUtc) ||
                (a.StartTime >= slotStartUtc && a.EndTime <= slotEndUtc));

            var hasTimeOffConflict = IsInTimeOff(slotStartUtc, slotEndUtc);

            var isAvailable = !hasAppointmentConflict && !hasTimeOffConflict;

            slots.Add(new ScheduleSlotDto
            {
                // Return UTC times for the API (frontend will receive these)
                StartTime = slotStartUtc,
                EndTime = slotEndUtc,
                ProviderId = providerId,
                ProviderName = provider.LastName + ", " + provider.FirstName,
                ProviderColor = provider.Color ?? "#2196F3",
                LocationId = locationId,
                LocationName = locationName,
                // Formatted times in local timezone
                StartTimeFormatted = currentLocalTime.ToString("h:mm tt"),
                EndTimeFormatted = slotLocalEnd.ToString("h:mm tt"),
                // Timezone information
                TimeZoneId = locationTimeZoneId,
                TimeZoneAbbreviation = timezoneAbbr,
                StartTimeWithTimezone = $"{currentLocalTime:h:mm tt} {timezoneAbbr}",
                EndTimeWithTimezone = $"{slotLocalEnd:h:mm tt} {timezoneAbbr}",
                IsAvailable = isAvailable
            });

            currentLocalTime = currentLocalTime.Add(slotDuration);
        }

        return slots;
    }

    public async Task<AppointmentConflictDto> CheckForConflictAsync(int providerId, DateTime startTime, DateTime endTime)
    {
        var query = _context.Appointments
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .Where(a => a.ProviderId == providerId &&
                       a.Status != (int)AppointmentStatus.Cancelled);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        // Check for overlapping appointments
        var conflictingAppointment = await query
            .Where(a =>
                (a.StartTime <= startTime && a.EndTime > startTime) ||
                (a.StartTime < endTime && a.EndTime >= endTime) ||
                (a.StartTime >= startTime && a.EndTime <= endTime))
            .FirstOrDefaultAsync();

        if (conflictingAppointment != null)
        {
            return new AppointmentConflictDto
            {
                HasConflict = true,
                ProviderName = conflictingAppointment.Provider != null
                    ? $"{conflictingAppointment.Provider.LastName}, {conflictingAppointment.Provider.FirstName}"
                    : "Provider",
                LocationName = conflictingAppointment.Location?.Name ?? "",
                ConflictStart = conflictingAppointment.StartTime.ToString("h:mm tt"),
                ConflictEnd = conflictingAppointment.EndTime.ToString("h:mm tt"),
                ConflictAppointmentId = conflictingAppointment.AppointmentId
            };
        }

        return new AppointmentConflictDto { HasConflict = false };
    }

    // ISSUE #6 FIX: Check availability for multiple dates (recurring appointments)
    public async Task<RecurringAvailabilityCheckResponse> CheckRecurringAvailabilityAsync(RecurringAvailabilityCheckRequest request)
    {
        var response = new RecurringAvailabilityCheckResponse
        {
            ProviderId = request.ProviderId,
            TotalRequested = request.ProposedDates.Count,
            Dates = new List<RecurringDateAvailability>()
        };

        // Get provider info
        var provider = await _context.Providers
            .Include(p => p.ProviderSchedules)
            .FirstOrDefaultAsync(p => p.ProviderId == request.ProviderId);

        if (provider == null)
        {
            response.ProviderName = "Unknown Provider";
            return response;
        }

        response.ProviderName = $"{provider.LastName}, {provider.FirstName}";

        // Get location timezone for formatting
        var locationId = _locationProvider.LocationId;
        var location = locationId.HasValue
            ? await _context.Locations.FindAsync(locationId.Value)
            : await _context.Locations.FirstOrDefaultAsync(l => l.TenantId == _tenantProvider.TenantId);
        var timeZoneId = location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        // Get all appointments for this provider in the date range
        var minDate = request.ProposedDates.Min();
        var maxDate = request.ProposedDates.Max().AddMinutes(request.DurationMinutes);

        var existingAppointments = await _context.Appointments
            .Where(a => a.ProviderId == request.ProviderId &&
                       a.Status != (int)AppointmentStatus.Cancelled &&
                       a.StartTime >= minDate && a.StartTime <= maxDate)
            .ToListAsync();

        // Get time-off for this provider in the date range
        var minDateOnly = DateOnly.FromDateTime(TimezoneHelper.ConvertFromUtc(minDate, timeZoneId));
        var maxDateOnly = DateOnly.FromDateTime(TimezoneHelper.ConvertFromUtc(maxDate, timeZoneId));

        var timeOff = await _context.TherapistUnavailabilities
            .Where(u => u.ProviderId == request.ProviderId &&
                       u.IsApproved == true &&
                       u.StartDate <= maxDateOnly && u.EndDate >= minDateOnly)
            .ToListAsync();

        foreach (var proposedUtc in request.ProposedDates)
        {
            var proposedEndUtc = proposedUtc.AddMinutes(request.DurationMinutes);
            var proposedLocal = TimezoneHelper.ConvertFromUtc(proposedUtc, timeZoneId);
            var dayOfWeek = (int)proposedLocal.DayOfWeek;
            var proposedLocalDate = DateOnly.FromDateTime(proposedLocal);

            var availability = new RecurringDateAvailability
            {
                ProposedDateTime = proposedUtc,
                FormattedDate = proposedLocal.ToString("ddd, MMM d, yyyy"),
                FormattedTime = proposedLocal.ToString("h:mm tt"),
                IsAvailable = true
            };

            // Check 1: Is provider scheduled to work this day?
            var schedule = provider.ProviderSchedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek && s.IsAvailable == true);
            if (schedule == null)
            {
                // Check if there's any schedule - if none exists, use default hours
                var anySchedule = provider.ProviderSchedules.Any();
                if (anySchedule)
                {
                    availability.IsAvailable = false;
                    availability.ConflictReason = "Provider not scheduled this day";
                    response.Dates.Add(availability);
                    continue;
                }
                // If no schedule configured, assume available Mon-Fri 8am-5pm
                if (dayOfWeek == 0 || dayOfWeek == 6)
                {
                    availability.IsAvailable = false;
                    availability.ConflictReason = "Weekend - provider not scheduled";
                    response.Dates.Add(availability);
                    continue;
                }
            }
            else
            {
                // Check if within scheduled hours
                var proposedTimeOnly = TimeOnly.FromDateTime(proposedLocal);
                var proposedEndTimeOnly = proposedTimeOnly.AddMinutes(request.DurationMinutes);
                if (proposedTimeOnly < schedule.StartTime || proposedEndTimeOnly > schedule.EndTime)
                {
                    availability.IsAvailable = false;
                    availability.ConflictReason = $"Outside working hours ({schedule.StartTime:h:mm tt}-{schedule.EndTime:h:mm tt})";
                    response.Dates.Add(availability);
                    continue;
                }
            }

            // Check 2: Does provider have time-off this day?
            var hasTimeOff = timeOff.Any(t =>
                t.StartDate <= proposedLocalDate && t.EndDate >= proposedLocalDate &&
                (t.IsFullDay == true ||
                 (t.StartTime.HasValue && t.EndTime.HasValue &&
                  TimeOnly.FromDateTime(proposedLocal) >= t.StartTime.Value &&
                  TimeOnly.FromDateTime(proposedLocal) < t.EndTime.Value)));

            if (hasTimeOff)
            {
                availability.IsAvailable = false;
                availability.ConflictReason = "Provider has time off";
                response.Dates.Add(availability);
                continue;
            }

            // Check 3: Is there a conflicting appointment?
            var conflicting = existingAppointments.FirstOrDefault(a =>
                (a.StartTime <= proposedUtc && a.EndTime > proposedUtc) ||
                (a.StartTime < proposedEndUtc && a.EndTime >= proposedEndUtc) ||
                (a.StartTime >= proposedUtc && a.EndTime <= proposedEndUtc));

            if (conflicting != null)
            {
                availability.IsAvailable = false;
                availability.ConflictReason = "Existing appointment";
                availability.ConflictingAppointmentId = conflicting.AppointmentId;
                response.Dates.Add(availability);
                continue;
            }

            response.Dates.Add(availability);
        }

        response.AvailableCount = response.Dates.Count(d => d.IsAvailable);
        response.ConflictCount = response.Dates.Count(d => !d.IsAvailable);

        return response;
    }

    // ISSUE #6 FIX: Create a recurring appointment series
    public async Task<RecurringAppointmentCreateResponse> CreateRecurringAppointmentsAsync(RecurringAppointmentCreateRequest request)
    {
        var response = new RecurringAppointmentCreateResponse
        {
            Results = new List<RecurringAppointmentResult>()
        };

        if (request.SelectedDates == null || request.SelectedDates.Count == 0)
        {
            response.Success = false;
            response.Message = "No dates selected for recurring series";
            return response;
        }

        // Generate a unique series ID
        var seriesId = $"REC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        response.SeriesId = seriesId;

        var tenantId = _tenantProvider.TenantId ?? throw new InvalidOperationException("Tenant context required");

        foreach (var startTimeUtc in request.SelectedDates.OrderBy(d => d))
        {
            var endTimeUtc = startTimeUtc.AddMinutes(request.DurationMinutes);

            var result = new RecurringAppointmentResult
            {
                RequestedDateTime = startTimeUtc
            };

            try
            {
                // Double-check for conflicts before creating
                var conflict = await CheckForConflictAsync(request.ProviderId, startTimeUtc, endTimeUtc);
                if (conflict.HasConflict)
                {
                    result.Created = false;
                    result.ErrorMessage = "Scheduling conflict detected";
                    response.Results.Add(result);
                    continue;
                }

                var appointment = new Appointment
                {
                    TenantId = tenantId,
                    PatientId = request.PatientId,
                    ProviderId = request.ProviderId,
                    LocationId = request.LocationId ?? _locationProvider.LocationId,
                    StartTime = startTimeUtc,
                    EndTime = endTimeUtc,
                    Type = request.Type,
                    Status = (int)AppointmentStatus.Scheduled,
                    Notes = string.IsNullOrEmpty(request.Notes)
                        ? $"Series: {seriesId}"
                        : $"{request.Notes}\nSeries: {seriesId}",
                    IsRecurring = true,
                    RecurrencePattern = request.RecurrencePattern,
                    IsTelehealth = request.IsTelehealth,
                    // Reminder preferences for SMS notifications
                    Reminder24hEnabled = request.Reminder24h,
                    Reminder1hEnabled = request.Reminder1h,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Appointments.Add(appointment);
                await _context.SaveChangesAsync();

                result.Created = true;
                result.AppointmentId = appointment.AppointmentId;
            }
            catch (Exception ex)
            {
                result.Created = false;
                result.ErrorMessage = ex.Message;
            }

            response.Results.Add(result);
        }

        response.TotalCreated = response.Results.Count(r => r.Created);
        response.TotalFailed = response.Results.Count(r => !r.Created);
        response.Success = response.TotalCreated > 0;
        response.Message = response.TotalFailed == 0
            ? $"Successfully created {response.TotalCreated} appointments"
            : $"Created {response.TotalCreated} appointments, {response.TotalFailed} failed";

        return response;
    }

    /// <summary>
    /// Auto-assign available providers for multiple time slots.
    /// For each slot, finds the first available provider based on schedule, time-off, and existing appointments.
    /// </summary>
    public async Task<AutoAssignProvidersResponse> AutoAssignProvidersAsync(AutoAssignProvidersRequest request)
    {
        var response = new AutoAssignProvidersResponse
        {
            TotalRequested = request.ProposedDates.Count,
            Slots = new List<AutoAssignSlot>()
        };

        var tenantId = _tenantProvider.TenantId ?? throw new InvalidOperationException("Tenant context required");

        // Get all active providers for this tenant
        var providers = await _context.Providers
            .Include(p => p.ProviderSchedules)
            .Where(p => p.TenantId == tenantId && p.IsActive == true)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();

        if (!providers.Any())
        {
            // No active providers - mark all slots as unavailable
            foreach (var proposedUtc in request.ProposedDates)
            {
                response.Slots.Add(new AutoAssignSlot
                {
                    ProposedDateTime = proposedUtc,
                    FormattedDate = proposedUtc.ToString("ddd, MMM d, yyyy"),
                    FormattedTime = proposedUtc.ToString("h:mm tt"),
                    IsAvailable = false,
                    UnavailableReason = "No active providers"
                });
            }
            response.UnavailableCount = response.TotalRequested;
            return response;
        }

        // Get location timezone for formatting
        var locationId = _locationProvider.LocationId;
        var location = locationId.HasValue
            ? await _context.Locations.FindAsync(locationId.Value)
            : await _context.Locations.FirstOrDefaultAsync(l => l.TenantId == tenantId);
        var timeZoneId = location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        // Get date range for queries
        var minDate = request.ProposedDates.Min();
        var maxDate = request.ProposedDates.Max().AddMinutes(request.DurationMinutes);
        var minDateOnly = DateOnly.FromDateTime(TimezoneHelper.ConvertFromUtc(minDate, timeZoneId));
        var maxDateOnly = DateOnly.FromDateTime(TimezoneHelper.ConvertFromUtc(maxDate, timeZoneId));

        // Get all appointments for all providers in the date range
        var existingAppointments = await _context.Appointments
            .Where(a => a.TenantId == tenantId &&
                       a.Status != (int)AppointmentStatus.Cancelled &&
                       a.StartTime >= minDate && a.StartTime <= maxDate)
            .ToListAsync();

        // Get all time-off for all providers in the date range
        var allTimeOff = await _context.TherapistUnavailabilities
            .Where(u => u.TenantId == tenantId &&
                       u.IsApproved == true &&
                       u.StartDate <= maxDateOnly && u.EndDate >= minDateOnly)
            .ToListAsync();

        foreach (var proposedUtc in request.ProposedDates.OrderBy(d => d))
        {
            var proposedEndUtc = proposedUtc.AddMinutes(request.DurationMinutes);
            var proposedLocal = TimezoneHelper.ConvertFromUtc(proposedUtc, timeZoneId);
            var dayOfWeek = (int)proposedLocal.DayOfWeek;
            var proposedLocalDate = DateOnly.FromDateTime(proposedLocal);
            var proposedTimeOnly = TimeOnly.FromDateTime(proposedLocal);
            var proposedEndTimeOnly = proposedTimeOnly.AddMinutes(request.DurationMinutes);

            var slot = new AutoAssignSlot
            {
                ProposedDateTime = proposedUtc,
                FormattedDate = proposedLocal.ToString("ddd, MMM d, yyyy"),
                FormattedTime = proposedLocal.ToString("h:mm tt"),
                IsAvailable = false
            };

            // If preferred provider is specified, try them first
            var providersToCheck = request.PreferredProviderId.HasValue
                ? providers.OrderByDescending(p => p.ProviderId == request.PreferredProviderId.Value).ToList()
                : providers;

            foreach (var provider in providersToCheck)
            {
                // Check 1: Is provider scheduled to work this day?
                var schedule = provider.ProviderSchedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek && s.IsAvailable == true);
                if (schedule == null)
                {
                    // Check if there's any schedule - if none exists, use default hours Mon-Fri 8am-5pm
                    var anySchedule = provider.ProviderSchedules.Any();
                    if (anySchedule)
                        continue; // Provider has a schedule but not this day

                    // No schedule configured - assume Mon-Fri 8am-5pm
                    if (dayOfWeek == 0 || dayOfWeek == 6)
                        continue; // Weekend
                }
                else
                {
                    // Check if within scheduled hours
                    if (proposedTimeOnly < schedule.StartTime || proposedEndTimeOnly > schedule.EndTime)
                        continue; // Outside working hours
                }

                // Check 2: Does provider have time-off this day?
                var providerTimeOff = allTimeOff.Where(t => t.ProviderId == provider.ProviderId);
                var hasTimeOff = providerTimeOff.Any(t =>
                    t.StartDate <= proposedLocalDate && t.EndDate >= proposedLocalDate &&
                    (t.IsFullDay == true ||
                     (t.StartTime.HasValue && t.EndTime.HasValue &&
                      proposedTimeOnly >= t.StartTime.Value &&
                      proposedTimeOnly < t.EndTime.Value)));

                if (hasTimeOff)
                    continue; // Provider has time off

                // Check 3: Is there a conflicting appointment?
                var providerAppointments = existingAppointments.Where(a => a.ProviderId == provider.ProviderId);
                var hasConflict = providerAppointments.Any(a =>
                    (a.StartTime <= proposedUtc && a.EndTime > proposedUtc) ||
                    (a.StartTime < proposedEndUtc && a.EndTime >= proposedEndUtc) ||
                    (a.StartTime >= proposedUtc && a.EndTime <= proposedEndUtc));

                if (hasConflict)
                    continue; // Provider has conflicting appointment

                // Provider is available! Assign them
                slot.IsAvailable = true;
                slot.AssignedProviderId = provider.ProviderId;
                slot.AssignedProviderName = $"{provider.FirstName} {provider.LastName}";
                break;
            }

            if (!slot.IsAvailable)
            {
                slot.UnavailableReason = "No providers available";
            }

            response.Slots.Add(slot);
        }

        response.AvailableCount = response.Slots.Count(s => s.IsAvailable);
        response.UnavailableCount = response.Slots.Count(s => !s.IsAvailable);

        return response;
    }

    /// <summary>
    /// Helper method to send SignalR notification when an appointment changes.
    /// Call this after any appointment modification (create, update, cancel, check-in, reschedule, reinstate).
    /// </summary>
    private async Task SendAppointmentChangeNotificationAsync(
        Appointment appointment,
        string changeType,
        int? changedByUserId = null)
    {
        try
        {
            // Load related entities if not already loaded
            if (appointment.Patient == null)
            {
                await _context.Entry(appointment).Reference(a => a.Patient).LoadAsync();
            }
            if (appointment.Provider == null)
            {
                await _context.Entry(appointment).Reference(a => a.Provider).LoadAsync();
            }
            if (appointment.Location == null)
            {
                await _context.Entry(appointment).Reference(a => a.Location).LoadAsync();
            }

            // Decrypt PHI fields for notification
            if (appointment.Patient != null)
                _encryptionHelper.DecryptEntity(appointment.Patient);
            if (appointment.Provider != null)
                _encryptionHelper.DecryptEntity(appointment.Provider);

            // Get user name if we have a user ID
            string? changedByUserName = null;
            if (changedByUserId.HasValue)
            {
                var user = await _context.Users.FindAsync(changedByUserId.Value);
                if (user != null)
                    changedByUserName = $"{user.FirstName} {user.LastName}";
            }

            var notification = new AppointmentChangeNotification
            {
                AppointmentId = appointment.AppointmentId,
                ChangeType = changeType,
                PatientId = appointment.PatientId,
                PatientName = appointment.Patient != null
                    ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}"
                    : "Unknown",
                ProviderId = appointment.ProviderId,
                ProviderName = appointment.Provider != null
                    ? $"{appointment.Provider.LastName}, {appointment.Provider.FirstName}"
                    : "Unknown",
                LocationId = appointment.LocationId,
                LocationName = appointment.Location?.Name ?? "Unknown",
                StartTime = appointment.StartTime,
                EndTime = appointment.EndTime,
                Status = appointment.Status ?? 0,
                StatusName = GetStatusName(appointment.Status ?? 0),
                AppointmentType = appointment.Type,
                AppointmentTypeName = GetAppointmentTypeName(appointment.Type),
                ChangedAt = DateTime.UtcNow,
                ChangedByUserId = changedByUserId,
                ChangedByUserName = changedByUserName,
                RescheduledFromAppointmentId = appointment.RescheduledFromAppointmentId,
                RescheduledToAppointmentId = appointment.RescheduledToAppointmentId,
                IsTelehealth = appointment.IsTelehealth
            };

            await _scheduleNotificationService.NotifyAppointmentChangedAsync(
                appointment.TenantId,
                notification);
        }
        catch (Exception ex)
        {
            // Log but don't throw - notification failure shouldn't break the main operation
            Console.WriteLine($"[AppointmentService] Failed to send SignalR notification: {ex.Message}");
        }
    }

    private static string GetStatusName(int status)
    {
        return status switch
        {
            0 => "Scheduled",
            1 => "Confirmed",
            2 => "Checked In",
            3 => "In Progress",
            4 => "Completed",
            5 => "No Show",
            6 => "Cancelled",
            7 => "Rescheduled",
            8 => "Missed",
            _ => "Unknown"
        };
    }

    private static string GetAppointmentTypeName(int type)
    {
        return type switch
        {
            0 => "New Patient",
            1 => "Follow-Up",
            2 => "Annual Physical",
            3 => "Wellness Exam",
            4 => "Consultation",
            5 => "Telehealth",
            6 => "Procedure",
            7 => "Urgent Visit",
            8 => "Lab Review",
            9 => "Med Review",
            10 => "New Longevity Patient",
            11 => "Follow-Up Longevity Patient",
            _ => "Unknown"
        };
    }

    // ============================================
    // PATIENT PORTAL BOOKING
    // ============================================

    /// <summary>
    /// Get available time slots for patient portal booking.
    /// Returns merged/deduplicated slots across all providers (no provider info exposed).
    /// </summary>
    public async Task<List<PortalAvailableSlotDto>> GetPortalAvailableSlotsAsync(int tenantId, int? locationId, DateTime date, int durationMinutes = 30, int? providerId = null)
    {
        // Active providers for this tenant. When locationId is set, restrict to providers
        // who actually work at that location (have any ProviderSchedule row there). Before
        // this filter the locationId param was silently ignored — patients saw slots from
        // providers who did not work at their preferred location.
        // 2026-05: schedule rows with LocationId == null are treated as "works at any
        // location in this tenant" — same OR clause as GetPortalBookableProvidersAsync.
        // Without this, prod clinics with legacy NULL ProviderSchedules.LocationId rows
        // show the provider on the picker but the date/time step finds no slots.
        var providersQuery = _context.Providers
            .Include(p => p.ProviderSchedules)
                .ThenInclude(ps => ps.Location)
            .Where(p => p.TenantId == tenantId && p.IsActive == true);

        if (locationId.HasValue)
        {
            providersQuery = providersQuery.Where(p =>
                p.ProviderSchedules.Any(ps =>
                    ps.LocationId == null || ps.LocationId == locationId.Value));
        }

        if (providerId.HasValue)
        {
            providersQuery = providersQuery.Where(p => p.ProviderId == providerId.Value);
        }

        var providers = await providersQuery.ToListAsync();

        if (!providers.Any())
            return new List<PortalAvailableSlotDto>();

        // Collect all available slots across the selected provider set
        var allSlots = new List<ScheduleSlotDto>();
        foreach (var provider in providers)
        {
            try
            {
                var providerSlots = await GetAvailableSlotsAsync(provider.ProviderId, date, durationMinutes);
                allSlots.AddRange(providerSlots.Where(s => s.IsAvailable));
            }
            catch
            {
                // Skip providers with errors (e.g., no schedule configured)
                continue;
            }
        }

        // Filter out past time slots (for today's date)
        var utcNow = DateTime.UtcNow;
        allSlots = allSlots.Where(s => s.StartTime > utcNow).ToList();

        // Merge/deduplicate by start time — if 3 providers are free at 9:00 AM, show it once
        var mergedSlots = allSlots
            .GroupBy(s => s.StartTime)
            .Select(g => g.First())
            .OrderBy(s => s.StartTime)
            .Select(s => new PortalAvailableSlotDto
            {
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                StartTimeFormatted = s.StartTimeFormatted,
                EndTimeFormatted = s.EndTimeFormatted,
                TimeZoneAbbreviation = s.TimeZoneAbbreviation
            })
            .ToList();

        return mergedSlots;
    }

    /// <summary>
    /// Book an appointment from the patient portal.
    /// If dto.ProviderId is set, validate that specific provider is free at the requested
    /// time and assign them. Otherwise pick a random available provider (legacy behavior).
    /// If dto.Type is set, use it. Otherwise fall back to FollowUpVisit (legacy default).
    /// </summary>
    public async Task<Appointment> BookPortalAppointmentAsync(int patientId, int tenantId, int? locationId, PortalBookAppointmentDto dto)
    {
        var providersQuery = _context.Providers
            .Include(p => p.ProviderSchedules)
                .ThenInclude(ps => ps.Location)
            .Where(p => p.TenantId == tenantId && p.IsActive == true);

        if (locationId.HasValue)
        {
            // Same NULL-LocationId handling as the slots query — schedules with
            // LocationId == null are "works at any location in this tenant".
            providersQuery = providersQuery.Where(p =>
                p.ProviderSchedules.Any(ps =>
                    ps.LocationId == null || ps.LocationId == locationId.Value));
        }

        if (dto.ProviderId.HasValue)
        {
            providersQuery = providersQuery.Where(p => p.ProviderId == dto.ProviderId.Value);
        }

        var providers = await providersQuery.ToListAsync();

        if (!providers.Any())
            throw new InvalidOperationException(dto.ProviderId.HasValue
                ? "The selected provider is not available."
                : "No providers available at this location.");

        // Find which providers are actually available for this time slot
        // Uses GetAvailableSlotsAsync which checks: schedule, conflicts, and time-off
        var availableProviderIds = new List<int>();
        var durationMinutes = (int)(dto.EndTime - dto.StartTime).TotalMinutes;
        foreach (var provider in providers)
        {
            try
            {
                var slots = await GetAvailableSlotsAsync(provider.ProviderId, dto.StartTime, durationMinutes);
                var hasSlot = slots.Any(s => s.IsAvailable && s.StartTime == dto.StartTime);
                if (hasSlot)
                    availableProviderIds.Add(provider.ProviderId);
            }
            catch
            {
                continue; // skip providers with errors
            }
        }

        if (!availableProviderIds.Any())
            throw new InvalidOperationException("This time slot is no longer available. Please select another time.");

        // Honor the patient's provider choice when set, otherwise random-assign
        int selectedProviderId;
        if (dto.ProviderId.HasValue && availableProviderIds.Contains(dto.ProviderId.Value))
        {
            selectedProviderId = dto.ProviderId.Value;
        }
        else
        {
            selectedProviderId = availableProviderIds[Random.Shared.Next(availableProviderIds.Count)];
        }

        // Honor the patient's chosen visit type when set, otherwise legacy default
        var appointmentType = dto.Type ?? (int)EHR.Models.AppointmentType.FollowUpVisit;

        // Create the appointment using existing flow
        var createDto = new AppointmentCreateDto
        {
            PatientId = patientId,
            ProviderId = selectedProviderId,
            LocationId = locationId,
            Type = appointmentType,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Reason = dto.Reason,
            IsTelehealth = false
        };

        var appointment = await CreateAppointmentAsync(createDto);

        // Set CreatedByPatientId (after creation since CreateAppointmentAsync uses CreatedByUserId param)
        appointment.CreatedByPatientId = patientId;
        await _context.SaveChangesAsync();

        return appointment;
    }

    /// <summary>
    /// List active providers a patient is allowed to book on the portal. Filters by tenant
    /// and (when locationId is set) by providers who have a schedule at that location. Sorted
    /// alphabetically by last name.
    /// </summary>
    public async Task<List<PortalProviderDto>> GetPortalBookableProvidersAsync(int tenantId, int? locationId)
    {
        var query = _context.Providers
            .Where(p => p.TenantId == tenantId && p.IsActive == true);

        if (locationId.HasValue)
        {
            // A ProviderSchedule row with LocationId == null is treated as
            // "this provider works at any location in the tenant" — so the
            // booking grid stays non-empty for single-location clinics or
            // legacy data where schedules were never bound to a location.
            // Tenant scoping above already prevents cross-tenant leak.
            query = query.Where(p => p.ProviderSchedules.Any(ps =>
                ps.LocationId == null || ps.LocationId == locationId.Value));
        }

        var providers = await query
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Select(p => new
            {
                p.ProviderId,
                p.FirstName,
                p.LastName,
                p.Credentials,
                p.Specialty,
                p.Color,
                p.ProfilePicturePath
            })
            .ToListAsync();

        return providers.Select(p =>
        {
            var first = (p.FirstName ?? "").Trim();
            var last  = (p.LastName  ?? "").Trim();
            var full  = string.IsNullOrEmpty(p.Credentials)
                ? $"Dr. {first} {last}".Trim()
                : $"Dr. {first} {last}, {p.Credentials}".Trim();
            var initials = ((first.Length > 0 ? first[0].ToString() : "") +
                           (last.Length > 0 ? last[0].ToString() : "")).ToUpperInvariant();
            return new PortalProviderDto
            {
                ProviderId  = p.ProviderId,
                DisplayName = full,
                Credentials = p.Credentials,
                Specialty   = p.Specialty,
                Color       = string.IsNullOrEmpty(p.Color) ? "#1B72BE" : p.Color,
                Initials    = string.IsNullOrEmpty(initials) ? "?" : initials,
                HasPhoto    = !string.IsNullOrEmpty(p.ProfilePicturePath)
            };
        }).ToList();
    }
}
