using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for telehealth waiting room and video session.
/// Tracks active patient connections for real-time presence detection.
/// No [Authorize] attribute — patients connect anonymously with their telehealth token.
/// Clinical staff connect with auth token for session notifications.
/// </summary>
public class TelehealthHub : Hub
{
    private readonly ILogger<TelehealthHub> _logger;

    /// <summary>
    /// Tracks active patient connections: ConnectionId → (AppointmentId, PatientName, Token)
    /// </summary>
    private static readonly ConcurrentDictionary<string, PatientConnectionInfo> _activePatients = new();

    public TelehealthHub(ILogger<TelehealthHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Patient joins the waiting room group using their telehealth token.
    /// Stores connection for presence tracking and notifies clinical staff.
    /// </summary>
    public async Task JoinWaitingRoom(string telehealthToken, int appointmentId, string patientName)
    {
        if (string.IsNullOrWhiteSpace(telehealthToken)) return;

        // Add to token group (for receiving admit notifications)
        await Groups.AddToGroupAsync(Context.ConnectionId, $"telehealth_token_{telehealthToken}");

        // Track this patient connection for presence detection
        _activePatients[Context.ConnectionId] = new PatientConnectionInfo
        {
            AppointmentId = appointmentId,
            PatientName = patientName,
            Token = telehealthToken,
            JoinedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Patient {PatientName} joined waiting room for appointment {AppointmentId} (ConnectionId: {ConnectionId})",
            patientName, appointmentId, Context.ConnectionId);

        // Notify clinical staff that patient is waiting
        await Clients.Group($"telehealth_{appointmentId}")
            .SendAsync("PatientWaiting", new TelehealthPatientWaitingNotification
            {
                AppointmentId = appointmentId,
                PatientName = patientName,
                JoinedAt = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Clinical user joins the telehealth session group by appointmentId.
    /// Checks if a patient is already connected and sends immediate notification.
    /// </summary>
    public async Task JoinSession(int appointmentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"telehealth_{appointmentId}");
        _logger.LogInformation("Clinical user joined telehealth session: {AppointmentId} (ConnectionId: {ConnectionId})",
            appointmentId, Context.ConnectionId);

        // Check if a patient is already in the waiting room for this appointment
        var waitingPatient = _activePatients.Values
            .FirstOrDefault(p => p.AppointmentId == appointmentId);

        if (waitingPatient != null)
        {
            _logger.LogInformation(
                "Patient {PatientName} already waiting for appointment {AppointmentId}, notifying provider",
                waitingPatient.PatientName, appointmentId);

            // Send directly to this provider connection (not the whole group)
            await Clients.Caller.SendAsync("PatientWaiting", new TelehealthPatientWaitingNotification
            {
                AppointmentId = appointmentId,
                PatientName = waitingPatient.PatientName,
                JoinedAt = waitingPatient.JoinedAt
            });
        }
    }

    /// <summary>
    /// Leave a telehealth session group.
    /// </summary>
    public async Task LeaveSession(int appointmentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"telehealth_{appointmentId}");
        _logger.LogInformation("User left telehealth session: {AppointmentId}", appointmentId);
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        // Check if this was a patient connection
        if (_activePatients.TryRemove(Context.ConnectionId, out var patientInfo))
        {
            _logger.LogInformation(
                "Patient {PatientName} disconnected from waiting room for appointment {AppointmentId}",
                patientInfo.PatientName, patientInfo.AppointmentId);

            // Notify clinical staff that patient left
            await Clients.Group($"telehealth_{patientInfo.AppointmentId}")
                .SendAsync("PatientLeft", new
                {
                    PatientName = patientInfo.PatientName,
                    LeftAt = DateTime.UtcNow
                });
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Tracks an active patient SignalR connection for presence detection.
/// </summary>
public class PatientConnectionInfo
{
    public int AppointmentId { get; set; }
    public string PatientName { get; set; }
    public string Token { get; set; }
    public DateTime JoinedAt { get; set; }
}

// ============================================
// TELEHEALTH NOTIFICATION DTOs
// ============================================

/// <summary>
/// Notification sent to clinical staff when a patient joins the waiting room.
/// </summary>
public class TelehealthPatientWaitingNotification
{
    public int AppointmentId { get; set; }
    public string PatientName { get; set; }
    public DateTime JoinedAt { get; set; }
}

// ============================================
// TELEHEALTH NOTIFICATION SERVICE
// ============================================

/// <summary>
/// Interface for sending telehealth-specific notifications via SignalR.
/// </summary>
public interface ITelehealthNotificationService
{
    /// <summary>
    /// Notify the patient that they have been admitted to the video call.
    /// Sends to the telehealth_token_{token} group.
    /// </summary>
    Task NotifyPatientAdmittedAsync(string telehealthToken);
}

/// <summary>
/// Sends telehealth notifications via the TelehealthHub.
/// </summary>
public class TelehealthNotificationService : ITelehealthNotificationService
{
    private readonly IHubContext<TelehealthHub> _hubContext;
    private readonly ILogger<TelehealthNotificationService> _logger;

    public TelehealthNotificationService(
        IHubContext<TelehealthHub> hubContext,
        ILogger<TelehealthNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyPatientAdmittedAsync(string telehealthToken)
    {
        try
        {
            await _hubContext.Clients
                .Group($"telehealth_token_{telehealthToken}")
                .SendAsync("PatientAdmitted", new { AdmittedAt = DateTime.UtcNow });

            _logger.LogInformation("Sent PatientAdmitted notification for token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send PatientAdmitted notification");
        }
    }
}
