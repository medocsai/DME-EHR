using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

/// <summary>
/// HIPAA-compliant service for managing audio recording sessions.
/// Handles session creation, status updates, and transcription management.
/// All transcription data is encrypted at rest.
/// </summary>
public interface IRecordingSessionService
{
    /// <summary>
    /// Create a new recording session for an appointment.
    /// Called when provider clicks "Start Recording".
    /// </summary>
    Task<RecordingSessionResultDto> CreateSessionAsync(int appointmentId, int userId);

    /// <summary>
    /// Update recording session status.
    /// </summary>
    Task<RecordingSessionResultDto> UpdateStatusAsync(int sessionId, string status, int userId);

    /// <summary>
    /// Get recording session by ID.
    /// </summary>
    Task<RecordingSessionDto?> GetSessionAsync(int sessionId);

    /// <summary>
    /// Get recording session by appointment ID.
    /// </summary>
    Task<RecordingSessionDto?> GetSessionByAppointmentAsync(int appointmentId);

    /// <summary>
    /// Check for an unfinished recording session for an appointment.
    /// Unfinished = StartTime exists, EndTime is null, CompleteTranscription is null
    /// </summary>
    Task<UnfinishedSessionCheckResultDto> CheckUnfinishedSessionAsync(int appointmentId, int userId, string? userIp);

    /// <summary>
    /// Discard an existing session and create a new one.
    /// Used when provider chooses "Start Over".
    /// </summary>
    Task<RecordingSessionResultDto> DiscardAndCreateNewSessionAsync(int oldSessionId, int appointmentId, int userId, string? userIp);

    /// <summary>
    /// Save progress on a recording session without completing it.
    /// Sets status to "Paused", does NOT set EndTime, does NOT merge transcriptions.
    /// Provider can resume later from where they left off.
    /// </summary>
    Task<SaveProgressResultDto> SaveProgressAsync(int sessionId, int elapsedSeconds, int userId, string? userIp);

    /// <summary>
    /// Get the next sequence number for a chunk in a session.
    /// Used when resuming to continue chunk numbering correctly.
    /// </summary>
    Task<int> GetNextSequenceNumberAsync(int sessionId);

}

