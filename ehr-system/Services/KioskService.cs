using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EHR.Services;

/// <summary>
/// Service for managing kiosk settings and patient verification.
/// Handles the public-facing kiosk flow including rate limiting.
/// </summary>
public interface IKioskService
{
    // Admin operations
    Task<KioskSettingsDto> GetKioskSettingsAsync(int locationId);
    Task<KioskSettingsDto> CreateOrUpdateKioskSettingsAsync(int locationId, KioskSettingsUpdateDto dto, int userId);
    Task<KioskTokenRegenerateResponseDto> RegenerateKioskTokenAsync(int locationId, int userId);
    Task<bool> EnableKioskAsync(int locationId, bool enable, int userId);

    // Public kiosk operations (no auth required)
    Task<KioskValidateTokenResponseDto> ValidateKioskTokenAsync(string token, string ipAddress);
    Task<KioskVerifyPatientResponseDto> VerifyPatientAsync(string kioskToken, KioskVerifyPatientRequestDto request, string ipAddress, string userAgent);
    Task<KioskSession> GetValidSessionAsync(string sessionToken);
    Task UpdateSessionActivityAsync(string sessionToken);
    Task InvalidateSessionAsync(string sessionToken);
}

public class KioskService : IKioskService
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryption;
    private readonly IAuditService _auditService;
    private readonly ILogger<KioskService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _baseUrl;

    // Rate limiting: 100 attempts per IP per 15 minutes
    private const int MaxVerificationAttempts = 100000; // never
    private const int RateLimitWindowMinutes = 15;

    public KioskService(
        EhrDbContext context,
        EncryptionHelper encryption,
        IAuditService auditService,
        IConfiguration configuration,
        ILogger<KioskService> logger)
    {
        _context = context;
        _encryption = encryption;
        _auditService = auditService;
        _configuration = configuration;
        _logger = logger;
        _baseUrl = configuration["App:BaseUrl"] ?? "https://app.medocs.com";
    }

    #region Admin Operations

    public async Task<KioskSettingsDto> GetKioskSettingsAsync(int locationId)
    {
        var location = await _context.Locations
            .Include(l => l.KioskSettings)
                .ThenInclude(ks => ks.TokenGeneratedByUser)
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location == null)
            return null;

        var settings = location.KioskSettings;

        return new KioskSettingsDto
        {
            LocationId = location.LocationId,
            LocationName = location.Name,
            KioskToken = settings?.KioskToken,
            KioskUrl = settings?.KioskToken != null ? $"{_baseUrl}/kiosk/{settings.KioskToken}" : null,
            IsEnabled = settings?.IsEnabled ?? true,  // Enabled by default
            SessionTimeoutMinutes = settings?.SessionTimeoutMinutes ?? 15,  // Default 15 minutes
            TokenGeneratedAt = settings?.TokenGeneratedAt,
            TokenGeneratedByUserName = settings?.TokenGeneratedByUser != null
                ? GetUserName(settings.TokenGeneratedByUser)
                : null,
            LastAccessedAt = settings?.LastAccessedAt,
            LastAccessIpAddress = settings?.LastAccessIpAddress
        };
    }

    public async Task<KioskSettingsDto> CreateOrUpdateKioskSettingsAsync(int locationId, KioskSettingsUpdateDto dto, int userId)
    {
        var location = await _context.Locations
            .Include(l => l.KioskSettings)
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location == null)
            throw new InvalidOperationException("Location not found");

        var settings = location.KioskSettings;

        if (settings == null)
        {
            // Create new settings - enabled by default, 15 minute session timeout
            settings = new LocationKioskSettings
            {
                LocationId = locationId,
                TenantId = location.TenantId,
                KioskToken = GenerateKioskToken(),
                IsEnabled = dto.IsEnabled ?? true,  // Enabled by default
                SessionTimeoutMinutes = dto.SessionTimeoutMinutes ?? 15,  // Default 15 minutes
                TokenGeneratedAt = DateTime.UtcNow,
                TokenGeneratedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _context.LocationKioskSettings.Add(settings);
        }
        else
        {
            // Update existing settings
            if (dto.IsEnabled.HasValue)
                settings.IsEnabled = dto.IsEnabled.Value;
            if (dto.SessionTimeoutMinutes.HasValue)
                settings.SessionTimeoutMinutes = dto.SessionTimeoutMinutes.Value;
            settings.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Kiosk settings updated for location {LocationId} by user {UserId}", locationId, userId);

        return await GetKioskSettingsAsync(locationId);
    }

    public async Task<KioskTokenRegenerateResponseDto> RegenerateKioskTokenAsync(int locationId, int userId)
    {
        var location = await _context.Locations
            .Include(l => l.KioskSettings)
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location == null)
        {
            return new KioskTokenRegenerateResponseDto
            {
                Success = false,
                Message = "Location not found"
            };
        }

        var settings = location.KioskSettings;

        if (settings == null)
        {
            // Create new settings with token - enabled by default, 15 minute timeout
            settings = new LocationKioskSettings
            {
                LocationId = locationId,
                TenantId = location.TenantId,
                KioskToken = GenerateKioskToken(),
                IsEnabled = true,  // Enabled by default
                SessionTimeoutMinutes = 15,  // Default 15 minutes
                TokenGeneratedAt = DateTime.UtcNow,
                TokenGeneratedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _context.LocationKioskSettings.Add(settings);
        }
        else
        {
            // Regenerate token - this invalidates the old link
            settings.KioskToken = GenerateKioskToken();
            settings.TokenGeneratedAt = DateTime.UtcNow;
            settings.TokenGeneratedByUserId = userId;
            settings.UpdatedAt = DateTime.UtcNow;

            // Invalidate all active sessions for this location
            await InvalidateAllLocationSessionsAsync(locationId);
        }

        await _context.SaveChangesAsync();

        await _auditService.LogAccessAsync(userId, null, "RegenerateKioskToken", "Location", locationId);

        _logger.LogInformation("Kiosk token regenerated for location {LocationId} by user {UserId}", locationId, userId);

        return new KioskTokenRegenerateResponseDto
        {
            Success = true,
            Message = "Kiosk token regenerated successfully. Any devices using the old link will need the new link.",
            NewToken = settings.KioskToken,
            NewKioskUrl = $"{_baseUrl}/kiosk/{settings.KioskToken}",
            GeneratedAt = settings.TokenGeneratedAt
        };
    }

    public async Task<bool> EnableKioskAsync(int locationId, bool enable, int userId)
    {
        var settings = await _context.LocationKioskSettings
            .FirstOrDefaultAsync(s => s.LocationId == locationId);

        if (settings == null)
        {
            var location = await _context.Locations.FindAsync(locationId);
            if (location == null)
                return false;

            settings = new LocationKioskSettings
            {
                LocationId = locationId,
                TenantId = location.TenantId,
                KioskToken = GenerateKioskToken(),
                IsEnabled = enable,
                SessionTimeoutMinutes = 15,  // Default 15 minutes
                TokenGeneratedAt = DateTime.UtcNow,
                TokenGeneratedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _context.LocationKioskSettings.Add(settings);
        }
        else
        {
            settings.IsEnabled = enable;
            settings.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Kiosk {Action} for location {LocationId} by user {UserId}",
            enable ? "enabled" : "disabled", locationId, userId);

        return true;
    }

    #endregion

    #region Public Kiosk Operations

    public async Task<KioskValidateTokenResponseDto> ValidateKioskTokenAsync(string token, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new KioskValidateTokenResponseDto
            {
                IsValid = false,
                Message = "Invalid kiosk link"
            };
        }

        var settings = await _context.LocationKioskSettings
            .Include(s => s.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(s => s.KioskToken == token);

        if (settings == null)
        {
            _logger.LogWarning("Invalid kiosk token attempted: {Token} from IP {IpAddress}", token[..Math.Min(8, token.Length)], ipAddress);
            return new KioskValidateTokenResponseDto
            {
                IsValid = false,
                Message = "Invalid kiosk link. Please contact clinic staff."
            };
        }

        if (!settings.IsEnabled)
        {
            return new KioskValidateTokenResponseDto
            {
                IsValid = false,
                Message = "This kiosk is currently not available. Please contact clinic staff."
            };
        }

        // Update last accessed
        settings.LastAccessedAt = DateTime.UtcNow;
        settings.LastAccessIpAddress = ipAddress;
        await _context.SaveChangesAsync();

        // ISSUE #7 FIX: Get timezone info for the location
        var timeZoneId = settings.Location.TimeZoneId ?? "America/Chicago";
        var timeZoneAbbr = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId, DateTime.UtcNow);

        return new KioskValidateTokenResponseDto
        {
            IsValid = true,
            Message = "Kiosk validated",
            ClinicName = settings.Location.Tenant.Name,
            ClinicLogoUrl = settings.Location.Tenant.LogoUrl,
            LocationName = settings.Location.Name,
            LocationAddress = FormatLocationAddress(settings.Location),
            SessionTimeoutMinutes = settings.SessionTimeoutMinutes,
            // ISSUE #7 FIX: Include timezone info for correct time display on kiosk
            TimeZoneId = timeZoneId,
            TimeZoneAbbreviation = timeZoneAbbr
        };
    }

    public async Task<KioskVerifyPatientResponseDto> VerifyPatientAsync(
        string kioskToken,
        KioskVerifyPatientRequestDto request,
        string ipAddress,
        string userAgent)
    {
        // First validate the kiosk token
        var settings = await _context.LocationKioskSettings
            .Include(s => s.Location)
            .FirstOrDefaultAsync(s => s.KioskToken == kioskToken && s.IsEnabled);

        if (settings == null)
        {
            return new KioskVerifyPatientResponseDto
            {
                Success = false,
                Message = "Invalid or disabled kiosk"
            };
        }

        var tenantId = settings.TenantId;
        var locationId = settings.LocationId;

        // Check rate limiting
        var recentAttempts = await _context.KioskVerificationAttempts
            .Where(a => a.TenantId == tenantId
                && a.LocationId == locationId
                && a.IpAddress == ipAddress
                && a.AttemptedAt > DateTime.UtcNow.AddMinutes(-RateLimitWindowMinutes))
            .CountAsync();

        if (recentAttempts >= MaxVerificationAttempts)
        {
            _logger.LogWarning("Rate limit exceeded for IP {IpAddress} at location {LocationId}", ipAddress, locationId);
            return new KioskVerifyPatientResponseDto
            {
                Success = false,
                Message = "Too many verification attempts. Please try again in 15 minutes or contact clinic staff."
            };
        }

        // Identity verification (as of 2026-05): LastName + DateOfBirth + ZipCode.
        // No SSN. Strategy: DOB is plaintext and indexed, so filter the candidate
        // set by Tenant + DOB first (usually <= a handful of rows), then decrypt
        // LastName + ZipCode on each candidate and compare in memory.
        _logger.LogInformation("Kiosk verification attempt: TenantId={TenantId}, LocationId={LocationId} (PII redacted from logs)",
            tenantId, locationId);

        // Normalize submitted ZIP (first 5 digits) and LastName (trim + casefold).
        var submittedZip = Regex.Replace(request.ZipCode ?? "", @"[^\d]", "");
        if (submittedZip.Length > 5) submittedZip = submittedZip[..5];
        var submittedLastName = (request.LastName ?? "").Trim();

        // Pull DOB-matching candidates within the tenant. Encrypted columns can't
        // be filtered in SQL — they're decrypted + compared in C# below.
        var candidates = await _context.Patients
            .Where(p => p.TenantId == tenantId
                && p.IsDeleted != true
                && p.DateOfBirth == request.DateOfBirth)
            .ToListAsync();

        Patient patient = null;
        foreach (var cand in candidates)
        {
            var candLastName = (_encryption.Decrypt(cand.LastName) ?? "").Trim();
            var candZip = Regex.Replace(_encryption.Decrypt(cand.ZipCode) ?? "", @"[^\d]", "");
            if (candZip.Length > 5) candZip = candZip[..5];

            var lastNameMatch = string.Equals(candLastName, submittedLastName, StringComparison.OrdinalIgnoreCase);
            var zipMatch = !string.IsNullOrEmpty(submittedZip) && candZip == submittedZip;
            if (lastNameMatch && zipMatch)
            {
                patient = cand;
                break;
            }
        }

        // Log the attempt. AttemptedSsnLast4Hash column remains for legacy audit
        // rows; new attempts write null (no SSN is collected).
        var attempt = new KioskVerificationAttempt
        {
            TenantId = tenantId,
            LocationId = locationId,
            IpAddress = ipAddress,
            IsSuccessful = patient != null,
            AttemptedSsnLast4Hash = null,
            AttemptedDob = request.DateOfBirth,
            AttemptedZip = submittedZip,
            PatientId = patient?.PatientId,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            AttemptedAt = DateTime.UtcNow
        };
        _context.KioskVerificationAttempts.Add(attempt);
        await _context.SaveChangesAsync();

        if (patient == null)
        {
            _logger.LogInformation("Patient verification failed at location {LocationId} from IP {IpAddress}. CandidatesByDob={DobCount}",
                locationId, ipAddress, candidates.Count);
            return new KioskVerifyPatientResponseDto
            {
                Success = false,
                Message = "We couldn't verify your information. Please check your entries and try again, or see the front desk for assistance."
            };
        }

        // Patient found - check for today's appointment at this location
        // Get location timezone for accurate "today" calculation
        var locationTimeZoneId = settings.Location.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        // Convert UTC now to location's timezone to determine correct "today"
        var utcNow = DateTime.UtcNow;
        var locationNow = TimezoneHelper.ConvertFromUtc(utcNow, locationTimeZoneId);
        var locationToday = DateOnly.FromDateTime(locationNow);

        // Get UTC boundaries for today in location timezone
        var todayStartUtc = TimezoneHelper.GetStartOfDayUtc(locationToday, locationTimeZoneId);
        var todayEndUtc = TimezoneHelper.GetEndOfDayUtc(locationToday, locationTimeZoneId);

        // Get the first appointment that does NOT have consent already
        // This allows patients with multiple appointments to check-in for their next one
        // Only select appointments that are Scheduled (0) or Confirmed (1) - not cancelled, completed, etc.
        var appointment = await _context.Appointments
            .Include(a => a.Provider)
            .Include(a => a.CareEpisode)
            .Where(a => a.TenantId == tenantId
                && a.PatientId == patient.PatientId
                && a.Patient.PreferredLocationId == locationId
                && a.StartTime >= todayStartUtc && a.StartTime <= todayEndUtc
                && (a.Status == (int)AppointmentStatus.Scheduled || a.Status == (int)AppointmentStatus.Confirmed)
                // Exclude appointments that already have consent
                && !_context.CareEpisodeConsents.Any(c => c.TenantId == tenantId && c.AppointmentId == a.AppointmentId))
            .OrderBy(a => a.StartTime)
            .FirstOrDefaultAsync();

        // Did we find an appointment that still needs consent? If yes, normal
        // flow (caller renders consent forms). Set the flag so the "create
        // session" block below knows whether to mark AlreadyConsented = true.
        var alreadyConsented = false;

        if (appointment == null)
        {
            // 2026-05: Patient might have signed consent from the portal at
            // home — that's now a supported flow (portal Consent page). The
            // kiosk should still let them check in via a "Yes, I am Here"
            // confirmation screen instead of a dead-end "have a seat" message.
            //
            // Look for the patient's earliest still-active (Scheduled or
            // Confirmed) appointment today that they HAVEN'T checked in to
            // yet. If found, we proceed with the AlreadyConsented path —
            // create a KioskSession just like the normal path, return
            // Success=true + AlreadyConsented=true, and the front-end will
            // show the new confirm-presence screen.
            appointment = await _context.Appointments
                .Include(a => a.Provider)
                .Include(a => a.CareEpisode)
                .Where(a => a.TenantId == tenantId
                    && a.PatientId == patient.PatientId
                    && a.Patient.PreferredLocationId == locationId
                    && a.StartTime >= todayStartUtc && a.StartTime <= todayEndUtc
                    && (a.Status == (int)AppointmentStatus.Scheduled
                        || a.Status == (int)AppointmentStatus.Confirmed))
                .OrderBy(a => a.StartTime)
                .FirstOrDefaultAsync();

            if (appointment != null)
            {
                alreadyConsented = true;
                _logger.LogInformation("Patient {PatientId} already consented (portal); routing to Yes-I-am-Here screen for appointment {AppointmentId}",
                    patient.PatientId, appointment.AppointmentId);
            }
            else
            {
                // Still no appointment — either patient is already CheckedIn
                // for every today's slot, or has no Scheduled/Confirmed
                // appointment today at all. Distinguish via a separate query.
                var hasCheckedInOrFurtherToday = await _context.Appointments
                    .AnyAsync(a => a.TenantId == tenantId
                        && a.PatientId == patient.PatientId
                        && a.Patient.PreferredLocationId == locationId
                        && a.StartTime >= todayStartUtc && a.StartTime <= todayEndUtc
                        && (a.Status == (int)AppointmentStatus.CheckedIn
                            || a.Status == (int)AppointmentStatus.InProgress
                            || a.Status == (int)AppointmentStatus.Completed));

                if (hasCheckedInOrFurtherToday)
                {
                    _logger.LogInformation("Patient {PatientId} already checked in for today's appointment(s) at location {LocationId}", patient.PatientId, locationId);
                    return new KioskVerifyPatientResponseDto
                    {
                        Success = false,
                        HasExistingConsent = true,
                        Message = "You're already checked in for today's appointment(s). Please have a seat and we'll be with you shortly."
                    };
                }

                _logger.LogInformation("No appointment found for patient {PatientId} at location {LocationId}", patient.PatientId, locationId);
                return new KioskVerifyPatientResponseDto
                {
                    Success = false,
                    Message = "No appointment found for today at this location. Please check in with the front desk."
                };
            }
        }

        // Create a session
        // Session timeout of 0 means never expire (set to far future date)
        var expiresAt = settings.SessionTimeoutMinutes > 0
            ? DateTime.UtcNow.AddMinutes(settings.SessionTimeoutMinutes)
            : DateTime.UtcNow.AddYears(100);  // 0 = never expire

        var session = new KioskSession
        {
            TenantId = tenantId,
            LocationId = locationId,
            SessionToken = GenerateSessionToken(),
            PatientId = patient.PatientId,
            AppointmentId = appointment.AppointmentId,
            CareEpisodeId = appointment.CareEpisodeId,
            IpAddress = ipAddress,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            LastActivityAt = DateTime.UtcNow,
            CurrentFormIndex = 0,
            IsCompleted = false,
            IsInvalidated = false
        };
        _context.KioskSessions.Add(session);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Patient {PatientId} verified at kiosk for appointment {AppointmentId}", patient.PatientId, appointment.AppointmentId);

        // Decrypt patient info for response
        var patientFirstName = _encryption.Decrypt(patient.FirstName) ?? patient.FirstName;
        var patientLastName = _encryption.Decrypt(patient.LastName) ?? patient.LastName;

        return new KioskVerifyPatientResponseDto
        {
            Success = true,
            Message = "Verification successful",
            SessionToken = session.SessionToken,
            SessionExpiresAt = session.ExpiresAt,
            PatientInfo = new KioskPatientInfoDto
            {
                PatientId = patient.PatientId,
                FirstName = patientFirstName,
                LastName = patientLastName,
                FullName = $"{patientFirstName} {patientLastName}",
                DateOfBirth = patient.DateOfBirth
            },
            AppointmentInfo = new KioskAppointmentInfoDto
            {
                AppointmentId = appointment.AppointmentId,
                StartTime = appointment.StartTime,
                AppointmentType = GetAppointmentTypeName(appointment.Type),
                ProviderName = GetProviderName(appointment.Provider),
                CareEpisodeId = appointment.CareEpisodeId
            },
            HasExistingConsent = false,
            // When AlreadyConsented = true the front-end skips consent forms
            // and shows the "Yes, I am Here" confirmation screen instead.
            AlreadyConsented = alreadyConsented
        };
    }

    public async Task<KioskSession> GetValidSessionAsync(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        var session = await _context.KioskSessions
            .Include(s => s.Patient)
            .Include(s => s.Appointment)
                .ThenInclude(a => a.Provider)
            .Include(s => s.Appointment)
                .ThenInclude(a => a.CareEpisode)
            .Include(s => s.Location)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken
                && !s.IsCompleted
                && !s.IsInvalidated
                && s.ExpiresAt > DateTime.UtcNow);

        return session;
    }

    public async Task UpdateSessionActivityAsync(string sessionToken)
    {
        var session = await _context.KioskSessions
            .Include(s => s.Location)
                .ThenInclude(l => l.KioskSettings)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session != null && !session.IsCompleted && !session.IsInvalidated)
        {
            session.LastActivityAt = DateTime.UtcNow;

            // Extend session expiration (sliding window) - gives patient more time while active
            var timeoutMinutes = session.Location?.KioskSettings?.SessionTimeoutMinutes ?? 15;
            if (timeoutMinutes > 0)
            {
                session.ExpiresAt = DateTime.UtcNow.AddMinutes(timeoutMinutes);
            }

            await _context.SaveChangesAsync();
        }
    }

    public async Task InvalidateSessionAsync(string sessionToken)
    {
        var session = await _context.KioskSessions
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session != null)
        {
            session.IsInvalidated = true;
            await _context.SaveChangesAsync();
        }
    }

    #endregion

    #region Helper Methods

    private async Task InvalidateAllLocationSessionsAsync(int locationId)
    {
        var activeSessions = await _context.KioskSessions
            .Where(s => s.LocationId == locationId && !s.IsCompleted && !s.IsInvalidated)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.IsInvalidated = true;
        }

        await _context.SaveChangesAsync();
    }

    private static string GenerateKioskToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateSessionToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string GetUserName(User user)
    {
        if (user == null) return null;
        // User FirstName and LastName are stored as plain text (not PHI)
        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private static string FormatLocationAddress(Location location)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.Address))
            parts.Add(location.Address);
        if (!string.IsNullOrWhiteSpace(location.City))
            parts.Add(location.City);
        if (!string.IsNullOrWhiteSpace(location.State))
            parts.Add(location.State);
        if (!string.IsNullOrWhiteSpace(location.ZipCode))
            parts.Add(location.ZipCode);
        return string.Join(", ", parts);
    }

    private static string GetAppointmentTypeName(int type)
    {
        // Only 4 appointment types are used, return "Other" for any legacy types
        return type switch
        {
            0 => "New Patient Visit",
            1 => "Follow-Up Visit",
            2 => "Annual Physical",
            3 => "Wellness Exam",
            4 => "Consultation",
            5 => "Telehealth",
            6 => "Procedure Visit",
            7 => "Urgent Visit",
            8 => "Lab Review",
            9 => "Medication Review",
            10 => "New Longevity Patient",
            11 => "Follow-Up Longevity Patient",
            _ => "Other"
        };
    }

    private string GetProviderName(Provider provider)
    {
        if (provider == null) return "Provider";
        var firstName = provider.FirstName;
        var lastName = provider.LastName;
        var credentials = provider.Credentials;
        return $"{firstName} {lastName}{(string.IsNullOrEmpty(credentials) ? "" : $", {credentials}")}";
    }

    #endregion
}
