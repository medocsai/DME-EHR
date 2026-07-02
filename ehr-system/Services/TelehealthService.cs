using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Hubs;

namespace EHR.Services;

public interface ITelehealthService
{
    /// <summary>
    /// Generate a unique telehealth token and Jitsi room name for an appointment.
    /// </summary>
    Task<(string Token, string RoomName)> GenerateTelehealthSessionAsync(int appointmentId);

    /// <summary>
    /// Send telehealth invite email to the patient with join link.
    /// </summary>
    Task<bool> SendTelehealthInviteEmailAsync(int appointmentId);

    /// <summary>
    /// Validate a telehealth token and return appointment information for the patient join page.
    /// </summary>
    Task<TelehealthJoinInfo> ValidateTokenAsync(string token);

    /// <summary>
    /// Handle patient joining the waiting room — auto check-in + send notifications.
    /// </summary>
    Task<TelehealthJoinResult> PatientJoinWaitingRoomAsync(string token);

    /// <summary>
    /// Admit the patient into the video call — sends PatientAdmitted event via SignalR.
    /// </summary>
    Task<bool> AdmitPatientAsync(int appointmentId, int admittedByUserId);

    /// <summary>
    /// Get Jitsi configuration for a telehealth appointment (domain, room name, etc).
    /// </summary>
    Task<TelehealthConfigDto> GetJitsiConfigAsync(int appointmentId, string displayName, bool isProvider);

    /// <summary>
    /// Verify patient identity (SSN last 4 + DOB) before allowing them into the telehealth waiting room.
    /// </summary>
    Task<TelehealthVerifyIdentityResult> VerifyPatientIdentityAsync(string token, string lastName, DateOnly dateOfBirth, string zipCode);

}

