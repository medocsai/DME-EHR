using System;
using System.Threading.Tasks;
using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// Interface for SMS messaging service
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends an SMS message to the specified phone number
    /// </summary>
    /// <param name="phoneNumber">The recipient's phone number</param>
    /// <param name="message">The message content</param>
    /// <returns>True if the SMS was sent (or queued) successfully</returns>
    Task<bool> SendSmsAsync(string phoneNumber, string message);

    /// <summary>
    /// Sends a no-show notification SMS to a patient
    /// </summary>
    Task<bool> SendNoShowNotificationAsync(string phoneNumber, string patientFirstName, DateTime appointmentTime, string timezoneAbbr = "");
}

/// <summary>
/// Mock SMS service implementation
/// Logs SMS messages for development/testing - replace with real SMS provider (Twilio, etc.) in production
/// </summary>
public class SmsService : ISmsService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmsService> _logger;

    // Configuration for future real SMS provider integration
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;

    public SmsService(IConfiguration config, ILogger<SmsService> logger)
    {
        _config = config;
        _logger = logger;

        // SMS provider configuration - will be used when real SMS provider is integrated
        _accountSid = _config["Sms:AccountSid"] ?? "MOCK_ACCOUNT_SID";
        _authToken = _config["Sms:AuthToken"] ?? "MOCK_AUTH_TOKEN";
        _fromNumber = _config["Sms:FromNumber"] ?? "+15551234567";
    }

    /// <summary>
    /// Sends an SMS message (mock implementation - logs instead of sending)
    /// </summary>
    public async Task<bool> SendSmsAsync(string phoneNumber, string message)
    {
        try
        {
            // Clean up phone number (remove formatting)
            var cleanedNumber = CleanPhoneNumber(phoneNumber);

            if (string.IsNullOrWhiteSpace(cleanedNumber))
            {
                _logger.LogWarning("SMS not sent - invalid phone number: {PhoneMasked}", PhiLog.MaskPhone(phoneNumber));
                return false;
            }

            // MOCK IMPLEMENTATION - Log the SMS instead of actually sending
            // When integrating a real SMS provider (Twilio, etc.), replace this block
            _logger.LogInformation(
                "[MOCK SMS] To: {PhoneNumber}, From: {FromNumber}, Message: {Message}",
                cleanedNumber,
                _fromNumber,
                message);

            // Simulate async operation
            await Task.Delay(100);

            _logger.LogInformation("SMS queued successfully to {PhoneMasked}", PhiLog.MaskPhone(cleanedNumber));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {PhoneMasked}", PhiLog.MaskPhone(phoneNumber));
            return false;
        }
    }

    /// <summary>
    /// Sends a no-show notification SMS to a patient
    /// </summary>
    public async Task<bool> SendNoShowNotificationAsync(string phoneNumber, string patientFirstName, DateTime appointmentTime, string timezoneAbbr = "")
    {
        var formattedTime = appointmentTime.ToString("h:mm tt");
        if (!string.IsNullOrEmpty(timezoneAbbr))
            formattedTime += $" {timezoneAbbr}";
        var formattedDate = appointmentTime.ToString("MMMM d, yyyy");

        var message = $"Hi {patientFirstName}, we noticed you missed your appointment at {formattedTime} on {formattedDate}. " +
                      $"Please call us to reschedule at your earliest convenience. Thank you - PTEHR";

        return await SendSmsAsync(phoneNumber, message);
    }

    /// <summary>
    /// Cleans phone number by removing formatting characters
    /// </summary>
    private string CleanPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;

        // Remove common formatting characters
        var cleaned = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());

        // Ensure US numbers have country code
        if (cleaned.Length == 10)
            cleaned = "+1" + cleaned;
        else if (cleaned.Length == 11 && cleaned.StartsWith("1"))
            cleaned = "+" + cleaned;

        return cleaned;
    }
}
