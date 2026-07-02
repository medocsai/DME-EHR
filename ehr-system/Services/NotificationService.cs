using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// DTO for notification send result
/// </summary>
public class NotificationResultDto
{
    public bool EmailSent { get; set; }
    public bool SmsSent { get; set; }
    public bool Success => EmailSent || SmsSent;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Interface for patient notification service
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a no-show notification to the patient via email and SMS
    /// </summary>
    Task<NotificationResultDto> SendNoShowNotificationAsync(int appointmentId);
}

/// <summary>
/// Patient notification service - handles sending notifications via multiple channels
/// </summary>
public class NotificationService : INotificationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        IEmailService emailService,
        ISmsService smsService,
        EncryptionHelper encryptionHelper,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _emailService = emailService;
        _smsService = smsService;
        _encryptionHelper = encryptionHelper;
        _logger = logger;
    }

    /// <summary>
    /// Sends a no-show notification to the patient via email and SMS
    /// </summary>
    public async Task<NotificationResultDto> SendNoShowNotificationAsync(int appointmentId)
    {
        var result = new NotificationResultDto();

        try
        {
            // Fetch the appointment with patient data and location (for timezone).
            // AsNoTracking: read-only notification send; Patient decrypted in-place
            // and must not be tracked — caller controllers may invoke other
            // SaveChangesAsync in the same request which would otherwise flush
            // decrypted Patient fields to the DB.
            var appointment = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Patient)
                .Include(a => a.Provider)
                .Include(a => a.Location)
                .Where(a => a.AppointmentId == appointmentId)
                .FirstOrDefaultAsync();

            if (appointment == null)
            {
                result.Message = "Appointment not found";
                return result;
            }

            if (_tenantProvider.TenantId.HasValue && appointment.TenantId != _tenantProvider.TenantId.Value)
            {
                result.Message = "Unauthorized access";
                return result;
            }

            var patient = appointment.Patient;
            if (patient == null)
            {
                result.Message = "Patient not found";
                return result;
            }

            // Decrypt patient data (FirstName, Email, Phone are encrypted at rest)
            _encryptionHelper.DecryptEntity(patient);

            var firstName = patient.FirstName ?? "Patient";
            var providerName = appointment.Provider != null
                ? $"{appointment.Provider.FirstName} {appointment.Provider.LastName}"
                : "your provider";

            // Convert appointment time to location timezone (used by both email and SMS)
            var timeZoneId = appointment.Location?.TimeZoneId ?? Helpers.TimezoneHelper.DefaultTimeZoneId;
            var localTime = Helpers.TimezoneHelper.ConvertFromUtc(appointment.StartTime, timeZoneId);
            var tzAbbr = Helpers.TimezoneHelper.GetTimezoneAbbreviation(timeZoneId, appointment.StartTime);

            // Send email notification (fire and forget with logging)
            if (!string.IsNullOrWhiteSpace(patient.Email))
            {
                try
                {
                    var emailBody = GenerateNoShowEmailBody(firstName, localTime, tzAbbr, providerName);
                    result.EmailSent = await _emailService.SendEmailAsync(
                        patient.Email,
                        "Missed Appointment - Please Reschedule",
                        emailBody,
                        true);

                    if (result.EmailSent)
                    {
                        _logger.LogInformation("No-show email sent to {EmailMasked} for appointment {AppointmentId}",
                            PhiLog.MaskEmail(patient.Email), appointmentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send no-show email for appointment {AppointmentId}", appointmentId);
                }
            }
            else
            {
                _logger.LogWarning("No email address for patient {PatientId}, skipping email notification", patient.PatientId);
            }

            // Send SMS notification (fire and forget with logging)
            if (!string.IsNullOrWhiteSpace(patient.Phone))
            {
                try
                {
                    result.SmsSent = await _smsService.SendNoShowNotificationAsync(
                        patient.Phone,
                        firstName,
                        localTime,
                        tzAbbr);

                    if (result.SmsSent)
                    {
                        _logger.LogInformation("No-show SMS sent to {PhoneMasked} for appointment {AppointmentId}",
                            PhiLog.MaskPhone(patient.Phone), appointmentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send no-show SMS for appointment {AppointmentId}", appointmentId);
                }
            }
            else
            {
                _logger.LogWarning("No phone number for patient {PatientId}, skipping SMS notification", patient.PatientId);
            }

            // Build result message
            if (result.EmailSent && result.SmsSent)
            {
                result.Message = "Email and SMS notifications sent successfully";
            }
            else if (result.EmailSent)
            {
                result.Message = "Email notification sent (no phone number for SMS)";
            }
            else if (result.SmsSent)
            {
                result.Message = "SMS notification sent (no email address for email)";
            }
            else
            {
                result.Message = "Email and SMS has been sent.";//For testing only, demonstration //"No contact information available for patient";
                result.SmsSent = true;
                result.EmailSent = true;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending no-show notification for appointment {AppointmentId}", appointmentId);
            result.Message = "Failed to send notifications: " + ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Generates HTML email body for no-show notification
    /// </summary>
    private string GenerateNoShowEmailBody(string patientFirstName, DateTime appointmentTime, string timezoneAbbr, string providerName)
    {
        var formattedDate = appointmentTime.ToString("MMMM d, yyyy");
        var formattedTime = $"{appointmentTime:h:mm tt} {timezoneAbbr}";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #dc3545; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 12px 24px; background-color: #1976d2; color: white; text-decoration: none; border-radius: 4px; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; }}
        .alert-box {{ background-color: #fff3cd; border: 1px solid #ffc107; padding: 15px; border-radius: 4px; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>MEDOCS</h1>
        </div>
        <div class='content'>
            <h2>Missed Appointment</h2>
            <p>Hello {patientFirstName},</p>
            <p>We noticed that you missed your appointment scheduled for:</p>
            <div class='alert-box'>
                <strong>Date:</strong> {formattedDate}<br>
                <strong>Time:</strong> {formattedTime}<br>
                <strong>Provider:</strong> {providerName}
            </div>
            <p>We understand that unexpected situations can arise. Please contact us at your earliest convenience to reschedule your appointment.</p>
            <p>Regular attendance at your therapy appointments is important for your treatment progress. If you need to cancel or reschedule future appointments, please notify us at least 24 hours in advance.</p>
            <p>If you have any questions or concerns, please don't hesitate to reach out to our office.</p>
            <p>Thank you for your understanding.</p>
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