public class RecordingSessionService : IRecordingSessionService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryption;
    private readonly IAuditService _auditService;

    public RecordingSessionService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryption,
        IAuditService auditService)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryption = encryption;
        _auditService = auditService;
    }

    public async Task<RecordingSessionResultDto> CreateSessionAsync(int appointmentId, int userId)
    {
        // Validate tenant context
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Get appointment details and validate access
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId && a.TenantId == tenantId);

        if (appointment == null)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Appointment not found"
            };
        }

        // Check if there's already an active recording session for this appointment
        var existingSession = await _context.RecordingSessions
            .FirstOrDefaultAsync(rs => rs.AppointmentId == appointmentId
                && rs.TenantId == tenantId
                && (rs.RecordingStatus == "NotStarted" || rs.RecordingStatus == "Recording" || rs.RecordingStatus == "Paused"));

        if (existingSession != null)
        {
            return new RecordingSessionResultDto
            {
                Success = true,
                Message = "Existing session found",
                SessionId = existingSession.SessionId,
                RecordingStatus = existingSession.RecordingStatus
            };
        }

        // Create new recording session
        var session = new RecordingSession
        {
            TenantId = tenantId,
            AppointmentId = appointmentId,
            ProviderId = appointment.ProviderId,
            PatientId = appointment.PatientId,
            RecordingStatus = "NotStarted",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _context.RecordingSessions.Add(session);
        await _context.SaveChangesAsync();

        // Audit log - HIPAA compliance
        await _auditService.LogAccessAsync(
            userId,
            null,
            "CREATE_RECORDING_SESSION",
            "RecordingSession",
            session.SessionId,
            null,
            $"{{\"appointmentId\":{appointmentId},\"patientId\":{appointment.PatientId}}}");

        return new RecordingSessionResultDto
        {
            Success = true,
            Message = "Session created successfully",
            SessionId = session.SessionId,
            RecordingStatus = session.RecordingStatus
        };
    }

    public async Task<RecordingSessionResultDto> UpdateStatusAsync(int sessionId, string status, int userId)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        var session = await _context.RecordingSessions
            .FirstOrDefaultAsync(rs => rs.SessionId == sessionId && rs.TenantId == tenantId);

        if (session == null)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Session not found"
            };
        }

        var oldStatus = session.RecordingStatus;

        // Validate status transitions
        var validTransitions = new Dictionary<string, string[]>
        {
            { "NotStarted", new[] { "Recording", "Cancelled" } },
            { "Recording", new[] { "Paused", "Completed", "Cancelled" } },
            { "Paused", new[] { "Recording", "Completed", "Cancelled" } }
        };

        if (!validTransitions.ContainsKey(oldStatus) || !validTransitions[oldStatus].Contains(status))
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = $"Invalid status transition from {oldStatus} to {status}"
            };
        }

        session.RecordingStatus = status;
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedByUserId = userId;

        // Set start time when recording begins
        if (status == "Recording" && session.StartTime == null)
        {
            session.StartTime = DateTime.UtcNow;
        }

        // Set end time when completed or cancelled
        if (status == "Completed" || status == "Cancelled")
        {
            session.EndTime = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Audit log
        await _auditService.LogAccessAsync(
            userId,
            null,
            "UPDATE_RECORDING_STATUS",
            "RecordingSession",
            sessionId,
            $"{{\"oldStatus\":\"{oldStatus}\"}}",
            $"{{\"newStatus\":\"{status}\"}}");

        return new RecordingSessionResultDto
        {
            Success = true,
            Message = $"Status updated to {status}",
            SessionId = sessionId,
            RecordingStatus = status
        };
    }

    public async Task<RecordingSessionDto?> GetSessionAsync(int sessionId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return null;

        var session = await _context.RecordingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(rs => rs.SessionId == sessionId && rs.TenantId == _tenantProvider.TenantId.Value);

        if (session == null)
            return null;

        return MapToDto(session);
    }

    public async Task<RecordingSessionDto?> GetSessionByAppointmentAsync(int appointmentId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return null;

        var session = await _context.RecordingSessions
            .AsNoTracking()
            .Where(rs => rs.AppointmentId == appointmentId && rs.TenantId == _tenantProvider.TenantId.Value)
            .OrderByDescending(rs => rs.CreatedAt)
            .FirstOrDefaultAsync();

        if (session == null)
            return null;

        return MapToDto(session);
    }

    public async Task<UnfinishedSessionCheckResultDto> CheckUnfinishedSessionAsync(int appointmentId, int userId, string? userIp)
    {
        var result = new UnfinishedSessionCheckResultDto();

        try
        {
            if (!_tenantProvider.TenantId.HasValue)
            {
                result.Success = false;
                result.Message = "Tenant context required";
                return result;
            }

            var tenantId = _tenantProvider.TenantId.Value;

            // Find incomplete session - any session that doesn't have a successfully generated note
            // This includes: Recording, Paused, Failed, FailedNoteGeneration
            // Excludes: Completed (with note), Discarded, Cancelled, NotStarted
            var incompleteStatuses = new[] { "Recording", "Paused", "Failed", "FailedNoteGeneration" };

            var unfinishedSession = await _context.RecordingSessions
                .AsNoTracking()
                .Where(rs => rs.AppointmentId == appointmentId
                    && rs.TenantId == tenantId
                    && rs.StartTime != null
                    && incompleteStatuses.Contains(rs.RecordingStatus))
                .OrderByDescending(rs => rs.UpdatedAt ?? rs.StartTime)
                .FirstOrDefaultAsync();

            // Get chunk count and total duration if session exists
            int chunkCount = 0;
            int totalChunkDurationSeconds = 0;
            if (unfinishedSession != null)
            {
                var chunks = await _context.TranscriptionChunks
                    .Where(c => c.SessionId == unfinishedSession.SessionId)
                    .Select(c => c.ChunkDurationSeconds ?? 0)
                    .ToListAsync();

                chunkCount = chunks.Count;
                totalChunkDurationSeconds = (int)chunks.Sum();
            }

            // Audit log - session resume attempt
            await _auditService.LogAccessAsync(
                userId,
                userIp,
                "SESSION_RESUME_ATTEMPT",
                "RecordingSession",
                unfinishedSession?.SessionId,
                null,
                $"{{\"appointmentId\":{appointmentId},\"sessionFound\":{(unfinishedSession != null).ToString().ToLower()},\"result\":\"success\"}}");

            if (unfinishedSession != null)
            {
                // Use the sum of chunk durations as the elapsed time (or TotalDurationSeconds if available)
                // This is more accurate than wall-clock time since it excludes time when page was closed
                var elapsedSeconds = unfinishedSession.TotalDurationSeconds ?? totalChunkDurationSeconds;
                var hasTranscription = !string.IsNullOrEmpty(unfinishedSession.CompleteTranscription);
                var status = unfinishedSession.RecordingStatus;

                result.Success = true;
                result.HasUnfinishedSession = true;
                result.SessionId = unfinishedSession.SessionId;
                result.StartTime = unfinishedSession.StartTime;
                result.ElapsedSeconds = elapsedSeconds;
                result.ChunkCount = chunkCount;
                result.RecordingStatus = status;
                result.HasTranscription = hasTranscription;

                // Determine what actions are available based on status
                result.CanResume = status == "Paused" || status == "Recording";
                result.CanRetryNoteGeneration = status == "FailedNoteGeneration" && hasTranscription;
                result.CanRetrySaveAndFinish = status == "Failed" && chunkCount > 0;

                result.Message = "Unfinished session found";

                // Audit log - session data retrieved
                await _auditService.LogAccessAsync(
                    userId,
                    userIp,
                    "SESSION_DATA_RETRIEVED",
                    "RecordingSession",
                    unfinishedSession.SessionId,
                    null,
                    $"{{\"appointmentId\":{appointmentId},\"elapsedSeconds\":{elapsedSeconds},\"chunkCount\":{chunkCount},\"status\":\"{status}\"}}");
            }
            else
            {
                result.Success = true;
                result.HasUnfinishedSession = false;
                result.Message = "No unfinished session found";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = "Error checking for unfinished session";

            // Log error but still audit the attempt
            await _auditService.LogAccessAsync(
                userId,
                userIp,
                "SESSION_RESUME_ATTEMPT",
                "RecordingSession",
                null,
                null,
                $"{{\"appointmentId\":{appointmentId},\"sessionFound\":false,\"result\":\"error\",\"error\":\"{ex.Message}\"}}");
        }

        return result;
    }

    public async Task<RecordingSessionResultDto> DiscardAndCreateNewSessionAsync(int oldSessionId, int appointmentId, int userId, string? userIp)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Get the old session
        var oldSession = await _context.RecordingSessions
            .FirstOrDefaultAsync(rs => rs.SessionId == oldSessionId && rs.TenantId == tenantId);

        if (oldSession == null)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Old session not found"
            };
        }

        // Verify appointment matches
        if (oldSession.AppointmentId != appointmentId)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Session does not belong to this appointment"
            };
        }

        // Get chunk count and elapsed time for audit
        var chunkCount = await _context.TranscriptionChunks
            .CountAsync(c => c.SessionId == oldSessionId);
        var elapsedSeconds = oldSession.StartTime.HasValue
            ? (int)(DateTime.UtcNow - oldSession.StartTime.Value).TotalSeconds
            : 0;

        // Mark old session as ended (DO NOT DELETE - HIPAA compliance)
        oldSession.EndTime = DateTime.UtcNow;
        oldSession.RecordingStatus = "Discarded";
        oldSession.UpdatedAt = DateTime.UtcNow;
        oldSession.UpdatedByUserId = userId;

        // Get appointment for new session
        var appointment = await _context.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId && a.TenantId == tenantId);

        if (appointment == null)
        {
            return new RecordingSessionResultDto
            {
                Success = false,
                Message = "Appointment not found"
            };
        }

        // Create new session
        var newSession = new RecordingSession
        {
            TenantId = tenantId,
            AppointmentId = appointmentId,
            ProviderId = appointment.ProviderId,
            PatientId = appointment.PatientId,
            RecordingStatus = "NotStarted",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _context.RecordingSessions.Add(newSession);
        await _context.SaveChangesAsync();

        // Audit log - session discarded
        await _auditService.LogAccessAsync(
            userId,
            userIp,
            "SESSION_DISCARDED",
            "RecordingSession",
            oldSessionId,
            $"{{\"oldSessionId\":{oldSessionId},\"elapsedTimeBeforeDiscard\":{elapsedSeconds},\"chunkCountBeforeDiscard\":{chunkCount}}}",
            $"{{\"newSessionId\":{newSession.SessionId},\"reason\":\"user_chose_to_restart\",\"patientId\":{appointment.PatientId},\"appointmentId\":{appointmentId}}}");

        return new RecordingSessionResultDto
        {
            Success = true,
            Message = "Old session discarded, new session created",
            SessionId = newSession.SessionId,
            RecordingStatus = newSession.RecordingStatus
        };
    }

    public async Task<SaveProgressResultDto> SaveProgressAsync(int sessionId, int elapsedSeconds, int userId, string? userIp)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new SaveProgressResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        var session = await _context.RecordingSessions
            .FirstOrDefaultAsync(rs => rs.SessionId == sessionId && rs.TenantId == tenantId);

        if (session == null)
        {
            return new SaveProgressResultDto
            {
                Success = false,
                Message = "Session not found"
            };
        }

        // Validate that session can be paused (must be Recording or already Paused)
        if (session.RecordingStatus != "Recording" && session.RecordingStatus != "Paused")
        {
            return new SaveProgressResultDto
            {
                Success = false,
                Message = $"Cannot save progress for session with status '{session.RecordingStatus}'"
            };
        }

        var oldStatus = session.RecordingStatus;

        // Update session - set to Paused, record elapsed time, do NOT set EndTime
        session.RecordingStatus = "Paused";
        session.TotalDurationSeconds = elapsedSeconds;
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedByUserId = userId;
        // EndTime stays NULL - session is not finished

        await _context.SaveChangesAsync();

        // Get chunk count for audit
        var chunkCount = await _context.TranscriptionChunks
            .CountAsync(c => c.SessionId == sessionId);

        // Audit log - HIPAA compliance
        await _auditService.LogAccessAsync(
            userId,
            userIp,
            "SESSION_PROGRESS_SAVED",
            "RecordingSession",
            sessionId,
            $"{{\"oldStatus\":\"{oldStatus}\"}}",
            $"{{\"newStatus\":\"Paused\",\"elapsedSeconds\":{elapsedSeconds},\"chunkCount\":{chunkCount},\"patientId\":{session.PatientId},\"appointmentId\":{session.AppointmentId}}}");

        return new SaveProgressResultDto
        {
            Success = true,
            Message = "Progress saved successfully. You can resume recording later.",
            SessionId = sessionId,
            RecordingStatus = "Paused",
            ElapsedSeconds = elapsedSeconds,
            ChunkCount = chunkCount
        };
    }

    public async Task<int> GetNextSequenceNumberAsync(int sessionId)
    {
        var maxSequence = await _context.TranscriptionChunks
            .Where(c => c.SessionId == sessionId)
            .MaxAsync(c => (int?)c.SequenceNumber) ?? 0;

        return maxSequence + 1;
    }

    private RecordingSessionDto MapToDto(RecordingSession session)
    {
        return new RecordingSessionDto
        {
            SessionId = session.SessionId,
            AppointmentId = session.AppointmentId,
            ProviderId = session.ProviderId,
            PatientId = session.PatientId,
            RecordingStatus = session.RecordingStatus,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            TotalDurationSeconds = session.TotalDurationSeconds,
            CreatedAt = session.CreatedAt
        };
    }
}

