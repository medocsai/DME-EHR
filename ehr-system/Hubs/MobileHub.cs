using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for mobile app communication.
/// Handles real-time events between web pages opened in Capacitor Browser and the mobile app.
/// </summary>
[Authorize]
public class MobileHub : Hub
{
    private readonly ILogger<MobileHub> _logger;

    public MobileHub(ILogger<MobileHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = Context.User?.FindFirst("TenantId")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            // Add user to their personal group for targeted notifications
            await Groups.AddToGroupAsync(Context.ConnectionId, $"mobile_user_{userId}");
        }

        _logger.LogInformation("User {UserId} connected to MobileHub (Tenant: {TenantId})",
            userId, tenantId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"mobile_user_{userId}");
        }

        _logger.LogInformation("User {UserId} disconnected from MobileHub", userId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Called by the mobile-write-report.html web page when the user closes or saves/signs the note.
    /// This sends a notification to the mobile app to close the Capacitor Browser.
    /// </summary>
    /// <param name="data">Data containing action (closed/saved/signed), noteId, and userId</param>
    public async Task MobileWriteReportClosed(MobileWriteReportClosedData data)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        _logger.LogInformation(
            "MobileWriteReportClosed from user {UserId}: Action={Action}, NoteId={NoteId}",
            userId, data.Action, data.NoteId);

        // Send the close event to the mobile app (same user, different connection)
        if (!string.IsNullOrEmpty(userId))
        {
            await Clients.Group($"mobile_user_{userId}").SendAsync("WriteReportClosed", new
            {
                action = data.Action,
                noteId = data.NoteId,
                timestamp = DateTime.UtcNow
            });
        }
    }
}

/// <summary>
/// Data sent when the mobile write report page is closed
/// </summary>
public class MobileWriteReportClosedData
{
    /// <summary>
    /// Action: "closed" (without saving), "saved" (draft saved), "signed" (note signed)
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// The clinical note ID if one was created/updated
    /// </summary>
    public int? NoteId { get; set; }

    /// <summary>
    /// The user ID (for validation)
    /// </summary>
    public string? UserId { get; set; }
}

/// <summary>
/// Service interface for sending mobile notifications via SignalR
/// </summary>
public interface IMobileNotificationService
{
    /// <summary>
    /// Send a notification to close the browser to a specific user's mobile app
    /// </summary>
    Task SendCloseBrowserAsync(int userId, string action, int? noteId);
}

/// <summary>
/// Service implementation for sending mobile notifications via SignalR
/// </summary>
public class MobileNotificationService : IMobileNotificationService
{
    private readonly IHubContext<MobileHub> _hubContext;
    private readonly ILogger<MobileNotificationService> _logger;

    public MobileNotificationService(
        IHubContext<MobileHub> hubContext,
        ILogger<MobileNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendCloseBrowserAsync(int userId, string action, int? noteId)
    {
        try
        {
            await _hubContext.Clients
                .Group($"mobile_user_{userId}")
                .SendAsync("WriteReportClosed", new
                {
                    action = action,
                    noteId = noteId,
                    timestamp = DateTime.UtcNow
                });

            _logger.LogInformation(
                "Sent WriteReportClosed to user {UserId}: Action={Action}, NoteId={NoteId}",
                userId, action, noteId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WriteReportClosed to user {UserId}", userId);
        }
    }
}
