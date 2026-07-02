using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Data;
using EHR.Services.Storage;
using EHR.Hubs;

namespace EHR.Services;

/// <summary>
/// Interface for internal messaging service operations
/// </summary>
public interface IMessagingService
{
    // Conversations
    Task<List<ConversationListDto>> GetConversationsAsync(int userId);
    Task<ConversationListDto?> GetOrCreateConversationAsync(int userId, int otherUserId);
    Task<int?> GetConversationIdAsync(int userId, int otherUserId);

    // Messages
    Task<MessageHistoryResponseDto> GetMessagesAsync(int conversationId, int userId, MessageHistoryRequestDto request);
    Task<MessageSentResponseDto> SendTextMessageAsync(int senderId, SendMessageDto dto);
    Task<MessageSentResponseDto> SendVoiceNoteAsync(int senderId, SendVoiceNoteDto dto, IFormFile audioFile);
    Task<MessageSentResponseDto> SendFileAttachmentAsync(int senderId, SendFileAttachmentDto dto, IFormFile file);
    Task<bool> MarkMessagesAsReadAsync(int conversationId, int userId);
    Task<bool> MarkMessageAsReadAsync(int messageId, int userId);

    // Users
    Task<List<MessagingUserDto>> GetAvailableUsersAsync(int currentUserId);
    Task<UnreadMessagesSummaryDto> GetUnreadSummaryAsync(int userId);

    // Presence
    Task UpdateUserPresenceAsync(int userId, bool isOnline, string? connectionId = null);
    Task<Dictionary<int, bool>> GetUsersOnlineStatusAsync(IEnumerable<int> userIds);
    Task<string?> GetUserConnectionIdAsync(int userId);

    // Search
    Task<List<MessageSearchResultDto>> SearchMessagesAsync(int userId, SearchMessagesRequestDto request);
}

