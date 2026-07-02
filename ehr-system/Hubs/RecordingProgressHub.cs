using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace EHR.Hubs;

/// <summary>
/// SignalR hub for real-time recording session progress updates.
/// Sends progress notifications during Save & Finish processing.
/// </summary>
[Authorize]
public class RecordingProgressHub : Hub
{
    private readonly ILogger<RecordingProgressHub> _logger;

    public RecordingProgressHub(ILogger<RecordingProgressHub> logger)
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
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        _logger.LogInformation("User {UserId} connected to RecordingProgressHub (Tenant: {TenantId})",
            userId, tenantId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        _logger.LogInformation("User {UserId} disconnected from RecordingProgressHub", userId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a specific session group to receive progress updates for that session
    /// </summary>
    public async Task JoinSessionGroup(int sessionId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");
        _logger.LogDebug("User {UserId} joined session group: session_{SessionId}", userId, sessionId);
    }

    /// <summary>
    /// Leave a specific session group (stops receiving progress updates for that session)
    /// </summary>
    public async Task LeaveSessionGroup(int sessionId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session_{sessionId}");
        _logger.LogDebug("User {UserId} left session group: session_{SessionId}", userId, sessionId);
    }
}

/// <summary>
/// Progress update notification sent via SignalR during recording processing
/// </summary>
public class RecordingProgressUpdate
{
    /// <summary>
    /// Recording session ID
    /// </summary>
    public int SessionId { get; set; }

    /// <summary>
    /// Event type: WaitingForChunks, MergingTranscriptions, SelectingTemplate, GeneratingNote, NoteComplete, AllComplete, Error
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Simple, user-friendly message to display (non-technical)
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional details (e.g., "2 of 3 done", "Clinical Note 1 of 2")
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Current step in format "1/5", "2/5", etc.
    /// </summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the update
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Status: in-progress, completed, failed
    /// </summary>
    public string Status { get; set; } = "in-progress";

    /// <summary>
    /// Error message if status is failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// For multiple notes: current note number being processed
    /// </summary>
    public int? CurrentNoteNumber { get; set; }

    /// <summary>
    /// For multiple notes: total number of notes to generate
    /// </summary>
    public int? TotalNotes { get; set; }

    /// <summary>
    /// Clinical note ID(s) when complete
    /// </summary>
    public List<int>? ClinicalNoteIds { get; set; }
}

/// <summary>
/// Service interface for sending recording progress notifications via SignalR
/// </summary>
public interface IRecordingProgressService
{
    /// <summary>
    /// Send progress update to a specific user
    /// </summary>
    Task SendProgressAsync(int userId, RecordingProgressUpdate update);

    /// <summary>
    /// Send progress update to all users watching a specific session
    /// </summary>
    Task SendSessionProgressAsync(int sessionId, RecordingProgressUpdate update);

    /// <summary>
    /// Send waiting for chunks progress
    /// </summary>
    Task SendWaitingForChunksAsync(int userId, int sessionId, int completedChunks, int totalChunks);

    /// <summary>
    /// Send merging transcriptions progress
    /// </summary>
    Task SendMergingTranscriptionsAsync(int userId, int sessionId, int chunkCount);

    /// <summary>
    /// Send selecting template progress
    /// </summary>
    Task SendSelectingTemplateAsync(int userId, int sessionId, int templateCount);

    /// <summary>
    /// Send generating note progress
    /// </summary>
    Task SendGeneratingNoteAsync(int userId, int sessionId, int currentNote, int totalNotes, string templateName);

    /// <summary>
    /// Send note complete progress
    /// </summary>
    Task SendNoteCompleteAsync(int userId, int sessionId, int currentNote, int totalNotes, int clinicalNoteId);

    /// <summary>
    /// Send all complete progress
    /// </summary>
    Task SendAllCompleteAsync(int userId, int sessionId, List<int> clinicalNoteIds);

    /// <summary>
    /// Send error progress
    /// </summary>
    Task SendErrorAsync(int userId, int sessionId, string errorMessage);
}

/// <summary>
/// Service implementation for sending recording progress notifications via SignalR
/// </summary>
public class RecordingProgressService : IRecordingProgressService
{
    private readonly IHubContext<RecordingProgressHub> _hubContext;
    private readonly ILogger<RecordingProgressService> _logger;

    // Simple, motivational messages for each step (rotated for variety)
    private static readonly string[] WaitingMessages = new[]
    {
        "Waiting for transcriptions to finish...",
        "Almost there, just waiting for the last bit...",
        "Finishing up the transcription...",
        "Just a moment, finalizing audio processing...",
        "Wrapping up the last few seconds..."
    };

    private static readonly string[] MergingMessages = new[]
    {
        "Combining everything together...",
        "Putting all the pieces together...",
        "Organizing your notes...",
        "Bringing it all together...",
        "Creating your complete transcript..."
    };

    private static readonly string[] SelectingTemplateMessages = new[]
    {
        "Finding the right format...",
        "Getting ready for your report...",
        "Selecting the best template...",
        "Preparing your clinical note format..."
    };

    private static readonly string[] GeneratingMessages = new[]
    {
        "Creating your clinical note...",
        "Writing your report...",
        "Generating your documentation...",
        "Building your clinical note...",
        "Almost done, finalizing your note..."
    };

    private static readonly string[] CompleteMessages = new[]
    {
        "All done!",
        "Your clinical note is ready!",
        "Report complete and ready for review!",
        "Finished! Your note is ready."
    };

    private readonly Random _random = new();

    public RecordingProgressService(
        IHubContext<RecordingProgressHub> hubContext,
        ILogger<RecordingProgressService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendProgressAsync(int userId, RecordingProgressUpdate update)
    {
        try
        {
            // Send to user's personal group
            await _hubContext.Clients
                .Group($"user_{userId}")
                .SendAsync("RecordingProgress", update);

            // Also send to session group for clients that joined via JoinSessionGroup
            await _hubContext.Clients
                .Group($"session_{update.SessionId}")
                .SendAsync("RecordingProgress", update);

            _logger.LogDebug("Sent progress update to user {UserId} and session {SessionId}: {Event} - {Progress}%",
                userId, update.SessionId, update.Event, update.Progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send progress update to user {UserId}", userId);
        }
    }

    public async Task SendSessionProgressAsync(int sessionId, RecordingProgressUpdate update)
    {
        try
        {
            await _hubContext.Clients
                .Group($"session_{sessionId}")
                .SendAsync("RecordingProgress", update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send progress update for session {SessionId}", sessionId);
        }
    }

    public async Task SendWaitingForChunksAsync(int userId, int sessionId, int completedChunks, int totalChunks)
    {
        var message = WaitingMessages[_random.Next(WaitingMessages.Length)];
        var progress = totalChunks > 0 ? (int)((completedChunks / (double)totalChunks) * 20) : 10;

        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "WaitingForChunks",
            Message = message,
            Details = "Processing your recording",
            Progress = progress,
            Step = "1/5",
            Status = "in-progress"
        };

        await SendProgressAsync(userId, update);
    }

    public async Task SendMergingTranscriptionsAsync(int userId, int sessionId, int chunkCount)
    {
        var message = MergingMessages[_random.Next(MergingMessages.Length)];

        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "MergingTranscriptions",
            Message = message,
            Details = "Processing your recording",
            Progress = 30,
            Step = "2/5",
            Status = "in-progress"
        };

        await SendProgressAsync(userId, update);
    }

    public async Task SendSelectingTemplateAsync(int userId, int sessionId, int templateCount)
    {
        var message = SelectingTemplateMessages[_random.Next(SelectingTemplateMessages.Length)];
        var details = templateCount > 1
            ? $"{templateCount} templates found"
            : "Template selected";

        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "SelectingTemplate",
            Message = message,
            Details = details,
            Progress = 45,
            Step = "3/5",
            Status = "in-progress",
            TotalNotes = templateCount
        };

        await SendProgressAsync(userId, update);
    }

    public async Task SendGeneratingNoteAsync(int userId, int sessionId, int currentNote, int totalNotes, string templateName)
    {
        var message = GeneratingMessages[_random.Next(GeneratingMessages.Length)];
        var details = totalNotes > 1
            ? $"Clinical Note {currentNote} of {totalNotes}"
            : "This usually takes 10-30 seconds";

        // Progress: 50% base + up to 49% based on note progress
        var noteProgress = totalNotes > 0
            ? (int)(((currentNote - 1) / (double)totalNotes) * 49)
            : 0;
        var progress = 50 + noteProgress;

        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "GeneratingNote",
            Message = message,
            Details = details,
            Progress = progress,
            Step = "4/5",
            Status = "in-progress",
            CurrentNoteNumber = currentNote,
            TotalNotes = totalNotes
        };

        await SendProgressAsync(userId, update);
    }

    public async Task SendNoteCompleteAsync(int userId, int sessionId, int currentNote, int totalNotes, int clinicalNoteId)
    {
        string message;
        string details;

        if (totalNotes == 1)
        {
            message = "Clinical note created!";
            details = "Finalizing...";
        }
        else if (currentNote < totalNotes)
        {
            message = $"Report {currentNote} of {totalNotes} complete";
            details = $"Starting report {currentNote + 1}...";
        }
        else
        {
            message = $"All {totalNotes} reports complete!";
            details = "Finalizing...";
        }

        var noteProgress = totalNotes > 0
            ? (int)((currentNote / (double)totalNotes) * 49)
            : 49;
        var progress = 50 + noteProgress;

        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "NoteComplete",
            Message = message,
            Details = details,
            Progress = progress,
            Step = "4/5",
            Status = "in-progress",
            CurrentNoteNumber = currentNote,
            TotalNotes = totalNotes,
            ClinicalNoteIds = new List<int> { clinicalNoteId }
        };

        await SendProgressAsync(userId, update);
    }

    public async Task SendAllCompleteAsync(int userId, int sessionId, List<int> clinicalNoteIds)
    {
        var message = CompleteMessages[_random.Next(CompleteMessages.Length)];
        var noteCount = clinicalNoteIds.Count;
        var details = noteCount > 1
            ? $"{noteCount} clinical notes ready for review"
            : "Ready for review";

        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "AllComplete",
            Message = message,
            Details = details,
            Progress = 100,
            Step = "5/5",
            Status = "completed",
            TotalNotes = noteCount,
            ClinicalNoteIds = clinicalNoteIds
        };

        await SendProgressAsync(userId, update);
    }

    public async Task SendErrorAsync(int userId, int sessionId, string errorMessage)
    {
        var update = new RecordingProgressUpdate
        {
            SessionId = sessionId,
            Event = "Error",
            Message = "Something went wrong",
            Details = "Please try again or create the note manually",
            Progress = 0,
            Step = "0/5",
            Status = "failed",
            Error = errorMessage
        };

        await SendProgressAsync(userId, update);
    }
}
