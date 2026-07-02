using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EHR.Services;
using EHR.Models;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for real-time messaging features.
/// Handles: message delivery, typing indicators, read receipts, presence updates.
/// </summary>
[Authorize]
public class MessagingHub : Hub
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessagingHub> _logger;

    public MessagingHub(
        IServiceProvider serviceProvider,
        ILogger<MessagingHub> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();

        if (userId.HasValue && tenantId.HasValue)
        {
            // Add user to their personal group and tenant group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"messaging_tenant_{tenantId}");

            // Update presence to online
            using var scope = _serviceProvider.CreateScope();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
            await messagingService.UpdateUserPresenceAsync(userId.Value, true, Context.ConnectionId);

            // Notify other users in tenant about online status
            await Clients.Group($"messaging_tenant_{tenantId}")
                .SendAsync("UserPresenceChanged", new UserPresenceNotification
                {
                    UserId = userId.Value,
                    UserName = GetUserName(),
                    IsOnline = true,
                    LastActiveAt = DateTime.UtcNow
                });

            _logger.LogInformation("User {UserId} connected to MessagingHub (Tenant: {TenantId})",
                userId, tenantId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();

        if (userId.HasValue && tenantId.HasValue)
        {
            // Remove from groups first
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"messaging_tenant_{tenantId}");

            using var scope = _serviceProvider.CreateScope();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

            // Only mark offline if this was the user's active connection
            // This handles the case where user has multiple tabs open
            var currentConnectionId = await messagingService.GetUserConnectionIdAsync(userId.Value);

            if (currentConnectionId == null || currentConnectionId == Context.ConnectionId)
            {
                // Update presence to offline
                await messagingService.UpdateUserPresenceAsync(userId.Value, false, null);

                // Notify other users in tenant about offline status
                await Clients.Group($"messaging_tenant_{tenantId}")
                    .SendAsync("UserPresenceChanged", new UserPresenceNotification
                    {
                        UserId = userId.Value,
                        UserName = GetUserName(),
                        IsOnline = false,
                        LastActiveAt = DateTime.UtcNow
                    });

                _logger.LogInformation("User {UserId} disconnected from MessagingHub (offline)", userId);
            }
            else
            {
                _logger.LogInformation("User {UserId} disconnected from MessagingHub (still has active connection)", userId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a specific conversation group to receive messages
    /// </summary>
    public async Task JoinConversation(int conversationId)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        _logger.LogDebug("User {UserId} joined conversation group: conversation_{ConversationId}",
            userId, conversationId);
    }

    /// <summary>
    /// Leave a conversation group
    /// </summary>
    public async Task LeaveConversation(int conversationId)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        _logger.LogDebug("User {UserId} left conversation group: conversation_{ConversationId}",
            userId, conversationId);
    }

    /// <summary>
    /// Send typing indicator to other participant in conversation
    /// </summary>
    public async Task SendTypingIndicator(int conversationId, bool isTyping)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        // Send to conversation group (excluding sender)
        await Clients.OthersInGroup($"conversation_{conversationId}")
            .SendAsync("TypingIndicator", new TypingIndicatorNotification
            {
                ConversationId = conversationId,
                UserId = userId.Value,
                UserName = GetUserName(),
                IsTyping = isTyping
            });
    }

    /// <summary>
    /// Mark messages as read and send read receipt
    /// </summary>
    public async Task MarkAsRead(int conversationId, int messageId)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        using var scope = _serviceProvider.CreateScope();
        var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

        // Mark message as read in database
        var success = await messagingService.MarkMessageAsReadAsync(messageId, userId.Value);

        if (success)
        {
            // Send read receipt to conversation
            await Clients.OthersInGroup($"conversation_{conversationId}")
                .SendAsync("MessageRead", new MessageReadNotification
                {
                    ConversationId = conversationId,
                    MessageId = messageId,
                    ReadByUserId = userId.Value,
                    ReadAt = DateTime.UtcNow
                });
        }
    }

    /// <summary>
    /// Mark all messages in conversation as read
    /// </summary>
    public async Task MarkAllAsRead(int conversationId)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        using var scope = _serviceProvider.CreateScope();
        var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

        var success = await messagingService.MarkMessagesAsReadAsync(conversationId, userId.Value);

        if (success)
        {
            await Clients.OthersInGroup($"conversation_{conversationId}")
                .SendAsync("AllMessagesRead", new
                {
                    ConversationId = conversationId,
                    ReadByUserId = userId.Value,
                    ReadAt = DateTime.UtcNow
                });
        }
    }

    /// <summary>
    /// Update activity timestamp (for presence "away" detection)
    /// </summary>
    public async Task Heartbeat()
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();

        if (userId.HasValue && tenantId.HasValue)
        {
            using var scope = _serviceProvider.CreateScope();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
            await messagingService.UpdateUserPresenceAsync(userId.Value, true, Context.ConnectionId);
        }
    }

    #region Private Helpers

    private int? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private int? GetTenantId()
    {
        var tenantIdClaim = Context.User?.FindFirst("TenantId")?.Value;
        return int.TryParse(tenantIdClaim, out var tenantId) ? tenantId : null;
    }

    private string GetUserName()
    {
        var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value ?? "Unknown";
        return email;
    }

    #endregion
}