/// <summary>
/// Service for handling internal messaging operations.
/// Implements HIPAA-compliant encryption for all message content.
/// </summary>
public class MessagingService : IMessagingService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryption;
    private readonly IFileStorageService _fileStorage;
    private readonly IMessagingNotificationService _notificationService;
    private readonly ILogger<MessagingService> _logger;

    // Maximum file size: 10 MB
    private const long MaxFileSize = 10 * 1024 * 1024;
    // Maximum voice note duration: 5 minutes
    private const int MaxVoiceNoteDurationSeconds = 300;
    // Allowed file types
    private static readonly HashSet<string> AllowedFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".gif", ".txt"
    };
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/jpeg", "image/png", "image/gif", "text/plain",
        "audio/webm", "audio/mp3", "audio/mpeg", "audio/ogg", "audio/wav"
    };

    public MessagingService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryption,
        IFileStorageService fileStorage,
        IMessagingNotificationService notificationService,
        ILogger<MessagingService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryption = encryption;
        _fileStorage = fileStorage;
        _notificationService = notificationService;
        _logger = logger;
    }

    #region Conversations

    /// <summary>
    /// Get all conversations for a user, sorted by most recent activity
    /// </summary>
    public async Task<List<ConversationListDto>> GetConversationsAsync(int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("GetConversationsAsync called without TenantId for user {UserId}", userId);
            return new List<ConversationListDto>();
        }

        var conversations = await _context.Conversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value &&
                       (c.User1Id == userId || c.User2Id == userId))
            .Include(c => c.User1)
            .Include(c => c.User2)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync();

        // Get online status for all other users
        var otherUserIds = conversations
            .Select(c => c.User1Id == userId ? c.User2Id : c.User1Id)
            .ToList();
        var onlineStatuses = await GetUsersOnlineStatusAsync(otherUserIds);

        return conversations.Select(c =>
        {
            var isUser1 = c.User1Id == userId;
            var otherUser = isUser1 ? c.User2 : c.User1;
            var unreadCount = isUser1 ? c.User1UnreadCount : c.User2UnreadCount;

            return new ConversationListDto
            {
                ConversationId = c.ConversationId,
                OtherUser = new MessagingUserDto
                {
                    UserId = otherUser.UserId,
                    FirstName = otherUser.FirstName,
                    LastName = otherUser.LastName,
                    Email = otherUser.Email,
                    Role = otherUser.Role,
                    IsOnline = onlineStatuses.GetValueOrDefault(otherUser.UserId, false)
                },
                LastMessagePreview = DecryptPreview(c.LastMessageText),
                LastMessageAt = c.LastMessageAt,
                LastMessageSenderId = c.LastMessageSenderId,
                UnreadCount = unreadCount,
                CreatedAt = c.CreatedAt
            };
        }).ToList();
    }

    /// <summary>
    /// Get or create a conversation between two users
    /// </summary>
    public async Task<ConversationListDto?> GetOrCreateConversationAsync(int userId, int otherUserId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("GetOrCreateConversationAsync called without TenantId for user {UserId}", userId);
            return null;
        }

        // Ensure both users are in the same tenant
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId.Value &&
                       (u.UserId == userId || u.UserId == otherUserId) &&
                       u.IsActive == true)
            .ToListAsync();

        if (users.Count != 2)
        {
            _logger.LogWarning("Cannot create conversation: users not found or inactive. UserId: {UserId}, OtherUserId: {OtherUserId}",
                userId, otherUserId);
            return null;
        }

        // Normalize user IDs (lower ID always User1)
        var user1Id = Math.Min(userId, otherUserId);
        var user2Id = Math.Max(userId, otherUserId);

        // Try to find existing conversation
        var conversation = await _context.Conversations
            .Include(c => c.User1)
            .Include(c => c.User2)
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId.Value &&
                c.User1Id == user1Id &&
                c.User2Id == user2Id);

        if (conversation == null)
        {
            // Create new conversation
            conversation = new Conversation
            {
                TenantId = tenantId.Value,
                User1Id = user1Id,
                User2Id = user2Id,
                CreatedAt = DateTime.UtcNow
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();

            // Reload with navigation properties
            conversation = await _context.Conversations
                .Include(c => c.User1)
                .Include(c => c.User2)
                .FirstAsync(c => c.ConversationId == conversation.ConversationId);
        }

        var isUser1 = conversation.User1Id == userId;
        var otherUser = isUser1 ? conversation.User2 : conversation.User1;
        var isOnline = await IsUserOnlineAsync(otherUser.UserId);

        return new ConversationListDto
        {
            ConversationId = conversation.ConversationId,
            OtherUser = new MessagingUserDto
            {
                UserId = otherUser.UserId,
                FirstName = otherUser.FirstName,
                LastName = otherUser.LastName,
                Email = otherUser.Email,
                Role = otherUser.Role,
                IsOnline = isOnline
            },
            LastMessagePreview = DecryptPreview(conversation.LastMessageText),
            LastMessageAt = conversation.LastMessageAt,
            UnreadCount = isUser1 ? conversation.User1UnreadCount : conversation.User2UnreadCount,
            CreatedAt = conversation.CreatedAt
        };
    }

    /// <summary>
    /// Get conversation ID between two users (if exists)
    /// </summary>
    public async Task<int?> GetConversationIdAsync(int userId, int otherUserId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return null;

        var user1Id = Math.Min(userId, otherUserId);
        var user2Id = Math.Max(userId, otherUserId);

        return await _context.Conversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value &&
                       c.User1Id == user1Id &&
                       c.User2Id == user2Id)
            .Select(c => (int?)c.ConversationId)
            .FirstOrDefaultAsync();
    }

    #endregion

    #region Messages

    /// <summary>
    /// Get message history for a conversation with pagination
    /// </summary>
    public async Task<MessageHistoryResponseDto> GetMessagesAsync(int conversationId, int userId, MessageHistoryRequestDto request)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required");

        // Verify user has access to conversation
        var conversation = await _context.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.ConversationId == conversationId &&
                c.TenantId == tenantId.Value &&
                (c.User1Id == userId || c.User2Id == userId));

        if (conversation == null)
            throw new UnauthorizedAccessException("Access to conversation denied");

        var pageSize = Math.Min(Math.Max(request.PageSize, 10), 100);
        var query = _context.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId &&
                       m.TenantId == tenantId.Value);

        // Filter by soft delete for current user
        var isUser1 = conversation.User1Id == userId;
        if (isUser1)
            query = query.Where(m => !m.IsDeletedBySender || m.SenderId != userId);
        else
            query = query.Where(m => !m.IsDeletedByRecipient || m.RecipientId != userId);

        // Apply pagination
        if (request.BeforeMessageId.HasValue)
        {
            query = query.Where(m => m.MessageId < request.BeforeMessageId.Value)
                         .OrderByDescending(m => m.MessageId);
        }
        else if (request.AfterMessageId.HasValue)
        {
            query = query.Where(m => m.MessageId > request.AfterMessageId.Value)
                         .OrderBy(m => m.MessageId);
        }
        else
        {
            // Default: get most recent messages
            query = query.OrderByDescending(m => m.MessageId);
        }

        var messages = await query
            .Take(pageSize + 1) // Take one extra to check if there are more
            .Include(m => m.Sender)
            .ToListAsync();

        // Check if there are more messages
        var hasMore = messages.Count > pageSize;
        if (hasMore)
            messages = messages.Take(pageSize).ToList();

        // If we loaded older messages, reverse to chronological order
        if (request.BeforeMessageId.HasValue || !request.AfterMessageId.HasValue)
            messages.Reverse();

        var result = new MessageHistoryResponseDto
        {
            Messages = messages.Select(m => MapToMessageDto(m, userId)).ToList(),
            HasMoreOlder = request.BeforeMessageId.HasValue ? hasMore : messages.Count == pageSize,
            HasMoreNewer = request.AfterMessageId.HasValue && hasMore,
            OldestMessageId = messages.FirstOrDefault()?.MessageId,
            NewestMessageId = messages.LastOrDefault()?.MessageId
        };

        return result;
    }

    /// <summary>
    /// Send a text message
    /// </summary>
    public async Task<MessageSentResponseDto> SendTextMessageAsync(int senderId, SendMessageDto dto)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required");

        if (string.IsNullOrWhiteSpace(dto.MessageText))
            return new MessageSentResponseDto { Success = false, Message = "Message text is required" };

        // Get or create conversation
        var conversationDto = await GetOrCreateConversationAsync(senderId, dto.RecipientId);
        if (conversationDto == null)
            return new MessageSentResponseDto { Success = false, Message = "Unable to create conversation" };

        // Get sender info for notification
        var sender = await _context.Users.FindAsync(senderId);
        var senderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : "Unknown";

        // Create message with encrypted content
        var message = new Message
        {
            TenantId = tenantId.Value,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            RecipientId = dto.RecipientId,
            MessageText = _encryption.Encrypt(dto.MessageText),
            MessageType = (int)MessageType.Text,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);

        // Update conversation
        await UpdateConversationLastMessageAsync(conversationDto.ConversationId, senderId, dto.MessageText);

        await _context.SaveChangesAsync();

        _logger.LogInformation("Text message sent from {SenderId} to {RecipientId} in conversation {ConversationId}",
            senderId, dto.RecipientId, conversationDto.ConversationId);

        // Send real-time notification to recipient
        var truncatedPreview = dto.MessageText.Length > 50
            ? dto.MessageText.Substring(0, 47) + "..."
            : dto.MessageText;

        await _notificationService.SendNewMessageNotificationAsync(dto.RecipientId, new NewMessageNotification
        {
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            SenderName = senderName,
            MessagePreview = truncatedPreview,
            MessageType = (int)MessageType.Text,
            CreatedAt = message.CreatedAt
        });

        // Also send to conversation group for real-time updates
        await _notificationService.SendMessageToConversationAsync(conversationDto.ConversationId, new MessageDto
        {
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            SenderName = senderName,
            MessageText = dto.MessageText,
            MessageType = (int)MessageType.Text,
            CreatedAt = message.CreatedAt,
            CreatedAtFormatted = "Just now"
        });

        return new MessageSentResponseDto
        {
            Success = true,
            Message = "Message sent",
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            CreatedAt = message.CreatedAt
        };
    }

    /// <summary>
    /// Send a voice note message
    /// </summary>
    public async Task<MessageSentResponseDto> SendVoiceNoteAsync(int senderId, SendVoiceNoteDto dto, IFormFile audioFile)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required");

        // Validate file
        if (audioFile == null || audioFile.Length == 0)
            return new MessageSentResponseDto { Success = false, Message = "Audio file is required" };

        if (audioFile.Length > MaxFileSize)
            return new MessageSentResponseDto { Success = false, Message = $"File size exceeds {MaxFileSize / (1024 * 1024)}MB limit" };

        if (dto.DurationSeconds > MaxVoiceNoteDurationSeconds)
            return new MessageSentResponseDto { Success = false, Message = $"Voice note exceeds {MaxVoiceNoteDurationSeconds / 60} minute limit" };

        // Get or create conversation
        var conversationDto = await GetOrCreateConversationAsync(senderId, dto.RecipientId);
        if (conversationDto == null)
            return new MessageSentResponseDto { Success = false, Message = "Unable to create conversation" };

        // Upload file to cloud storage
        var fileName = $"voicenote_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{Path.GetExtension(audioFile.FileName)}";
        var folderPath = $"messaging/tenant_{tenantId.Value}/voicenotes";

        FileUploadResult uploadResult;
        using (var stream = audioFile.OpenReadStream())
        {
            uploadResult = await _fileStorage.UploadFileAsync(stream, fileName, folderPath, audioFile.ContentType);
        }

        if (!uploadResult.Success)
            return new MessageSentResponseDto { Success = false, Message = uploadResult.ErrorMessage ?? "Failed to upload voice note" };

        // Create message
        var message = new Message
        {
            TenantId = tenantId.Value,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            RecipientId = dto.RecipientId,
            MessageText = !string.IsNullOrWhiteSpace(dto.Caption) ? _encryption.Encrypt(dto.Caption) : null,
            MessageType = (int)MessageType.VoiceNote,
            FileUrl = uploadResult.CloudPath,
            FileName = fileName,
            FileSize = audioFile.Length,
            FileMimeType = audioFile.ContentType,
            FileDurationSeconds = dto.DurationSeconds,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);

        // Update conversation
        await UpdateConversationLastMessageAsync(conversationDto.ConversationId, senderId, "[Voice Note]");

        await _context.SaveChangesAsync();

        _logger.LogInformation("Voice note sent from {SenderId} to {RecipientId} in conversation {ConversationId}, duration: {Duration}s",
            senderId, dto.RecipientId, conversationDto.ConversationId, dto.DurationSeconds);

        // Get sender info for notification
        var sender = await _context.Users.FindAsync(senderId);
        var senderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : "Unknown";

        // Send real-time notification to recipient
        await _notificationService.SendNewMessageNotificationAsync(dto.RecipientId, new NewMessageNotification
        {
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            SenderName = senderName,
            MessagePreview = "Voice note",
            MessageType = (int)MessageType.VoiceNote,
            CreatedAt = message.CreatedAt
        });

        // Also send to conversation group for real-time updates
        await _notificationService.SendMessageToConversationAsync(conversationDto.ConversationId, new MessageDto
        {
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            SenderName = senderName,
            MessageType = (int)MessageType.VoiceNote,
            FileName = fileName,
            FileDurationSeconds = dto.DurationSeconds,
            CreatedAt = message.CreatedAt,
            CreatedAtFormatted = "Just now"
        });

        // Get signed URL for playback
        var signedUrl = await _fileStorage.GetSignedUrlAsync(uploadResult.CloudPath, 60);

        return new MessageSentResponseDto
        {
            Success = true,
            Message = "Voice note sent",
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            CreatedAt = message.CreatedAt,
            FileUrl = signedUrl
        };
    }

    /// <summary>
    /// Send a file attachment
    /// </summary>
    public async Task<MessageSentResponseDto> SendFileAttachmentAsync(int senderId, SendFileAttachmentDto dto, IFormFile file)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required");

        // Validate file
        if (file == null || file.Length == 0)
            return new MessageSentResponseDto { Success = false, Message = "File is required" };

        if (file.Length > MaxFileSize)
            return new MessageSentResponseDto { Success = false, Message = $"File size exceeds {MaxFileSize / (1024 * 1024)}MB limit" };

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedFileTypes.Contains(extension))
            return new MessageSentResponseDto { Success = false, Message = "File type not allowed. Allowed types: PDF, DOC, DOCX, XLS, XLSX, JPG, PNG, TXT" };

        // Get or create conversation
        var conversationDto = await GetOrCreateConversationAsync(senderId, dto.RecipientId);
        if (conversationDto == null)
            return new MessageSentResponseDto { Success = false, Message = "Unable to create conversation" };

        // Upload file to cloud storage
        var safeFileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var folderPath = $"messaging/tenant_{tenantId.Value}/files";

        FileUploadResult uploadResult;
        using (var stream = file.OpenReadStream())
        {
            uploadResult = await _fileStorage.UploadFileAsync(stream, safeFileName, folderPath, file.ContentType);
        }

        if (!uploadResult.Success)
            return new MessageSentResponseDto { Success = false, Message = uploadResult.ErrorMessage ?? "Failed to upload file" };

        // Create message
        var message = new Message
        {
            TenantId = tenantId.Value,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            RecipientId = dto.RecipientId,
            MessageText = !string.IsNullOrWhiteSpace(dto.Caption) ? _encryption.Encrypt(dto.Caption) : null,
            MessageType = (int)MessageType.File,
            FileUrl = uploadResult.CloudPath,
            FileName = file.FileName, // Keep original name for display
            FileSize = file.Length,
            FileMimeType = file.ContentType,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);

        // Update conversation
        await UpdateConversationLastMessageAsync(conversationDto.ConversationId, senderId, $"[File: {file.FileName}]");

        await _context.SaveChangesAsync();

        _logger.LogInformation("File sent from {SenderId} to {RecipientId} in conversation {ConversationId}, file: {FileName}",
            senderId, dto.RecipientId, conversationDto.ConversationId, file.FileName);

        // Get sender info for notification
        var sender = await _context.Users.FindAsync(senderId);
        var senderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : "Unknown";

        // Send real-time notification to recipient
        await _notificationService.SendNewMessageNotificationAsync(dto.RecipientId, new NewMessageNotification
        {
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            SenderName = senderName,
            MessagePreview = file.FileName,
            MessageType = (int)MessageType.File,
            CreatedAt = message.CreatedAt
        });

        // Also send to conversation group for real-time updates
        await _notificationService.SendMessageToConversationAsync(conversationDto.ConversationId, new MessageDto
        {
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            SenderId = senderId,
            SenderName = senderName,
            MessageType = (int)MessageType.File,
            FileName = file.FileName,
            FileSize = file.Length,
            FileMimeType = file.ContentType,
            CreatedAt = message.CreatedAt,
            CreatedAtFormatted = "Just now"
        });

        // Get signed URL for download
        var signedUrl = await _fileStorage.GetSignedUrlAsync(uploadResult.CloudPath, 60);

        return new MessageSentResponseDto
        {
            Success = true,
            Message = "File sent",
            MessageId = message.MessageId,
            ConversationId = conversationDto.ConversationId,
            CreatedAt = message.CreatedAt,
            FileUrl = signedUrl
        };
    }

    /// <summary>
    /// Mark all messages in a conversation as read for a user
    /// </summary>
    public async Task<bool> MarkMessagesAsReadAsync(int conversationId, int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return false;

        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c =>
                c.ConversationId == conversationId &&
                c.TenantId == tenantId.Value &&
                (c.User1Id == userId || c.User2Id == userId));

        if (conversation == null) return false;

        var now = DateTime.UtcNow;

        // Mark all unread messages as read
        var unreadMessages = await _context.Messages
            .Where(m => m.ConversationId == conversationId &&
                       m.RecipientId == userId &&
                       !m.IsRead)
            .ToListAsync();

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
            message.ReadAt = now;
        }

        // Reset unread count for this user
        if (conversation.User1Id == userId)
            conversation.User1UnreadCount = 0;
        else
            conversation.User2UnreadCount = 0;

        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Mark a specific message as read
    /// </summary>
    public async Task<bool> MarkMessageAsReadAsync(int messageId, int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return false;

        var message = await _context.Messages
            .Include(m => m.Conversation)
            .FirstOrDefaultAsync(m =>
                m.MessageId == messageId &&
                m.TenantId == tenantId.Value &&
                m.RecipientId == userId &&
                !m.IsRead);

        if (message == null) return false;

        message.IsRead = true;
        message.ReadAt = DateTime.UtcNow;

        // Decrement unread count
        var conversation = message.Conversation;
        if (conversation.User1Id == userId && conversation.User1UnreadCount > 0)
            conversation.User1UnreadCount--;
        else if (conversation.User2Id == userId && conversation.User2UnreadCount > 0)
            conversation.User2UnreadCount--;

        await _context.SaveChangesAsync();
        return true;
    }

    #endregion

    #region Users

    /// <summary>
    /// Get list of users available for messaging (same tenant, active)
    /// </summary>
    public async Task<List<MessagingUserDto>> GetAvailableUsersAsync(int currentUserId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return new List<MessagingUserDto>();

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId.Value &&
                       u.UserId != currentUserId &&
                       u.IsActive == true)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ToListAsync();

        var userIds = users.Select(u => u.UserId).ToList();
        var onlineStatuses = await GetUsersOnlineStatusAsync(userIds);

        return users.Select(u => new MessagingUserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            Role = u.Role,
            IsOnline = onlineStatuses.GetValueOrDefault(u.UserId, false)
        }).ToList();
    }

    /// <summary>
    /// Get summary of unread messages for badge display
    /// </summary>
    public async Task<UnreadMessagesSummaryDto> GetUnreadSummaryAsync(int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            return new UnreadMessagesSummaryDto();

        var conversations = await _context.Conversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value &&
                       (c.User1Id == userId || c.User2Id == userId))
            .ToListAsync();

        var totalUnread = 0;
        var unreadConversations = 0;

        foreach (var conv in conversations)
        {
            var unread = conv.User1Id == userId ? conv.User1UnreadCount : conv.User2UnreadCount;
            if (unread > 0)
            {
                totalUnread += unread;
                unreadConversations++;
            }
        }

        return new UnreadMessagesSummaryDto
        {
            TotalUnreadCount = totalUnread,
            UnreadConversationsCount = unreadConversations
        };
    }

    #endregion

    #region Presence

    /// <summary>
    /// Update user's online/offline status
    /// </summary>
    public async Task UpdateUserPresenceAsync(int userId, bool isOnline, string? connectionId = null)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return;

        var presence = await _context.UserPresences
            .FirstOrDefaultAsync(p => p.TenantId == tenantId.Value && p.UserId == userId);

        if (presence == null)
        {
            presence = new UserPresence
            {
                TenantId = tenantId.Value,
                UserId = userId,
                Status = isOnline ? (int)PresenceStatus.Online : (int)PresenceStatus.Offline,
                LastActiveAt = DateTime.UtcNow,
                ConnectionId = connectionId
            };
            _context.UserPresences.Add(presence);
        }
        else
        {
            presence.Status = isOnline ? (int)PresenceStatus.Online : (int)PresenceStatus.Offline;
            presence.LastActiveAt = DateTime.UtcNow;
            presence.ConnectionId = connectionId;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Get online status for multiple users
    /// </summary>
    public async Task<Dictionary<int, bool>> GetUsersOnlineStatusAsync(IEnumerable<int> userIds)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            return userIds.ToDictionary(id => id, _ => false);

        var presences = await _context.UserPresences
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId.Value && userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Status, p.LastActiveAt })
            .ToListAsync();

        // Consider online if status is Online and last active within 5 minutes
        var cutoff = DateTime.UtcNow.AddMinutes(-5);

        return userIds.ToDictionary(
            id => id,
            id =>
            {
                var presence = presences.FirstOrDefault(p => p.UserId == id);
                return presence != null &&
                       presence.Status == (int)PresenceStatus.Online &&
                       presence.LastActiveAt > cutoff;
            });
    }

    private async Task<bool> IsUserOnlineAsync(int userId)
    {
        var statuses = await GetUsersOnlineStatusAsync(new[] { userId });
        return statuses.GetValueOrDefault(userId, false);
    }

    /// <summary>
    /// Get the current connection ID for a user (for handling multiple connections)
    /// </summary>
    public async Task<string?> GetUserConnectionIdAsync(int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue)
            return null;

        var presence = await _context.UserPresences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId.Value && p.UserId == userId);

        return presence?.ConnectionId;
    }

    #endregion

    #region Search

    /// <summary>
    /// Search messages by text content
    /// </summary>
    public async Task<List<MessageSearchResultDto>> SearchMessagesAsync(int userId, SearchMessagesRequestDto request)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue || string.IsNullOrWhiteSpace(request.Query))
            return new List<MessageSearchResultDto>();

        // Get user's conversations
        var conversationIds = await _context.Conversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value &&
                       (c.User1Id == userId || c.User2Id == userId))
            .Select(c => c.ConversationId)
            .ToListAsync();

        if (request.ConversationId.HasValue)
            conversationIds = conversationIds.Where(id => id == request.ConversationId.Value).ToList();

        if (!conversationIds.Any())
            return new List<MessageSearchResultDto>();

        // Search messages
        // Note: For better performance with encrypted content, consider implementing
        // a search index or using decrypted data in a secure manner
        var messages = await _context.Messages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId) &&
                       m.MessageType == (int)MessageType.Text)
            .Include(m => m.Conversation)
                .ThenInclude(c => c.User1)
            .Include(m => m.Conversation)
                .ThenInclude(c => c.User2)
            .OrderByDescending(m => m.CreatedAt)
            .Take(500) // Limit for performance
            .ToListAsync();

        var searchTermLower = request.Query.ToLower();
        var results = new List<MessageSearchResultDto>();

        foreach (var message in messages)
        {
            if (string.IsNullOrEmpty(message.MessageText)) continue;

            var decrypted = _encryption.Decrypt(message.MessageText);
            if (decrypted.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                var otherUser = message.Conversation.User1Id == userId
                    ? message.Conversation.User2
                    : message.Conversation.User1;

                results.Add(new MessageSearchResultDto
                {
                    MessageId = message.MessageId,
                    ConversationId = message.ConversationId,
                    OtherUserName = $"{otherUser.FirstName} {otherUser.LastName}",
                    MessagePreview = TruncateText(decrypted, 100),
                    CreatedAt = message.CreatedAt,
                    HighlightedText = HighlightSearchTerm(decrypted, request.Query)
                });

                if (results.Count >= request.MaxResults)
                    break;
            }
        }

        return results;
    }

    #endregion

    #region Private Helpers

    private async Task UpdateConversationLastMessageAsync(int conversationId, int senderId, string previewText)
    {
        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId);

        if (conversation == null) return;

        conversation.LastMessageText = _encryption.Encrypt(TruncateText(previewText, 200));
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.LastMessageSenderId = senderId;
        conversation.UpdatedAt = DateTime.UtcNow;

        // Increment unread count for recipient
        if (conversation.User1Id == senderId)
            conversation.User2UnreadCount++;
        else
            conversation.User1UnreadCount++;
    }

    private MessageDto MapToMessageDto(Message message, int currentUserId)
    {
        string? fileUrl = null;
        if (!string.IsNullOrEmpty(message.FileUrl))
        {
            // Generate signed URL for file access (60 minutes expiration)
            fileUrl = _fileStorage.GetSignedUrlAsync(message.FileUrl, 60).Result;
        }

        return new MessageDto
        {
            MessageId = message.MessageId,
            ConversationId = message.ConversationId,
            SenderId = message.SenderId,
            SenderName = message.Sender != null ? $"{message.Sender.FirstName} {message.Sender.LastName}" : "Unknown",
            RecipientId = message.RecipientId,
            MessageText = !string.IsNullOrEmpty(message.MessageText) ? _encryption.Decrypt(message.MessageText) : null,
            MessageType = message.MessageType,
            FileUrl = fileUrl,
            FileName = message.FileName,
            FileSize = message.FileSize,
            FileMimeType = message.FileMimeType,
            FileDurationSeconds = message.FileDurationSeconds,
            IsRead = message.IsRead,
            ReadAt = message.ReadAt,
            CreatedAt = message.CreatedAt,
            CreatedAtFormatted = FormatMessageTimestamp(message.CreatedAt),
            IsMine = message.SenderId == currentUserId
        };
    }

    private string? DecryptPreview(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return null;
        try
        {
            return _encryption.Decrypt(encryptedText);
        }
        catch
        {
            return "[Unable to decrypt]";
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }

    private static string FormatMessageTimestamp(DateTime utcTime)
    {
        var local = utcTime.ToLocalTime();
        var now = DateTime.Now;

        if (local.Date == now.Date)
            return local.ToString("h:mm tt");
        else if (local.Date == now.Date.AddDays(-1))
            return "Yesterday";
        else if (local.Date > now.Date.AddDays(-7))
            return local.ToString("ddd");
        else if (local.Year == now.Year)
            return local.ToString("MMM d");
        else
            return local.ToString("MMM d, yyyy");
    }

    private static string HighlightSearchTerm(string text, string searchTerm)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchTerm))
            return text;

        var index = text.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return text;

        // Get context around the match
        var start = Math.Max(0, index - 30);
        var end = Math.Min(text.Length, index + searchTerm.Length + 30);

        var snippet = text[start..end];
        if (start > 0) snippet = "..." + snippet;
        if (end < text.Length) snippet += "...";

        return snippet;
    }

    #endregion
}