/// <summary>
/// Result DTO for recording session operations
/// </summary>
public class RecordingSessionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? SessionId { get; set; }
    public string? RecordingStatus { get; set; }
}

/// <summary>
/// DTO for recording session data
/// </summary>
public class RecordingSessionDto
{
    public int SessionId { get; set; }
    public int AppointmentId { get; set; }
    public int ProviderId { get; set; }
    public int PatientId { get; set; }
    public string RecordingStatus { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? TotalDurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request DTO for creating a recording session
/// </summary>
public class CreateRecordingSessionRequest
{
    public int AppointmentId { get; set; }
}

/// <summary>
/// Request DTO for updating recording session status
/// </summary>
public class UpdateRecordingStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Result DTO for checking unfinished session
/// </summary>
public class UnfinishedSessionCheckResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool HasUnfinishedSession { get; set; }
    public int? SessionId { get; set; }
    public DateTime? StartTime { get; set; }
    public int ElapsedSeconds { get; set; }
    public int ChunkCount { get; set; }
    public string? RecordingStatus { get; set; }

    /// <summary>
    /// True if session can be resumed (status is Paused or Recording)
    /// </summary>
    public bool CanResume { get; set; }

    /// <summary>
    /// True if note generation can be retried (status is FailedNoteGeneration and has transcription)
    /// </summary>
    public bool CanRetryNoteGeneration { get; set; }

    /// <summary>
    /// True if save-and-finish can be retried (status is Failed and has chunks)
    /// </summary>
    public bool CanRetrySaveAndFinish { get; set; }

    /// <summary>
    /// True if session has merged transcription available
    /// </summary>
    public bool HasTranscription { get; set; }
}

/// <summary>
/// Request DTO for discarding a session and starting over
/// </summary>
public class DiscardSessionRequest
{
    public int OldSessionId { get; set; }
    public int AppointmentId { get; set; }
}

/// <summary>
/// Result DTO for save progress operation
/// </summary>
public class SaveProgressResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? SessionId { get; set; }
    public string? RecordingStatus { get; set; }
    public int ElapsedSeconds { get; set; }
    public int ChunkCount { get; set; }
}

/// <summary>
/// Request DTO for saving progress on a recording session
/// </summary>
public class SaveProgressRequest
{
    public int ElapsedSeconds { get; set; }
}

