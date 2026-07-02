using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EHR.Services;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for real-time patient-provider messaging.
/// Supports both patient (Role=8) and provider connections.
/// Groups: patient_msg_{patientId}, provider_msg_{userId}, patient_conversation_{conversationId}
/// </summary>
[Authorize]
public class PatientMessagingHub : Hub
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PatientMessagingHub> _logger;

    public PatientMessagingHub(
        IServiceProvider serviceProvider,
        ILogger<PatientMessagingHub> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var role = GetRole();
        var tenantId = GetTenantId();

        if (role == "8") // Patient
        {
            var patientId = GetPatientId();
            if (patientId.HasValue)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"patient_msg_{patientId}");
                _logger.LogInformation("Patient {PatientId} connected to PatientMessagingHub", patientId);
            }
        }
        else // Provider/Staff
        {
            var userId = GetUserId();
            if (userId.HasValue)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"provider_msg_{userId}");
                _logger.LogInformation("Provider (UserId={UserId}) connected to PatientMessagingHub", userId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = GetRole();

        if (role == "8")
        {
            var patientId = GetPatientId();
            if (patientId.HasValue)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"patient_msg_{patientId}");
                _logger.LogInformation("Patient {PatientId} disconnected from PatientMessagingHub", patientId);
            }
        }
        else
        {
            var userId = GetUserId();
            if (userId.HasValue)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"provider_msg_{userId}");
                _logger.LogInformation("Provider (UserId={UserId}) disconnected from PatientMessagingHub", userId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a specific conversation group to receive real-time messages
    /// </summary>
    public async Task JoinConversation(int conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"patient_conversation_{conversationId}");
        _logger.LogDebug("Connection joined patient_conversation_{ConversationId}", conversationId);
    }

    /// <summary>
    /// Leave a conversation group
    /// </summary>
    public async Task LeaveConversation(int conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"patient_conversation_{conversationId}");
        _logger.LogDebug("Connection left patient_conversation_{ConversationId}", conversationId);
    }

    /// <summary>
    /// Send typing indicator to conversation partner
    /// </summary>
    public async Task SendTypingIndicator(int conversationId, bool isTyping)
    {
        var senderType = GetRole() == "8" ? "Patient" : "Provider";
        await Clients.OthersInGroup($"patient_conversation_{conversationId}")
            .SendAsync("PatientMsgTyping", new
            {
                ConversationId = conversationId,
                SenderType = senderType,
                IsTyping = isTyping
            });
    }

    /// <summary>
    /// Mark messages as read and notify the other party
    /// </summary>
    public async Task MarkAsRead(int conversationId)
    {
        var role = GetRole();
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPatientMessagingService>();

        bool success;
        if (role == "8")
        {
            var patientId = GetPatientId();
            if (!patientId.HasValue) return;
            success = await service.MarkAsReadByPatientAsync(conversationId, patientId.Value);
        }
        else
        {
            var userId = GetUserId();
            if (!userId.HasValue) return;
            success = await service.MarkAsReadByUserAsync(conversationId, userId.Value);
        }

        if (success)
        {
            await Clients.OthersInGroup($"patient_conversation_{conversationId}")
                .SendAsync("PatientMsgRead", new
                {
                    ConversationId = conversationId,
                    ReadAt = DateTime.UtcNow
                });
        }
    }

    #region Private Helpers

    private int? GetUserId()
    {
        var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private int? GetPatientId()
    {
        var claim = Context.User?.FindFirst("PatientId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private int? GetTenantId()
    {
        var claim = Context.User?.FindFirst("TenantId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private string GetRole()
    {
        return Context.User?.FindFirst(ClaimTypes.Role)?.Value ?? "";
    }

    #endregion
}

// ============================================
// Notification service for sending SignalR events
// ============================================

public interface IPatientMessagingNotificationService
{
    Task SendMessageToConversationAsync(int conversationId, PatientMessageDto message);
    Task SendNewMessageToPatientAsync(int patientId, PatientMsgNewNotification notification);
    Task SendNewMessageToProviderAsync(int userId, PatientMsgNewNotification notification);
    Task SendUnreadUpdateToPatientAsync(int patientId, PatientMessagingUnreadDto unread);
    Task SendUnreadUpdateToProviderAsync(int userId, PatientMessagingUnreadDto unread);
}

public class PatientMsgNewNotification
{
    public int PatientMessageId { get; set; }
    public int PatientConversationId { get; set; }
    public string SenderType { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string MessagePreview { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class PatientMessagingNotificationService : IPatientMessagingNotificationService
{
    private readonly IHubContext<PatientMessagingHub> _hubContext;
    private readonly ILogger<PatientMessagingNotificationService> _logger;

    public PatientMessagingNotificationService(
        IHubContext<PatientMessagingHub> hubContext,
        ILogger<PatientMessagingNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendMessageToConversationAsync(int conversationId, PatientMessageDto message)
    {
        try
        {
            await _hubContext.Clients
                .Group($"patient_conversation_{conversationId}")
                .SendAsync("PatientMsgReceive", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to patient_conversation_{ConversationId}", conversationId);
        }
    }

    public async Task SendNewMessageToPatientAsync(int patientId, PatientMsgNewNotification notification)
    {
        try
        {
            await _hubContext.Clients
                .Group($"patient_msg_{patientId}")
                .SendAsync("PatientMsgNew", notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send new message notification to patient {PatientId}", patientId);
        }
    }

    public async Task SendNewMessageToProviderAsync(int userId, PatientMsgNewNotification notification)
    {
        try
        {
            await _hubContext.Clients
                .Group($"provider_msg_{userId}")
                .SendAsync("PatientMsgNew", notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send new message notification to provider (UserId={UserId})", userId);
        }
    }

    public async Task SendUnreadUpdateToPatientAsync(int patientId, PatientMessagingUnreadDto unread)
    {
        try
        {
            await _hubContext.Clients
                .Group($"patient_msg_{patientId}")
                .SendAsync("PatientMsgUnreadUpdate", unread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send unread update to patient {PatientId}", patientId);
        }
    }

    public async Task SendUnreadUpdateToProviderAsync(int userId, PatientMessagingUnreadDto unread)
    {
        try
        {
            await _hubContext.Clients
                .Group($"provider_msg_{userId}")
                .SendAsync("PatientMsgUnreadUpdate", unread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send unread update to provider (UserId={UserId})", userId);
        }
    }
}