/// <summary>
/// Service interface for sending messaging notifications via SignalR
/// </summary>
public interface IMessagingNotificationService
{
    Task SendNewMessageNotificationAsync(int recipientUserId, NewMessageNotification notification);
    Task SendMessageToConversationAsync(int conversationId, MessageDto message);
    Task SendTypingIndicatorAsync(int conversationId, int userId, string userName, bool isTyping);
    Task SendReadReceiptAsync(int conversationId, int messageId, int readByUserId);
    Task SendPresenceUpdateAsync(int tenantId, int userId, string userName, bool isOnline);
}

/// <summary>
/// Service for sending messaging notifications via SignalR
/// </summary>
public class MessagingNotificationService : IMessagingNotificationService
{
    private readonly IHubContext<MessagingHub> _hubContext;
    private readonly ILogger<MessagingNotificationService> _logger;

    public MessagingNotificationService(
        IHubContext<MessagingHub> hubContext,
        ILogger<MessagingNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Send new message notification to recipient's personal group
    /// </summary>
    public async Task SendNewMessageNotificationAsync(int recipientUserId, NewMessageNotification notification)
    {
        try
        {
            await _hubContext.Clients
                .Group($"user_{recipientUserId}")
                .SendAsync("NewMessage", notification);

            _logger.LogDebug("Sent new message notification to user {UserId}", recipientUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send new message notification to user {UserId}", recipientUserId);
        }
    }

    /// <summary>
    /// Send message to all participants in a conversation
    /// </summary>
    public async Task SendMessageToConversationAsync(int conversationId, MessageDto message)
    {
        try
        {
            await _hubContext.Clients
                .Group($"conversation_{conversationId}")
                .SendAsync("ReceiveMessage", message);

            _logger.LogDebug("Sent message {MessageId} to conversation {ConversationId}",
                message.MessageId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to conversation {ConversationId}", conversationId);
        }
    }

    /// <summary>
    /// Send typing indicator to conversation
    /// </summary>
    public async Task SendTypingIndicatorAsync(int conversationId, int userId, string userName, bool isTyping)
    {
        try
        {
            await _hubContext.Clients
                .Group($"conversation_{conversationId}")
                .SendAsync("TypingIndicator", new TypingIndicatorNotification
                {
                    ConversationId = conversationId,
                    UserId = userId,
                    UserName = userName,
                    IsTyping = isTyping
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send typing indicator for conversation {ConversationId}", conversationId);
        }
    }

    /// <summary>
    /// Send read receipt to conversation
    /// </summary>
    public async Task SendReadReceiptAsync(int conversationId, int messageId, int readByUserId)
    {
        try
        {
            await _hubContext.Clients
                .Group($"conversation_{conversationId}")
                .SendAsync("MessageRead", new MessageReadNotification
                {
                    ConversationId = conversationId,
                    MessageId = messageId,
                    ReadByUserId = readByUserId,
                    ReadAt = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send read receipt for message {MessageId}", messageId);
        }
    }

    /// <summary>
    /// Send presence update to tenant group
    /// </summary>
    public async Task SendPresenceUpdateAsync(int tenantId, int userId, string userName, bool isOnline)
    {
        try
        {
            await _hubContext.Clients
                .Group($"messaging_tenant_{tenantId}")
                .SendAsync("UserPresenceChanged", new UserPresenceNotification
                {
                    UserId = userId,
                    UserName = userName,
                    IsOnline = isOnline,
                    LastActiveAt = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send presence update for user {UserId}", userId);
        }
    }
}
