using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// HIPAA-compliant Recording Session API
/// Handles audio recording session management and transcription for appointments
/// </summary>
[ApiController]
[Route("api/recording")]
[Authorize]
public class RecordingController : ControllerBase
{
    private readonly IRecordingSessionService _recordingService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IAuditService _auditService;

    public RecordingController(
        IRecordingSessionService recordingService,
        ITranscriptionService transcriptionService,
        IAuditService auditService)
    {
        _recordingService = recordingService;
        _transcriptionService = transcriptionService;
        _auditService = auditService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    /// <summary>
    /// Create a new recording session for an appointment.
    /// Called when provider clicks "Start Recording" in the modal.
    /// If a session already exists for this appointment, returns the existing session.
    /// </summary>
    [HttpPost("session")]
    [Authorize(Roles = "0,1,2")] // SuperAdmin, ClinicAdmin, Clinician
    public async Task<ActionResult<RecordingSessionResultDto>> CreateSession([FromBody] CreateRecordingSessionRequest request)
    {
        if (request.AppointmentId <= 0)
        {
            return BadRequest(new RecordingSessionResultDto
            {
                Success = false,
                Message = "Valid appointment ID is required"
            });
        }

        var userId = GetUserId();
        var result = await _recordingService.CreateSessionAsync(request.AppointmentId, userId);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Get recording session by ID
    /// </summary>
    [HttpGet("session/{sessionId}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<RecordingSessionDto>> GetSession(int sessionId)
    {
        var session = await _recordingService.GetSessionAsync(sessionId);

        if (session == null)
        {
            return NotFound(new { message = "Session not found" });
        }

        return Ok(session);
    }

    /// <summary>
    /// Get recording session by appointment ID
    /// </summary>
    [HttpGet("session/by-appointment/{appointmentId}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<RecordingSessionDto>> GetSessionByAppointment(int appointmentId)
    {
        var session = await _recordingService.GetSessionByAppointmentAsync(appointmentId);

        if (session == null)
        {
            return NotFound(new { message = "No session found for this appointment" });
        }

        return Ok(session);
    }

    /// <summary>
    /// Update recording session status.
    /// Valid transitions:
    /// - NotStarted -> Recording, Cancelled
    /// - Recording -> Paused, Completed, Cancelled
    /// - Paused -> Recording, Completed, Cancelled
    /// </summary>
    [HttpPut("session/{sessionId}/status")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<RecordingSessionResultDto>> UpdateStatus(int sessionId, [FromBody] UpdateRecordingStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(new RecordingSessionResultDto
            {
                Success = false,
                Message = "Status is required"
            });
        }

        var validStatuses = new[] { "Recording", "Paused", "Completed", "Cancelled" };
        if (!validStatuses.Contains(request.Status))
        {
            return BadRequest(new RecordingSessionResultDto
            {
                Success = false,
                Message = $"Invalid status. Valid values: {string.Join(", ", validStatuses)}"
            });
        }

        var userId = GetUserId();
        var result = await _recordingService.UpdateStatusAsync(sessionId, request.Status, userId);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Upload an audio chunk for transcription.
    /// Accepts audio file, converts to WAV, transcribes with Gemini, stores encrypted.
    /// </summary>
    [HttpPost("session/{sessionId}/chunk")]
    [Authorize(Roles = "0,1,2")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit per chunk
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
    public async Task<ActionResult<TranscriptionChunkResultDto>> UploadChunk(
        int sessionId,
        [FromForm] IFormFile audio,
        [FromForm] int sequenceNumber,
        [FromForm] decimal durationSeconds,
        [FromForm] DateTime? recordedTimestamp = null)
    {
        if (audio == null || audio.Length == 0)
        {
            return BadRequest(new TranscriptionChunkResultDto
            {
                Success = false,
                Message = "No audio file provided"
            });
        }

        if (sequenceNumber < 1)
        {
            return BadRequest(new TranscriptionChunkResultDto
            {
                Success = false,
                Message = "Sequence number must be 1 or greater"
            });
        }

        var userId = GetUserId();
        var timestamp = recordedTimestamp ?? DateTime.UtcNow;

        using var audioStream = audio.OpenReadStream();
        var result = await _transcriptionService.ProcessChunkAsync(
            sessionId,
            sequenceNumber,
            audioStream,
            audio.FileName,
            audio.ContentType,
            durationSeconds,
            timestamp,
            userId);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Get all transcription chunks for a session.
    /// Returns chunks with decrypted transcription text.
    /// </summary>
    [HttpGet("session/{sessionId}/chunks")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<List<TranscriptionChunkDto>>> GetSessionChunks(int sessionId)
    {
        var chunks = await _transcriptionService.GetSessionChunksAsync(sessionId);
        return Ok(chunks);
    }

    /// <summary>
    /// Check for an unfinished recording session for an appointment.
    /// Called when modal opens to determine if Resume Recording should be shown.
    /// </summary>
    [HttpGet("session/check-unfinished/{appointmentId}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<UnfinishedSessionCheckResultDto>> CheckUnfinishedSession(int appointmentId)
    {
        if (appointmentId <= 0)
        {
            return BadRequest(new UnfinishedSessionCheckResultDto
            {
                Success = false,
                Message = "Valid appointment ID is required"
            });
        }

        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _recordingService.CheckUnfinishedSessionAsync(appointmentId, userId, userIp);

        return Ok(result);
    }

    /// <summary>
    /// Discard an existing session and create a new one.
    /// Called when provider clicks "Start Over" and confirms.
    /// Old session is marked as ended but NOT deleted (HIPAA compliance).
    /// </summary>
    [HttpPost("session/discard-and-restart")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<RecordingSessionResultDto>> DiscardAndRestart([FromBody] DiscardSessionRequest request)
    {
        if (request.OldSessionId <= 0)
        {
            return BadRequest(new RecordingSessionResultDto
            {
                Success = false,
                Message = "Valid old session ID is required"
            });
        }

        if (request.AppointmentId <= 0)
        {
            return BadRequest(new RecordingSessionResultDto
            {
                Success = false,
                Message = "Valid appointment ID is required"
            });
        }

        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _recordingService.DiscardAndCreateNewSessionAsync(
            request.OldSessionId, request.AppointmentId, userId, userIp);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Resume an existing recording session.
    /// Called when provider clicks "Resume Recording".
    /// </summary>
    [HttpPost("session/{sessionId}/resume")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<RecordingSessionResultDto>> ResumeSession(int sessionId)
    {
        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Get session to verify it exists and is resumable
        var session = await _recordingService.GetSessionAsync(sessionId);
        if (session == null)
        {
            return NotFound(new RecordingSessionResultDto
            {
                Success = false,
                Message = "Session not found"
            });
        }

        // Audit log - session resumed
        await _auditService.LogAccessAsync(
            userId,
            userIp,
            "SESSION_RESUMED",
            "RecordingSession",
            sessionId,
            null,
            $"{{\"sessionId\":{sessionId},\"appointmentId\":{session.AppointmentId},\"patientId\":{session.PatientId},\"previousStatus\":\"{session.RecordingStatus}\"}}");

        return Ok(new RecordingSessionResultDto
        {
            Success = true,
            Message = "Session resumed",
            SessionId = sessionId,
            RecordingStatus = session.RecordingStatus
        });
    }

    /// <summary>
    /// Save progress on a recording session without completing it.
    /// Sets status to "Paused", does NOT set EndTime, does NOT merge transcriptions.
    /// Provider can resume later from where they left off.
    /// Called when provider clicks "Save Progress" button.
    /// </summary>
    [HttpPut("session/{sessionId}/save-progress")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<SaveProgressResultDto>> SaveProgress(int sessionId, [FromBody] SaveProgressRequest request)
    {
        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _recordingService.SaveProgressAsync(sessionId, request.ElapsedSeconds, userId, userIp);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Get the next sequence number for a chunk in a session.
    /// Used when resuming to continue chunk numbering correctly.
    /// </summary>
    [HttpGet("session/{sessionId}/next-sequence")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<int>> GetNextSequenceNumber(int sessionId)
    {
        var nextSequence = await _recordingService.GetNextSequenceNumberAsync(sessionId);
        return Ok(new { nextSequenceNumber = nextSequence });
    }

    /// <summary>
    /// Save and finish a recording session.
    /// Waits for all chunk transcriptions to complete, merges transcriptions and audio files.
    /// Called when provider clicks "Save &amp; Finish".
    /// Accepts optional selectedTemplateIds to specify which templates to generate notes for.
    /// </summary>
    [HttpPost("session/{sessionId}/save-and-finish")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<SaveAndFinishResultDto>> SaveAndFinish(int sessionId, [FromBody] SaveAndFinishRequestDto? request = null)
    {
        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _transcriptionService.SaveAndFinishAsync(sessionId, userId, userIp, request?.SelectedTemplateIds);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Generate a clinical note from the merged transcription using AI.
    /// Uses tenant + location matching to find appropriate templates.
    /// Called automatically after save-and-finish completes successfully.
    /// </summary>
    [HttpPost("session/{sessionId}/generate-clinical-note")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<GenerateClinicalNoteResultDto>> GenerateClinicalNote(int sessionId)
    {
        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _transcriptionService.GenerateClinicalNoteAsync(sessionId, userId, userIp);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }


    /// <summary>
    /// Retry note generation for a session that previously failed.
    /// Only works for sessions with status "FailedNoteGeneration" or "Completed".
    /// </summary>
    [HttpPost("session/{sessionId}/retry-note-generation")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<GenerateClinicalNoteResultDto>> RetryNoteGeneration(int sessionId)
    {
        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _transcriptionService.RetryNoteGenerationAsync(sessionId, userId, userIp);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Get the audio file for a session (for playback).
    /// Returns the decrypted merged audio as a WAV file.
    /// </summary>
    [HttpGet("session/{sessionId}/audio")]
    [Authorize(Roles = "0,1,2")]
    public async Task<IActionResult> GetSessionAudio(int sessionId)
    {
        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _transcriptionService.GetSessionAudioAsync(sessionId, userId, userIp);

        if (!result.Success || result.AudioData == null)
        {
            return NotFound(new { message = result.Message });
        }

        return File(result.AudioData, result.ContentType, $"session_{sessionId}_audio.wav");
    }

    /// <summary>
    /// Generate a clinical note from telehealth session transcription text.
    /// Does not require a RecordingSession — uses encounter context directly.
    /// </summary>
    [HttpPost("generate-note-from-transcription")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<GenerateClinicalNoteResultDto>> GenerateNoteFromTranscription(
        [FromBody] EHR.Models.DTOs.TelehealthNoteGenerationRequest request)
    {
        if (request.EncounterId <= 0)
        {
            return BadRequest(new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Valid encounter ID is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Transcription))
        {
            return BadRequest(new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Transcription text is required"
            });
        }

        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _transcriptionService.GenerateNoteFromTranscriptionAsync(
            request.EncounterId, request.Transcription, userId, userIp, request.SelectedTemplateIds);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Get matching clinical note templates for an encounter (for template selection UI).
    /// </summary>
    [HttpGet("matching-templates")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<List<MatchingTemplateDto>>> GetMatchingTemplates([FromQuery] int encounterId)
    {
        if (encounterId <= 0)
            return BadRequest(new { Success = false, Message = "Valid encounter ID is required" });

        var templates = await _transcriptionService.GetMatchingTemplatesForEncounterAsync(encounterId);
        return Ok(templates);
    }

    /// <summary>
    /// Start background note generation from transcription. Returns immediately with a job ID.
    /// </summary>
    [HttpPost("start-note-generation")]
    [Authorize(Roles = "0,1,2")]
    public ActionResult StartNoteGeneration(
        [FromBody] EHR.Models.DTOs.TelehealthNoteGenerationRequest request,
        [FromServices] INoteGenerationJobService jobService,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] ILocationProvider locationProvider)
    {
        if (request.EncounterId <= 0)
            return BadRequest(new { Success = false, Message = "Valid encounter ID is required" });

        if (string.IsNullOrWhiteSpace(request.Transcription))
            return BadRequest(new { Success = false, Message = "Transcription text is required" });

        if (request.SelectedTemplateIds == null || request.SelectedTemplateIds.Count == 0)
            return BadRequest(new { Success = false, Message = "At least one template must be selected" });

        var userId = GetUserId();
        var userIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var patientName = request.PatientName ?? "Patient";

        // Capture tenant/location context from the current request to pass to the background scope
        var tenantId = tenantProvider.TenantId;
        var tenantSubdomain = tenantProvider.TenantSubdomain;
        var locationId = locationProvider.LocationId;
        var locationName = locationProvider.LocationName;

        var jobId = jobService.CreateJob(request.EncounterId, userId, patientName, request.SelectedTemplateIds);

        // Fire background work with its own DI scope, propagating tenant/location context
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();

            // Set tenant/location context on the new scope's providers
            var scopedTenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
            scopedTenantProvider.TenantId = tenantId;
            scopedTenantProvider.TenantSubdomain = tenantSubdomain;
            var scopedLocationProvider = scope.ServiceProvider.GetRequiredService<ILocationProvider>();
            scopedLocationProvider.LocationId = locationId;
            scopedLocationProvider.LocationName = locationName;

            var transcriptionService = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
            await transcriptionService.GenerateNotesInBackgroundAsync(
                jobId, request.EncounterId, request.Transcription, userId, userIp, request.SelectedTemplateIds);
        });

        return Ok(new { Success = true, JobId = jobId, Message = "Note generation started" });
    }

    /// <summary>
    /// Poll the status of a background note generation job.
    /// </summary>
    [HttpGet("note-generation-status/{jobId}")]
    [Authorize(Roles = "0,1,2")]
    public ActionResult GetNoteGenerationStatus(string jobId, [FromServices] INoteGenerationJobService jobService)
    {
        var job = jobService.GetJob(jobId);
        if (job == null)
            return NotFound(new { Success = false, Message = "Job not found" });

        return Ok(new
        {
            Success = true,
            job.JobId,
            Status = job.Status.ToString().ToLower(),
            job.TotalNotes,
            job.CompletedNotes,
            job.PatientName,
            job.EncounterId,
            job.ErrorMessage,
            GeneratedNotes = job.GeneratedNotes.Select(n => new
            {
                n.ClinicalNoteId,
                n.TemplateId,
                n.TemplateName
            })
        });
    }

    /// <summary>
    /// Get all active note generation jobs for the current user.
    /// </summary>
    [HttpGet("active-note-generations")]
    [Authorize(Roles = "0,1,2")]
    public ActionResult GetActiveNoteGenerations([FromServices] INoteGenerationJobService jobService)
    {
        var userId = GetUserId();
        var jobs = jobService.GetActiveJobsForUser(userId);

        return Ok(jobs.Select(j => new
        {
            j.JobId,
            Status = j.Status.ToString().ToLower(),
            j.TotalNotes,
            j.CompletedNotes,
            j.PatientName,
            j.EncounterId
        }));
    }
}
