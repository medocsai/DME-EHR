using System.Net;
using System.Net.Mail;
using EHR.Helpers;

namespace EHR.Services;

public interface IEmailService
{
    Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string firstName);
    Task<bool> SendOtpEmailAsync(string email, string otpCode, string firstName);
    Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true);
    Task<bool> SendPaymentReceiptAsync(string email, string patientName, decimal amount, string paymentMethod, DateTime date);
    Task<bool> SendBalanceNotificationAsync(string email, string patientName, decimal balance, string portalUrl);
    Task<bool> SendInstallmentReminderAsync(string email, string patientName, decimal amount, DateTime dueDate);
    Task<bool> SendPaymentFailedAsync(string email, string patientName, decimal amount, int retryNumber);
    Task<bool> SendWelcomeEmailAsync(string email, string patientName, string clinicName, string portalUrl);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;

        // Email configuration - defaults to provided Gmail settings
        _smtpHost = _config["Email:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(_config["Email:SmtpPort"] ?? "587");
        _smtpUsername = _config["Email:Username"] ?? "contact@medocs.ai";
        _smtpPassword = _config["Email:Password"] ?? "vmsm osvj lqyn wqvx";
        _fromEmail = _config["Email:FromEmail"] ?? "contact@medocs.ai";
        _fromName = _config["Email:FromName"] ?? "MEDOCS";
    }

    public async Task<bool> SendPasswordResetEmailAsync(string email, string resetToken, string firstName)
    {
        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5000";
        var resetLink = $"{baseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";

        var subject = "Password Reset Request - MEDOCS";
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
        .button {{ display: inline-block; padding: 12px 24px; background-color: #1976d2; color: white; text-decoration: none; border-radius: 4px; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>MEDOCS</h1>
        </div>
        <div class='content'>
            <h2>Password Reset Request</h2>
            <p>Hello {firstName},</p>
            <p>We received a request to reset your password for your MEDOCS account. Click the button below to reset your password:</p>
            <p style='text-align: center;'>
                <a href='{resetLink}' class='button'>Reset Password</a>
            </p>
            <p>If you didn't request a password reset, you can safely ignore this email. Your password will not be changed.</p>
            <p>This link will expire in 1 hour for security reasons.</p>
            <p><strong>If the button doesn't work, copy and paste this link into your browser:</strong></p>
            <p style='word-break: break-all; font-size: 12px;'>{resetLink}</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from MEDOCS. Please do not reply to this email.</p>
            <p>&copy; MEDOCS LLC</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(email, subject, body, true);
    }

    public async Task<bool> SendOtpEmailAsync(string email, string otpCode, string firstName)
    {
        var subject = "Your Verification Code - MEDOCS";
        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #1B72BE; color: white; padding: 20px; text-align: center; border-radius: 8px 8px 0 0; }}
        .content {{ padding: 30px; background-color: #f9f9f9; }}
        .otp-code {{ font-size: 36px; font-weight: bold; letter-spacing: 8px; text-align: center;
                     padding: 20px; background: #fff; border: 2px dashed #1B72BE; border-radius: 8px;
                     margin: 20px 0; color: #1B72BE; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #666; border-radius: 0 0 8px 8px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>MEDOCS</h1>
        </div>
        <div class='content'>
            <h2>Verification Code</h2>
            <p>Hello {firstName},</p>
            <p>Your one-time verification code is:</p>
            <div class='otp-code'>{otpCode}</div>
            <p>This code will expire in <strong>5 minutes</strong>.</p>
            <p>If you did not attempt to sign in, please ignore this email or contact support immediately.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from MEDOCS. Please do not reply to this email.</p>
            <p>&copy; MEDOCS LLC</p>
        </div>
    </div>
</body>
</html>";

        return await SendEmailAsync(email, subject, body, true);
    }

    public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true)
    {
        // Global kill switch — set Email:Enabled to false to disable all emails.
        // Saves to .html file with the ORIGINAL recipient so the preview is faithful.
        var emailEnabled = _config.GetValue<bool?>("Email:Enabled") ?? true;
        if (!emailEnabled)
        {
            _logger.LogInformation("Email to {EmailMasked} saved to file — Email:Enabled is false. Subject: {Subject}", PhiLog.MaskEmail(to), subject);
            await SaveEmailToFileAsync(to, subject, body);
            return true;
        }

        // Block ALL real sends when running on localhost (development).
        // Saves to .html file with the ORIGINAL recipient — @testmd.com redirect does NOT apply here,
        // since nothing is actually leaving the machine. Production is where the redirect matters.
        var baseUrl = _config["App:BaseUrl"] ?? "";
        if (baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Email to {EmailMasked} saved to file — running on localhost. Subject: {Subject}", PhiLog.MaskEmail(to), subject);
            await SaveEmailToFileAsync(to, subject, body);
            return true;
        }

        // -------- From here on, we are on a real (production) send path --------

        // Test-user redirect — any address on @testmd.com is sent to hmajeed@medocs.ai instead.
        // Lets us create fake test patients/providers with @testmd.com emails and still receive their OTPs,
        // invitations, receipts, etc. in a real inbox for testing. Only applies to live SMTP delivery.
        var effectiveTo = to;
        if (!string.IsNullOrWhiteSpace(to) && to.EndsWith("@testmd.com", StringComparison.OrdinalIgnoreCase))
        {
            effectiveTo = "hmajeed@medocs.ai";
            subject = $"[TEST for {to}] {subject}";
            _logger.LogInformation("Email for {OriginalEmail} redirected to {RedirectEmail} (@testmd.com test redirect)", to, effectiveTo);
        }

        // Block other test/fake email domains (after @testmd.com redirect has had its chance)
        var blockedDomains = new[] { "@test.com", "@example.com", "@localhost", "@test.local", "@mailinator.com" };
        if (!string.IsNullOrWhiteSpace(effectiveTo) && blockedDomains.Any(domain => effectiveTo.EndsWith(domain, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Email to {EmailMasked} blocked - test domain detected", PhiLog.MaskEmail(effectiveTo));
            return true;
        }

        try
        {
            using var smtp = new SmtpClient(_smtpHost)
            {
                Port = _smtpPort,
                Credentials = new NetworkCredential(_smtpUsername, _smtpPassword),
                EnableSsl = true
            };

            var message = new MailMessage
            {
                From = new MailAddress(_fromEmail, _fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            message.To.Add(effectiveTo);

            await smtp.SendMailAsync(message);
            _logger.LogInformation("Email sent successfully to {EmailMasked}", PhiLog.MaskEmail(effectiveTo));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {EmailMasked}", PhiLog.MaskEmail(effectiveTo));
            return false;
        }
    }

    // ============================================
    // BILLING EMAIL METHODS
    // ============================================

    public async Task<bool> SendPaymentReceiptAsync(string email, string patientName, decimal amount, string paymentMethod, DateTime date)
    {
        var subject = "Payment Receipt - MEDOCS";
        var body = BuildBillingEmail(patientName, "Payment Received",
            $@"<p>Your payment has been processed successfully.</p>
            <table style='width:100%; border-collapse:collapse; margin:16px 0;'>
                <tr><td style='padding:8px; border-bottom:1px solid #eee; color:#666;'>Amount</td><td style='padding:8px; border-bottom:1px solid #eee; font-weight:bold;'>${amount:F2}</td></tr>
                <tr><td style='padding:8px; border-bottom:1px solid #eee; color:#666;'>Method</td><td style='padding:8px; border-bottom:1px solid #eee;'>{paymentMethod}</td></tr>
                <tr><td style='padding:8px; border-bottom:1px solid #eee; color:#666;'>Date</td><td style='padding:8px; border-bottom:1px solid #eee;'>{date:MMMM d, yyyy}</td></tr>
            </table>
            <p>Thank you for your payment.</p>");

        return await SendEmailAsync(email, subject, body, true);
    }

    public async Task<bool> SendBalanceNotificationAsync(string email, string patientName, decimal balance, string portalUrl)
    {
        var subject = "You Have a Balance Due - MEDOCS";
        var body = BuildBillingEmail(patientName, "Balance Notification",
            $@"<p>Your account has a balance of <strong>${balance:F2}</strong>.</p>
            <p>You can pay online through your patient portal or contact our office to arrange payment.</p>
            <p style='text-align:center; margin:24px 0;'>
                <a href='{portalUrl}' style='display:inline-block; padding:12px 24px; background-color:#1B72BE; color:white; text-decoration:none; border-radius:8px; font-weight:500;'>View & Pay Online</a>
            </p>
            <p>Payment plans are available if needed.</p>");

        return await SendEmailAsync(email, subject, body, true);
    }

    public async Task<bool> SendInstallmentReminderAsync(string email, string patientName, decimal amount, DateTime dueDate)
    {
        var subject = "Upcoming Payment Reminder - MEDOCS";
        var body = BuildBillingEmail(patientName, "Payment Reminder",
            $@"<p>This is a friendly reminder that your next installment payment is coming up.</p>
            <table style='width:100%; border-collapse:collapse; margin:16px 0;'>
                <tr><td style='padding:8px; border-bottom:1px solid #eee; color:#666;'>Amount</td><td style='padding:8px; border-bottom:1px solid #eee; font-weight:bold;'>${amount:F2}</td></tr>
                <tr><td style='padding:8px; border-bottom:1px solid #eee; color:#666;'>Due Date</td><td style='padding:8px; border-bottom:1px solid #eee;'>{dueDate:MMMM d, yyyy}</td></tr>
            </table>
            <p>Your saved payment method will be charged automatically on the due date.</p>");

        return await SendEmailAsync(email, subject, body, true);
    }

    public async Task<bool> SendPaymentFailedAsync(string email, string patientName, decimal amount, int retryNumber)
    {
        var subject = "Payment Failed - Action Required - MEDOCS";
        var body = BuildBillingEmail(patientName, "Payment Failed",
            $@"<p>We were unable to process your payment of <strong>${amount:F2}</strong>.</p>
            <p>This was attempt {retryNumber} of 3. Please update your payment method or contact our office.</p>
            <p>We will automatically retry in 5 days. If all attempts fail, your payment plan may be affected.</p>");

        return await SendEmailAsync(email, subject, body, true);
    }

    // ============================================
    // WELCOME EMAIL
    // ============================================

    public async Task<bool> SendWelcomeEmailAsync(string email, string patientName, string clinicName, string portalUrl)
    {
        var subject = $"Welcome to {clinicName} - MEDOCS Patient Portal";
        var body = BuildBillingEmail(patientName, $"Welcome to {clinicName}",
            $@"<p>Your profile has been created at <strong>{clinicName}</strong>.</p>
            <p>You can access your patient portal to:</p>
            <ul style='padding-left:20px; margin:12px 0;'>
                <li>View upcoming appointments</li>
                <li>Upload documents for your visit</li>
                <li>Message your care team</li>
                <li>View your medical records</li>
            </ul>
            <p style='text-align:center; margin:24px 0;'>
                <a href='{portalUrl}' style='display:inline-block; padding:14px 32px; background-color:#1B72BE; color:white; text-decoration:none; border-radius:8px; font-weight:600; font-size:16px;'>Access Patient Portal</a>
            </p>
            <p>If you haven't set up your account yet, click the button above and select <strong>Set Up Account</strong>. You'll need your date of birth and last 4 digits of SSN to verify your identity.</p>");

        return await SendEmailAsync(email, subject, body, true);
    }

    private static string BuildBillingEmail(string patientName, string heading, string content)
    {
        return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family:Arial,sans-serif; line-height:1.6; color:#333; margin:0; padding:0;'>
    <div style='max-width:600px; margin:0 auto;'>
        <div style='background-color:#1B72BE; color:white; padding:20px; text-align:center;'>
            <h1 style='margin:0; font-size:24px;'>MEDOCS</h1>
        </div>
        <div style='padding:24px; background-color:#f9fafb;'>
            <h2 style='color:#1B72BE; margin-top:0;'>{heading}</h2>
            <p>Hello {patientName},</p>
            {content}
        </div>
        <div style='padding:16px; text-align:center; font-size:12px; color:#6b7280;'>
            <p>This is an automated message from MEDOCS. Please do not reply.</p>
            <p>&copy; MEDOCS LLC</p>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Save email as HTML file for localhost/dev when email sending is disabled.
    /// Files saved to: {project_root}/../emails_sent/
    /// </summary>
    private async Task SaveEmailToFileAsync(string to, string subject, string body)
    {
        try
        {
            // Save to emails_sent folder relative to project root (ehr-system/../emails_sent)
            var emailsDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "emails_sent");
            Directory.CreateDirectory(emailsDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var safeTo = to.Replace("@", "_at_").Replace(".", "_");
            var safeSubject = string.Join("_", subject.Split(Path.GetInvalidFileNameChars())).Trim();
            if (safeSubject.Length > 50) safeSubject = safeSubject[..50];
            var filename = $"{timestamp}_{safeTo}_{safeSubject}.html";

            // Wrap body with dev-mode banner showing email metadata
            var wrappedBody = $@"<!--
  EMAIL PREVIEW (not actually sent)
  To: {to}
  Subject: {subject}
  Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-->
<div style='background:#FEF3C7; border:2px solid #F59E0B; padding:12px 16px; margin:0 0 16px 0; font-family:Arial,sans-serif; font-size:13px;'>
    <strong style='color:#92400E;'>EMAIL PREVIEW (Not Sent)</strong><br>
    <strong>To:</strong> {to}<br>
    <strong>Subject:</strong> {subject}<br>
    <strong>Date:</strong> {DateTime.Now:MMMM d, yyyy h:mm tt}
</div>
{body}";

            var filePath = Path.Combine(emailsDir, filename);
            await File.WriteAllTextAsync(filePath, wrappedBody);
            _logger.LogInformation("Email saved to file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save email to file for {EmailMasked}", PhiLog.MaskEmail(to));
        }
    }
}
