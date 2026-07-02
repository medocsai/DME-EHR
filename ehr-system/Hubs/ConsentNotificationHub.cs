using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for real-time consent notifications.
/// Sends notifications to admin users when patients complete consent forms at kiosk.
/// </summary>
[Authorize]
public class ConsentNotificationHub : Hub
{
    private readonly ILogger<ConsentNotificationHub> _logger;

    public ConsentNotificationHub(ILogger<ConsentNotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Get user's tenant ID from claims
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst("Role")?.Value ?? Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            // Add user to tenant group for targeted notifications
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");

            // Also add to role-specific groups for more granular notifications
            if (role == "0" || role == "1") // SuperAdmin or ClinicAdmin
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}_admins");
            }
        }

        _logger.LogInformation("User {UserId} connected to ConsentNotificationHub (Tenant: {TenantId})",
            userId, tenantId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}_admins");
        }

        _logger.LogInformation("User {UserId} disconnected from ConsentNotificationHub", userId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a specific location group to receive location-specific notifications
    /// </summary>
    public async Task JoinLocationGroup(int locationId)
    {
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}_location_{locationId}");
            _logger.LogDebug("User joined location group: tenant_{TenantId}_location_{LocationId}",
                tenantId, locationId);
        }
    }

    /// <summary>
    /// Leave a specific location group
    /// </summary>
    public async Task LeaveLocationGroup(int locationId)
    {
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}_location_{locationId}");
        }
    }
}

/// <summary>
/// DTO for consent completion notification sent via SignalR
/// </summary>
public class ConsentCompletedNotification
{
    public int ConsentId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public int AppointmentId { get; set; }
    public DateTime AppointmentTime { get; set; }
    public string AppointmentType { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; }
    public int FormCount { get; set; }
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// Service interface for sending consent notifications via SignalR
/// </summary>
public interface IConsentNotificationService
{
    Task NotifyConsentCompletedAsync(int tenantId, ConsentCompletedNotification notification);
}

/// <summary>
/// Service implementation for sending consent notifications via SignalR
/// </summary>
public class ConsentNotificationService : IConsentNotificationService
{
    private readonly IHubContext<ConsentNotificationHub> _hubContext;
    private readonly ILogger<ConsentNotificationService> _logger;

    public ConsentNotificationService(
        IHubContext<ConsentNotificationHub> hubContext,
        ILogger<ConsentNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyConsentCompletedAsync(int tenantId, ConsentCompletedNotification notification)
    {
        try
        {
            // Send to all admins in the tenant (only to admin group to avoid duplicates)
            // Admins receive notifications for all locations
            await _hubContext.Clients
                .Group($"tenant_{tenantId}_admins")
                .SendAsync("ConsentCompleted", notification);

            _logger.LogInformation(
                "Sent consent completion notification for patient {PatientId} at location {LocationId}",
                notification.PatientId, notification.LocationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send consent notification for patient {PatientId}",
                notification.PatientId);
        }
    }
}
