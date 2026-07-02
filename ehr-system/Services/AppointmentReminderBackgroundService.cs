using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// Background service that periodically checks for upcoming appointments
/// and sends email reminders to patients (24h and 1h before).
/// </summary>
public class AppointmentReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppointmentReminderBackgroundService> _logger;
    private readonly IConfiguration _config;

    public AppointmentReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AppointmentReminderBackgroundService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Appointment Reminder Background Service started");

        // Initial delay to let the application fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in appointment reminder processing cycle");
            }

            var intervalMinutes = await GetCheckIntervalAsync();
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Appointment Reminder Background Service stopped");
    }

    private async Task ProcessRemindersAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EhrDbContext>();

        // Get all active tenants
        var tenantIds = await context.Tenants
            .Where(t => t.IsDeleted != true && (t.Status == null || t.Status == 1))
            .Select(t => t.TenantId)
            .ToListAsync(stoppingToken);

        foreach (var tenantId in tenantIds)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ProcessTenantRemindersAsync(tenantId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing reminders for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task ProcessTenantRemindersAsync(int tenantId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EhrDbContext>();

        // Check if reminders are enabled for this tenant
        var remindersEnabled = await GetTenantBoolSetting(context, tenantId, SettingKeys.AppointmentRemindersEnabled, true);
        if (!remindersEnabled) return;

        var send24h = await GetTenantBoolSetting(context, tenantId, SettingKeys.Reminder24hEnabled, true);
        var send1h = await GetTenantBoolSetting(context, tenantId, SettingKeys.Reminder1hEnabled, true);

        var utcNow = DateTime.UtcNow;
        var scheduledStatus = (int)AppointmentStatus.Scheduled;
        var confirmedStatus = (int)AppointmentStatus.Confirmed;

        // Process 24-hour reminders (window: 23-25 hours from now)
        if (send24h)
        {
            var window24hStart = utcNow.AddHours(23);
            var window24hEnd = utcNow.AddHours(25);

            var appointments24h = await context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Provider)
                .Include(a => a.Location)
                .Where(a => a.TenantId == tenantId)
                .Where(a => a.Status == scheduledStatus || a.Status == confirmedStatus)
                .Where(a => a.Reminder24hEnabled)
                .Where(a => a.Reminder24hSentAt == null)
                .Where(a => a.StartTime >= window24hStart && a.StartTime <= window24hEnd)
                .ToListAsync(stoppingToken);

            foreach (var appt in appointments24h)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await SendReminderAsync(scope.ServiceProvider, context, appt, "24h");
            }
        }

        // Process 1-hour reminders (window: 30-90 minutes from now)
        if (send1h)
        {
            var window1hStart = utcNow.AddMinutes(30);
            var window1hEnd = utcNow.AddMinutes(90);

            var appointments1h = await context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Provider)
                .Include(a => a.Location)
                .Where(a => a.TenantId == tenantId)
                .Where(a => a.Status == scheduledStatus || a.Status == confirmedStatus)
                .Where(a => a.Reminder1hEnabled)
                .Where(a => a.Reminder1hSentAt == null)
                .Where(a => a.StartTime >= window1hStart && a.StartTime <= window1hEnd)
                .ToListAsync(stoppingToken);

            foreach (var appt in appointments1h)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await SendReminderAsync(scope.ServiceProvider, context, appt, "1h");
            }
        }
    }

    private async Task SendReminderAsync(
        IServiceProvider serviceProvider,
        EhrDbContext context,
        Appointment appointment,
        string reminderType)
    {
        try
        {
            var patient = appointment.Patient;
            if (patient == null) return;

            // CRITICAL (PHI encryption safety):
            // Detach the patient entity from the change tracker BEFORE calling
            // DecryptEntity. DecryptEntity uses reflection to overwrite the
            // entity's encrypted string properties with plaintext values, which
            // EF Core would otherwise see as property changes on a tracked entity
            // and mark Modified. The SaveChangesAsync call below (to update the
            // Reminder*SentAt timestamp on the appointment) would then flush the
            // decrypted patient back to the DB, silently corrupting the patient's
            // PHI fields from encrypted to plaintext. Detach first → decrypt has
            // no tracking effect → only the appointment update gets persisted.
            context.Entry(patient).State = EntityState.Detached;

            // Decrypt patient data (FirstName, Email, etc. are encrypted at rest)
            var encryptionHelper = serviceProvider.GetRequiredService<EncryptionHelper>();
            encryptionHelper.DecryptEntity(patient);

            if (string.IsNullOrWhiteSpace(patient.Email))
            {
                _logger.LogDebug("No email for patient {PatientId}, skipping {ReminderType} reminder",
                    patient.PatientId, reminderType);
                return;
            }

            var emailService = serviceProvider.GetRequiredService<IEmailService>();

            // Format appointment time in the location's timezone
            var timeZoneId = appointment.Location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
            var localTime = TimezoneHelper.ConvertFromUtc(appointment.StartTime, timeZoneId);
            var formattedDate = localTime.ToString("MMMM d, yyyy");
            var formattedTime = TimezoneHelper.FormatTimeWithTimezone(appointment.StartTime, timeZoneId);

            var firstName = patient.FirstName ?? "Patient";
            var providerName = appointment.Provider != null
                ? $"{appointment.Provider.FirstName} {appointment.Provider.LastName}"
                : "your provider";
            var locationName = appointment.Location?.Name;

            var reminderLabel = reminderType == "24h" ? "Tomorrow's" : "Upcoming";
            var subject = $"Appointment Reminder - {formattedDate} at {formattedTime}";

            // Generate portal link using location's portal code (no token needed)
            var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
            var portalCode = appointment.Location?.PortalCode;
            if (string.IsNullOrEmpty(portalCode))
            {
                // Fallback: fetch portal code for this tenant's location
                var loc = await context.Locations
                    .FirstOrDefaultAsync(l => l.TenantId == appointment.TenantId && l.IsActive == true && l.PortalCode != null);
                portalCode = loc?.PortalCode ?? "";
            }
            var portalUrl = $"{baseUrl}/Portal/{portalCode}";

            var emailBody = GenerateReminderEmailBody(firstName, formattedDate, formattedTime, providerName, reminderLabel, locationName, portalUrl);

            var sent = await emailService.SendEmailAsync(patient.Email, subject, emailBody, true);

            if (sent)
            {
                if (reminderType == "24h")
                    appointment.Reminder24hSentAt = DateTime.UtcNow;
                else
                    appointment.Reminder1hSentAt = DateTime.UtcNow;

                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Sent {ReminderType} reminder for appointment {AppointmentId} to {Email}",
                    reminderType, appointment.AppointmentId, patient.Email);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to send {ReminderType} reminder for appointment {AppointmentId}",
                    reminderType, appointment.AppointmentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {ReminderType} reminder for appointment {AppointmentId}",
                reminderType, appointment.AppointmentId);
        }
    }

    private static async Task<bool> GetTenantBoolSetting(EhrDbContext context, int tenantId, string key, bool defaultValue)
    {
        var value = await context.SystemSettings
            .Where(s => s.TenantId == tenantId && s.SettingKey == key)
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return bool.TryParse(value, out bool result) ? result : defaultValue;
    }

    private async Task<int> GetCheckIntervalAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EhrDbContext>();

            var value = await context.SystemSettings
                .Where(s => s.SettingKey == SettingKeys.ReminderCheckIntervalMinutes)
                .Select(s => s.SettingValue)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int interval) && interval >= 1)
                return interval;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read reminder check interval, using default");
        }

        return 5; // Default: 5 minutes
    }

    private static string GenerateReminderEmailBody(
        string patientFirstName, string formattedDate, string formattedTime,
        string providerName, string reminderLabel, string? locationName, string portalUrl)
    {
        var locationHtml = !string.IsNullOrEmpty(locationName)
            ? $"<br><strong>Location:</strong> {locationName}"
            : "";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #1B72BE; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; }}
        .info-box {{ background-color: #e3f2fd; border: 1px solid #90caf9; padding: 15px; border-radius: 4px; margin: 15px 0; }}
        .btn {{ display: inline-block; padding: 12px 24px; background-color: #1B72BE; color: white; text-decoration: none; border-radius: 6px; font-weight: bold; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>MEDOCS</h1>
        </div>
        <div class='content'>
            <h2>{reminderLabel} Appointment Reminder</h2>
            <p>Hello {patientFirstName},</p>
            <p>This is a friendly reminder about your upcoming appointment:</p>
            <div class='info-box'>
                <strong>Date:</strong> {formattedDate}<br>
                <strong>Time:</strong> {formattedTime}<br>
                <strong>Provider:</strong> {providerName}
                {locationHtml}
            </div>
            <p>If you need to reschedule or cancel your appointment, please contact our office at least 24 hours in advance.</p>
            <p style='text-align: center;'>
                <a href='{portalUrl}' class='btn' style='color: white;'>Access Patient Portal</a>
            </p>
            <p>We look forward to seeing you!</p>
            <p>Best regards,<br>Your Healthcare Team</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from MEDOCS. Please do not reply to this email.</p>
            <p>&copy; MEDOCS LLC</p>
        </div>
    </div>
</body>
</html>";
    }
}