public class TelehealthService : ITelehealthService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ITelehealthNotificationService _telehealthNotificationService;
    private readonly IScheduleNotificationService _scheduleNotificationService;
    private readonly ILogger<TelehealthService> _logger;

    public TelehealthService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryptionHelper,
        IEmailService emailService,
        IConfiguration config,
        ITelehealthNotificationService telehealthNotificationService,
        IScheduleNotificationService scheduleNotificationService,
        ILogger<TelehealthService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
        _emailService = emailService;
        _config = config;
        _telehealthNotificationService = telehealthNotificationService;
        _scheduleNotificationService = scheduleNotificationService;
        _logger = logger;
    }

    public async Task<(string Token, string RoomName)> GenerateTelehealthSessionAsync(int appointmentId)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            throw new ArgumentException($"Appointment {appointmentId} not found");

        // Generate secure token (GUID-based, URL-safe)
        var token = Guid.NewGuid().ToString("N"); // 32-char hex string

        // Generate Jitsi room name from a FRESH 128-bit random GUID, independent
        // of the access token — knowing one does not reveal the other. The old
        // format ("prefix-{tenantId}-{appointmentId}-{first 8 hex}") leaked tenant
        // / appointment IDs in URL logs and was guessable: only 32 bits of
        // entropy in the suffix and three predictable prefix components.
        var roomPrefix = _config["Telehealth:RoomPrefix"] ?? "imehr";
        var roomRandom = Guid.NewGuid().ToString("N"); // 32 hex chars = 128 bits entropy
        var roomName = $"{roomPrefix}-{roomRandom}";

        // Save to appointment
        appointment.TelehealthToken = token;
        appointment.TelehealthUrl = roomName;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Generated telehealth session for appointment {AppointmentId}: room={RoomName}",
            appointmentId, roomName);

        return (token, roomName);
    }

    public async Task<bool> SendTelehealthInviteEmailAsync(int appointmentId)
    {
        try
        {
            // AsNoTracking: read-only email send; Patient/Provider decrypted in-place,
            // must not be tracked.
            var appointment = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Patient)
                .Include(a => a.Provider)
                .Include(a => a.Tenant)
                .Include(a => a.Location)
                .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

            if (appointment == null)
            {
                _logger.LogWarning("Cannot send telehealth email: appointment {AppointmentId} not found", appointmentId);
                return false;
            }

            if (string.IsNullOrWhiteSpace(appointment.TelehealthToken))
            {
                _logger.LogWarning("Cannot send telehealth email: no token for appointment {AppointmentId}", appointmentId);
                return false;
            }

            // Decrypt PHI for email content
            if (appointment.Patient != null)
                _encryptionHelper.DecryptEntity(appointment.Patient);
            if (appointment.Provider != null)
                _encryptionHelper.DecryptEntity(appointment.Provider);

            var patientEmail = appointment.Patient?.Email;
            if (string.IsNullOrWhiteSpace(patientEmail))
            {
                _logger.LogWarning("Cannot send telehealth email: patient has no email for appointment {AppointmentId}",
                    appointmentId);
                return false;
            }

            var patientFirstName = appointment.Patient?.FirstName ?? "Patient";
            var providerName = appointment.Provider != null
                ? $"Dr. {appointment.Provider.LastName}"
                : "your provider";
            var clinicName = appointment.Tenant?.Name ?? "MEDOCS";
            var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
            var joinLink = $"{baseUrl}/Telehealth/Join/{appointment.TelehealthToken}";
            var timeZoneId = appointment.Location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
            var localTime = TimezoneHelper.ConvertFromUtc(appointment.StartTime, timeZoneId);
            var tzAbbr = TimezoneHelper.GetTimezoneAbbreviation(timeZoneId, appointment.StartTime);
            var appointmentTime = $"{localTime:dddd, MMMM d, yyyy 'at' h:mm tt} {tzAbbr}";

            var subject = $"Your Telehealth Visit with {providerName} — {clinicName}";
            var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #1976d2; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 14px 28px; background-color: #00897B; color: white; text-decoration: none; border-radius: 6px; margin: 20px 0; font-size: 16px; font-weight: bold; }}
        .info-box {{ background-color: #e3f2fd; border-left: 4px solid #1976d2; padding: 15px; margin: 15px 0; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>{clinicName}</h1>
        </div>
        <div class='content'>
            <h2>Your Telehealth Visit is Scheduled</h2>
            <p>Hello {patientFirstName},</p>
            <p>You have an upcoming telehealth video visit. Here are the details:</p>
            <div class='info-box'>
                <strong>Provider:</strong> {providerName}<br>
                <strong>Date & Time:</strong> {appointmentTime}<br>
                <strong>Visit Type:</strong> Telehealth (Video Call)
            </div>
            <p>When it's time for your appointment, click the button below to join your video visit:</p>
            <p style='text-align: center;'>
                <a href='{joinLink}' class='button'>Join Video Visit</a>
            </p>
            <p><strong>How it works:</strong></p>
            <ul>
                <li>Click the link above at your appointment time</li>
                <li>You'll enter a waiting room</li>
                <li>Your provider will admit you when they're ready</li>
                <li>Make sure your camera and microphone are enabled</li>
            </ul>
            <p><strong>Tips for a successful visit:</strong></p>
            <ul>
                <li>Find a quiet, well-lit space</li>
                <li>Use a stable internet connection</li>
                <li>Test your camera and microphone beforehand</li>
                <li>Have any relevant documents or medication bottles nearby</li>
            </ul>
            <p><strong>If the button doesn't work, copy and paste this link into your browser:</strong></p>
            <p style='word-break: break-all; font-size: 12px;'>{joinLink}</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from {clinicName}. Please do not reply to this email.</p>
            <p>&copy; {clinicName} — Powered by MEDOCS</p>
        </div>
    </div>
</body>
</html>";

            var result = await _emailService.SendEmailAsync(patientEmail, subject, body, true);

            if (result)
                _logger.LogInformation("Telehealth invite email sent to {EmailMasked} for appointment {AppointmentId}",
                    PhiLog.MaskEmail(patientEmail), appointmentId);
            else
                _logger.LogWarning("Failed to send telehealth invite email to {EmailMasked} for appointment {AppointmentId}",
                    PhiLog.MaskEmail(patientEmail), appointmentId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending telehealth invite email for appointment {AppointmentId}", appointmentId);
            return false;
        }
    }

    public async Task<TelehealthJoinInfo> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new TelehealthJoinInfo { IsValid = false, Message = "Invalid link" };

        // AsNoTracking: read-only token validation; Patient/Provider decrypted
        // in-place, must not be tracked.
        var appointment = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Include(a => a.Tenant)
            .FirstOrDefaultAsync(a => a.TelehealthToken == token);

        if (appointment == null)
            return new TelehealthJoinInfo { IsValid = false, Message = "This telehealth link is invalid or has expired." };

        // Check if appointment is cancelled or completed
        var status = appointment.Status ?? 0;
        if (status == (int)AppointmentStatus.Cancelled)
            return new TelehealthJoinInfo { IsValid = false, Message = "This appointment has been cancelled." };
        if (status == (int)AppointmentStatus.Completed)
            return new TelehealthJoinInfo { IsValid = false, Message = "This appointment has already been completed." };
        if (status == (int)AppointmentStatus.NoShow)
            return new TelehealthJoinInfo { IsValid = false, Message = "This appointment was marked as no-show." };

        // Decrypt PHI
        if (appointment.Patient != null)
            _encryptionHelper.DecryptEntity(appointment.Patient);
        if (appointment.Provider != null)
            _encryptionHelper.DecryptEntity(appointment.Provider);

        var providerName = appointment.Provider != null
            ? $"Dr. {appointment.Provider.FirstName} {appointment.Provider.LastName}"
            : "Your Provider";

        var patientName = appointment.Patient != null
            ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}"
            : "Patient";

        // Get Jitsi config so the patient joins the correct room
        var jitsiDomain = _config["Telehealth:JitsiDomain"] ?? "8x8.vc";
        var appId = _config["Telehealth:JaaS:AppId"] ?? "";
        var roomName = appointment.TelehealthUrl; // This is the Jitsi room name

        // Generate JaaS JWT for the patient (not a moderator)
        var jwt = GenerateJaaSJwt(
            userId: $"patient-{appointment.PatientId}",
            userName: patientName,
            userEmail: appointment.Patient?.Email ?? "",
            roomName: roomName,
            isModerator: false
        );

        return new TelehealthJoinInfo
        {
            IsValid = true,
            AppointmentId = appointment.AppointmentId,
            PatientName = patientName,
            ProviderName = providerName,
            AppointmentTime = appointment.StartTime,
            AppointmentStatus = status,
            ClinicName = appointment.Tenant?.Name ?? "MEDOCS",
            Message = "Ready to join",
            RoomName = roomName,
            JitsiDomain = jitsiDomain,
            Jwt = jwt,
            AppId = appId
        };
    }

    public async Task<TelehealthJoinResult> PatientJoinWaitingRoomAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new TelehealthJoinResult { Success = false, Message = "Invalid token" };

        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .FirstOrDefaultAsync(a => a.TelehealthToken == token);

        if (appointment == null)
            return new TelehealthJoinResult { Success = false, Message = "Appointment not found" };

        // CRITICAL (PHI encryption safety):
        // Appointment itself is tracked and will be updated below (Status change
        // + SaveChangesAsync). Detach Patient and Provider so their decrypted
        // fields don't get flushed when the appointment save happens.
        if (appointment.Patient != null)
            _context.Entry(appointment.Patient).State = EntityState.Detached;
        if (appointment.Provider != null)
            _context.Entry(appointment.Provider).State = EntityState.Detached;

        // Decrypt for notification names
        if (appointment.Patient != null)
            _encryptionHelper.DecryptEntity(appointment.Patient);
        if (appointment.Provider != null)
            _encryptionHelper.DecryptEntity(appointment.Provider);

        var patientName = appointment.Patient != null
            ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}"
            : "Patient";

        var currentStatus = appointment.Status ?? 0;

        // If Scheduled or Confirmed → transition to CheckedIn + create encounter
        if (currentStatus == (int)AppointmentStatus.Scheduled ||
            currentStatus == (int)AppointmentStatus.Confirmed)
        {
            appointment.Status = (int)AppointmentStatus.CheckedIn;
            appointment.CheckInTime = DateTime.UtcNow;
            appointment.UpdatedAt = DateTime.UtcNow;

            // Auto-create encounter if not exists (same pattern as CheckInAsync)
            var existingEncounter = await _context.Encounters
                .AnyAsync(e => e.AppointmentId == appointment.AppointmentId);
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

            await _context.SaveChangesAsync();

            // Send appointment change notification (dashboard updates)
            await SendAppointmentChangeNotificationAsync(appointment, "telehealthPatientWaiting");

            _logger.LogInformation("Telehealth patient checked in for appointment {AppointmentId}", appointment.AppointmentId);
        }
        else if (currentStatus == (int)AppointmentStatus.CheckedIn ||
                 currentStatus == (int)AppointmentStatus.InProgress)
        {
            // Already in progress or checked in — just set CheckInTime if not set
            if (!appointment.CheckInTime.HasValue)
            {
                appointment.CheckInTime = DateTime.UtcNow;
                appointment.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        // Note: PatientWaiting notification is now handled by TelehealthHub directly
        // when the patient's SignalR connection calls JoinWaitingRoom (real-time presence)

        return new TelehealthJoinResult
        {
            Success = true,
            AppointmentId = appointment.AppointmentId,
            AppointmentStatus = appointment.Status ?? 0,
            Message = "You are in the waiting room"
        };
    }

    public async Task<bool> AdmitPatientAsync(int appointmentId, int admittedByUserId)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return false;

        if (string.IsNullOrWhiteSpace(appointment.TelehealthToken))
            return false;

        // Send admit notification to the patient via TelehealthHub
        await _telehealthNotificationService.NotifyPatientAdmittedAsync(appointment.TelehealthToken);

        _logger.LogInformation("Patient admitted to telehealth call for appointment {AppointmentId} by user {UserId}",
            appointmentId, admittedByUserId);

        return true;
    }

    public async Task<TelehealthConfigDto> GetJitsiConfigAsync(int appointmentId, string displayName, bool isProvider)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId);
        if (appointment == null)
            return null;

        var jitsiDomain = _config["Telehealth:JitsiDomain"] ?? "8x8.vc";
        var appId = _config["Telehealth:JaaS:AppId"] ?? "";
        var roomName = appointment.TelehealthUrl;

        // Generate JaaS JWT for this user
        var jwt = GenerateJaaSJwt(
            userId: $"provider-{appointmentId}",
            userName: displayName,
            userEmail: "",
            roomName: roomName,
            isModerator: isProvider
        );

        return new TelehealthConfigDto
        {
            JitsiDomain = jitsiDomain,
            RoomName = roomName,
            DisplayName = displayName,
            IsProvider = isProvider,
            Jwt = jwt,
            AppId = appId
        };
    }

    /// <summary>
    /// Generate a JaaS (Jitsi as a Service) RS256-signed JWT for authenticated Jitsi access.
    /// This eliminates the Jitsi login screen and enables white-labeling.
    /// </summary>
    private string GenerateJaaSJwt(string userId, string userName, string userEmail, string roomName, bool isModerator)
    {
        try
        {
            var appId = _config["Telehealth:JaaS:AppId"];
            var kid = _config["Telehealth:JaaS:Kid"];
            var privateKeyPath = _config["Telehealth:JaaS:PrivateKeyPath"];

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(kid) || string.IsNullOrEmpty(privateKeyPath))
            {
                _logger.LogWarning("JaaS configuration missing — returning empty JWT. Jitsi will use unauthenticated mode.");
                return "";
            }

            // Read private key from file
            var keyFullPath = Path.Combine(AppContext.BaseDirectory, privateKeyPath);
            if (!File.Exists(keyFullPath))
            {
                // Try relative to content root
                keyFullPath = Path.Combine(Directory.GetCurrentDirectory(), privateKeyPath);
            }
            if (!File.Exists(keyFullPath))
            {
                _logger.LogError("JaaS private key not found at {Path}", keyFullPath);
                return "";
            }

            var privateKeyPem = File.ReadAllText(keyFullPath);

            var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);

            var securityKey = new RsaSecurityKey(rsa) { KeyId = kid };
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            var now = DateTimeOffset.UtcNow;

            // Build JWT payload with JaaS-required structure
            var payload = new JwtPayload
            {
                { "iss", "chat" },
                { "aud", "jitsi" },
                { "sub", appId },
                { "room", roomName },
                { "exp", now.AddHours(2).ToUnixTimeSeconds() },
                { "nbf", now.AddSeconds(-10).ToUnixTimeSeconds() },
                { "context", new Dictionary<string, object>
                    {
                        { "user", new Dictionary<string, object>
                            {
                                { "id", userId },
                                { "name", userName },
                                { "email", userEmail ?? "" },
                                { "avatar", "" },
                                { "moderator", isModerator }
                            }
                        },
                        { "features", new Dictionary<string, object>
                            {
                                { "livestreaming", false },
                                { "recording", false },
                                { "transcription", false },
                                { "outbound-call", false },
                                { "sip-outbound-call", false },
                                { "sip-inbound-call", false },
                                { "inbound-call", false }
                            }
                        }
                    }
                }
            };

            var header = new JwtHeader(signingCredentials);
            header["kid"] = kid;

            var token = new JwtSecurityToken(header, payload);
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.WriteToken(token);

            _logger.LogDebug("Generated JaaS JWT for user {UserId} in room {RoomName}", userId, roomName);
            return jwt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate JaaS JWT for user {UserId}", userId);
            return "";
        }
    }

    /// <summary>
    /// Helper to send appointment change notifications (same pattern as AppointmentService).
    /// </summary>
    private async Task SendAppointmentChangeNotificationAsync(Appointment appointment, string changeType)
    {
        try
        {
            // Load related entities if needed
            if (appointment.Patient == null)
                await _context.Entry(appointment).Reference(a => a.Patient).LoadAsync();
            if (appointment.Provider == null)
                await _context.Entry(appointment).Reference(a => a.Provider).LoadAsync();
            if (appointment.Location == null)
                await _context.Entry(appointment).Reference(a => a.Location).LoadAsync();

            // CRITICAL (PHI encryption safety):
            // Detach Patient and Provider before decryption. This helper runs
            // after appointment CRUD operations where the caller still has a
            // tracked appointment and will (or has already) invoked SaveChanges.
            // Without detach, the decrypted Patient/Provider would be flushed.
            if (appointment.Patient != null
                && _context.Entry(appointment.Patient).State != EntityState.Detached)
                _context.Entry(appointment.Patient).State = EntityState.Detached;
            if (appointment.Provider != null
                && _context.Entry(appointment.Provider).State != EntityState.Detached)
                _context.Entry(appointment.Provider).State = EntityState.Detached;

            // Decrypt PHI for notification
            if (appointment.Patient != null)
                _encryptionHelper.DecryptEntity(appointment.Patient);
            if (appointment.Provider != null)
                _encryptionHelper.DecryptEntity(appointment.Provider);

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
                IsTelehealth = appointment.IsTelehealth,
                ChangedAt = DateTime.UtcNow
            };

            await _scheduleNotificationService.NotifyAppointmentChangedAsync(
                appointment.TenantId, notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send telehealth appointment notification for {AppointmentId}",
                appointment.AppointmentId);
        }
    }

    private static string GetStatusName(int status) => status switch
    {
        0 => "Scheduled",
        1 => "Confirmed",
        2 => "CheckedIn",
        3 => "InProgress",
        4 => "Completed",
        5 => "NoShow",
        6 => "Cancelled",
        7 => "Rescheduled",
        8 => "Missed",
        _ => "Unknown"
    };

    private static string GetAppointmentTypeName(int type) => type switch
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
        _ => "Appointment"
    };

    /// <summary>
    /// Verify patient identity via LastName + DateOfBirth + ZipCode before
    /// entering the telehealth waiting room. (2026-05: switched from SSN to
    /// LastName/ZIP, matching kiosk/portal-setup/tablet-intake flows.)
    /// </summary>
    public async Task<TelehealthVerifyIdentityResult> VerifyPatientIdentityAsync(string token, string lastName, DateOnly dateOfBirth, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(lastName)
            || string.IsNullOrWhiteSpace(zipCode))
            return new TelehealthVerifyIdentityResult { Success = false, Message = "Invalid request." };

        // Find appointment by telehealth token.
        // AsNoTracking: identity verification is read-only; we decrypt the
        // patient's LastName / ZipCode in-place for comparison and must not
        // accidentally write the plaintext back to the DB.
        var appointment = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .FirstOrDefaultAsync(a => a.TelehealthToken == token);

        if (appointment == null)
            return new TelehealthVerifyIdentityResult { Success = false, Message = "This telehealth link is invalid or has expired." };

        var patient = appointment.Patient;
        if (patient == null)
            return new TelehealthVerifyIdentityResult { Success = false, Message = "Patient record not found." };

        // Normalize submitted values.
        var submittedLastName = (lastName ?? "").Trim();
        var submittedZip = System.Text.RegularExpressions.Regex.Replace(zipCode ?? "", @"[^\d]", "");
        if (submittedZip.Length > 5) submittedZip = submittedZip[..5];

        // Decrypt patient's stored values for comparison. LastName + ZipCode
        // are encrypted; DateOfBirth is plaintext.
        var patientLastName = (_encryptionHelper.Decrypt(patient.LastName) ?? "").Trim();
        var patientZipRaw = _encryptionHelper.Decrypt(patient.ZipCode) ?? "";
        var patientZip = System.Text.RegularExpressions.Regex.Replace(patientZipRaw, @"[^\d]", "");
        if (patientZip.Length > 5) patientZip = patientZip[..5];

        var lastNameMatches = string.Equals(patientLastName, submittedLastName, StringComparison.OrdinalIgnoreCase);
        var zipMatches = !string.IsNullOrEmpty(patientZip) && patientZip == submittedZip;
        var dobMatches = patient.DateOfBirth == dateOfBirth;

        if (!lastNameMatches || !zipMatches || !dobMatches)
        {
            _logger.LogInformation("Telehealth verify-identity failed for token {Token}: identity mismatch (PII redacted)", token);
            return new TelehealthVerifyIdentityResult
            {
                Success = false,
                Message = "The information you entered does not match our records. Please try again."
            };
        }

        // Verification passed — decrypt the rest of the entity for display
        _encryptionHelper.DecryptEntity(patient);
        if (appointment.Provider != null) _encryptionHelper.DecryptEntity(appointment.Provider);

        var providerName = appointment.Provider != null
            ? $"Dr. {appointment.Provider.FirstName} {appointment.Provider.LastName}"
            : "Your Provider";

        // Format appointment time in location timezone
        var timeZoneId = appointment.Location?.TimeZoneId ?? Helpers.TimezoneHelper.DefaultTimeZoneId;
        var formattedTime = Helpers.TimezoneHelper.FormatTimeWithTimezone(appointment.StartTime, timeZoneId);
        var formattedDate = Helpers.TimezoneHelper.FormatDateWithTimezone(appointment.StartTime, timeZoneId);

        _logger.LogInformation("Telehealth verify-identity succeeded for patient {PatientId}, appointment {AppointmentId}",
            patient.PatientId, appointment.AppointmentId);

        return new TelehealthVerifyIdentityResult
        {
            Success = true,
            Message = "Identity verified.",
            ProviderName = providerName,
            AppointmentTime = $"{formattedDate} at {formattedTime}",
            PatientFirstName = patient.FirstName
        };
    }
}
