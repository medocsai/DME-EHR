using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using EHR.Services;
using EHR.Hubs;
using EHR.Models.Generated;

namespace EHR.Controllers;

/// <summary>
/// Controller for patient messaging.
/// Two route groups:
///   /api/portal/messaging/* — Patient-facing (Role=8)
///   /api/patient-messaging/* — Staff-facing (standard auth, all roles)
/// </summary>
public class PatientMessagingController : Controller
{
    private readonly IPatientMessagingService _messagingService;
    private readonly IPatientMessagingNotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly EhrDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<PatientMessagingController> _logger;

    public PatientMessagingController(
        IPatientMessagingService messagingService,
        IPatientMessagingNotificationService notificationService,
        IEmailService emailService,
        EhrDbContext context,
        IConfiguration config,
        ILogger<PatientMessagingController> logger)
    {
        _messagingService = messagingService;
        _notificationService = notificationService;
        _emailService = emailService;
        _context = context;
        _config = config;
        _logger = logger;
    }

    // ============================================
    // PATIENT-FACING ENDPOINTS (Role=8)
    // ============================================

    /// <summary>
    /// Get staff users the patient can message (from past appointments)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/providers")]
    public async Task<IActionResult> GetUsersForPatient()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        var users = await _messagingService.GetUsersForPatientAsync(patientId);
        return Ok(users);
    }

    /// <summary>
    /// Get all conversations for the patient
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/conversations")]
    public async Task<IActionResult> GetPatientConversations()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        var conversations = await _messagingService.GetConversationsForPatientAsync(patientId);
        return Ok(conversations);
    }

    /// <summary>
    /// Get or create a conversation with a staff user
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/conversations/{userId}")]
    public async Task<IActionResult> GetOrCreateConversation(int userId)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        var conversation = await _messagingService.GetOrCreateConversationAsync(patientId, userId);
        if (conversation == null)
            return BadRequest(new { message = "Cannot start conversation." });

        return Ok(conversation);
    }

    /// <summary>
    /// Get messages in a conversation (patient side)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/conversations/{conversationId}/messages")]
    public async Task<IActionResult> GetPatientMessages(int conversationId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        var messages = await _messagingService.GetMessagesAsync(conversationId, patientId, "Patient", beforeMessageId, pageSize);
        return Ok(messages);
    }

    /// <summary>
    /// Send a message from patient
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/messages")]
    public async Task<IActionResult> SendPatientMessage([FromBody] PatientSendMessageRequest request)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.MessageText))
            return BadRequest(new { message = "Message text is required." });

        var result = await _messagingService.SendMessageFromPatientAsync(patientId, request.ConversationId, request.MessageText.Trim());

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        // Get conversation for notification routing
        var conversation = await _messagingService.GetConversationAsync(request.ConversationId);
        if (conversation != null)
        {
            // Get patient name for notification
            var patient = await _context.Patients.FindAsync(patientId);
            var patientName = patient != null ? $"{patient.FirstName} {patient.LastName}".Trim() : "Patient";

            var notification = new PatientMsgNewNotification
            {
                PatientMessageId = result.PatientMessageId,
                PatientConversationId = result.PatientConversationId,
                SenderType = "Patient",
                SenderName = patientName,
                MessagePreview = request.MessageText.Trim().Length > 100 ? request.MessageText.Trim()[..100] + "..." : request.MessageText.Trim(),
                CreatedAt = result.CreatedAt
            };

            // Send SignalR to conversation group
            var messageDto = new PatientMessageDto
            {
                PatientMessageId = result.PatientMessageId,
                PatientConversationId = result.PatientConversationId,
                SenderType = "Patient",
                SenderName = patientName,
                MessageText = request.MessageText.Trim(),
                IsReadByPatient = true,
                IsReadByUser = false,
                CreatedAt = result.CreatedAt
            };
            await _notificationService.SendMessageToConversationAsync(request.ConversationId, messageDto);

            // Send badge update to the staff user in this conversation
            await _notificationService.SendNewMessageToProviderAsync(conversation.UserId, notification);
            var unread = await _messagingService.GetUnreadSummaryForUserAsync(conversation.UserId);
            await _notificationService.SendUnreadUpdateToProviderAsync(conversation.UserId, unread);
        }

        return Ok(result);
    }

    /// <summary>
    /// Mark conversation as read by patient
    /// </summary>
    [HttpPut]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/conversations/{conversationId}/read")]
    public async Task<IActionResult> MarkReadByPatient(int conversationId)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        await _messagingService.MarkAsReadByPatientAsync(conversationId, patientId);
        return Ok();
    }

    /// <summary>
    /// Get unread summary for patient
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/messaging/unread")]
    public async Task<IActionResult> GetPatientUnread()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        var unread = await _messagingService.GetUnreadSummaryForPatientAsync(patientId);
        return Ok(unread);
    }

    // ============================================
    // STAFF-FACING ENDPOINTS
    // ============================================
    // MESSAGING ROLE RESTRICTION:
    // Currently only Providers (Clinician, Role=2) can access patient messaging.
    // The restriction is enforced on the frontend (JS role check).
    // The backend uses [Authorize] (any authenticated user) — the architecture
    // supports all roles (UserId-based). To expand messaging to other roles
    // (Admin, FrontDesk, MA, Nurse), just update the JS messagingRoles array
    // in PatientMessagingProviderModule.js. No backend changes needed.
    // ============================================

    /// <summary>
    /// Get all patients for messaging (WhatsApp-style list with search + pagination)
    /// </summary>
    [HttpGet]
    [Authorize]
    [Route("api/patient-messaging/patients")]
    public async Task<IActionResult> GetAllPatientsForMessaging([FromQuery] string? search, [FromQuery] int? locationId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var tenantId = GetTenantId();
        if (tenantId == 0) return BadRequest(new { message = "Tenant not found." });

        var userId = GetUserId();
        if (userId == 0) return BadRequest(new { message = "User not found." });

        var result = await _messagingService.GetAllPatientsForMessagingAsync(tenantId, userId, locationId, search, skip, take);
        return Ok(result);
    }

    /// <summary>
    /// Get all patient conversations for the current user
    /// </summary>
    [HttpGet]
    [Authorize]
    [Route("api/patient-messaging/conversations")]
    public async Task<IActionResult> GetUserConversations()
    {
        var userId = GetUserId();
        if (userId == 0) return BadRequest(new { message = "User not found." });

        var conversations = await _messagingService.GetConversationsForUserAsync(userId);
        return Ok(conversations);
    }

    /// <summary>
    /// Create or get a conversation with a patient (staff side)
    /// </summary>
    [HttpPost]
    [Authorize]
    [Route("api/patient-messaging/conversations/create")]
    public async Task<IActionResult> CreateUserConversation([FromBody] CreateConversationRequest request)
    {
        var userId = GetUserId();
        if (userId == 0) return BadRequest(new { message = "User not found." });

        var conversation = await _messagingService.GetOrCreateConversationAsync(request.PatientId, userId);
        if (conversation == null)
            return BadRequest(new { message = "Cannot start conversation with this patient." });

        return Ok(conversation);
    }

    /// <summary>
    /// Get messages in a conversation (staff side)
    /// </summary>
    [HttpGet]
    [Authorize]
    [Route("api/patient-messaging/conversations/{conversationId}/messages")]
    public async Task<IActionResult> GetUserMessages(int conversationId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        var userId = GetUserId();
        if (userId == 0) return BadRequest(new { message = "User not found." });

        var messages = await _messagingService.GetMessagesAsync(conversationId, userId, "Provider", beforeMessageId, pageSize);
        return Ok(messages);
    }

    /// <summary>
    /// Send a message from staff to patient
    /// </summary>
    [HttpPost]
    [Authorize]
    [Route("api/patient-messaging/messages")]
    public async Task<IActionResult> SendUserMessage([FromBody] PatientSendMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == 0) return BadRequest(new { message = "User not found." });

        if (string.IsNullOrWhiteSpace(request.MessageText))
            return BadRequest(new { message = "Message text is required." });

        var result = await _messagingService.SendMessageFromUserAsync(userId, request.ConversationId, request.MessageText.Trim());

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        // Get conversation for notification routing
        var conversation = await _messagingService.GetConversationAsync(request.ConversationId);
        if (conversation != null)
        {
            var user = await _context.Users.FindAsync(userId);
            var userName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Staff";

            var notification = new PatientMsgNewNotification
            {
                PatientMessageId = result.PatientMessageId,
                PatientConversationId = result.PatientConversationId,
                SenderType = "Provider",
                SenderName = userName,
                MessagePreview = request.MessageText.Trim().Length > 100 ? request.MessageText.Trim()[..100] + "..." : request.MessageText.Trim(),
                CreatedAt = result.CreatedAt
            };

            // Send SignalR to conversation group
            var messageDto = new PatientMessageDto
            {
                PatientMessageId = result.PatientMessageId,
                PatientConversationId = result.PatientConversationId,
                SenderType = "Provider",
                SenderName = userName,
                MessageText = request.MessageText.Trim(),
                IsReadByPatient = false,
                IsReadByUser = true,
                CreatedAt = result.CreatedAt
            };
            await _notificationService.SendMessageToConversationAsync(request.ConversationId, messageDto);

            // Send badge update to patient
            await _notificationService.SendNewMessageToPatientAsync(conversation.PatientId, notification);
            var unread = await _messagingService.GetUnreadSummaryForPatientAsync(conversation.PatientId);
            await _notificationService.SendUnreadUpdateToPatientAsync(conversation.PatientId, unread);

            // Fire-and-forget: check in 10 seconds if message was read, send email if not
            var conversationId = request.ConversationId;
            var messageId = result.PatientMessageId;
            var messageText = request.MessageText.Trim();
            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(10_000);

                    // Need a new scope since this runs outside the request
                    using var scope = scopeFactory.CreateScope();
                    var ctx = scope.ServiceProvider.GetRequiredService<EhrDbContext>();
                    var emailSvc = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    var msg = await ctx.PatientMessages.FindAsync(messageId);
                    if (msg != null && !msg.IsReadByPatient)
                    {
                        var conv = await ctx.PatientConversations
                            .Include(c => c.Patient)
                            .Include(c => c.User)
                            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId);

                        if (conv?.Patient != null)
                        {
                            var enc = scope.ServiceProvider.GetRequiredService<EHR.Helpers.EncryptionHelper>();
                            enc.DecryptEntity(conv.Patient);

                            var patientEmail = conv.Patient.Email;
                            if (!string.IsNullOrWhiteSpace(patientEmail))
                            {
                                var uName = $"{conv.User.FirstName} {conv.User.LastName}".Trim();
                                var baseUrl = config["App:BaseUrl"] ?? "http://localhost:5000";

                                // Find portal code for the reply link
                                var portalAccount = await ctx.PatientPortalAccounts
                                    .FirstOrDefaultAsync(a => a.PatientId == conv.PatientId && a.TenantId == conv.TenantId && a.IsActive);
                                var portalCode = "";
                                if (portalAccount != null)
                                {
                                    var location = await ctx.Locations
                                        .FirstOrDefaultAsync(l => l.LocationId == portalAccount.LocationId && l.TenantId == conv.TenantId);
                                    portalCode = location?.PortalCode ?? "";
                                }

                                var replyUrl = $"{baseUrl}/Portal/{portalCode}?openChat={conversationId}";

                                await SendPatientMessageEmailAsync(emailSvc, patientEmail, conv.Patient.FirstName ?? "Patient", uName, messageText, replyUrl);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't throw — this is fire-and-forget
                    Console.WriteLine($"Email fallback error: {ex.Message}");
                }
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get all conversations for a patient across all providers (Patient Profile tab — any staff role)
    /// </summary>
    [HttpGet]
    [Authorize]
    [Route("api/patient-messaging/patients/{patientId}/conversations")]
    public async Task<IActionResult> GetPatientConversationsForProfile(int patientId)
    {
        var conversations = await _messagingService.GetAllConversationsForPatientAsync(patientId);
        return Ok(conversations);
    }

    /// <summary>
    /// Get messages for a specific patient conversation (Patient Profile tab — any staff role)
    /// </summary>
    [HttpGet]
    [Authorize]
    [Route("api/patient-messaging/patients/{patientId}/conversations/{conversationId}/messages")]
    public async Task<IActionResult> GetPatientConversationMessagesForProfile(int patientId, int conversationId)
    {
        var messages = await _messagingService.GetMessagesForProfileAsync(conversationId, patientId);
        return Ok(messages);
    }

    /// <summary>
    /// Mark conversation as read by staff user
    /// </summary>
    [HttpPut]
    [Authorize]
    [Route("api/patient-messaging/conversations/{conversationId}/read")]
    public async Task<IActionResult> MarkReadByUser(int conversationId)
    {
        var userId = GetUserId();
        if (userId == 0) return BadRequest(new { message = "User not found." });

        await _messagingService.MarkAsReadByUserAsync(conversationId, userId);
        return Ok();
    }

    /// <summary>
    /// Get unread summary for staff user
    /// </summary>
    [HttpGet]
    [Authorize]
    [Route("api/patient-messaging/unread")]
    public async Task<IActionResult> GetUserUnread()
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(new PatientMessagingUnreadDto());

        var unread = await _messagingService.GetUnreadSummaryForUserAsync(userId);
        return Ok(unread);
    }

    // ============================================
    // Private helpers
    // ============================================

    private int GetPatientId()
    {
        var claim = User.FindFirst("PatientId");
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private int GetTenantId()
    {
        var claim = User.FindFirst("TenantId");
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private int GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return 0;
        return int.Parse(userIdClaim.Value);
    }

    private static async Task SendPatientMessageEmailAsync(IEmailService emailService, string toEmail, string patientFirstName, string senderName, string messageText, string replyUrl)
    {
        var subject = $"New Message from {senderName} - MEDOCS Patient Portal";
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
        .message-box {{ background: white; border-left: 4px solid #1976d2; padding: 15px; margin: 15px 0; border-radius: 4px; }}
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
            <h2>New Message</h2>
            <p>Hello {patientFirstName},</p>
            <p>You have a new message from <strong>{senderName}</strong>:</p>
            <div class='message-box'>
                {System.Net.WebUtility.HtmlEncode(messageText.Length > 300 ? messageText[..300] + "..." : messageText)}
            </div>
            <p style='text-align: center;'>
                <a href='{replyUrl}' style='display: inline-block; padding: 12px 24px; background-color: #1976d2; color: #ffffff !important; text-decoration: none; border-radius: 4px; margin: 20px 0; font-weight: bold;'>Access Patient Portal</a>
            </p>
            <p>If the button doesn't work, copy and paste this link into your browser:</p>
            <p style='word-break: break-all; font-size: 12px;'>{replyUrl}</p>
        </div>
        <div class='footer'>
            <p>This is an automated message from MEDOCS. Please do not reply to this email.</p>
            <p>&copy; MEDOCS LLC</p>
        </div>
    </div>
</body>
</html>";

        await emailService.SendEmailAsync(toEmail, subject, body, true);
    }
}

// ============================================
// Request DTOs
// ============================================

public class PatientSendMessageRequest
{
    public int ConversationId { get; set; }
    public string MessageText { get; set; } = "";
}

public class CreateConversationRequest
{
    public int PatientId { get; set; }
}
