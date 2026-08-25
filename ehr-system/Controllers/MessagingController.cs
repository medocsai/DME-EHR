using EHR.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EHR.Services;
using EHR.Models;
using EHR.Hubs;

namespace EHR.Controllers;

/// <summary>
/// API controller for internal messaging system.
/// Handles conversations, messages, file uploads, and user presence.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagingController : ControllerBase
{
    private readonly IMessagingService _messagingService;
    private readonly IMessagingNotificationService _notificationService;
    private readonly ILogger<MessagingController> _logger;

    public MessagingController(
        IMessagingService messagingService,
        IMessagingNotificationService notificationService,
        ILogger<MessagingController> logger)
    {
        _messagingService = messagingService;
        _notificationService = notificationService;
        _logger = logger;
    }

    #region Conversations

    /// <summary>
    /// Get all conversations for the current user
    /// </summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationListDto>>> GetConversations()
    {
        try
        {
            var userId = GetUserId();
            _logger.LogInformation("GetConversations called. UserId: {UserId}, Claims: {Claims}",
                userId, string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

            if (userId == null) return Unauthorized();

            var conversations = await _messagingService.GetConversationsAsync(userId.Value);
            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversations. Exception: {Message}", ex.Message);
            return this.ServerError(ex, "Failed to get conversations.");
        }
    }

    /// <summary>
    /// Get or create a conversation with another user
    /// </summary>
    [HttpPost("conversations/{otherUserId}")]
    public async Task<ActionResult<ConversationListDto>> GetOrCreateConversation(int otherUserId)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            if (userId == otherUserId)
                return BadRequest(new { message = "Cannot create conversation with yourself" });

            var conversation = await _messagingService.GetOrCreateConversationAsync(userId.Value, otherUserId);
            if (conversation == null)
                return BadRequest(new { message = "Unable to create conversation. User may be inactive or in a different clinic." });

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation");
            return StatusCode(500, new { message = "Failed to create conversation" });
        }
    }

    #endregion

    #region Messages

    /// <summary>
    /// Get messages for a conversation with pagination
    /// </summary>
    [HttpGet("conversations/{conversationId}/messages")]
    public async Task<ActionResult<MessageHistoryResponseDto>> GetMessages(
        int conversationId,
        [FromQuery] int? beforeMessageId = null,
        [FromQuery] int? afterMessageId = null,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var request = new MessageHistoryRequestDto
            {
                BeforeMessageId = beforeMessageId,
                AfterMessageId = afterMessageId,
                PageSize = pageSize
            };

            var messages = await _messagingService.GetMessagesAsync(conversationId, userId.Value, request);
            return Ok(messages);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for conversation {ConversationId}", conversationId);
            return StatusCode(500, new { message = "Failed to get messages" });
        }
    }

    /// <summary>
    /// Send a text message
    /// </summary>
    [HttpPost("messages")]
    public async Task<ActionResult<MessageSentResponseDto>> SendTextMessage([FromBody] SendMessageDto dto)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var result = await _messagingService.SendTextMessageAsync(userId.Value, dto);

            if (result.Success && result.MessageId.HasValue && result.ConversationId.HasValue)
            {
                // Send real-time notification to recipient
                await _notificationService.SendNewMessageNotificationAsync(dto.RecipientId, new NewMessageNotification
                {
                    MessageId = result.MessageId.Value,
                    ConversationId = result.ConversationId.Value,
                    SenderId = userId.Value,
                    SenderName = GetUserName(),
                    MessagePreview = TruncateText(dto.MessageText, 100),
                    MessageType = (int)MessageType.Text,
                    CreatedAt = result.CreatedAt ?? DateTime.UtcNow
                });
            }

            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending text message");
            return StatusCode(500, new { message = "Failed to send message" });
        }
    }

    /// <summary>
    /// Send a voice note
    /// </summary>
    [HttpPost("voice-notes")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20MB limit
    public async Task<ActionResult<MessageSentResponseDto>> SendVoiceNote(
        [FromForm] int recipientId,
        [FromForm] decimal durationSeconds,
        [FromForm] string? caption,
        IFormFile audioFile)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var dto = new SendVoiceNoteDto
            {
                RecipientId = recipientId,
                DurationSeconds = durationSeconds,
                Caption = caption
            };

            var result = await _messagingService.SendVoiceNoteAsync(userId.Value, dto, audioFile);

            if (result.Success && result.MessageId.HasValue && result.ConversationId.HasValue)
            {
                await _notificationService.SendNewMessageNotificationAsync(recipientId, new NewMessageNotification
                {
                    MessageId = result.MessageId.Value,
                    ConversationId = result.ConversationId.Value,
                    SenderId = userId.Value,
                    SenderName = GetUserName(),
                    MessagePreview = "Voice note",
                    MessageType = (int)MessageType.VoiceNote,
                    DurationSeconds = durationSeconds,
                    CreatedAt = result.CreatedAt ?? DateTime.UtcNow
                });
            }

            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending voice note");
            return StatusCode(500, new { message = "Failed to send voice note" });
        }
    }

    /// <summary>
    /// Send a file attachment
    /// </summary>
    [HttpPost("attachments")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20MB limit
    public async Task<ActionResult<MessageSentResponseDto>> SendFileAttachment(
        [FromForm] int recipientId,
        [FromForm] string? caption,
        IFormFile file)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var dto = new SendFileAttachmentDto
            {
                RecipientId = recipientId,
                Caption = caption
            };

            var result = await _messagingService.SendFileAttachmentAsync(userId.Value, dto, file);

            if (result.Success && result.MessageId.HasValue && result.ConversationId.HasValue)
            {
                await _notificationService.SendNewMessageNotificationAsync(recipientId, new NewMessageNotification
                {
                    MessageId = result.MessageId.Value,
                    ConversationId = result.ConversationId.Value,
                    SenderId = userId.Value,
                    SenderName = GetUserName(),
                    MessagePreview = file.FileName,
                    MessageType = (int)MessageType.File,
                    FileName = file.FileName,
                    CreatedAt = result.CreatedAt ?? DateTime.UtcNow
                });
            }

            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending file attachment");
            return StatusCode(500, new { message = "Failed to send file" });
        }
    }

    /// <summary>
    /// Mark all messages in a conversation as read
    /// </summary>
    [HttpPut("conversations/{conversationId}/read")]
    public async Task<ActionResult> MarkMessagesAsRead(int conversationId)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var success = await _messagingService.MarkMessagesAsReadAsync(conversationId, userId.Value);
            return success ? Ok() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking messages as read");
            return StatusCode(500, new { message = "Failed to mark messages as read" });
        }
    }

    /// <summary>
    /// Mark a specific message as read
    /// </summary>
    [HttpPut("messages/{messageId}/read")]
    public async Task<ActionResult> MarkMessageAsRead(int messageId)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var success = await _messagingService.MarkMessageAsReadAsync(messageId, userId.Value);
            return success ? Ok() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message as read");
            return StatusCode(500, new { message = "Failed to mark message as read" });
        }
    }

    #endregion

    #region Users

    /// <summary>
    /// Get list of users available for messaging (same clinic, active)
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult<List<MessagingUserDto>>> GetAvailableUsers()
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var users = await _messagingService.GetAvailableUsersAsync(userId.Value);
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available users");
            return StatusCode(500, new { message = "Failed to get users" });
        }
    }

    /// <summary>
    /// Get unread messages summary (for badge display)
    /// </summary>
    [HttpGet("unread-summary")]
    public async Task<ActionResult<UnreadMessagesSummaryDto>> GetUnreadSummary()
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var summary = await _messagingService.GetUnreadSummaryAsync(userId.Value);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unread summary");
            return StatusCode(500, new { message = "Failed to get unread summary" });
        }
    }

    #endregion

    #region Search

    /// <summary>
    /// Search messages
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<MessageSearchResultDto>>> SearchMessages(
        [FromQuery] string query,
        [FromQuery] int? conversationId = null,
        [FromQuery] int maxResults = 20)
    {
        try
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var request = new SearchMessagesRequestDto
            {
                Query = query,
                ConversationId = conversationId,
                MaxResults = maxResults
            };

            var results = await _messagingService.SearchMessagesAsync(userId.Value, request);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching messages");
            return StatusCode(500, new { message = "Failed to search messages" });
        }
    }

    #endregion

    #region Private Helpers

    private int? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private string GetUserName()
    {
        return User.FindFirst(ClaimTypes.Email)?.Value ?? "Unknown";
    }

    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? "";
        return text[..(maxLength - 3)] + "...";
    }

    #endregion
}
