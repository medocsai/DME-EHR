using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for real-time Care Notes notifications.
///
/// Why it exists:
///   When staff create a care note addressed to a provider, the provider's
///   top-nav bell badge should update instantly -- not after the next 60s
///   poll. Same for when the provider's count drops because they opened
///   the patient profile (auto-marks seen).
///
/// Group strategy:
///   Each clinician joins their personal group "carenotes_provider_{providerId}"
///   on connect. The notifier (CareNotesNotifier) sends "UnseenChanged" to
///   that group when the count for that provider changes.
///
/// Auth:
///   [Authorize] inherits the global JWT bearer scheme. Only Clinician role
///   (Role=2) realistically connects -- the bell is hidden for everyone else
///   on the frontend -- but the hub itself does not enforce role; it just
///   uses the ProviderId claim to route. Without a valid ProviderId claim,
///   the connection joins no group and receives no events (silent no-op).
/// </summary>
[Authorize]
public class CareNotesHub : Hub
{
    private readonly ILogger<CareNotesHub> _logger;

    public CareNotesHub(ILogger<CareNotesHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var providerId = GetProviderId();
        if (providerId.HasValue && providerId.Value > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(providerId.Value));
            _logger.LogInformation("Provider {ProviderId} connected to CareNotesHub", providerId.Value);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var providerId = GetProviderId();
        if (providerId.HasValue && providerId.Value > 0)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(providerId.Value));
        }
        await base.OnDisconnectedAsync(exception);
    }

    public static string GroupName(int providerId) => $"carenotes_provider_{providerId}";

    private int? GetProviderId()
    {
        var claim = Context.User?.FindFirst("ProviderId")?.Value;
        return int.TryParse(claim, out var pid) ? pid : null;
    }
}

/// <summary>
/// CareNotesNotifier -- the Hired Guy that pushes Care Notes events over
/// SignalR. CareNoteService calls this guy whenever a provider's unseen
/// count may have changed.
///
/// Job description:
///   - "Tell provider X that their unseen count is now N."
///   - That's it. He doesn't compute the count (the service does).
///   - He doesn't decide WHO to notify (the service decides).
/// </summary>
public interface ICareNotesNotifier
{
    Task NotifyUnseenChangedAsync(int providerId, int count);
}

public class CareNotesNotifier : ICareNotesNotifier
{
    private readonly IHubContext<CareNotesHub> _hubContext;
    private readonly ILogger<CareNotesNotifier> _logger;

    public CareNotesNotifier(IHubContext<CareNotesHub> hubContext, ILogger<CareNotesNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyUnseenChangedAsync(int providerId, int count)
    {
        if (providerId <= 0) return;
        try
        {
            await _hubContext.Clients
                .Group(CareNotesHub.GroupName(providerId))
                .SendAsync("UnseenChanged", new { count });
        }
        catch (Exception ex)
        {
            // Never let SignalR failures break the calling service.
            // The 60s polling fallback in the frontend will eventually
            // catch up on a missed push.
            _logger.LogWarning(ex, "CareNotes SignalR push failed for provider {ProviderId}", providerId);
        }
    }
}
