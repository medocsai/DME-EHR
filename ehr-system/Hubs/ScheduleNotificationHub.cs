using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for real-time schedule and dashboard notifications.
/// Sends notifications to users when appointments are created, updated, cancelled, or checked in.
/// </summary>
[Authorize]
public class ScheduleNotificationHub : Hub
{
    private readonly ILogger<ScheduleNotificationHub> _logger;

    public ScheduleNotificationHub(ILogger<ScheduleNotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Get user's tenant ID and role from claims
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst("Role")?.Value ?? Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var providerId = Context.User?.FindFirst("ProviderId")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            // Add user to tenant group for targeted notifications
            await Groups.AddToGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}");

            // Add admins to admin group (can see all appointments)
            if (role == "0" || role == "1") // SuperAdmin or ClinicAdmin
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}_admins");
            }

            // Add providers to their specific group (for provider-specific appointments)
            if (!string.IsNullOrEmpty(providerId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}_provider_{providerId}");
            }
        }

        _logger.LogInformation("User {UserId} connected to ScheduleNotificationHub (Tenant: {TenantId}, Provider: {ProviderId})",
            userId, tenantId, providerId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var providerId = Context.User?.FindFirst("ProviderId")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}_admins");

            if (!string.IsNullOrEmpty(providerId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}_provider_{providerId}");
            }
        }

        _logger.LogInformation("User {UserId} disconnected from ScheduleNotificationHub", userId);

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
            await Groups.AddToGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}_location_{locationId}");
            _logger.LogDebug("User joined schedule location group: schedule_tenant_{TenantId}_location_{LocationId}",
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
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"schedule_tenant_{tenantId}_location_{locationId}");
        }
    }
}

/// <summary>
/// DTO for appointment change notification sent via SignalR
/// </summary>
public class AppointmentChangeNotification
{
    public int AppointmentId { get; set; }
    public string ChangeType { get; set; } // "created", "updated", "cancelled", "checkedIn", "rescheduled", "reinstated"
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public int? ProviderId { get; set; }
    public string ProviderName { get; set; }
    public int? LocationId { get; set; }
    public string LocationName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; }
    public int AppointmentType { get; set; }
    public string AppointmentTypeName { get; set; }
    public DateTime ChangedAt { get; set; }
    public int? ChangedByUserId { get; set; }
    public string ChangedByUserName { get; set; }

    // For rescheduled appointments
    public int? RescheduledFromAppointmentId { get; set; }
    public int? RescheduledToAppointmentId { get; set; }

    // Telehealth flag
    public bool? IsTelehealth { get; set; }
}

/// <summary>
/// DTO for clinical note change notification sent via SignalR
/// </summary>
public class ClinicalNoteChangeNotification
{
    public int ClinicalNoteId { get; set; }
    public string ChangeType { get; set; } // "created", "updated", "signed"
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public int? ProviderId { get; set; }
    public string ProviderName { get; set; }
    public int? AppointmentId { get; set; }
    public int Status { get; set; } // 0=Draft, 2=Signed
    public string StatusName { get; set; }
    public string TemplateName { get; set; }
    public DateTime? ServiceDate { get; set; }
    public DateTime ChangedAt { get; set; }
    public int? ChangedByUserId { get; set; }
}

/// <summary>
/// DTO for authorization/require schedule change notification sent via SignalR
/// </summary>
public class AuthorizationChangeNotification
{
    public int CareEpisodeId { get; set; }
    public string ChangeType { get; set; } // "created", "updated", "scheduled"
    public int PatientId { get; set; }
    public string PatientName { get; set; }
    public int? RequiredAppointments { get; set; }
    public int? ScheduledAppointments { get; set; }
    public int? ExpectedVisits { get; set; }
    public DateTime ChangedAt { get; set; }
    public int? ChangedByUserId { get; set; }
}

/// <summary>
/// Service interface for sending schedule notifications via SignalR
/// </summary>
public interface IScheduleNotificationService
{
    Task NotifyAppointmentChangedAsync(int tenantId, AppointmentChangeNotification notification);
    Task NotifyClinicalNoteChangedAsync(int tenantId, ClinicalNoteChangeNotification notification);
    Task NotifyAuthorizationChangedAsync(int tenantId, AuthorizationChangeNotification notification);
}

/// <summary>
/// Service implementation for sending schedule notifications via SignalR
/// </summary>
public class ScheduleNotificationService : IScheduleNotificationService
{
    private readonly IHubContext<ScheduleNotificationHub> _hubContext;
    private readonly ILogger<ScheduleNotificationService> _logger;

    public ScheduleNotificationService(
        IHubContext<ScheduleNotificationHub> hubContext,
        ILogger<ScheduleNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyAppointmentChangedAsync(int tenantId, AppointmentChangeNotification notification)
    {
        try
        {
            // Send to all users in the tenant (they can filter on client side based on location/provider)
            await _hubContext.Clients
                .Group($"schedule_tenant_{tenantId}")
                .SendAsync("AppointmentChanged", notification);

            _logger.LogInformation(
                "Sent appointment change notification: {ChangeType} for appointment {AppointmentId} (Patient: {PatientId}, Provider: {ProviderId})",
                notification.ChangeType, notification.AppointmentId, notification.PatientId, notification.ProviderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send schedule notification for appointment {AppointmentId}",
                notification.AppointmentId);
        }
    }

    public async Task NotifyClinicalNoteChangedAsync(int tenantId, ClinicalNoteChangeNotification notification)
    {
        try
        {
            // Send to all users in the tenant for dashboard updates
            await _hubContext.Clients
                .Group($"schedule_tenant_{tenantId}")
                .SendAsync("ClinicalNoteChanged", notification);

            _logger.LogInformation(
                "Sent clinical note change notification: {ChangeType} for note {ClinicalNoteId} (Patient: {PatientId}, Provider: {ProviderId})",
                notification.ChangeType, notification.ClinicalNoteId, notification.PatientId, notification.ProviderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send clinical note notification for note {ClinicalNoteId}",
                notification.ClinicalNoteId);
        }
    }

    public async Task NotifyAuthorizationChangedAsync(int tenantId, AuthorizationChangeNotification notification)
    {
        try
        {
            // Send to all users in the tenant for require-schedule widget updates
            await _hubContext.Clients
                .Group($"schedule_tenant_{tenantId}")
                .SendAsync("AuthorizationChanged", notification);

            _logger.LogInformation(
                "Sent authorization change notification: {ChangeType} for care episode {CareEpisodeId} (Patient: {PatientId})",
                notification.ChangeType, notification.CareEpisodeId, notification.PatientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send authorization notification for care episode {CareEpisodeId}",
                notification.CareEpisodeId);
        }
    }
}
