using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EHR.Services;
using EHR.Models.DTOs;
using EHR.Models.Generated;
using EHR.Helpers;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// MEDOCS AI Voice Entry API
/// Handles real-time audio chunk processing for voice-to-field population
/// in the Encounter Workspace (Vitals, History, CC/HPI tabs).
/// Standalone from the clinical note recording pipeline.
/// </summary>
[ApiController]
[Route("api/medocs-voice")]
[Authorize]
public class MedocsVoiceController : ControllerBase
{
    private readonly IMedocsVoiceProcessingService _voiceService;
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryption;
    private readonly ILogger<MedocsVoiceController> _logger;

    public MedocsVoiceController(
        IMedocsVoiceProcessingService voiceService,
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryption,
        ILogger<MedocsVoiceController> logger)
    {
        _voiceService = voiceService;
        _context = context;
        _tenantProvider = tenantProvider;
        _encryption = encryption;
        _logger = logger;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    /// <summary>
    /// Process an audio chunk: transcribe and extract structured data for a specific tab.
    /// For telehealth chunks, also persists the transcription to the database.
    /// </summary>
    [HttpPost("process-chunk")]
    [Authorize(Roles = "0,1,2,6,7")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<MedocsVoiceResultDto>> ProcessChunk(
        IFormFile audio,
        [FromForm] string tabKey,
        [FromForm] int sequenceNumber,
        [FromForm] decimal durationSeconds,
        [FromForm] string? previousContext,
        [FromForm] int patientId,
        [FromForm] int encounterId)
    {
        if (audio == null || audio.Length == 0)
        {
            return BadRequest(new MedocsVoiceResultDto
            {
                Success = false,
                Message = "Audio file is required"
            });
        }

        if (string.IsNullOrWhiteSpace(tabKey))
        {
            return BadRequest(new MedocsVoiceResultDto
            {
                Success = false,
                Message = "Tab key is required"
            });
        }

        if (patientId <= 0 || encounterId <= 0)
        {
            return BadRequest(new MedocsVoiceResultDto
            {
                Success = false,
                Message = "Valid patient and encounter IDs are required"
            });
        }

        var userId = GetUserId();

        _logger.LogInformation(
            "[TelehealthPersist] process-chunk received: tabKey={TabKey}, seq={Seq}, dur={Dur}s, patientId={PatientId}, encounterId={EncounterId}, audioLen={AudioLen}",
            tabKey, sequenceNumber, durationSeconds, patientId, encounterId, audio.Length);

        using var audioStream = audio.OpenReadStream();
        var result = await _voiceService.ProcessChunkAsync(
            audioStream,
            audio.ContentType,
            audio.FileName,
            tabKey,
            durationSeconds,
            previousContext,
            patientId,
            encounterId,
            userId);

        _logger.LogInformation(
            "[TelehealthPersist] ProcessChunkAsync result: success={Success}, hasTranscription={HasTrans}, transcriptionLen={TransLen}",
            result.Success, !string.IsNullOrWhiteSpace(result.Transcription), result.Transcription?.Length ?? 0);

        // Persist telehealth transcription chunks to DB
        if (result.Success && tabKey == "telehealth" && !string.IsNullOrWhiteSpace(result.Transcription))
        {
            try
            {
                var tenantId = _tenantProvider.TenantId ?? 0;
                _logger.LogInformation(
                    "[TelehealthPersist] Attempting DB save: tenantId={TenantId}, encounterId={EncounterId}, seq={Seq}, dur={Dur}s, plaintextLen={PlainLen}",
                    tenantId, encounterId, sequenceNumber, durationSeconds, result.Transcription.Length);

                if (tenantId == 0)
                {
                    _logger.LogError(
                        "[TelehealthPersist] ABORTING save: TenantId is 0 for encounterId={EncounterId}. Tenant resolution failed.",
                        encounterId);
                }
                else
                {
                    string cipher;
                    try
                    {
                        cipher = _encryption.Encrypt(result.Transcription);
                        _logger.LogInformation(
                            "[TelehealthPersist] Encrypt OK: cipherLen={CipherLen}", cipher?.Length ?? 0);
                    }
                    catch (Exception encEx)
                    {
                        _logger.LogError(encEx,
                            "[TelehealthPersist] Encrypt FAILED for encounterId={EncounterId}, seq={Seq}",
                            encounterId, sequenceNumber);
                        throw;
                    }

                    var chunk = new TelehealthTranscriptionChunk
                    {
                        TenantId = tenantId,
                        EncounterId = encounterId,
                        SequenceNumber = sequenceNumber,
                        TranscriptionText = cipher,
                        DurationSeconds = durationSeconds,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.TelehealthTranscriptionChunks.Add(chunk);
                    var rowsAffected = await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "[TelehealthPersist] DB save SUCCESS: chunkId={ChunkId}, rowsAffected={Rows}, encounterId={EncounterId}, seq={Seq}",
                        chunk.ChunkId, rowsAffected, encounterId, sequenceNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[TelehealthPersist] DB save FAILED for encounterId={EncounterId}, seq={Seq}. Transcription was returned to frontend but NOT persisted.",
                    encounterId, sequenceNumber);
                // Don't fail the response — transcription was still returned to frontend
            }
        }
        else if (tabKey == "telehealth")
        {
            _logger.LogWarning(
                "[TelehealthPersist] Skipped DB save for encounterId={EncounterId}, seq={Seq}: success={Success}, hasTranscription={HasTrans}",
                encounterId, sequenceNumber, result.Success, !string.IsNullOrWhiteSpace(result.Transcription));
        }

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Post-call telehealth extraction: processes the full accumulated transcription
    /// to extract all clinical sections (vitals, history, CC/HPI) at once.
    /// Called after the telehealth call ends.
    /// </summary>
    [HttpPost("extract-telehealth-session")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult<MedocsVoiceResultDto>> ExtractTelehealthSession(
        [FromBody] TelehealthExtractionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Transcription))
        {
            return BadRequest(new MedocsVoiceResultDto
            {
                Success = false,
                Message = "Transcription text is required"
            });
        }

        var result = await _voiceService.ExtractFromFullTranscriptionAsync(request.Transcription);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Extract all sections from accumulated unified voice transcription.
    /// Called periodically during recording and on finalize.
    /// </summary>
    [HttpPost("extract-unified")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult<MedocsVoiceResultDto>> ExtractUnified(
        [FromBody] UnifiedExtractionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Transcription))
        {
            return BadRequest(new MedocsVoiceResultDto
            {
                Success = false,
                Message = "Transcription text is required"
            });
        }

        var result = await _voiceService.ExtractFromUnifiedTranscriptionAsync(request.Transcription, request.ExistingData);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Get all transcription chunks for an encounter (for loading transcription on revisit).
    /// </summary>
    [HttpGet("telehealth-transcription/{encounterId}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> GetTelehealthTranscription(int encounterId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;

        var chunks = await _context.TelehealthTranscriptionChunks
            .Where(c => c.EncounterId == encounterId && c.TenantId == tenantId)
            .OrderBy(c => c.SequenceNumber)
            .Select(c => new { c.SequenceNumber, c.TranscriptionText })
            .ToListAsync();

        if (chunks.Count == 0)
            return Ok(new { HasTranscription = false, Transcription = "" });

        // Decrypt and join all chunks
        var segments = chunks.Select(c =>
        {
            try { return _encryption.Decrypt(c.TranscriptionText); }
            catch { return ""; }
        }).Where(t => !string.IsNullOrWhiteSpace(t));

        var fullTranscription = string.Join("\n", segments);

        return Ok(new { HasTranscription = true, Transcription = fullTranscription, ChunkCount = chunks.Count });
    }
}
