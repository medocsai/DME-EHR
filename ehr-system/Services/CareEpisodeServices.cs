using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Hubs;

namespace EHR.Services;

// ============================================
// INSURANCE VALIDATION SERVICE
// ============================================
public interface IInsuranceValidationService
{
    /// <summary>
    /// Validates insurance and fetches authorization data from insurance API
    /// Currently returns mock data - will be replaced with real API integration
    /// </summary>
    Task<InsuranceAuthorizationDto> ValidateInsuranceAsync(int insuranceId);
}

public class InsuranceValidationService : IInsuranceValidationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public InsuranceValidationService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<InsuranceAuthorizationDto> ValidateInsuranceAsync(int insuranceId)
    {
        var insurance = await _context.Insurances
            .Where(i => i.InsuranceId == insuranceId)
            .FirstOrDefaultAsync();

        if (insurance == null)
        {
            return new InsuranceAuthorizationDto
            {
                IsValid = false,
                Message = "Insurance not found"
            };
        }

        // MOCK RESPONSE - This structure matches what the real API will return
        // When real API is integrated, replace this block with actual API call
        var authStartDate = DateOnly.FromDateTime(DateTime.Today);
        var authEndDate = authStartDate.AddMonths(6);

        return new InsuranceAuthorizationDto
        {
            IsValid = true,
            AuthorizationNumber = $"AUTH-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(10000, 99999)}",
            AuthorizedVisits = 7,
            AuthorizationStartDate = authStartDate,
            AuthorizationEndDate = authEndDate,
            Copay = insurance.Copay ?? 25.00m,
            Deductible = insurance.Deductible ?? 500.00m,
            DeductibleMet = (insurance.Deductible ?? 500.00m) * 0.3m, // 30% met for mock
            Coinsurance = insurance.Coinsurance ?? 20.00m,
            CoverageNotes = "Physical Therapy covered under policy. Prior authorization obtained.",
            Message = "Insurance validated successfully. Authorization active.",
            VerifiedAt = DateTime.UtcNow
        };
    }
}

// ============================================
// SYSTEM SETTINGS SERVICE
// ============================================
public interface ISystemSettingsService
{
    Task<List<SystemSettingDto>> GetAllSettingsAsync();
    Task<SystemSettingDto?> GetSettingAsync(string settingKey);
    Task<int> GetIntSettingAsync(string settingKey, int defaultValue);
    Task<bool> GetBoolSettingAsync(string settingKey, bool defaultValue);
    Task<SystemSettingDto> UpsertSettingAsync(string settingKey, SystemSettingUpdateDto dto);
    Task InitializeDefaultSettingsAsync();
}

public class SystemSettingsService : ISystemSettingsService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public SystemSettingsService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<SystemSettingDto>> GetAllSettingsAsync()
    {
        var query = _context.SystemSettings.AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(s => s.TenantId == _tenantProvider.TenantId.Value);
        }

        return await query
            .Select(s => new SystemSettingDto
            {
                SystemSettingId = s.SystemSettingId,
                SettingKey = s.SettingKey,
                SettingValue = s.SettingValue,
                DataType = s.DataType,
                Description = s.Description,
                Category = s.Category,
                DefaultValue = s.DefaultValue
            })
            .ToListAsync();
    }

    public async Task<SystemSettingDto?> GetSettingAsync(string settingKey)
    {
        var query = _context.SystemSettings
            .Where(s => s.SettingKey == settingKey);

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(s => s.TenantId == _tenantProvider.TenantId.Value);
        }

        var setting = await query.FirstOrDefaultAsync();
        if (setting == null) return null;

        return new SystemSettingDto
        {
            SystemSettingId = setting.SystemSettingId,
            SettingKey = setting.SettingKey,
            SettingValue = setting.SettingValue,
            DataType = setting.DataType,
            Description = setting.Description,
            Category = setting.Category,
            DefaultValue = setting.DefaultValue
        };
    }

    public async Task<int> GetIntSettingAsync(string settingKey, int defaultValue)
    {
        var setting = await GetSettingAsync(settingKey);
        if (setting == null || string.IsNullOrEmpty(setting.SettingValue))
            return defaultValue;

        return int.TryParse(setting.SettingValue, out int value) ? value : defaultValue;
    }

    public async Task<bool> GetBoolSettingAsync(string settingKey, bool defaultValue)
    {
        var setting = await GetSettingAsync(settingKey);
        if (setting == null || string.IsNullOrEmpty(setting.SettingValue))
            return defaultValue;

        return bool.TryParse(setting.SettingValue, out bool value) ? value : defaultValue;
    }

    public async Task<SystemSettingDto> UpsertSettingAsync(string settingKey, SystemSettingUpdateDto dto)
    {
        if (!_tenantProvider.TenantId.HasValue)
            throw new InvalidOperationException("Tenant context required");

        var existing = await _context.SystemSettings
            .Where(s => s.TenantId == _tenantProvider.TenantId.Value && s.SettingKey == settingKey)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.SettingValue = dto.SettingValue;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new SystemSetting
            {
                TenantId = _tenantProvider.TenantId.Value,
                SettingKey = settingKey,
                SettingValue = dto.SettingValue,
                DataType = "int",
                CreatedAt = DateTime.UtcNow
            };
            _context.SystemSettings.Add(existing);
        }

        await _context.SaveChangesAsync();

        return new SystemSettingDto
        {
            SystemSettingId = existing.SystemSettingId,
            SettingKey = existing.SettingKey,
            SettingValue = existing.SettingValue,
            DataType = existing.DataType,
            Description = existing.Description,
            Category = existing.Category,
            DefaultValue = existing.DefaultValue
        };
    }

    public async Task InitializeDefaultSettingsAsync()
    {
        if (!_tenantProvider.TenantId.HasValue) return;

        var defaultSettings = new List<(string key, string value, string dataType, string description, string category)>
        {
            (SettingKeys.NoShowAlertThresholdMinutes, "30", "int", "Minutes after appointment time before showing no-show alert", "Appointments"),
            (SettingKeys.LowVisitsRemainingThreshold, "3", "int", "Threshold for low visits remaining alerts", "Care Episodes"),
            (SettingKeys.DefaultAppointmentDuration, "45", "int", "Default appointment duration in minutes", "Appointments"),
            (SettingKeys.MissedVisitGracePeriodHours, "24", "int", "Hours after which a no-show becomes a missed visit", "Appointments"),
            (SettingKeys.AutoCheckInOnNoteCreation, "true", "bool", "Automatically check in patient when clinical note is created", "Clinical Notes"),
            // Appointment Reminder Settings
            (SettingKeys.AppointmentRemindersEnabled, "true", "bool", "Enable/disable appointment email reminders", "Appointment Reminders"),
            (SettingKeys.Reminder24hEnabled, "true", "bool", "Send reminder emails 24 hours before appointments", "Appointment Reminders"),
            (SettingKeys.Reminder1hEnabled, "true", "bool", "Send reminder emails 1 hour before appointments", "Appointment Reminders"),
            (SettingKeys.ReminderCheckIntervalMinutes, "5", "int", "How often the system checks for reminders to send (minutes)", "Appointment Reminders")
        };

        foreach (var (key, value, dataType, description, category) in defaultSettings)
        {
            var exists = await _context.SystemSettings
                .AnyAsync(s => s.TenantId == _tenantProvider.TenantId.Value && s.SettingKey == key);

            if (!exists)
            {
                _context.SystemSettings.Add(new SystemSetting
                {
                    TenantId = _tenantProvider.TenantId.Value,
                    SettingKey = key,
                    SettingValue = value,
                    DataType = dataType,
                    Description = description,
                    Category = category,
                    DefaultValue = value,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
    }
}

// ============================================
// ENHANCED CARE EPISODE SERVICE
// ============================================
public interface IEnhancedCareEpisodeService
{
    // Core CRUD
    Task<List<CareEpisodeListDto>> GetCareEpisodesAsync(int? patientId = null, int? status = null, int? providerId = null);
    Task<CareEpisodeDetailDto?> GetCareEpisodeDetailAsync(int careEpisodeId);
    Task<CareEpisodeDto?> GetActiveCareEpisodeForPatientAsync(int patientId);
    Task<CareEpisode> CreateCareEpisodeAsync(CareEpisodeCreateDto dto);
    Task<CareEpisode?> UpdateCareEpisodeAsync(int careEpisodeId, CareEpisodeUpdateDto dto);

    // Business logic
    Task<CareEpisodeEligibilityDto> CheckEligibilityAsync(int patientId);
    Task<CareEpisode?> MarkAsCompletedAsync(int careEpisodeId, string dischargeReason, int userId);
    Task<CareEpisode?> RestoreCareEpisodeAsync(int careEpisodeId, int userId);
    Task<CareEpisode?> ExtendEndDateAsync(int careEpisodeId, DateOnly newEndDate);
    Task LinkUnlinkedInitialEvaluationsAsync(int careEpisodeId, int patientId);
    Task UpdateVisitCountsAsync(int careEpisodeId);
    Task UpdateOverdueStatusesAsync();

    // Dashboard
    Task<CareEpisodeDashboardDto> GetDashboardDataAsync(int? providerId = null);
    Task<List<CareEpisodeAlertDto>> GetLowVisitsAlertsAsync(int threshold, int? providerId = null);
    Task<List<NoShowAlertDto>> GetNoShowAlertsAsync(int thresholdMinutes, int? providerId = null);

    // Scheduling
    Task<List<RequireScheduleDto>> GetCareEpisodesNeedingScheduleAsync();
}

public class EnhancedCareEpisodeService : IEnhancedCareEpisodeService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly ISystemSettingsService _settingsService;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IInsuranceAuthorizationService _authorizationService;
    private readonly IScheduleNotificationService? _notificationService;
    private readonly ILogger<EnhancedCareEpisodeService> _logger;

    public EnhancedCareEpisodeService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        ISystemSettingsService settingsService,
        EncryptionHelper encryptionHelper,
        IInsuranceAuthorizationService authorizationService,
        ILogger<EnhancedCareEpisodeService> logger,
        IScheduleNotificationService? notificationService = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _settingsService = settingsService;
        _encryptionHelper = encryptionHelper;
        _authorizationService = authorizationService;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task<List<CareEpisodeListDto>> GetCareEpisodesAsync(int? patientId = null, int? status = null, int? providerId = null)
    {
        // AsNoTracking: read-only list projection; Patient decrypted for DTO.
        var query = _context.CareEpisodes
            .AsNoTracking()
            .Include(ce => ce.Patient)
            .Include(ce => ce.PrimaryProvider)
            .Include(ce => ce.Appointments)
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(ce => ce.TenantId == _tenantProvider.TenantId.Value);
        }

        // LOCATION FILTERING: Filter by patient's location within the tenant
        if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(ce => ce.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        if (patientId.HasValue) query = query.Where(ce => ce.PatientId == patientId);
        if (status.HasValue) query = query.Where(ce => ce.Status == status);
        // Filter by provider - show care episodes where the provider is the primary provider
        // or where the provider has any appointment with this care episode
        if (providerId.HasValue)
        {
            query = query.Where(ce => ce.PrimaryProviderId == providerId ||
                                      ce.Appointments.Any(a => a.ProviderId == providerId));
        }

        var lowVisitsThreshold = await _settingsService.GetIntSettingAsync(SettingKeys.LowVisitsRemainingThreshold, 3);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Load entities first to decrypt patient data
        var episodes = await query.OrderByDescending(ce => ce.StartDate).ToListAsync();

        // Decrypt patient data for each episode
        foreach (var episode in episodes)
        {
            if (episode.Patient != null)
            {
                _encryptionHelper.DecryptEntity(episode.Patient);
            }
        }

        // Build DTOs - CareEpisode is purely clinical, no Insurance relationship
        var result = new List<CareEpisodeListDto>();
        foreach (var ce in episodes)
        {
            // Calculate visits dynamically from appointments
            var (visitsUsed, missedVisits) = CalculateVisitCounts(ce.Appointments);
            var visitsRemaining = (ce.ExpectedVisits ?? 0) - visitsUsed;

            result.Add(new CareEpisodeListDto
            {
                CareEpisodeId = ce.CareEpisodeId,
                PatientId = ce.PatientId,
                PatientName = ce.Patient != null ? $"{ce.Patient.FirstName} {ce.Patient.LastName}" : "",
                PrimaryProviderName = ce.PrimaryProvider != null ? $"{ce.PrimaryProvider.FirstName} {ce.PrimaryProvider.LastName}" : null,
                PrimaryProviderId = ce.PrimaryProviderId,
                StartDate = ce.StartDate,
                EndDate = ce.EndDate,
                PrimaryDiagnosis = ce.PrimaryDiagnosisCode + " - " + ce.PrimaryDiagnosisDescription,
                ExpectedVisits = ce.ExpectedVisits,
                VisitsUsed = visitsUsed,
                VisitsRemaining = visitsRemaining,
                MissedVisits = missedVisits,
                Status = ce.Status ?? 0,
                // HasLowVisits uses ExpectedVisits (clinical goal)
                HasLowVisits = visitsRemaining <= lowVisitsThreshold && visitsRemaining >= 0,
                IsExpiringSoon = ce.EndDate.HasValue && ce.EndDate.Value <= today.AddDays(14) && ce.EndDate.Value >= today
            });
        }

        return result;
    }

    public async Task<CareEpisodeDetailDto?> GetCareEpisodeDetailAsync(int careEpisodeId)
    {
        try
        {
            _logger.LogInformation("Loading care episode detail for ID: {CareEpisodeId}", careEpisodeId);

            var query = _context.CareEpisodes
                .Include(ce => ce.Patient)
                .Include(ce => ce.PrimaryProvider)
                .Include(ce => ce.Appointments)
                    .ThenInclude(a => a.Patient)
                .Include(ce => ce.Appointments)
                    .ThenInclude(a => a.Provider)
                .Include(ce => ce.Appointments)
                    .ThenInclude(a => a.ClinicalNotes)
                        .ThenInclude(cn => cn.Provider)
                .Include(ce => ce.Appointments)
                    .ThenInclude(a => a.ClinicalNotes)
                        .ThenInclude(cn => cn.Template)
                .Where(ce => ce.CareEpisodeId == careEpisodeId);

            if (_tenantProvider.TenantId.HasValue)
            {
                query = query.Where(ce => ce.TenantId == _tenantProvider.TenantId.Value);
            }

            var ce = await query.FirstOrDefaultAsync();
            if (ce == null)
            {
                _logger.LogWarning("Care episode not found: {CareEpisodeId}", careEpisodeId);
                return null;
            }

            _logger.LogInformation("Care episode loaded. Appointments count: {Count}", ce.Appointments?.Count ?? 0);

            var lowVisitsThreshold = await _settingsService.GetIntSettingAsync(SettingKeys.LowVisitsRemainingThreshold, 3);

            // Calculate visits dynamically from appointments
            var (visitsUsed, missedVisits) = CalculateVisitCounts(ce.Appointments);
            var visitsRemaining = (ce.ExpectedVisits ?? 0) - visitsUsed;

            // Build appointment list with proper null checks and decryption
            var appointmentList = new List<AppointmentListDto>();
            foreach (var a in ce.Appointments ?? Enumerable.Empty<Appointment>())
            {
                try
                {
                    _logger.LogDebug("Processing appointment {AppointmentId}", a.AppointmentId);

                    var clinicalNotes = a.ClinicalNotes ?? new List<ClinicalNote>();
                    var hasNote = clinicalNotes.Any();
                    var hasSignedNote = hasNote && clinicalNotes.Any(n => n.SignedAt.HasValue);
                    var allNotesSigned = hasNote && clinicalNotes.All(n =>
                        n.Status == (int)NoteStatus.Signed ||
                        n.Status == (int)NoteStatus.Finalized ||
                        n.Status == (int)NoteStatus.Amended);

                    // Calculate documentation status for ribbon display
                    int documentationStatus;
                    var isCheckedIn = (a.Status ?? 0) >= (int)AppointmentStatus.CheckedIn &&
                                      (a.Status ?? 0) != (int)AppointmentStatus.Cancelled &&
                                      (a.Status ?? 0) != (int)AppointmentStatus.Missed;

                    if (!isCheckedIn)
                        documentationStatus = (int)AppointmentDocumentationStatus.NotApplicable;
                    else if ((a.Status ?? 0) == (int)AppointmentStatus.Completed)
                        documentationStatus = (int)AppointmentDocumentationStatus.Complete;
                    else
                        documentationStatus = (int)AppointmentDocumentationStatus.InProgress;

                    // Decrypt patient name for appointment
                    var patientName = "";
                    if (a.Patient != null)
                    {
                        var firstName = _encryptionHelper.Decrypt(a.Patient.FirstName) ?? a.Patient.FirstName;
                        var lastName = _encryptionHelper.Decrypt(a.Patient.LastName) ?? a.Patient.LastName;
                        patientName = $"{firstName} {lastName}";
                    }

                    // Build clinical notes list with proper null checks
                    var clinicalNotesList = new List<AppointmentClinicalNoteDto>();
                    foreach (var cn in clinicalNotes)
                    {
                        try
                        {
                            clinicalNotesList.Add(new AppointmentClinicalNoteDto
                            {
                                ClinicalNoteId = cn.ClinicalNoteId,
                                TemplateName = cn.Template?.Name ?? GetNoteTypeName(cn.Type),
                                Type = cn.Type,
                                Status = cn.Status,
                                ProviderId = cn.ProviderId,
                                ProviderUserId = cn.CreatedByUserId,
                                ProviderName = cn.Provider != null ? $"{cn.Provider.FirstName} {cn.Provider.LastName}" : "",
                                ServiceDate = cn.ServiceDate
                            });
                        }
                        catch (Exception cnEx)
                        {
                            _logger.LogError(cnEx, "Error processing clinical note {ClinicalNoteId} for appointment {AppointmentId}", cn.ClinicalNoteId, a.AppointmentId);
                            throw;
                        }
                    }

                    var apptDto = new AppointmentListDto
                    {
                        AppointmentId = a.AppointmentId,
                        PatientId = a.PatientId,
                        PatientName = patientName,
                        ProviderId = a.ProviderId,
                        ProviderName = a.Provider != null ? $"{a.Provider.FirstName} {a.Provider.LastName}" : "",
                        StartTime = a.StartTime,
                        EndTime = a.EndTime,
                        Type = a.Type,
                        Status = a.Status ?? 0,
                        HasNote = hasNote,
                        HasSignedNote = hasSignedNote,
                        DocumentationStatus = documentationStatus,
                        CareEpisodeId = a.CareEpisodeId,
                        ClinicalNotes = clinicalNotesList.OrderByDescending(cn => cn.ServiceDate).ToList()
                    };
                    appointmentList.Add(apptDto);
                }
                catch (Exception apptEx)
                {
                    _logger.LogError(apptEx, "Error processing appointment {AppointmentId} for care episode {CareEpisodeId}", a.AppointmentId, careEpisodeId);
                    throw;
                }
            }

            return new CareEpisodeDetailDto
            {
                CareEpisodeId = ce.CareEpisodeId,
                PatientId = ce.PatientId,
                StartDate = ce.StartDate,
                EndDate = ce.EndDate,
                PrimaryDiagnosis = ce.PrimaryDiagnosisCode + " - " + ce.PrimaryDiagnosisDescription,
                PrimaryDiagnosisCode = ce.PrimaryDiagnosisCode,
                PrimaryDiagnosisDescription = ce.PrimaryDiagnosisDescription,
                DiagnosisNotes = ce.DiagnosisNotes,
                DiagnosisCodes = ce.SecondaryDiagnoses,
                PrimaryProviderId = ce.PrimaryProviderId,
                PrimaryProviderName = ce.PrimaryProvider != null ? $"{ce.PrimaryProvider.FirstName} {ce.PrimaryProvider.LastName}" : null,
                PatientName = ce.Patient != null ? $"{_encryptionHelper.Decrypt(ce.Patient.FirstName)} {_encryptionHelper.Decrypt(ce.Patient.LastName)}" : "",
                VisitsUsed = visitsUsed,
                VisitsRemaining = visitsRemaining,
                MissedVisits = missedVisits,
                CopayAmount = ce.CopayAmount,
                CopayVisits = ce.CopayVisits,
                Goals = ce.Goals,
                PlanOfCare = ce.PlanOfCare,
                PhysicianName = ce.PhysicianName,
                ExpectedVisits = ce.ExpectedVisits,
                VisitFrequency = ce.VisitFrequency,
                Status = ce.Status ?? 0,
                DischargeReason = ce.DischargeReason,
                CompletionMethod = ce.CompletionMethod,
                CompletedAt = ce.CompletedAt,
                HasLowVisits = visitsRemaining <= lowVisitsThreshold && visitsRemaining >= 0,
                CreatedAt = ce.CreatedAt,
                UpdatedAt = ce.UpdatedAt,
                SecondaryDiagnoses = !string.IsNullOrEmpty(ce.SecondaryDiagnoses)
                    ? JsonSerializer.Deserialize<List<string>>(ce.SecondaryDiagnoses) ?? new List<string>()
                    : new List<string>(),
                Appointments = appointmentList.OrderBy(a => a.StartTime).ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading care episode detail for ID: {CareEpisodeId}", careEpisodeId);
            throw;
        }
    }

    public async Task<CareEpisodeDto?> GetActiveCareEpisodeForPatientAsync(int patientId)
    {
        var query = _context.CareEpisodes
            .Include(ce => ce.Patient)
            .Include(ce => ce.PrimaryProvider)
            .Include(ce => ce.Appointments)
            .Where(ce => ce.PatientId == patientId)
            .Where(ce => ce.Status == (int)CareEpisodeStatus.Active || ce.Status == (int)CareEpisodeStatus.Overdue);

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(ce => ce.TenantId == _tenantProvider.TenantId.Value);
        }

        var ce = await query.FirstOrDefaultAsync();
        if (ce == null) return null;

        var lowVisitsThreshold = await _settingsService.GetIntSettingAsync(SettingKeys.LowVisitsRemainingThreshold, 3);

        // Calculate visits dynamically from appointments
        var (visitsUsed, missedVisits) = CalculateVisitCounts(ce.Appointments);
        var visitsRemaining = (ce.ExpectedVisits ?? 0) - visitsUsed;

        return new CareEpisodeDto
        {
            CareEpisodeId = ce.CareEpisodeId,
            PatientId = ce.PatientId,
            StartDate = ce.StartDate,
            EndDate = ce.EndDate,
            PrimaryDiagnosis = ce.PrimaryDiagnosisCode + " - " + ce.PrimaryDiagnosisDescription,
            PrimaryDiagnosisCode = ce.PrimaryDiagnosisCode,
            PrimaryDiagnosisDescription = ce.PrimaryDiagnosisDescription,
            DiagnosisNotes = ce.DiagnosisNotes,
            DiagnosisCodes = ce.SecondaryDiagnoses,
            PrimaryProviderId = ce.PrimaryProviderId,
            PrimaryProviderName = ce.PrimaryProvider != null ? $"{ce.PrimaryProvider.FirstName} {ce.PrimaryProvider.LastName}" : null,
            VisitsUsed = visitsUsed,
            VisitsRemaining = visitsRemaining,
            MissedVisits = missedVisits,
            CopayAmount = ce.CopayAmount,
            CopayVisits = ce.CopayVisits,
            Goals = ce.Goals,
            PlanOfCare = ce.PlanOfCare,
            ExpectedVisits = ce.ExpectedVisits,
            VisitFrequency = ce.VisitFrequency,
            Status = ce.Status ?? 0,
            DischargeReason = ce.DischargeReason,
            CompletionMethod = ce.CompletionMethod,
            CompletedAt = ce.CompletedAt,
            // HasLowVisits uses ExpectedVisits (clinical goal)
            HasLowVisits = visitsRemaining <= lowVisitsThreshold && visitsRemaining >= 0
        };
    }

    public async Task<CareEpisodeEligibilityDto> CheckEligibilityAsync(int patientId)
    {
        var activeEpisode = await GetActiveCareEpisodeForPatientAsync(patientId);

        if (activeEpisode != null)
        {
            var statusName = activeEpisode.Status == (int)CareEpisodeStatus.Overdue ? "Overdue" : "Active";
            return new CareEpisodeEligibilityDto
            {
                PatientId = patientId,
                CanCreateEpisode = false,
                Reason = $"Patient already has an {statusName} Care Episode. Please complete or close the existing episode before creating a new one.",
                ActiveEpisode = activeEpisode
            };
        }

        return new CareEpisodeEligibilityDto
        {
            PatientId = patientId,
            CanCreateEpisode = true,
            Reason = "Patient can create a new Care Episode."
        };
    }

    public async Task<CareEpisode> CreateCareEpisodeAsync(CareEpisodeCreateDto dto)
    {
        if (!_tenantProvider.TenantId.HasValue)
            throw new InvalidOperationException("Tenant context required");

        // Check eligibility first
        var eligibility = await CheckEligibilityAsync(dto.PatientId);
        if (!eligibility.CanCreateEpisode)
        {
            throw new InvalidOperationException(eligibility.Reason);
        }

        // CareEpisode is purely clinical - no Insurance relationship
        var careEpisode = new CareEpisode
        {
            TenantId = _tenantProvider.TenantId.Value,
            PatientId = dto.PatientId,
            PrimaryProviderId = dto.PrimaryProviderId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            PrimaryDiagnosisCode = dto.PrimaryDiagnosisCode,
            PrimaryDiagnosisDescription = dto.PrimaryDiagnosisDescription,
            DiagnosisNotes = dto.DiagnosisNotes,
            SecondaryDiagnoses = dto.SecondaryDiagnoses != null ? JsonSerializer.Serialize(dto.SecondaryDiagnoses) : null,
            CopayAmount = dto.CopayAmount,
            CopayVisits = dto.CopayVisits,
            Goals = dto.Goals != null ? JsonSerializer.Serialize(dto.Goals) : null,
            PlanOfCare = dto.PlanOfCare,
            PhysicianName = dto.PhysicianName,
            ExpectedVisits = dto.ExpectedVisits,
            VisitFrequency = dto.VisitFrequency,
            Status = (int)CareEpisodeStatus.Active,
            MissedVisits = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.CareEpisodes.Add(careEpisode);
        await _context.SaveChangesAsync();

        // Link any unlinked Initial Evaluation appointments
        await LinkUnlinkedInitialEvaluationsAsync(careEpisode.CareEpisodeId, dto.PatientId);

        // Link any orphan consents for this patient (consents without a care episode)
        await LinkOrphanConsentsAsync(careEpisode.CareEpisodeId, dto.PatientId);

        // Send SignalR notification for real-time dashboard updates (require-schedule widget)
        await SendAuthorizationChangeNotificationAsync(careEpisode, "created");

        return careEpisode;
    }

    public async Task<CareEpisode?> UpdateCareEpisodeAsync(int careEpisodeId, CareEpisodeUpdateDto dto)
    {
        var ce = await _context.CareEpisodes.FindAsync(careEpisodeId);
        if (ce == null) return null;

        if (_tenantProvider.TenantId.HasValue && ce.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // CareEpisode is purely clinical - no Insurance relationship
        if (dto.PrimaryProviderId.HasValue) ce.PrimaryProviderId = dto.PrimaryProviderId;
        if (dto.EndDate.HasValue) ce.EndDate = dto.EndDate;
        if (dto.PrimaryDiagnosisCode != null) ce.PrimaryDiagnosisCode = dto.PrimaryDiagnosisCode;
        if (dto.PrimaryDiagnosisDescription != null) ce.PrimaryDiagnosisDescription = dto.PrimaryDiagnosisDescription;
        if (dto.DiagnosisNotes != null) ce.DiagnosisNotes = dto.DiagnosisNotes;
        if (dto.SecondaryDiagnoses != null) ce.SecondaryDiagnoses = JsonSerializer.Serialize(dto.SecondaryDiagnoses);
        if (dto.CopayAmount.HasValue) ce.CopayAmount = dto.CopayAmount;
        if (dto.CopayVisits.HasValue) ce.CopayVisits = dto.CopayVisits;
        if (dto.Goals != null) ce.Goals = JsonSerializer.Serialize(dto.Goals);
        if (dto.PlanOfCare != null) ce.PlanOfCare = dto.PlanOfCare;
        if (dto.PhysicianName != null) ce.PhysicianName = dto.PhysicianName;
        if (dto.ExpectedVisits.HasValue) ce.ExpectedVisits = dto.ExpectedVisits;
        if (dto.VisitFrequency.HasValue) ce.VisitFrequency = dto.VisitFrequency;
        if (dto.Status.HasValue) ce.Status = dto.Status.Value;
        if (dto.DischargeReason != null) ce.DischargeReason = dto.DischargeReason;

        ce.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Send SignalR notification for real-time dashboard updates (require-schedule widget)
        await SendAuthorizationChangeNotificationAsync(ce, "updated");

        return ce;
    }

    public async Task<CareEpisode?> MarkAsCompletedAsync(int careEpisodeId, string dischargeReason, int userId)
    {
        var ce = await _context.CareEpisodes.FindAsync(careEpisodeId);
        if (ce == null) return null;

        if (_tenantProvider.TenantId.HasValue && ce.TenantId != _tenantProvider.TenantId.Value)
            return null;

        ce.Status = (int)CareEpisodeStatus.Completed;
        ce.DischargeReason = dischargeReason;
        ce.CompletionMethod = (int)CareEpisodeCompletionMethod.Manual;
        ce.CompletedAt = DateTime.UtcNow;
        ce.CompletedByUserId = userId;
        ce.UpdatedAt = DateTime.UtcNow;

        if (!ce.EndDate.HasValue)
        {
            ce.EndDate = DateOnly.FromDateTime(DateTime.Today);
        }

        await _context.SaveChangesAsync();

        return ce;
    }

    public async Task<CareEpisode?> RestoreCareEpisodeAsync(int careEpisodeId, int userId)
    {
        var ce = await _context.CareEpisodes.FindAsync(careEpisodeId);
        if (ce == null) return null;

        if (_tenantProvider.TenantId.HasValue && ce.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Only completed episodes can be restored
        if (ce.Status != (int)CareEpisodeStatus.Completed)
            return null;

        // Restore to Active status and clear completion info
        ce.Status = (int)CareEpisodeStatus.Active;
        ce.CompletedAt = null;
        ce.CompletedByUserId = null;
        ce.CompletionMethod = null;
        ce.DischargeReason = null;
        ce.EndDate = null;
        ce.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Log the restoration action for audit purposes
        //_context.AuditLogs.Add(new AuditLog
        //{
        //    TenantId = _tenantProvider.TenantId ?? 0,
        //    UserId = userId,
        //    Action = "CareEpisode.Restore",
        //    ResourceType = "CareEpisode",
        //    ResourceId = careEpisodeId.ToString(),
        //    OldValue = "Completed",
        //    NewValue = "Active",
        //    Timestamp = DateTime.UtcNow
        //});
        //await _context.SaveChangesAsync();

        return ce;
    }

    public async Task<CareEpisode?> ExtendEndDateAsync(int careEpisodeId, DateOnly newEndDate)
    {
        var ce = await _context.CareEpisodes.FindAsync(careEpisodeId);
        if (ce == null) return null;

        if (_tenantProvider.TenantId.HasValue && ce.TenantId != _tenantProvider.TenantId.Value)
            return null;

        ce.EndDate = newEndDate;
        ce.UpdatedAt = DateTime.UtcNow;

        // If the episode was Overdue, and the new end date is in the future, set back to Active
        if (ce.Status == (int)CareEpisodeStatus.Overdue && newEndDate >= DateOnly.FromDateTime(DateTime.Today))
        {
            ce.Status = (int)CareEpisodeStatus.Active;
        }

        await _context.SaveChangesAsync();
        return ce;
    }

    public async Task LinkUnlinkedInitialEvaluationsAsync(int careEpisodeId, int patientId)
    {
        if (!_tenantProvider.TenantId.HasValue) return;

        // Find all New Patient Visit appointments for this patient that don't have a Care Episode linked
        var unlinkedAppts = await _context.Appointments
            .Where(a => a.TenantId == _tenantProvider.TenantId.Value)
            .Where(a => a.PatientId == patientId)
            .Where(a => a.Type == (int)AppointmentType.NewPatientVisit) // New Patient Visit
            .Where(a => a.CareEpisodeId == null)
            .ToListAsync();

        if (!unlinkedAppts.Any()) return;

        var linkedAppointmentIds = new List<int>();
        foreach (var appt in unlinkedAppts)
        {
            appt.CareEpisodeId = careEpisodeId;
            appt.UpdatedAt = DateTime.UtcNow;
            linkedAppointmentIds.Add(appt.AppointmentId);
        }

        await _context.SaveChangesAsync();

        // Also link any consents that belong to these appointments
        if (linkedAppointmentIds.Any())
        {
            var consentsToLink = await _context.CareEpisodeConsents
                .Where(c => c.TenantId == _tenantProvider.TenantId.Value)
                .Where(c => c.PatientId == patientId)
                .Where(c => c.CareEpisodeId == null)
                .Where(c => c.AppointmentId.HasValue && linkedAppointmentIds.Contains(c.AppointmentId.Value))
                .ToListAsync();

            foreach (var consent in consentsToLink)
            {
                consent.CareEpisodeId = careEpisodeId;
            }

            if (consentsToLink.Any())
            {
                await _context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Link orphan consents (consents without a care episode) to a newly created care episode.
    /// This handles the case where a patient signs consent at the kiosk before a care episode is created.
    /// </summary>
    private async Task LinkOrphanConsentsAsync(int careEpisodeId, int patientId)
    {
        if (!_tenantProvider.TenantId.HasValue) return;

        // Find all orphan consents for this patient (consents without a care episode)
        // that were signed within the last 30 days (reasonable window for linking)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var orphanConsents = await _context.CareEpisodeConsents
            .Where(c => c.TenantId == _tenantProvider.TenantId.Value)
            .Where(c => c.PatientId == patientId)
            .Where(c => c.CareEpisodeId == null)
            .Where(c => c.SignedAt >= thirtyDaysAgo)
            .ToListAsync();

        foreach (var consent in orphanConsents)
        {
            consent.CareEpisodeId = careEpisodeId;
        }

        if (orphanConsents.Any())
        {
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Check if a discharge appointment was completed and update care episode status accordingly.
    /// Note: VisitsUsed is no longer stored - it's calculated dynamically from appointments.
    /// </summary>
    public async Task UpdateVisitCountsAsync(int careEpisodeId)
    {
        var ce = await _context.CareEpisodes
            .Include(c => c.Appointments)
            .FirstOrDefaultAsync(c => c.CareEpisodeId == careEpisodeId);

        if (ce == null) return;

        // TODO: CareEpisode auto-completion logic - to be replaced with IM encounter workflow
        var dischargeCompleted = false;

        if (dischargeCompleted && ce.Status != (int)CareEpisodeStatus.Completed)
        {
            ce.Status = (int)CareEpisodeStatus.Completed;
            ce.CompletionMethod = (int)CareEpisodeCompletionMethod.Manual;
            ce.CompletedAt = DateTime.UtcNow;
            ce.DischargeReason = "Completed";
            ce.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Calculate visit counts dynamically from appointments collection.
    /// VisitsUsed = count of appointments with CheckedIn, InProgress, or Completed status.
    /// MissedVisits = count of NoShow appointments.
    /// </summary>
    private static (int visitsUsed, int missedVisits) CalculateVisitCounts(ICollection<Appointment> appointments)
    {
        if (appointments == null || appointments.Count == 0)
            return (0, 0);

        // Completed visits: appointments with CheckedIn, InProgress, or Completed status
        var completedVisits = appointments.Count(a =>
            a.Status == (int)AppointmentStatus.CheckedIn ||
            a.Status == (int)AppointmentStatus.InProgress ||
            a.Status == (int)AppointmentStatus.Completed);

        // Missed visits: appointments with NoShow status
        var missedVisits = appointments.Count(a =>
            a.Status == (int)AppointmentStatus.NoShow);

        return (completedVisits, missedVisits);
    }

    public async Task UpdateOverdueStatusesAsync()
    {
        if (!_tenantProvider.TenantId.HasValue) return;

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Find all Active care episodes where EndDate has passed
        var overdueEpisodes = await _context.CareEpisodes
            .Where(ce => ce.TenantId == _tenantProvider.TenantId.Value)
            .Where(ce => ce.Status == (int)CareEpisodeStatus.Active)
            .Where(ce => ce.EndDate.HasValue && ce.EndDate.Value < today)
            .ToListAsync();

        foreach (var ce in overdueEpisodes)
        {
            ce.Status = (int)CareEpisodeStatus.Overdue;
            ce.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<CareEpisodeDashboardDto> GetDashboardDataAsync(int? providerId = null)
    {
        var lowVisitsThreshold = await _settingsService.GetIntSettingAsync(SettingKeys.LowVisitsRemainingThreshold, 3);
        var noShowThreshold = await _settingsService.GetIntSettingAsync(SettingKeys.NoShowAlertThresholdMinutes, 30);

        // Update overdue statuses first
        await UpdateOverdueStatusesAsync();

        var activeEpisodes = await GetCareEpisodesAsync(status: (int)CareEpisodeStatus.Active, providerId: providerId);
        var overdueEpisodes = await GetCareEpisodesAsync(status: (int)CareEpisodeStatus.Overdue, providerId: providerId);

        var lowVisitsAlerts = await GetLowVisitsAlertsAsync(lowVisitsThreshold, providerId);
        var noShowAlerts = await GetNoShowAlertsAsync(noShowThreshold, providerId);

        // Get missed appointments
        var missedAppointments = await GetMissedAppointmentsAsync(providerId);

        return new CareEpisodeDashboardDto
        {
            ActiveEpisodes = activeEpisodes,
            LowVisitsAlerts = lowVisitsAlerts,
            OverdueEpisodes = overdueEpisodes.Select(e => new CareEpisodeAlertDto
            {
                CareEpisodeId = e.CareEpisodeId,
                PatientId = e.PatientId,
                PatientName = e.PatientName,
                AlertType = "Overdue",
                AlertMessage = $"Care Episode ended on {e.EndDate:MM/dd/yyyy} but has not been completed.",
                EndDate = e.EndDate,
                PrimaryDiagnosis = e.PrimaryDiagnosis
            }).ToList(),
            MissedAppointments = missedAppointments,
            NoShowAlerts = noShowAlerts,
            TotalActiveEpisodes = activeEpisodes.Count,
            TotalOverdueEpisodes = overdueEpisodes.Count,
            TotalLowVisitsAlerts = lowVisitsAlerts.Count,
            TotalNoShowAlerts = noShowAlerts.Count,
            TotalMissedAppointments = missedAppointments.Count
        };
    }

    /// <summary>
    /// Get alerts for care episodes with low remaining visits.
    /// This is based on ExpectedVisits (clinical goal) - purely clinical concern, no Insurance.
    /// For insurance authorization alerts, use the separate authorization tracking system.
    /// </summary>
    public async Task<List<CareEpisodeAlertDto>> GetLowVisitsAlertsAsync(int threshold, int? providerId = null)
    {
        // AsNoTracking: read-only alerts; Patient decrypted for DTO.
        var query = _context.CareEpisodes
            .AsNoTracking()
            .Include(ce => ce.Patient)
            .Include(ce => ce.Appointments)
            .Where(ce => ce.Status == (int)CareEpisodeStatus.Active || ce.Status == (int)CareEpisodeStatus.Overdue)
            .Where(ce => ce.ExpectedVisits.HasValue && ce.ExpectedVisits > 0); // Only episodes with expected visits

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(ce => ce.TenantId == _tenantProvider.TenantId.Value);
        }

        // LOCATION FILTERING: Filter by patient's location within the tenant
        if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(ce => ce.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        // Filter by provider - show care episodes where the provider is the primary provider
        // or where the provider has any appointment with this care episode
        if (providerId.HasValue)
        {
            query = query.Where(ce => ce.PrimaryProviderId == providerId ||
                                      ce.Appointments.Any(a => a.ProviderId == providerId));
        }

        var episodes = await query.ToListAsync();

        // Decrypt patient data for each episode
        foreach (var episode in episodes)
        {
            if (episode.Patient != null)
            {
                _encryptionHelper.DecryptEntity(episode.Patient);
            }
        }

        // Build alerts based on ExpectedVisits (clinical goal)
        var alerts = new List<CareEpisodeAlertDto>();
        foreach (var ce in episodes)
        {
            // Calculate visits dynamically from appointments
            var (visitsUsed, _) = CalculateVisitCounts(ce.Appointments);
            var visitsRemaining = (ce.ExpectedVisits ?? 0) - visitsUsed;

            if (visitsRemaining <= threshold && visitsRemaining >= 0)
            {
                alerts.Add(new CareEpisodeAlertDto
                {
                    CareEpisodeId = ce.CareEpisodeId,
                    PatientId = ce.PatientId,
                    PatientName = ce.Patient != null ? $"{ce.Patient.FirstName} {ce.Patient.LastName}" : "",
                    AlertType = "LowVisits",
                    AlertMessage = $"Only {visitsRemaining} of {ce.ExpectedVisits} expected visits remaining.",
                    VisitsRemaining = visitsRemaining,
                    EndDate = ce.EndDate,
                    PrimaryDiagnosis = $"{ce.PrimaryDiagnosisCode} - {ce.PrimaryDiagnosisDescription}"
                });
            }
        }

        return alerts;
    }

    public async Task<List<NoShowAlertDto>> GetNoShowAlertsAsync(int thresholdMinutes, int? providerId = null)
    {
        if (!_tenantProvider.TenantId.HasValue) return new List<NoShowAlertDto>();

        // Get location timezone for accurate "today" calculation
        var locationTimeZoneId = TimezoneHelper.DefaultTimeZoneId;
        if (_locationProvider.LocationId.HasValue)
        {
            var locationTz = await _context.Locations
                .Where(l => l.LocationId == _locationProvider.LocationId.Value)
                .Select(l => l.TimeZoneId)
                .FirstOrDefaultAsync();
            locationTimeZoneId = locationTz ?? TimezoneHelper.DefaultTimeZoneId;
        }

        // Get current time in location's timezone to determine correct "today"
        var utcNow = DateTime.UtcNow;
        var locationNow = TimezoneHelper.ConvertFromUtc(utcNow, locationTimeZoneId);
        var locationToday = DateOnly.FromDateTime(locationNow);

        // Get UTC boundaries for today in location timezone
        var todayStartUtc = TimezoneHelper.GetStartOfDayUtc(locationToday, locationTimeZoneId);
        var todayEndUtc = TimezoneHelper.GetEndOfDayUtc(locationToday, locationTimeZoneId);

        // Cutoff time is threshold minutes before current UTC time
        var cutoffTimeUtc = utcNow.AddMinutes(-thresholdMinutes);

        // Find appointments that are:
        // - Today (in location timezone)
        // - Scheduled status (not checked in)
        // - Start time has passed by more than threshold minutes
        // AsNoTracking: read-only alert list; Patient decrypted for DTO.
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Where(a => a.TenantId == _tenantProvider.TenantId.Value)
            .Where(a => a.StartTime >= todayStartUtc && a.StartTime <= todayEndUtc)
            .Where(a => a.Status == (int)AppointmentStatus.Scheduled)
            .Where(a => a.StartTime <= cutoffTimeUtc);

        // LOCATION FILTERING: Filter by patient's location within the tenant
        if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(a => a.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        // Filter by provider if specified
        if (providerId.HasValue)
        {
            query = query.Where(a => a.ProviderId == providerId);
        }

        var noShowAppts = await query.ToListAsync();

        // Decrypt patient data for each appointment
        foreach (var appt in noShowAppts)
        {
            if (appt.Patient != null)
            {
                _encryptionHelper.DecryptEntity(appt.Patient);
            }
        }

        return noShowAppts.Select(a => new NoShowAlertDto
        {
            AppointmentId = a.AppointmentId,
            PatientId = a.PatientId,
            PatientName = a.Patient != null ? $"{a.Patient.FirstName} {a.Patient.LastName}" : "",
            PatientEmail = a.Patient?.Email,
            PatientPhone = a.Patient?.Phone,
            ProviderId = a.ProviderId,
            ProviderName = a.Provider != null ? $"{a.Provider.FirstName} {a.Provider.LastName}" : "",
            ScheduledTime = a.StartTime,
            MinutesOverdue = (int)(utcNow - a.StartTime).TotalMinutes,
            CareEpisodeId = a.CareEpisodeId
        }).ToList();
    }

    private async Task<List<AppointmentListDto>> GetMissedAppointmentsAsync(int? providerId = null)
    {
        if (!_tenantProvider.TenantId.HasValue) return new List<AppointmentListDto>();

        // Get location timezone for accurate "today" calculation
        var locationTimeZoneId = TimezoneHelper.DefaultTimeZoneId;
        if (_locationProvider.LocationId.HasValue)
        {
            var locationTz = await _context.Locations
                .Where(l => l.LocationId == _locationProvider.LocationId.Value)
                .Select(l => l.TimeZoneId)
                .FirstOrDefaultAsync();
            locationTimeZoneId = locationTz ?? TimezoneHelper.DefaultTimeZoneId;
        }

        // Get current time in location's timezone to determine correct "today"
        var utcNow = DateTime.UtcNow;
        var locationNow = TimezoneHelper.ConvertFromUtc(utcNow, locationTimeZoneId);
        var locationToday = DateOnly.FromDateTime(locationNow);

        // Get UTC boundary for start of today in location timezone
        // Appointments before this are considered "past" (missed)
        var todayStartUtc = TimezoneHelper.GetStartOfDayUtc(locationToday, locationTimeZoneId);

        // Appointments from past dates that are still in Scheduled status
        // Exclude appointments that have been rescheduled (they are handled)
        // AsNoTracking: read-only alert list; Patient decrypted for DTO.
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Where(a => a.TenantId == _tenantProvider.TenantId.Value)
            .Where(a => a.StartTime < todayStartUtc)
            .Where(a => a.Status == (int)AppointmentStatus.Scheduled)
            .Where(a => a.RescheduledToAppointmentId == null);

        // LOCATION FILTERING: Filter by patient's location within the tenant
        if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(a => a.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        // Filter by provider if specified
        if (providerId.HasValue)
        {
            query = query.Where(a => a.ProviderId == providerId);
        }

        var missed = await query
            .OrderByDescending(a => a.StartTime)
            .Take(50)
            .ToListAsync();

        // Decrypt patient data for each appointment
        foreach (var appt in missed)
        {
            if (appt.Patient != null)
            {
                _encryptionHelper.DecryptEntity(appt.Patient);
            }
        }

        // Use location's today for DaysMissed calculation
        var locationTodayDateTime = locationToday.ToDateTime(TimeOnly.MinValue);
        return missed.Select(a => new AppointmentListDto
        {
            AppointmentId = a.AppointmentId,
            PatientId = a.PatientId,
            PatientName = a.Patient != null ? $"{a.Patient.FirstName} {a.Patient.LastName}" : "",
            PatientMRN = a.Patient?.Mrn,
            PatientEmail = a.Patient?.Email,
            PatientPhone = a.Patient?.Phone,
            ProviderId = a.ProviderId,
            ProviderName = a.Provider != null ? $"{a.Provider.FirstName} {a.Provider.LastName}" : "",
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            Type = a.Type,
            Status = a.Status ?? 0,
            DaysMissed = (int)(locationTodayDateTime - TimezoneHelper.ConvertFromUtc(a.StartTime, locationTimeZoneId).Date).TotalDays,
            CareEpisodeId = a.CareEpisodeId,
            RescheduledToAppointmentId = a.RescheduledToAppointmentId,
            RescheduledFromAppointmentId = a.RescheduledFromAppointmentId
        }).ToList();
    }

    /// <summary>
    /// Helper method to get note type name from type integer
    /// </summary>
    private static string GetNoteTypeName(int type) => type switch
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
        _ => "Clinical Note"
    };

    /// <summary>
    /// Get Care Episodes that need appointments scheduled.
    /// Returns episodes where the number of scheduled (future) appointments is less than expected visits.
    /// For Admin and Front Desk dashboard card.
    /// </summary>
    public async Task<List<RequireScheduleDto>> GetCareEpisodesNeedingScheduleAsync()
    {
        if (!_tenantProvider.TenantId.HasValue) return new List<RequireScheduleDto>();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayDateTime = DateTime.UtcNow.Date;

        // Query active care episodes with expected visits
        // AsNoTracking: read-only schedule-needed list; Patient decrypted for DTO.
        var query = _context.CareEpisodes
            .AsNoTracking()
            .Include(ce => ce.Patient)
            .Include(ce => ce.Appointments)
            .Where(ce => ce.TenantId == _tenantProvider.TenantId.Value)
            .Where(ce => ce.Status == (int)CareEpisodeStatus.Active || ce.Status == (int)CareEpisodeStatus.Overdue)
            .Where(ce => ce.ExpectedVisits.HasValue && ce.ExpectedVisits > 0);

        // LOCATION FILTERING: Filter by patient's location within the tenant
        if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(ce => ce.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        var episodes = await query.ToListAsync();

        // Decrypt patient data and build result
        var result = new List<RequireScheduleDto>();
        foreach (var ce in episodes)
        {
            if (ce.Patient != null)
            {
                _encryptionHelper.DecryptEntity(ce.Patient);
            }

            // Count only Follow-Up Visit type appointments (excluding New Patient Visit, etc.)
            // Only count appointments that are not cancelled (Scheduled, CheckedIn, InProgress, or Completed)
            var scheduledAppointments = ce.Appointments.Count(a =>
                a.Type == (int)AppointmentType.FollowUpVisit && // Only Follow-Up Visit type
                (a.Status == (int)AppointmentStatus.Scheduled ||
                 a.Status == (int)AppointmentStatus.CheckedIn ||
                 a.Status == (int)AppointmentStatus.InProgress ||
                 a.Status == (int)AppointmentStatus.Completed));

            var expectedVisits = ce.ExpectedVisits ?? 0;
            var requiredAppointments = expectedVisits - scheduledAppointments;

            // Only include if there are appointments that need to be scheduled
            if (requiredAppointments > 0)
            {
                result.Add(new RequireScheduleDto
                {
                    CareEpisodeId = ce.CareEpisodeId,
                    PatientId = ce.PatientId,
                    PatientName = ce.Patient != null ? $"{ce.Patient.FirstName} {ce.Patient.LastName}" : "",
                    PatientMRN = ce.Patient?.Mrn,
                    PatientPhone = ce.Patient?.Phone,
                    PatientEmail = ce.Patient?.Email,
                    PatientDOB = ce.Patient?.DateOfBirth,
                    ExpectedVisits = expectedVisits,
                    ScheduledAppointments = scheduledAppointments,
                    RequiredAppointments = requiredAppointments,
                    VisitFrequency = ce.VisitFrequency,
                    StartDate = ce.StartDate,
                    EndDate = ce.EndDate,
                    CreatedAt = ce.CreatedAt
                });
            }
        }

        // Sort by most appointments needed first
        return result.OrderByDescending(r => r.RequiredAppointments).ToList();
    }

    /// <summary>
    /// Sends SignalR notification for care episode/authorization changes to enable real-time dashboard updates
    /// </summary>
    private async Task SendAuthorizationChangeNotificationAsync(CareEpisode careEpisode, string changeType)
    {
        if (_notificationService == null || !_tenantProvider.TenantId.HasValue)
            return;

        try
        {
            // Load patient if not already loaded
            var patient = careEpisode.Patient ?? await _context.Patients.FindAsync(careEpisode.PatientId);

            // CRITICAL (PHI encryption safety):
            // Detach patient before decryption. This helper runs after care
            // episode CRUD where the caller's DbContext has a tracked careEpisode
            // and will SaveChanges. Without detach, the decrypted patient would
            // be flushed to the DB.
            if (patient != null
                && _context.Entry(patient).State != EntityState.Detached)
                _context.Entry(patient).State = EntityState.Detached;

            // Decrypt patient name if needed
            string patientName = "";
            if (patient != null)
            {
                _encryptionHelper?.DecryptEntity(patient);
                patientName = $"{patient.FirstName} {patient.LastName}";
            }

            // Calculate current scheduled appointments
            var appointments = careEpisode.Appointments ?? await _context.Appointments
                .Where(a => a.CareEpisodeId == careEpisode.CareEpisodeId)
                .ToListAsync();

            var followUpTypes = new List<int> { 1, 2 }; // FollowUp (1), ReEvaluation (2)
            var validStatuses = new List<int> { 0, 1, 2, 3, 4 }; // Scheduled, Confirmed, CheckedIn, InProgress, Completed

            var scheduledAppointments = appointments
                .Count(a => followUpTypes.Contains(a.Type) && a.Status.HasValue && validStatuses.Contains(a.Status.Value));

            var expectedVisits = careEpisode.ExpectedVisits ?? 0;
            var requiredAppointments = Math.Max(0, expectedVisits - scheduledAppointments);

            var notification = new AuthorizationChangeNotification
            {
                CareEpisodeId = careEpisode.CareEpisodeId,
                ChangeType = changeType,
                PatientId = careEpisode.PatientId,
                PatientName = patientName,
                RequiredAppointments = requiredAppointments,
                ScheduledAppointments = scheduledAppointments,
                ExpectedVisits = expectedVisits,
                ChangedAt = DateTime.UtcNow,
                ChangedByUserId = null
            };

            await _notificationService.NotifyAuthorizationChangedAsync(_tenantProvider.TenantId.Value, notification);
        }
        catch (Exception)
        {
            // Don't fail the main operation if notification fails
            // Logging is handled by the notification service
        }
    }
}
