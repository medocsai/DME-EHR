using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EHR.Helpers;
using EHR.Hubs;
using EHR.Models.Generated;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

/// <summary>
/// HIPAA-compliant transcription service for audio chunks.
/// Handles audio conversion to WAV, Gemini API transcription, and encrypted storage.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Process an audio chunk: convert to WAV, transcribe with Gemini, store encrypted.
    /// </summary>
    Task<TranscriptionChunkResultDto> ProcessChunkAsync(
        int sessionId,
        int sequenceNumber,
        Stream audioStream,
        string originalFileName,
        string contentType,
        decimal durationSeconds,
        DateTime recordedTimestamp,
        int userId);

    /// <summary>
    /// Get all chunks for a session with decrypted transcriptions.
    /// </summary>
    Task<List<TranscriptionChunkDto>> GetSessionChunksAsync(int sessionId);

    /// <summary>
    /// Save and finish a recording session:
    /// - Wait for all chunk transcriptions to complete
    /// - Merge transcriptions in sequence order
    /// - Merge audio files in sequence order
    /// - Store encrypted merged data
    /// </summary>
    Task<SaveAndFinishResultDto> SaveAndFinishAsync(int sessionId, int userId, string? userIp, List<int>? selectedTemplateIds = null);

    /// <summary>
    /// Generate a clinical note from the merged transcription using Gemini API.
    /// Uses selectedTemplateIds if provided, otherwise falls back to tenant + location matching.
    /// </summary>
    Task<GenerateClinicalNoteResultDto> GenerateClinicalNoteAsync(int sessionId, int userId, string? userIp, List<int>? selectedTemplateIds = null);

    /// <summary>
    /// Retry note generation for a session that previously failed.
    /// Only works for sessions with status "FailedNoteGeneration" or "Completed".
    /// </summary>
    Task<GenerateClinicalNoteResultDto> RetryNoteGenerationAsync(int sessionId, int userId, string? userIp);

    /// <summary>
    /// Get the merged audio file for a session (decrypted, ready for playback).
    /// Returns null if no merged audio exists.
    /// </summary>
    Task<SessionAudioResultDto> GetSessionAudioAsync(int sessionId, int userId, string? userIp);

    /// <summary>
    /// Generate a clinical note directly from telehealth session transcription text.
    /// Does not require a RecordingSession — uses the encounter to find patient, provider, and templates.
    /// </summary>
    Task<GenerateClinicalNoteResultDto> GenerateNoteFromTranscriptionAsync(
        int encounterId, string transcription, int userId, string? userIp, List<int>? selectedTemplateIds = null);

    /// <summary>
    /// Get matching clinical note templates for an encounter (for template selection UI).
    /// </summary>
    Task<List<MatchingTemplateDto>> GetMatchingTemplatesForEncounterAsync(int encounterId);

    /// <summary>
    /// Generate notes in parallel in the background. Designed to be called from a background task
    /// with its own DI scope. Updates job progress via INoteGenerationJobService.
    /// </summary>
    Task GenerateNotesInBackgroundAsync(
        string jobId, int encounterId, string transcription, int userId, string? userIp, List<int> selectedTemplateIds);
}

public class TranscriptionService : ITranscriptionService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EncryptionHelper _encryption;
    private readonly IAuditService _auditService;
    private readonly IRecordingProgressService _progressService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TranscriptionService> _logger;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IGeminiService _geminiService;
    private readonly IFileStorageService _storageService;
    private readonly IEncounterContextService _encounterContextService;
    private readonly FilePathBuilder _pathBuilder;
    private readonly MetadataBuilder _metadataBuilder;
    private readonly INoteGenerationJobService _jobService;
    private readonly string _tempPath;
    private readonly string _ffmpegPath;
    private readonly byte[] _fileEncryptionKey;

    public TranscriptionService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EncryptionHelper encryption,
        IAuditService auditService,
        IRecordingProgressService progressService,
        IConfiguration configuration,
        ILogger<TranscriptionService> logger,
        IWebHostEnvironment webHostEnvironment,
        IGeminiService geminiService,
        IFileStorageService storageService,
        IEncounterContextService encounterContextService,
        FilePathBuilder pathBuilder,
        MetadataBuilder metadataBuilder,
        INoteGenerationJobService jobService)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryption = encryption;
        _auditService = auditService;
        _progressService = progressService;
        _configuration = configuration;
        _logger = logger;
        _webHostEnvironment = webHostEnvironment;
        _geminiService = geminiService;
        _storageService = storageService;
        _encounterContextService = encounterContextService;
        _pathBuilder = pathBuilder;
        _metadataBuilder = metadataBuilder;
        _jobService = jobService;

        // Configure temp path for FFmpeg processing - still uses local storage
        var configuredTempPath = configuration["AudioStorage:TempPath"];
        var configuredFfmpegPath = configuration["AudioStorage:FfmpegPath"];

        _tempPath = ResolveToAbsolutePath(configuredTempPath, webHostEnvironment.ContentRootPath)
            ?? Path.Combine(Path.GetTempPath(), "PTEHR_Audio");
        _ffmpegPath = ResolveToAbsolutePath(configuredFfmpegPath, webHostEnvironment.ContentRootPath)
            ?? FindFfmpegPath();

        // Ensure temp directory exists
        Directory.CreateDirectory(_tempPath);

        // Derive file encryption key from main encryption key
        var keyString = configuration["Encryption:Key"] ??
            throw new InvalidOperationException("Encryption:Key not found");
        using var deriveBytes = new Rfc2898DeriveBytes(
            keyString + "_AUDIO",
            Encoding.UTF8.GetBytes("PTEHR_AUDIO_SALT_2024"),
            100000,
            HashAlgorithmName.SHA256);
        _fileEncryptionKey = deriveBytes.GetBytes(32);
    }

    private string FindFfmpegPath()
    {
        // Try common locations
        var possiblePaths = new[]
        {
            Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data", "Tools", "ffmpeg.exe"),
            Path.Combine(_webHostEnvironment.WebRootPath, "Content", "tools", "ffmpeg.exe"),
            Path.Combine(_webHostEnvironment.WebRootPath, "tools", "ffmpeg.exe"),
            "/usr/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "ffmpeg" // System PATH
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) || path == "ffmpeg")
                return path;
        }

        return "ffmpeg"; // Fallback to PATH
    }

    /// <summary>
    /// Resolves a path from config to absolute path. If the path is relative, combines with contentRoot.
    /// Uses Path.GetFullPath to normalize separators and resolve any relative segments.
    /// </summary>
    private static string? ResolveToAbsolutePath(string? configuredPath, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        // If already absolute, normalize and return
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        // Combine relative path with content root and normalize
        // Path.GetFullPath normalizes separators (/ vs \) and resolves .. segments
        return Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }

    public async Task<TranscriptionChunkResultDto> ProcessChunkAsync(
        int sessionId,
        int sequenceNumber,
        Stream audioStream,
        string originalFileName,
        string contentType,
        decimal durationSeconds,
        DateTime recordedTimestamp,
        int userId)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new TranscriptionChunkResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Validate session exists and user has access
        var session = await _context.RecordingSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId);

        if (session == null)
        {
            return new TranscriptionChunkResultDto
            {
                Success = false,
                Message = "Recording session not found"
            };
        }

        // Check if chunk already exists
        var existingChunk = await _context.TranscriptionChunks
            .FirstOrDefaultAsync(c => c.SessionId == sessionId && c.SequenceNumber == sequenceNumber);

        if (existingChunk != null && existingChunk.TranscriptionStatus == "Completed")
        {
            return new TranscriptionChunkResultDto
            {
                Success = true,
                Message = "Chunk already processed",
                ChunkId = existingChunk.ChunkId,
                TranscriptionStatus = existingChunk.TranscriptionStatus
            };
        }

        string? tempInputPath = null;
        string? tempWavPath = null;
        string? encryptedFilePath = null;

        try
        {
            // Step 1: Save audio to temp file
            var fileExtension = GetFileExtension(contentType, originalFileName);
            tempInputPath = Path.Combine(_tempPath, $"{Guid.NewGuid():N}{fileExtension}");

            await using (var fileStream = new FileStream(tempInputPath, FileMode.Create))
            {
                await audioStream.CopyToAsync(fileStream);
            }

            _logger.LogInformation("Chunk received: Session={SessionId}, Seq={SequenceNumber}, Size={Size}bytes",
                sessionId, sequenceNumber, new FileInfo(tempInputPath).Length);

            // Audit log - chunk received
            await _auditService.LogAccessAsync(
                userId,
                null,
                "RECEIVE_AUDIO_CHUNK",
                "TranscriptionChunk",
                null,
                null,
                $"{{\"sessionId\":{sessionId},\"sequenceNumber\":{sequenceNumber},\"duration\":{durationSeconds}}}");

            // Step 2: Convert to WAV format for Gemini
            tempWavPath = await ConvertToWavAsync(tempInputPath);

            _logger.LogInformation("Audio converted to WAV: {WavPath}", tempWavPath);

            // Step 3: Upload to Gemini and transcribe
            string transcriptionText;
            try
            {
                var fileUri = await UploadToGeminiAsync(tempWavPath);
                _logger.LogInformation("File uploaded to Gemini: {FileUri}", fileUri);

                transcriptionText = await TranscribeWithGeminiAsync(fileUri);
                _logger.LogInformation("Transcription received, length={Length}", transcriptionText?.Length ?? 0);
            }
            catch (Exception geminiEx)
            {
                _logger.LogError(geminiEx, "Gemini transcription failed for session {SessionId}, chunk {SequenceNumber}",
                    sessionId, sequenceNumber);

                // Store chunk with failed status for retry
                var failedChunk = await CreateOrUpdateChunkAsync(
                    session, sequenceNumber, null, durationSeconds, recordedTimestamp,
                    null, "Failed", geminiEx.Message, userId);

                return new TranscriptionChunkResultDto
                {
                    Success = false,
                    Message = "Transcription failed: " + geminiEx.Message,
                    ChunkId = failedChunk.ChunkId,
                    TranscriptionStatus = "Failed"
                };
            }

            // Step 4: Encrypt transcription text
            var encryptedTranscription = _encryption.Encrypt(transcriptionText);

            // Step 5: Encrypt and upload WAV file to cloud storage
            var storageFileName = _pathBuilder.GenerateEncryptedFileName("chunk.wav");
            var plainBytes = await File.ReadAllBytesAsync(tempWavPath);
            var encryptedBytes = EncryptFile(plainBytes);

            var folderPath = _pathBuilder.BuildRecordingChunkPath(tenantId);
            var metadata = _metadataBuilder.BuildRecordingChunkMetadata(
                tenantId, sessionId, sequenceNumber, durationSeconds);

            var uploadResult = await _storageService.UploadBytesAsync(
                encryptedBytes,
                storageFileName,
                folderPath,
                "application/octet-stream",
                metadata);

            if (!uploadResult.Success)
            {
                _logger.LogError("Failed to upload chunk to cloud storage: {Error}", uploadResult.ErrorMessage);
                throw new InvalidOperationException("Failed to upload audio chunk to cloud storage");
            }

            // Store cloud path as the storage file name
            var cloudPath = uploadResult.CloudPath;

            _logger.LogInformation("Encrypted audio uploaded to cloud: {CloudPath}", cloudPath);

            // Step 6: Store in database (store full cloud path)
            var chunk = await CreateOrUpdateChunkAsync(
                session, sequenceNumber, cloudPath, durationSeconds, recordedTimestamp,
                encryptedTranscription, "Completed", null, userId);

            // Audit log - transcription completed
            await _auditService.LogAccessAsync(
                userId,
                null,
                "TRANSCRIPTION_COMPLETED",
                "TranscriptionChunk",
                chunk.ChunkId,
                null,
                $"{{\"sessionId\":{sessionId},\"sequenceNumber\":{sequenceNumber},\"transcriptionLength\":{transcriptionText?.Length ?? 0}}}");

            return new TranscriptionChunkResultDto
            {
                Success = true,
                Message = "Chunk processed successfully",
                ChunkId = chunk.ChunkId,
                TranscriptionStatus = "Completed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chunk for session {SessionId}, sequence {SequenceNumber}",
                sessionId, sequenceNumber);

            return new TranscriptionChunkResultDto
            {
                Success = false,
                Message = "Failed to process chunk: " + ex.Message
            };
        }
        finally
        {
            // Clean up temp files
            CleanupTempFile(tempInputPath);
            CleanupTempFile(tempWavPath);
        }
    }

    private async Task<TranscriptionChunk> CreateOrUpdateChunkAsync(
        RecordingSession session,
        int sequenceNumber,
        string? audioFileName,
        decimal durationSeconds,
        DateTime recordedTimestamp,
        string? encryptedTranscription,
        string status,
        string? errorMessage,
        int userId)
    {
        var chunk = await _context.TranscriptionChunks
            .FirstOrDefaultAsync(c => c.SessionId == session.SessionId && c.SequenceNumber == sequenceNumber);

        if (chunk == null)
        {
            chunk = new TranscriptionChunk
            {
                SessionId = session.SessionId,
                SequenceNumber = sequenceNumber,
                CreatedAt = DateTime.UtcNow
            };
            _context.TranscriptionChunks.Add(chunk);
        }

        chunk.ChunkAudioFileName = audioFileName;
        chunk.ChunkDurationSeconds = durationSeconds;
        chunk.RecordedTimestamp = recordedTimestamp;
        chunk.TranscriptionText = encryptedTranscription;
        chunk.TranscriptionStatus = status;
        chunk.TranscriptionError = errorMessage;
        chunk.UpdatedAt = DateTime.UtcNow;

        if (status == "Completed")
        {
            chunk.TranscriptionCompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return chunk;
    }

    private string GetFileExtension(string contentType, string fileName)
    {
        // Try to get from content type
        var extension = contentType.ToLowerInvariant() switch
        {
            "audio/webm" => ".webm",
            "audio/mp3" or "audio/mpeg" => ".mp3",
            "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            "audio/mp4" or "audio/m4a" => ".m4a",
            "audio/aac" => ".aac",
            _ => Path.GetExtension(fileName)?.ToLowerInvariant() ?? ".webm"
        };

        return extension;
    }

    private async Task<string> ConvertToWavAsync(string inputPath)
    {
        var outputPath = Path.Combine(_tempPath, $"{Guid.NewGuid():N}.wav");

        // Target parameters for Gemini compatibility
        int targetSampleRate = 16000;
        int targetChannels = 1; // Mono
        string targetBitDepth = "s16"; // 16-bit

        var arguments = $"-i \"{inputPath}\" -ar {targetSampleRate} -ac {targetChannels} -sample_fmt {targetBitDepth} -y \"{outputPath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        process.Start();

        var errorOutput = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg conversion failed: {Error}", errorOutput);
            throw new InvalidOperationException($"FFmpeg conversion failed: {errorOutput}");
        }

        return outputPath;
    }

    private async Task<string> UploadToGeminiAsync(string filePath)
    {
        // Use centralized GeminiService for file upload
        var fileUri = await _geminiService.UploadFileAsync(filePath, "audio/wav");
        if (string.IsNullOrEmpty(fileUri))
        {
            throw new InvalidOperationException("Failed to upload file to Gemini");
        }
        return fileUri;
    }

    private async Task<string> TranscribeWithGeminiAsync(string fileUri)
    {
        // Use centralized GeminiService for transcription
        var response = await _geminiService.TranscribeAudioAsync(fileUri, "audio/wav");
        if (!response.Success)
        {
            // "Invalid transcription response" typically means silent / empty audio
            // (Gemini returned no candidates). Treat as empty transcript so a single
            // silent chunk doesn't fail the whole session — the merge step will skip it.
            if (string.Equals(response.ErrorMessage, "Invalid transcription response", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Gemini returned no transcription text (likely silent/empty audio); treating as empty");
                return "";
            }
            _logger.LogError("Gemini transcription failed: {Error}", response.ErrorMessage);
            throw new InvalidOperationException($"Gemini transcription failed: {response.ErrorMessage}");
        }
        return response.Text ?? "";
    }

    private byte[] EncryptFile(byte[] plainBytes)
    {
        using var aes = Aes.Create();
        aes.Key = _fileEncryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();

        // Write IV first
        msEncrypt.Write(aes.IV, 0, aes.IV.Length);

        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        {
            csEncrypt.Write(plainBytes, 0, plainBytes.Length);
        }

        return msEncrypt.ToArray();
    }

    private void CleanupTempFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temp file: {FilePath}", filePath);
        }
    }

    public async Task<List<TranscriptionChunkDto>> GetSessionChunksAsync(int sessionId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return new List<TranscriptionChunkDto>();

        var tenantId = _tenantProvider.TenantId.Value;

        // Verify session belongs to tenant
        var session = await _context.RecordingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId);

        if (session == null)
            return new List<TranscriptionChunkDto>();

        var chunks = await _context.TranscriptionChunks
            .Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.SequenceNumber)
            .ToListAsync();

        return chunks.Select(c => new TranscriptionChunkDto
        {
            ChunkId = c.ChunkId,
            SessionId = c.SessionId,
            SequenceNumber = c.SequenceNumber,
            ChunkDurationSeconds = c.ChunkDurationSeconds,
            RecordedTimestamp = c.RecordedTimestamp,
            TranscriptionText = SafeDecrypt(c.TranscriptionText),
            TranscriptionStatus = c.TranscriptionStatus,
            TranscriptionCompletedAt = c.TranscriptionCompletedAt,
            TranscriptionError = c.TranscriptionError
        }).ToList();
    }

    /// <summary>
    /// Save and finish a recording session.
    /// Waits for all chunks to be transcribed, then merges transcriptions and audio files.
    /// Sends real-time progress updates via SignalR.
    /// </summary>
    public async Task<SaveAndFinishResultDto> SaveAndFinishAsync(int sessionId, int userId, string? userIp, List<int>? selectedTemplateIds = null)
    {
        var startTime = DateTime.UtcNow;

        if (!_tenantProvider.TenantId.HasValue)
        {
            return new SaveAndFinishResultDto
            {
                Success = false,
                Status = "Failed",
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Get the session
        var session = await _context.RecordingSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId);

        if (session == null)
        {
            return new SaveAndFinishResultDto
            {
                Success = false,
                Status = "Failed",
                Message = "Session not found"
            };
        }

        // Audit log - save and finish started
        await _auditService.LogAccessAsync(
            userId,
            userIp,
            "SAVE_AND_FINISH_STARTED",
            "RecordingSession",
            sessionId,
            null,
            $"{{\"sessionId\":{sessionId},\"patientId\":{session.PatientId},\"appointmentId\":{session.AppointmentId}}}");

        try
        {
            // Step 1: Mark session as completing
            session.RecordingStatus = "Completing";
            session.EndTime = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            session.UpdatedByUserId = userId;
            await _context.SaveChangesAsync();

            // Step 2: Wait for all chunks to be transcribed (with progress updates)
            var totalChunks = await _context.TranscriptionChunks
                .CountAsync(c => c.SessionId == sessionId);
            var completedChunks = await _context.TranscriptionChunks
                .CountAsync(c => c.SessionId == sessionId && c.TranscriptionStatus == "Completed");

            // Send initial waiting progress
            await _progressService.SendWaitingForChunksAsync(userId, sessionId, completedChunks, totalChunks);

            var waitResult = await WaitForChunkTranscriptionsWithProgressAsync(sessionId, userId, userIp, totalChunks);
            if (!waitResult.Success)
            {
                session.RecordingStatus = "Failed";
                session.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _progressService.SendErrorAsync(userId, sessionId, waitResult.Message ?? "Failed waiting for transcriptions");

                return new SaveAndFinishResultDto
                {
                    Success = false,
                    Status = "Failed",
                    Message = waitResult.Message,
                    SessionId = sessionId
                };
            }

            // Step 3: Get all chunks ordered by sequence number
            var chunks = await _context.TranscriptionChunks
                .Where(c => c.SessionId == sessionId)
                .OrderBy(c => c.SequenceNumber)
                .ToListAsync();

            if (chunks.Count == 0)
            {
                session.RecordingStatus = "Completed";
                session.TotalDurationSeconds = 0;
                session.CompleteTranscription = _encryption.Encrypt("");
                session.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _auditService.LogAccessAsync(
                    userId, userIp, "SESSION_COMPLETED_NO_CHUNKS",
                    "RecordingSession", sessionId, null,
                    $"{{\"sessionId\":{sessionId},\"chunkCount\":0}}");

                return new SaveAndFinishResultDto
                {
                    Success = true,
                    Status = "Completed",
                    Message = "Session completed (no audio chunks)",
                    SessionId = sessionId
                };
            }

            // Step 4: Merge transcriptions in sequence order (send progress)
            await _progressService.SendMergingTranscriptionsAsync(userId, sessionId, chunks.Count);
            var mergedTranscription = await MergeTranscriptionsAsync(chunks);

            // Step 5: Merge audio files in sequence order
            var mergedAudioFileName = await MergeAudioFilesAsync(chunks, tenantId);

            // Step 6: Calculate total duration
            var totalDurationSeconds = (int)chunks.Sum(c => c.ChunkDurationSeconds ?? 0);

            // Step 7: Update session with merged data
            session.RecordingStatus = "Completed";
            session.TotalDurationSeconds = totalDurationSeconds;
            session.CompleteTranscription = _encryption.Encrypt(mergedTranscription);
            session.MergedAudioFileName = mergedAudioFileName;
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

            // Audit log - session merged and completed
            await _auditService.LogAccessAsync(
                userId,
                userIp,
                "SESSION_MERGED_AND_COMPLETED",
                "RecordingSession",
                sessionId,
                null,
                $"{{\"sessionId\":{sessionId},\"chunkCount\":{chunks.Count},\"totalDurationSeconds\":{totalDurationSeconds},\"transcriptionLength\":{mergedTranscription.Length},\"mergedAudioFile\":\"{mergedAudioFileName}\",\"processingTimeSeconds\":{processingTime:F2}}}");

            _logger.LogInformation(
                "Session {SessionId} completed: {ChunkCount} chunks, {Duration}s duration, {ProcessingTime}s processing",
                sessionId, chunks.Count, totalDurationSeconds, processingTime);

            // Step 8: Generate clinical note(s) from the merged transcription
            // This continues the same flow to provide seamless progress updates
            _logger.LogInformation("Session {SessionId}: Starting clinical note generation", sessionId);
            var noteResult = await GenerateClinicalNoteAsync(sessionId, userId, userIp, selectedTemplateIds);

            if (!noteResult.Success)
            {
                // Clinical note generation failed, but transcription merge succeeded
                // Mark session as FailedNoteGeneration so it appears in drafts and can be retried
                _logger.LogWarning("Session {SessionId}: Clinical note generation failed: {Message}",
                    sessionId, noteResult.Message);

                session.RecordingStatus = "FailedNoteGeneration";
                session.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Audit log - note generation failed
                await _auditService.LogAccessAsync(
                    userId,
                    userIp,
                    "NOTE_GENERATION_FAILED_SESSION_PRESERVED",
                    "RecordingSession",
                    sessionId,
                    null,
                    $"{{\"sessionId\":{sessionId},\"error\":\"{noteResult.Message?.Replace("\"", "'")}\",\"transcriptionPreserved\":true}}");

                return new SaveAndFinishResultDto
                {
                    Success = false,
                    Status = "FailedNoteGeneration",
                    Message = "Recording saved but note generation failed. You can retry from Draft Sessions. Error: " + noteResult.Message,
                    SessionId = sessionId,
                    ChunkCount = chunks.Count,
                    TotalDurationSeconds = totalDurationSeconds,
                    TranscriptionLength = mergedTranscription.Length
                };
            }

            // Step 9: Extract orders & prescriptions from transcription and save as drafts
            try
            {
                await _ExtractAndSaveOrdersAndPrescriptionsAsync(session, mergedTranscription, userId);
            }
            catch (Exception oxEx)
            {
                // Non-fatal — don't fail the whole session if order/rx extraction fails
                _logger.LogWarning(oxEx, "Session {SessionId}: Orders/prescriptions extraction failed (non-fatal)", sessionId);
            }

            // All done - note generation sends AllComplete progress via SignalR
            return new SaveAndFinishResultDto
            {
                Success = true,
                Status = "Completed",
                Message = "Session completed successfully",
                SessionId = sessionId,
                ChunkCount = chunks.Count,
                TotalDurationSeconds = totalDurationSeconds,
                TranscriptionLength = mergedTranscription.Length,
                ClinicalNoteIds = noteResult.ClinicalNoteId.HasValue
                    ? new List<int> { noteResult.ClinicalNoteId.Value }
                    : (noteResult.GeneratedNotes?.Select(n => n.ClinicalNoteId).ToList() ?? new List<int>())
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SaveAndFinish for session {SessionId}", sessionId);

            // Send error progress
            await _progressService.SendErrorAsync(userId, sessionId, ex.Message);

            // Update session status to failed
            session.RecordingStatus = "Failed";
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Audit log - error
            await _auditService.LogAccessAsync(
                userId,
                userIp,
                "SAVE_AND_FINISH_FAILED",
                "RecordingSession",
                sessionId,
                null,
                $"{{\"sessionId\":{sessionId},\"error\":\"{ex.Message.Replace("\"", "'")}\"}}");

            return new SaveAndFinishResultDto
            {
                Success = false,
                Status = "Failed",
                Message = "Failed to complete session: " + ex.Message,
                SessionId = sessionId
            };
        }
    }

    /// <summary>
    /// Wait for all chunk transcriptions to complete with polling.
    /// Times out after 10 minutes.
    /// </summary>
    private async Task<(bool Success, string Message)> WaitForChunkTranscriptionsAsync(
        int sessionId, int userId, string? userIp)
    {
        const int maxWaitTimeSeconds = 600; // 10 minutes
        const int pollIntervalSeconds = 3;
        var startTime = DateTime.UtcNow;

        while (true)
        {
            // Get chunk counts
            var totalChunks = await _context.TranscriptionChunks
                .CountAsync(c => c.SessionId == sessionId);

            var completedChunks = await _context.TranscriptionChunks
                .CountAsync(c => c.SessionId == sessionId &&
                    (c.TranscriptionStatus == "Completed" || c.TranscriptionStatus == "Failed"));

            var pendingChunks = totalChunks - completedChunks;

            _logger.LogInformation(
                "Session {SessionId}: Waiting for chunks - {Completed}/{Total} complete, {Pending} pending",
                sessionId, completedChunks, totalChunks, pendingChunks);

            // All chunks are done
            if (pendingChunks == 0)
            {
                var waitTime = (DateTime.UtcNow - startTime).TotalSeconds;

                // Audit log - all chunks ready
                await _auditService.LogAccessAsync(
                    userId, userIp, "CHUNKS_READY_FOR_MERGE",
                    "RecordingSession", sessionId, null,
                    $"{{\"sessionId\":{sessionId},\"totalChunks\":{totalChunks},\"waitTimeSeconds\":{waitTime:F2}}}");

                return (true, $"All {totalChunks} chunks ready");
            }

            // Check timeout
            var elapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
            if (elapsedSeconds >= maxWaitTimeSeconds)
            {
                var pendingChunkIds = await _context.TranscriptionChunks
                    .Where(c => c.SessionId == sessionId &&
                        c.TranscriptionStatus != "Completed" && c.TranscriptionStatus != "Failed")
                    .Select(c => c.ChunkId)
                    .ToListAsync();

                _logger.LogError(
                    "Session {SessionId}: Timeout waiting for chunks. Pending: {PendingChunkIds}",
                    sessionId, string.Join(",", pendingChunkIds));

                // Audit log - timeout
                await _auditService.LogAccessAsync(
                    userId, userIp, "CHUNK_WAIT_TIMEOUT",
                    "RecordingSession", sessionId, null,
                    $"{{\"sessionId\":{sessionId},\"pendingChunkIds\":[{string.Join(",", pendingChunkIds)}],\"waitTimeSeconds\":{elapsedSeconds:F2}}}");

                return (false, $"Timeout waiting for {pendingChunks} chunk(s) to transcribe. Please try again later.");
            }

            // Wait before next poll
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));
        }
    }

    /// <summary>
    /// Wait for all chunk transcriptions to complete with polling and SignalR progress updates.
    /// Times out after 10 minutes.
    /// </summary>
    private async Task<(bool Success, string Message)> WaitForChunkTranscriptionsWithProgressAsync(
        int sessionId, int userId, string? userIp, int totalChunks)
    {
        const int maxWaitTimeSeconds = 600; // 10 minutes
        const int pollIntervalSeconds = 3;
        var startTime = DateTime.UtcNow;
        var lastProgressSent = DateTime.MinValue;

        while (true)
        {
            // Get chunk counts
            var completedChunks = await _context.TranscriptionChunks
                .CountAsync(c => c.SessionId == sessionId &&
                    (c.TranscriptionStatus == "Completed" || c.TranscriptionStatus == "Failed"));

            var pendingChunks = totalChunks - completedChunks;

            _logger.LogInformation(
                "Session {SessionId}: Waiting for chunks - {Completed}/{Total} complete, {Pending} pending",
                sessionId, completedChunks, totalChunks, pendingChunks);

            // Send progress update every 5 seconds
            if ((DateTime.UtcNow - lastProgressSent).TotalSeconds >= 5)
            {
                await _progressService.SendWaitingForChunksAsync(userId, sessionId, completedChunks, totalChunks);
                lastProgressSent = DateTime.UtcNow;
            }

            // All chunks are done
            if (pendingChunks == 0)
            {
                var waitTime = (DateTime.UtcNow - startTime).TotalSeconds;

                // Audit log - all chunks ready
                await _auditService.LogAccessAsync(
                    userId, userIp, "CHUNKS_READY_FOR_MERGE",
                    "RecordingSession", sessionId, null,
                    $"{{\"sessionId\":{sessionId},\"totalChunks\":{totalChunks},\"waitTimeSeconds\":{waitTime:F2}}}");

                return (true, $"All {totalChunks} chunks ready");
            }

            // Check timeout
            var elapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
            if (elapsedSeconds >= maxWaitTimeSeconds)
            {
                var pendingChunkIds = await _context.TranscriptionChunks
                    .Where(c => c.SessionId == sessionId &&
                        c.TranscriptionStatus != "Completed" && c.TranscriptionStatus != "Failed")
                    .Select(c => c.ChunkId)
                    .ToListAsync();

                _logger.LogError(
                    "Session {SessionId}: Timeout waiting for chunks. Pending: {PendingChunkIds}",
                    sessionId, string.Join(",", pendingChunkIds));

                // Audit log - timeout
                await _auditService.LogAccessAsync(
                    userId, userIp, "CHUNK_WAIT_TIMEOUT",
                    "RecordingSession", sessionId, null,
                    $"{{\"sessionId\":{sessionId},\"pendingChunkIds\":[{string.Join(",", pendingChunkIds)}],\"waitTimeSeconds\":{elapsedSeconds:F2}}}");

                return (false, $"Timeout waiting for {pendingChunks} chunk(s) to transcribe. Please try again later.");
            }

            // Wait before next poll
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));
        }
    }

    /// <summary>
    /// Merge transcriptions from all chunks in sequence order.
    /// Decrypts each chunk, concatenates, then returns plain text (to be encrypted by caller).
    /// </summary>
    private async Task<string> MergeTranscriptionsAsync(List<TranscriptionChunk> chunks)
    {
        var transcriptionParts = new List<string>();

        foreach (var chunk in chunks.OrderBy(c => c.SequenceNumber))
        {
            if (!string.IsNullOrEmpty(chunk.TranscriptionText))
            {
                try
                {
                    var decrypted = _encryption.Decrypt(chunk.TranscriptionText);
                    if (!string.IsNullOrWhiteSpace(decrypted))
                    {
                        transcriptionParts.Add(decrypted.Trim());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to decrypt chunk {ChunkId} transcription, skipping",
                        chunk.ChunkId);
                }
            }
        }

        // Join with space to create natural flow
        var merged = string.Join(" ", transcriptionParts);

        _logger.LogInformation(
            "Merged {PartCount} transcription parts into {Length} characters",
            transcriptionParts.Count, merged.Length);

        return merged;
    }

    /// <summary>
    /// Merge audio files from all chunks in sequence order.
    /// Downloads chunks from cloud storage, decrypts, concatenates using ffmpeg, encrypts and uploads result.
    /// </summary>
    private async Task<string> MergeAudioFilesAsync(List<TranscriptionChunk> chunks, int tenantId)
    {
        var orderedChunks = chunks.OrderBy(c => c.SequenceNumber).ToList();
        var tempDecryptedFiles = new List<string>();

        try
        {
            // Step 1: Download and decrypt all chunk audio files from cloud storage
            foreach (var chunk in orderedChunks)
            {
                if (string.IsNullOrEmpty(chunk.ChunkAudioFileName))
                {
                    _logger.LogWarning("Chunk {ChunkId} has no audio file, skipping", chunk.ChunkId);
                    continue;
                }

                // Download encrypted file from cloud storage
                var encryptedBytes = await _storageService.DownloadBytesAsync(chunk.ChunkAudioFileName);
                if (encryptedBytes == null)
                {
                    _logger.LogWarning("Audio file not found in cloud storage for chunk {ChunkId}: {Path}",
                        chunk.ChunkId, chunk.ChunkAudioFileName);
                    continue;
                }

                // Decrypt and save to temp file for FFmpeg processing
                var decryptedBytes = DecryptFile(encryptedBytes);
                var tempDecryptedPath = Path.Combine(_tempPath, $"dec_{chunk.SequenceNumber}_{Guid.NewGuid():N}.wav");
                await File.WriteAllBytesAsync(tempDecryptedPath, decryptedBytes);
                tempDecryptedFiles.Add(tempDecryptedPath);
            }

            if (tempDecryptedFiles.Count == 0)
            {
                _logger.LogWarning("No audio files to merge");
                return string.Empty;
            }

            // Step 2: Create ffmpeg concat file list
            var concatListPath = Path.Combine(_tempPath, $"concat_{Guid.NewGuid():N}.txt");
            var concatContent = string.Join("\n", tempDecryptedFiles.Select(f => $"file '{f}'"));
            await File.WriteAllTextAsync(concatListPath, concatContent);

            // Step 3: Merge with ffmpeg
            var mergedTempPath = Path.Combine(_tempPath, $"merged_{Guid.NewGuid():N}.wav");
            var mergeResult = await MergeWithFfmpegAsync(concatListPath, mergedTempPath);

            if (!mergeResult)
            {
                throw new InvalidOperationException("FFmpeg merge failed");
            }

            // Step 4: Encrypt and upload merged file to cloud storage
            var plainBytes = await File.ReadAllBytesAsync(mergedTempPath);
            var encryptedMergedBytes = EncryptFile(plainBytes);

            var mergedFileName = _pathBuilder.GenerateEncryptedFileName("merged.wav");
            var folderPath = _pathBuilder.BuildMergedAudioPath(tenantId);
            var metadata = _metadataBuilder.BuildMergedAudioMetadata(
                tenantId,
                orderedChunks.First().SessionId,
                orderedChunks.Count,
                (int)orderedChunks.Sum(c => c.ChunkDurationSeconds ?? 0));

            var uploadResult = await _storageService.UploadBytesAsync(
                encryptedMergedBytes,
                mergedFileName,
                folderPath,
                "application/octet-stream",
                metadata);

            if (!uploadResult.Success)
            {
                throw new InvalidOperationException($"Failed to upload merged audio to cloud storage: {uploadResult.ErrorMessage}");
            }

            _logger.LogInformation(
                "Merged {FileCount} audio files, uploaded to cloud: {CloudPath}",
                tempDecryptedFiles.Count, uploadResult.CloudPath);

            // Cleanup
            CleanupTempFile(concatListPath);
            CleanupTempFile(mergedTempPath);

            return uploadResult.CloudPath;
        }
        finally
        {
            // Cleanup decrypted temp files
            foreach (var tempFile in tempDecryptedFiles)
            {
                CleanupTempFile(tempFile);
            }
        }
    }

    /// <summary>
    /// Decrypt file bytes using AES.
    /// </summary>
    private byte[] DecryptFile(byte[] encryptedBytes)
    {
        using var aes = Aes.Create();
        aes.Key = _fileEncryptionKey;

        // Read IV from the beginning
        var iv = new byte[16];
        Array.Copy(encryptedBytes, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var msDecrypt = new MemoryStream(encryptedBytes, 16, encryptedBytes.Length - 16);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var resultStream = new MemoryStream();

        csDecrypt.CopyTo(resultStream);
        return resultStream.ToArray();
    }

    /// <summary>
    /// Merge audio files using ffmpeg concat demuxer.
    /// </summary>
    private async Task<bool> MergeWithFfmpegAsync(string concatListPath, string outputPath)
    {
        var arguments = $"-f concat -safe 0 -i \"{concatListPath}\" -c copy -y \"{outputPath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        process.Start();

        var errorOutput = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg merge failed: {Error}", errorOutput);
            return false;
        }

        return true;
    }

    private string? SafeDecrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            return _encryption.Decrypt(value);
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// Generate a clinical note from the merged transcription using Gemini API.
    /// </summary>
    public async Task<GenerateClinicalNoteResultDto> GenerateClinicalNoteAsync(int sessionId, int userId, string? userIp, List<int>? selectedTemplateIds = null)
    {
        var startTime = DateTime.UtcNow;

        if (!_tenantProvider.TenantId.HasValue)
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Step 1: Get the session with appointment, patient, provider, and location info
        var session = await _context.RecordingSessions
            .Include(s => s.Appointment)
                .ThenInclude(a => a.Patient)
            .Include(s => s.Appointment)
                .ThenInclude(a => a.Provider)
            .Include(s => s.Appointment)
                .ThenInclude(a => a.Location)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId);

        if (session == null)
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Session not found"
            };
        }

        // Step 2: Verify session is completed and has transcription
        if (session.RecordingStatus != "Completed")
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = $"Session must be completed before generating clinical note. Current status: {session.RecordingStatus}"
            };
        }

        if (string.IsNullOrEmpty(session.CompleteTranscription))
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "No transcription available for this session"
            };
        }

        // Audit log - clinical note generation started
        await _auditService.LogAccessAsync(
            userId,
            userIp,
            "CLINICAL_NOTE_GENERATION_STARTED",
            "RecordingSession",
            sessionId,
            null,
            $"{{\"sessionId\":{sessionId},\"patientId\":{session.PatientId},\"appointmentId\":{session.AppointmentId}}}");

        try
        {
            // Step 3: Get ALL matching clinical note templates
            // Use the provider's current working location from JWT token (not appointment's location)
            var locationId = _locationProvider.LocationId;

            _logger.LogInformation(
                "Selecting templates for session {SessionId}: TenantId={TenantId}, LocationId={LocationId}, SelectedTemplateIds={SelectedIds}",
                sessionId, tenantId, locationId, selectedTemplateIds != null ? string.Join(",", selectedTemplateIds) : "none");

            var templates = await GetAllMatchingTemplatesAsync(tenantId, locationId, selectedTemplateIds);

            if (templates.Count == 0)
            {
                await _progressService.SendErrorAsync(userId, sessionId, "No suitable clinical note template found");
                return new GenerateClinicalNoteResultDto
                {
                    Success = false,
                    Message = "No suitable clinical note template found for this appointment type"
                };
            }

            // Send template selection progress
            await _progressService.SendSelectingTemplateAsync(userId, sessionId, templates.Count);

            _logger.LogInformation(
                "Found {TemplateCount} templates for session {SessionId}: {TemplateNames}",
                templates.Count, sessionId, string.Join(", ", templates.Select(t => t.Name)));

            // Step 4: Decrypt the CompleteTranscription
            string transcription;
            try
            {
                transcription = _encryption.Decrypt(session.CompleteTranscription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt transcription for session {SessionId}", sessionId);
                return new GenerateClinicalNoteResultDto
                {
                    Success = false,
                    Message = "Failed to decrypt transcription"
                };
            }

            if (string.IsNullOrWhiteSpace(transcription))
            {
                return new GenerateClinicalNoteResultDto
                {
                    Success = false,
                    Message = "Transcription is empty"
                };
            }

            // Step 5: Generate clinical notes for EACH template
            var serviceDate = DateOnly.FromDateTime(DateTime.Today);
            var appointmentType = session.Appointment?.Type ?? 0;

            // Gather encounter context (vitals, CC/HPI, history) for Scribe enhancement
            EncounterContextDto? encounterContext = null;
            if (session.PatientId > 0)
            {
                try
                {
                    // Find encounter for this appointment
                    int? encounterId = null;
                    if (session.AppointmentId > 0)
                    {
                        var encounter = await _context.Encounters
                            .AsNoTracking()
                            .FirstOrDefaultAsync(e => e.AppointmentId == session.AppointmentId);
                        encounterId = encounter?.EncounterId;
                    }

                    encounterContext = await _encounterContextService.GetEncounterContextAsync(
                        session.PatientId, encounterId);

                    if (encounterContext?.HasData == true)
                    {
                        _logger.LogInformation(
                            "Encounter context loaded for session {SessionId}: CC={HasCC}, Vitals={HasVitals}, Allergies={AllergyCount}, Meds={MedCount}",
                            sessionId,
                            !string.IsNullOrEmpty(encounterContext.ChiefComplaint),
                            encounterContext.Vitals != null,
                            encounterContext.ActiveAllergies.Count,
                            encounterContext.ActiveMedications.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load encounter context for session {SessionId}, proceeding without it", sessionId);
                    encounterContext = null;
                }
            }

            var generatedNotes = new List<GeneratedNoteInfo>();
            var currentNoteNumber = 0;

            foreach (var template in templates)
            {
                currentNoteNumber++;

                // Send progress: starting to generate this note
                await _progressService.SendGeneratingNoteAsync(userId, sessionId, currentNoteNumber, templates.Count, template.Name);

                _logger.LogInformation(
                    "Generating clinical note using template {TemplateId} ({TemplateName}) for session {SessionId}",
                    template.TemplateId, template.Name, sessionId);

                // Extract demographic information
                var patient = session.Appointment?.Patient;
                var provider = session.Appointment?.Provider;
                var location = session.Appointment?.Location;
                var appointmentDate = session.Appointment?.StartTime ?? DateTime.Now;

                // Decrypt patient information (encrypted for HIPAA compliance)
                var patientFirstName = patient != null && !string.IsNullOrEmpty(patient.FirstName)
                    ? SafeDecrypt(patient.FirstName) ?? "Unknown"
                    : "Unknown";
                var patientLastName = patient != null && !string.IsNullOrEmpty(patient.LastName)
                    ? SafeDecrypt(patient.LastName) ?? "Patient"
                    : "Patient";
                var patientDob = patient?.DateOfBirth ?? DateOnly.MinValue;

                // Decrypt provider information (encrypted for HIPAA compliance)
                var providerFirstName = provider != null && !string.IsNullOrEmpty(provider.FirstName)
                    ? SafeDecrypt(provider.FirstName) ?? "Unknown"
                    : "Unknown";
                var providerLastName = provider != null && !string.IsNullOrEmpty(provider.LastName)
                    ? SafeDecrypt(provider.LastName) ?? "Provider"
                    : "Provider";
                var providerCredentials = provider != null && !string.IsNullOrEmpty(provider.Credentials)
                    ? SafeDecrypt(provider.Credentials) ?? ""
                    : "";

                // Decrypt location name (encrypted for HIPAA compliance)
                var locationName = location != null && !string.IsNullOrEmpty(location.Name)
                    ? SafeDecrypt(location.Name) ?? "Unknown Location"
                    : "Unknown Location";

                // Call Gemini API to generate the clinical note with demographics and encounter context
                var generatedNote = await GenerateClinicalNoteWithGeminiAsync(
                    transcription,
                    template.HtmlContent,
                    template.Name,
                    patientFirstName,
                    patientLastName,
                    patientDob,
                    providerFirstName,
                    providerLastName,
                    providerCredentials,
                    appointmentDate,
                    locationName,
                    encounterContext);

                if (string.IsNullOrWhiteSpace(generatedNote))
                {
                    _logger.LogWarning(
                        "Failed to generate clinical note for template {TemplateId} ({TemplateName})",
                        template.TemplateId, template.Name);
                    continue; // Skip this template, try next one
                }

                // Encrypt the content for HIPAA compliance
                var encryptedContent = _encryption.Encrypt(generatedNote);

                var clinicalNote = new ClinicalNote
                {
                    TenantId = tenantId,
                    PatientId = session.PatientId,
                    ProviderId = session.ProviderId,
                    AppointmentId = session.AppointmentId,
                    TemplateId = template.TemplateId,
                    Type = appointmentType,
                    Status = 0, // Draft - provider must review and sign
                    ServiceDate = serviceDate,
                    HtmlContent = encryptedContent,
                    // PlainTextContent removed - redundant and was storing unencrypted PHI
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.ClinicalNotes.Add(clinicalNote);
                await _context.SaveChangesAsync();

                // Audit log - clinical note generated and saved as draft
                await _auditService.LogAccessAsync(
                    userId,
                    userIp,
                    "CLINICAL_NOTE_GENERATED",
                    "ClinicalNote",
                    clinicalNote.ClinicalNoteId,
                    null,
                    $"{{\"sessionId\":{sessionId},\"clinicalNoteId\":{clinicalNote.ClinicalNoteId},\"templateId\":{template.TemplateId},\"templateName\":\"{template.Name}\",\"transcriptionLength\":{transcription.Length},\"noteLength\":{generatedNote.Length}}}");

                generatedNotes.Add(new GeneratedNoteInfo
                {
                    ClinicalNoteId = clinicalNote.ClinicalNoteId,
                    TemplateId = template.TemplateId,
                    TemplateName = template.Name,
                    GeneratedNote = generatedNote,
                    NoteLength = generatedNote.Length
                });

                // Send progress: note complete
                await _progressService.SendNoteCompleteAsync(userId, sessionId, currentNoteNumber, templates.Count, clinicalNote.ClinicalNoteId);

                _logger.LogInformation(
                    "Clinical note generated for template {TemplateName}: ClinicalNoteId={ClinicalNoteId}, {NoteLength} chars",
                    template.Name, clinicalNote.ClinicalNoteId, generatedNote.Length);
            }

            if (generatedNotes.Count == 0)
            {
                await _progressService.SendErrorAsync(userId, sessionId, "Failed to generate clinical notes from any template");
                return new GenerateClinicalNoteResultDto
                {
                    Success = false,
                    Message = "Failed to generate clinical notes from any template"
                };
            }

            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
            var firstNote = generatedNotes.First();

            // Send all complete progress
            await _progressService.SendAllCompleteAsync(userId, sessionId, generatedNotes.Select(n => n.ClinicalNoteId).ToList());

            _logger.LogInformation(
                "Generated {NoteCount} clinical notes for session {SessionId} in {ProcessingTime}s",
                generatedNotes.Count, sessionId, processingTime);

            return new GenerateClinicalNoteResultDto
            {
                Success = true,
                Message = $"{generatedNotes.Count} clinical note(s) generated and saved as draft",
                SessionId = sessionId,
                TranscriptionLength = transcription.Length,
                GeneratedNotes = generatedNotes,
                // Legacy properties for backwards compatibility (first note)
                ClinicalNoteId = firstNote.ClinicalNoteId,
                TemplateId = firstNote.TemplateId,
                TemplateName = firstNote.TemplateName,
                GeneratedNote = firstNote.GeneratedNote,
                NoteLength = firstNote.NoteLength
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating clinical note for session {SessionId}", sessionId);

            // Send error progress
            await _progressService.SendErrorAsync(userId, sessionId, ex.Message);

            // Audit log - error
            await _auditService.LogAccessAsync(
                userId,
                userIp,
                "CLINICAL_NOTE_GENERATION_FAILED",
                "RecordingSession",
                sessionId,
                null,
                $"{{\"sessionId\":{sessionId},\"error\":\"{ex.Message.Replace("\"", "'")}\"}}");

            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Failed to generate clinical note: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Retry note generation for a session that previously failed.
    /// Only works for sessions with status "FailedNoteGeneration" or "Completed".
    /// </summary>
    public async Task<GenerateClinicalNoteResultDto> RetryNoteGenerationAsync(int sessionId, int userId, string? userIp)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Get the session
        var session = await _context.RecordingSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId);

        if (session == null)
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Session not found"
            };
        }

        // Validate session is in a state that allows retry
        var allowedStatuses = new[] { "FailedNoteGeneration", "Completed" };
        if (!allowedStatuses.Contains(session.RecordingStatus))
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = $"Cannot retry note generation for session with status '{session.RecordingStatus}'. Session must be in FailedNoteGeneration or Completed status."
            };
        }

        // Verify session has transcription
        if (string.IsNullOrEmpty(session.CompleteTranscription))
        {
            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "No transcription available for this session. Cannot retry note generation."
            };
        }

        // Audit log - retry started
        await _auditService.LogAccessAsync(
            userId,
            userIp,
            "NOTE_GENERATION_RETRY_STARTED",
            "RecordingSession",
            sessionId,
            $"{{\"previousStatus\":\"{session.RecordingStatus}\"}}",
            null);

        // Temporarily set status to Completed so GenerateClinicalNoteAsync will work
        var previousStatus = session.RecordingStatus;
        session.RecordingStatus = "Completed";
        await _context.SaveChangesAsync();

        try
        {
            // Call the existing note generation method
            var result = await GenerateClinicalNoteAsync(sessionId, userId, userIp);

            if (result.Success)
            {
                // Keep status as Completed on success
                _logger.LogInformation("Session {SessionId}: Note generation retry successful", sessionId);
            }
            else
            {
                // Restore to FailedNoteGeneration on failure
                session.RecordingStatus = "FailedNoteGeneration";
                session.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogWarning("Session {SessionId}: Note generation retry failed: {Message}", sessionId, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            // Restore to FailedNoteGeneration on exception
            session.RecordingStatus = "FailedNoteGeneration";
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogError(ex, "Session {SessionId}: Note generation retry exception", sessionId);

            return new GenerateClinicalNoteResultDto
            {
                Success = false,
                Message = "Failed to retry note generation: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Get the merged audio file for a session (decrypted, ready for playback).
    /// Returns null if no merged audio exists.
    /// </summary>
    public async Task<SessionAudioResultDto> GetSessionAudioAsync(int sessionId, int userId, string? userIp)
    {
        if (!_tenantProvider.TenantId.HasValue)
        {
            return new SessionAudioResultDto
            {
                Success = false,
                Message = "Tenant context required"
            };
        }

        var tenantId = _tenantProvider.TenantId.Value;

        // Get the session
        var session = await _context.RecordingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId);

        if (session == null)
        {
            return new SessionAudioResultDto
            {
                Success = false,
                Message = "Session not found"
            };
        }

        // Check if merged audio exists
        if (string.IsNullOrEmpty(session.MergedAudioFileName))
        {
            return new SessionAudioResultDto
            {
                Success = false,
                Message = "No merged audio available for this session"
            };
        }

        try
        {
            // Download encrypted file from cloud storage
            var encryptedBytes = await _storageService.DownloadBytesAsync(session.MergedAudioFileName);
            if (encryptedBytes == null)
            {
                return new SessionAudioResultDto
                {
                    Success = false,
                    Message = "Audio file not found in storage"
                };
            }

            // Decrypt the audio
            var decryptedBytes = DecryptFile(encryptedBytes);

            // Audit log - audio accessed
            await _auditService.LogAccessAsync(
                userId,
                userIp,
                "SESSION_AUDIO_ACCESSED",
                "RecordingSession",
                sessionId,
                null,
                $"{{\"sessionId\":{sessionId},\"patientId\":{session.PatientId},\"audioSize\":{decryptedBytes.Length}}}");

            return new SessionAudioResultDto
            {
                Success = true,
                Message = "Audio retrieved successfully",
                AudioData = decryptedBytes,
                ContentType = "audio/wav",
                DurationSeconds = session.TotalDurationSeconds ?? 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audio for session {SessionId}", sessionId);

            return new SessionAudioResultDto
            {
                Success = false,
                Message = "Failed to retrieve audio: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Get the best matching template for the given tenant and location.
    /// Priority: tenant-specific with location > tenant-specific without location > system templates
    /// </summary>
    private async Task<ClinicalNoteTemplate?> GetBestTemplateAsync(int tenantId, int? locationId)
    {
        // First try: Tenant-specific template with matching location
        if (locationId.HasValue)
        {
            var locationTemplate = await _context.ClinicalNoteTemplates
                .Where(t => t.TenantId == tenantId &&
                           t.LocationId == locationId.Value &&
                           t.IsActive)
                .OrderBy(t => t.SortOrder)
                .FirstOrDefaultAsync();

            if (locationTemplate != null)
                return locationTemplate;
        }

        // Second try: Tenant-specific template without location restriction
        var tenantTemplate = await _context.ClinicalNoteTemplates
            .Where(t => t.TenantId == tenantId &&
                       t.LocationId == null &&
                       t.IsActive)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefaultAsync();

        if (tenantTemplate != null)
            return tenantTemplate;

        // Third try: System template (TenantId == null)
        return await _context.ClinicalNoteTemplates
            .Where(t => t.TenantId == null &&
                       t.IsActive)
            .OrderBy(t => t.SortOrder)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get ALL matching templates for the given tenant and location.
    /// If selectedTemplateIds is provided, returns only those specific templates (provider chose them).
    /// Otherwise falls back to tenant + location matching.
    /// </summary>
    private async Task<List<ClinicalNoteTemplate>> GetAllMatchingTemplatesAsync(int tenantId, int? locationId, List<int>? selectedTemplateIds = null)
    {
        // If provider explicitly selected templates, use those directly
        if (selectedTemplateIds != null && selectedTemplateIds.Count > 0)
        {
            return await _context.ClinicalNoteTemplates
                .Where(t => selectedTemplateIds.Contains(t.TemplateId) && t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ToListAsync();
        }

        // Fallback: tenant + location matching (no appointment type filter)
        var templates = new List<ClinicalNoteTemplate>();

        // First: Tenant-specific templates with matching location
        if (locationId.HasValue)
        {
            var locationTemplates = await _context.ClinicalNoteTemplates
                .Where(t => t.TenantId == tenantId &&
                           t.LocationId == locationId.Value &&
                           t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ToListAsync();

            templates.AddRange(locationTemplates);
        }

        // Second: Tenant-specific templates without location restriction (that aren't already added)
        var tenantTemplates = await _context.ClinicalNoteTemplates
            .Where(t => t.TenantId == tenantId &&
                       t.LocationId == null &&
                       t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync();

        foreach (var t in tenantTemplates)
        {
            if (!templates.Any(existing => existing.TemplateId == t.TemplateId))
                templates.Add(t);
        }

        // If no tenant-specific templates found, try system templates
        if (templates.Count == 0)
        {
            var systemTemplates = await _context.ClinicalNoteTemplates
                .Where(t => t.TenantId == null &&
                           t.IsActive)
                .OrderBy(t => t.SortOrder)
                .ToListAsync();

            templates.AddRange(systemTemplates);
        }

        return templates;
    }

    /// <summary>
    /// Generate a clinical note using Gemini API based on transcription, template, and encounter context.
    /// Encounter context (vitals, CC/HPI, history) is included as supporting data so Gemini can
    /// produce a more complete note even if the doctor doesn't mention everything during dictation.
    /// </summary>
    private async Task<string> GenerateClinicalNoteWithGeminiAsync(
        string transcription,
        string templateHtml,
        string templateName,
        string patientFirstName,
        string patientLastName,
        DateOnly patientDob,
        string providerFirstName,
        string providerLastName,
        string providerCredentials,
        DateTime appointmentDate,
        string locationName,
        EncounterContextDto? encounterContext = null)
    {
        // Build encounter context section if available
        var encounterDataSection = "";
        if (encounterContext?.HasData == true)
        {
            encounterDataSection = $@"

ENCOUNTER DATA (already collected by nursing staff for this visit):
{encounterContext.ToPromptText()}
NOTE: The above encounter data was collected during intake. Include relevant items (vitals, CC, HPI, allergies, medications, problems) in the appropriate note sections. The transcription takes priority — if the provider mentions something different from the encounter data, use the provider's dictation.
";
        }

        // Build the prompt for clinical note generation with patient demographics and encounter context
        var prompt = $@"You are a board-certified medical documentation specialist generating LEGAL clinical documentation.
Based on the following provider-patient conversation transcription, generate a clinical note following the template structure provided.

PATIENT DEMOGRAPHICS:
- Patient Name: {patientFirstName} {patientLastName}
- Date of Birth: {patientDob:MM/dd/yyyy}

PROVIDER INFORMATION:
- Provider Name: {providerFirstName} {providerLastName}, {providerCredentials}

APPOINTMENT INFORMATION:
- Appointment Date: {appointmentDate:MM/dd/yyyy}
- Appointment Time: {appointmentDate:hh:mm tt}
- Location/Facility: {locationName}
{encounterDataSection}
TEMPLATE: {templateName}
TEMPLATE STRUCTURE:
{templateHtml}

TRANSCRIPTION:
{transcription}

INSTRUCTIONS:
1. Fill in the template sections based on information from the transcription.
2. Keep the HTML structure from the template intact.
3. Fill in only sections where relevant information is found in the transcription.
4. For sections without relevant information from the transcription, use encounter data if available for that section (vitals, CC, HPI, allergies, medications, active problems).
5. Maintain HIPAA compliance — use only information from the transcription, demographics, and encounter data provided.
6. Be concise but thorough — this will be reviewed by the provider before signing.
7. Include specific measurements, observations, and patient statements when mentioned.
8. DO NOT fabricate information not present in any of the provided data sources.
9. Return ONLY the filled-in HTML content, no additional commentary.

STRICT MEDICAL TERMINOLOGY — CRITICAL (this is a LEGAL medical document):
- ALL text in the clinical note MUST use formal medical/clinical terminology. This is a legal document that becomes part of the patient's permanent medical record.
- Convert ALL casual, colloquial, or layperson language from the transcription into proper medical terminology.
- Examples: ""bad headache"" → ""severe cephalgia"", ""sugar problem"" → ""diabetes mellitus"", ""blood pressure is high"" → ""hypertension noted"", ""can't sleep"" → ""reports insomnia"", ""stomach hurts"" → ""abdominal pain"", ""feeling dizzy"" → ""reports vertigo/dizziness"", ""trouble breathing"" → ""dyspnea"".
- Use standard medical abbreviations where appropriate (BP, HR, RR, SpO2, BMI, etc.).
- Write in third-person clinical prose (""Patient reports..."", ""Provider observed...""), NEVER use first person.
- NEVER include casual conversation, small talk, greetings, or non-clinical dialogue in the note.

MEDICATION HANDLING — CRITICAL:
- EXISTING MEDICATIONS from encounter data are already on file from prior visits. Do NOT remove, modify, or overwrite them.
- Only include medications that are NEWLY mentioned in THIS transcription as additions.
- If a medication from the transcription matches an existing medication (even with different spelling due to speech recognition), it is NOT new — do not duplicate it.
- FUZZY-MATCH drug names: Speech recognition often misspells medications. Match phonetically similar names to known drugs. Examples: ""Neuro-L"" or ""neural"" → likely ""Neurol Forte"", ""metforming"" → ""Metformin"", ""a moxicillin"" → ""Amoxicillin"", ""lip itor"" → ""Lipitor"", ""lie-sin-o-pril"" → ""Lisinopril"".
- When referencing medications, always use the correct standardized drug name, not the speech-recognized version.

PROTECTING EXISTING DATA — CRITICAL:
- The ENCOUNTER DATA section contains previously recorded information (from intake, prior visits, or nursing staff).
- NEVER overwrite, remove, or contradict existing encounter data unless the provider EXPLICITLY corrects it in the transcription.
- If the transcription mentions something already captured in encounter data, use the encounter data version (it was entered accurately) unless the provider explicitly states a correction.
- For medication lists, allergy lists, and problem lists: only ADD new items from the transcription. Never remove existing items.

Generate the clinical note now:";

        // Use centralized GeminiService with extended thinking for better clinical note generation
        var response = await _geminiService.GenerateTextAsync(prompt, useExtendedThinking: true);

        if (!response.Success)
        {
            _logger.LogError("API error for clinical note generation: {Error}", response.ErrorMessage);
            throw new InvalidOperationException($"API error: {response.ErrorMessage}");
        }

        return response.Text ?? "";
    }

    /// <summary>
    /// Strip HTML tags from content to create plain text version.
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ").Trim();
    }

    /// <summary>
    /// Generate a clinical note from raw telehealth transcription text (no RecordingSession required).
    /// </summary>
    public async Task<GenerateClinicalNoteResultDto> GenerateNoteFromTranscriptionAsync(
        int encounterId, string transcription, int userId, string? userIp, List<int>? selectedTemplateIds = null)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return new GenerateClinicalNoteResultDto { Success = false, Message = "Tenant context required" };

        if (string.IsNullOrWhiteSpace(transcription))
            return new GenerateClinicalNoteResultDto { Success = false, Message = "Transcription is empty" };

        var tenantId = _tenantProvider.TenantId.Value;

        // Load encounter with appointment, patient, provider, location
        var encounter = await _context.Encounters
            .Include(e => e.Appointment)
                .ThenInclude(a => a!.Patient)
            .Include(e => e.Appointment)
                .ThenInclude(a => a!.Provider)
            .Include(e => e.Appointment)
                .ThenInclude(a => a!.Location)
            .FirstOrDefaultAsync(e => e.EncounterId == encounterId && e.TenantId == tenantId);

        if (encounter == null)
            return new GenerateClinicalNoteResultDto { Success = false, Message = "Encounter not found" };

        var appointment = encounter.Appointment;
        var patient = appointment?.Patient;
        var provider = appointment?.Provider;
        var location = appointment?.Location;

        if (patient == null || provider == null)
            return new GenerateClinicalNoteResultDto { Success = false, Message = "Patient or provider not found for this encounter" };

        // Audit log
        await _auditService.LogAccessAsync(userId, userIp, "TELEHEALTH_NOTE_GENERATION_STARTED",
            "Encounter", encounterId, null,
            $"{{\"encounterId\":{encounterId},\"patientId\":{encounter.PatientId},\"transcriptionLength\":{transcription.Length}}}");

        try
        {
            // Get templates
            var locationId = _locationProvider.LocationId;
            var templates = await GetAllMatchingTemplatesAsync(tenantId, locationId, selectedTemplateIds);

            if (templates.Count == 0)
                return new GenerateClinicalNoteResultDto { Success = false, Message = "No suitable clinical note template found" };

            // Get encounter context
            EncounterContextDto? encounterContext = null;
            try
            {
                encounterContext = await _encounterContextService.GetEncounterContextAsync(encounter.PatientId, encounterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load encounter context for telehealth note, proceeding without it");
            }

            // Decrypt demographics
            var patientFirstName = SafeDecrypt(patient.FirstName) ?? "Unknown";
            var patientLastName = SafeDecrypt(patient.LastName) ?? "Patient";
            var patientDob = patient.DateOfBirth;
            var providerFirstName = SafeDecrypt(provider.FirstName) ?? "Unknown";
            var providerLastName = SafeDecrypt(provider.LastName) ?? "Provider";
            var providerCredentials = SafeDecrypt(provider.Credentials) ?? "";
            var locationName = location != null ? (SafeDecrypt(location.Name) ?? "Unknown Location") : "Unknown Location";
            var appointmentDate = appointment?.StartTime ?? DateTime.Now;
            var serviceDate = DateOnly.FromDateTime(DateTime.Today);
            var appointmentType = appointment?.Type ?? 0;

            var generatedNotes = new List<GeneratedNoteInfo>();

            foreach (var template in templates)
            {
                var generatedNote = await GenerateClinicalNoteWithGeminiAsync(
                    transcription, template.HtmlContent, template.Name,
                    patientFirstName, patientLastName, patientDob,
                    providerFirstName, providerLastName, providerCredentials,
                    appointmentDate, locationName, encounterContext);

                if (string.IsNullOrWhiteSpace(generatedNote)) continue;

                var encryptedContent = _encryption.Encrypt(generatedNote);
                var clinicalNote = new ClinicalNote
                {
                    TenantId = tenantId,
                    PatientId = encounter.PatientId,
                    ProviderId = encounter.ProviderId,
                    AppointmentId = encounter.AppointmentId ?? 0,
                    EncounterId = encounterId,
                    TemplateId = template.TemplateId,
                    Type = appointmentType,
                    Status = 0, // Draft
                    ServiceDate = serviceDate,
                    HtmlContent = encryptedContent,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.ClinicalNotes.Add(clinicalNote);
                await _context.SaveChangesAsync();

                await _auditService.LogAccessAsync(userId, userIp, "TELEHEALTH_NOTE_GENERATED",
                    "ClinicalNote", clinicalNote.ClinicalNoteId, null,
                    $"{{\"encounterId\":{encounterId},\"clinicalNoteId\":{clinicalNote.ClinicalNoteId},\"templateId\":{template.TemplateId},\"noteLength\":{generatedNote.Length}}}");

                generatedNotes.Add(new GeneratedNoteInfo
                {
                    ClinicalNoteId = clinicalNote.ClinicalNoteId,
                    TemplateId = template.TemplateId,
                    TemplateName = template.Name,
                    GeneratedNote = generatedNote,
                    NoteLength = generatedNote.Length
                });
            }

            if (generatedNotes.Count == 0)
                return new GenerateClinicalNoteResultDto { Success = false, Message = "Failed to generate clinical notes from any template" };

            var first = generatedNotes[0];
            return new GenerateClinicalNoteResultDto
            {
                Success = true,
                Message = $"Generated {generatedNotes.Count} clinical note(s) from telehealth session",
                TranscriptionLength = transcription.Length,
                GeneratedNotes = generatedNotes,
                ClinicalNoteId = first.ClinicalNoteId,
                TemplateId = first.TemplateId,
                TemplateName = first.TemplateName,
                GeneratedNote = first.GeneratedNote,
                NoteLength = first.NoteLength
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate telehealth note for encounter {EncounterId}", encounterId);
            return new GenerateClinicalNoteResultDto { Success = false, Message = "An error occurred generating the clinical note" };
        }
    }

    /// <inheritdoc />
    public async Task<List<MatchingTemplateDto>> GetMatchingTemplatesForEncounterAsync(int encounterId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return new List<MatchingTemplateDto>();

        var tenantId = _tenantProvider.TenantId.Value;
        var locationId = _locationProvider.LocationId;

        var templates = await GetAllMatchingTemplatesAsync(tenantId, locationId);

        return templates.Select(t => new MatchingTemplateDto
        {
            TemplateId = t.TemplateId,
            TemplateName = t.Name
        }).ToList();
    }

    /// <inheritdoc />
    public async Task GenerateNotesInBackgroundAsync(
        string jobId, int encounterId, string transcription, int userId, string? userIp, List<int> selectedTemplateIds)
    {
        try
        {
            if (!_tenantProvider.TenantId.HasValue)
            {
                _jobService.FailJob(jobId, "Tenant context required");
                return;
            }

            var tenantId = _tenantProvider.TenantId.Value;

            var encounter = await _context.Encounters
                .Include(e => e.Appointment)
                    .ThenInclude(a => a!.Patient)
                .Include(e => e.Appointment)
                    .ThenInclude(a => a!.Provider)
                .Include(e => e.Appointment)
                    .ThenInclude(a => a!.Location)
                .FirstOrDefaultAsync(e => e.EncounterId == encounterId && e.TenantId == tenantId);

            if (encounter == null)
            {
                _jobService.FailJob(jobId, "Encounter not found");
                return;
            }

            var appointment = encounter.Appointment;
            var patient = appointment?.Patient;
            var provider = appointment?.Provider;
            var location = appointment?.Location;

            if (patient == null || provider == null)
            {
                _jobService.FailJob(jobId, "Patient or provider not found");
                return;
            }

            await _auditService.LogAccessAsync(userId, userIp, "TELEHEALTH_NOTE_GENERATION_STARTED",
                "Encounter", encounterId, null,
                $"{{\"encounterId\":{encounterId},\"patientId\":{encounter.PatientId},\"transcriptionLength\":{transcription.Length},\"jobId\":\"{jobId}\"}}");

            var templates = await GetAllMatchingTemplatesAsync(tenantId, _locationProvider.LocationId, selectedTemplateIds);
            if (templates.Count == 0)
            {
                _jobService.FailJob(jobId, "No suitable clinical note templates found");
                return;
            }

            EncounterContextDto? encounterContext = null;
            try
            {
                encounterContext = await _encounterContextService.GetEncounterContextAsync(encounter.PatientId, encounterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load encounter context for background note gen, proceeding without it");
            }

            var patientFirstName = SafeDecrypt(patient.FirstName) ?? "Unknown";
            var patientLastName = SafeDecrypt(patient.LastName) ?? "Patient";
            var patientDob = patient.DateOfBirth;
            var providerFirstName = SafeDecrypt(provider.FirstName) ?? "Unknown";
            var providerLastName = SafeDecrypt(provider.LastName) ?? "Provider";
            var providerCredentials = SafeDecrypt(provider.Credentials) ?? "";
            var locationName = location != null ? (SafeDecrypt(location.Name) ?? "Unknown Location") : "Unknown Location";
            var appointmentDate = appointment?.StartTime ?? DateTime.Now;
            var serviceDate = DateOnly.FromDateTime(DateTime.Today);
            var appointmentType = appointment?.Type ?? 0;

            // Generate notes in parallel
            var completedCount = 0;
            var semaphore = new SemaphoreSlim(3); // Max 3 concurrent Gemini calls

            var tasks = templates.Select(async template =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var generatedNote = await GenerateClinicalNoteWithGeminiAsync(
                        transcription, template.HtmlContent, template.Name,
                        patientFirstName, patientLastName, patientDob,
                        providerFirstName, providerLastName, providerCredentials,
                        appointmentDate, locationName, encounterContext);

                    if (string.IsNullOrWhiteSpace(generatedNote)) return;

                    var encryptedContent = _encryption.Encrypt(generatedNote);
                    var clinicalNote = new ClinicalNote
                    {
                        TenantId = tenantId,
                        PatientId = encounter.PatientId,
                        ProviderId = encounter.ProviderId,
                        AppointmentId = encounter.AppointmentId ?? 0,
                        EncounterId = encounterId,
                        TemplateId = template.TemplateId,
                        Type = appointmentType,
                        Status = 0,
                        ServiceDate = serviceDate,
                        HtmlContent = encryptedContent,
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.ClinicalNotes.Add(clinicalNote);
                    await _context.SaveChangesAsync();

                    await _auditService.LogAccessAsync(userId, userIp, "TELEHEALTH_NOTE_GENERATED",
                        "ClinicalNote", clinicalNote.ClinicalNoteId, null,
                        $"{{\"encounterId\":{encounterId},\"clinicalNoteId\":{clinicalNote.ClinicalNoteId},\"templateId\":{template.TemplateId},\"jobId\":\"{jobId}\"}}");

                    var count = Interlocked.Increment(ref completedCount);
                    _jobService.UpdateProgress(jobId, count, new NoteGenerationNoteResult
                    {
                        ClinicalNoteId = clinicalNote.ClinicalNoteId,
                        TemplateId = template.TemplateId,
                        TemplateName = template.Name
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate note for template {TemplateId} in job {JobId}", template.TemplateId, jobId);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (completedCount > 0)
                _jobService.CompleteJob(jobId);
            else
                _jobService.FailJob(jobId, "Failed to generate any clinical notes");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background note generation failed for job {JobId}", jobId);
            _jobService.FailJob(jobId, "An unexpected error occurred during note generation");
        }
    }

    /// <summary>
    /// Extract orders and prescriptions from the transcription and save as Draft records.
    /// Called after note generation succeeds. Non-fatal — errors are logged but don't fail the session.
    /// </summary>
    private async Task _ExtractAndSaveOrdersAndPrescriptionsAsync(RecordingSession session, string transcription, int userId)
    {
        if (string.IsNullOrWhiteSpace(transcription)) return;

        var tenantId = _tenantProvider.TenantId!.Value;

        // Find encounter for this appointment
        int? encounterId = null;
        if (session.AppointmentId > 0)
        {
            var encounter = await _context.Encounters
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.AppointmentId == session.AppointmentId && e.TenantId == tenantId);
            encounterId = encounter?.EncounterId;
        }

        _logger.LogInformation("Session {SessionId}: Extracting orders/prescriptions from transcription ({Length} chars)",
            session.SessionId, transcription.Length);

        // Build extraction prompt with all enum values so Gemini returns correct ints
        var prompt = $@"You are a medical scribe AI. Extract any orders and prescriptions mentioned in this clinical encounter transcription.

TRANSCRIPTION:
{transcription}

Return ONLY a JSON object with two arrays: ""orders"" and ""prescriptions"".

ORDERS — extract any lab tests, imaging studies, or referrals the provider mentions ordering.
Each order object:
{{
  ""orderType"": integer (0=Lab, 1=Imaging, 2=Referral),
  ""labPanelName"": string or null (for Lab orders — only if the provider explicitly names a specific lab test),
  ""modality"": integer or null (for Imaging: 0=X-Ray, 1=CT, 2=MRI, 3=Ultrasound, 4=Mammogram, 5=DEXA, 6=Fluoroscopy, 7=PET, 8=Nuclear),
  ""bodyPart"": string or null (for Imaging: e.g. ""Chest"", ""Abdomen"", ""Left Knee"", ""Lumbar Spine""),
  ""contrastRequired"": boolean or null (for Imaging),
  ""referralSpecialty"": string or null (for Referral: e.g. ""Cardiology"", ""Orthopedics"", ""Neurology"", ""Dermatology"", ""ENT"", ""Gastroenterology""),
  ""referralReason"": string or null (for Referral),
  ""priority"": integer (0=Routine, 1=Urgent, 2=STAT — default 0),
  ""clinicalIndication"": string or null (reason for the order),
  ""diagnosisCode"": string or null (ICD-10 code if you can determine it),
  ""notes"": string or null
}}

PRESCRIPTIONS — extract any medications the provider prescribes or mentions starting/changing.
Each prescription object:
{{
  ""drugName"": string (brand or generic name — use proper standardized drug name),
  ""genericName"": string or null,
  ""strength"": string or null (e.g. ""500mg"", ""10mg"", ""250mg/5ml""),
  ""dosageForm"": integer (0=Tablet, 1=Capsule, 2=Liquid, 3=Cream, 4=Ointment, 5=Patch, 6=Injection, 7=Inhaler, 8=Drops, 9=Suppository, 99=Other — default 0),
  ""doseAmount"": string (e.g. ""1"", ""2"", ""0.5"" — default ""1""),
  ""doseUnit"": string (e.g. ""tablet"", ""capsule"", ""ml"", ""puff"" — match dosageForm),
  ""route"": integer (0=Oral, 1=Topical, 2=Subcutaneous, 3=IM, 4=IV, 5=Rectal, 6=Ophthalmic, 7=Otic, 8=Nasal, 9=Transdermal, 10=Inhalation — default 0),
  ""frequency"": integer (0=Daily, 1=BID, 2=TID, 3=QID, 4=QHS, 5=Q4H, 6=Q6H, 7=Q8H, 8=Q12H, 9=PRN, 10=Weekly, 11=BiWeekly, 12=Monthly, 99=AsDirected — default 0),
  ""directionsFreeText"": string (full SIG text, e.g. ""Take 1 tablet by mouth twice daily for 7 days""),
  ""quantity"": number (total quantity to dispense — calculate from frequency × days if possible, default 30),
  ""daysSupply"": integer (default 30),
  ""refills"": integer (0=no refills, default 0),
  ""pharmacyName"": string or null (if pharmacy is mentioned),
  ""pharmacyPhone"": string or null,
  ""pharmacyAddress"": string or null,
  ""diagnosisCode"": string or null (ICD-10 if relevant),
  ""notes"": string or null
}}

RULES:
1. Only extract orders/prescriptions that the provider EXPLICITLY mentions ordering or prescribing. Do not infer or fabricate.
2. Do NOT include existing medications the patient is already taking — only NEW prescriptions.
3. If no orders or prescriptions are mentioned, return empty arrays.
4. Use standardized drug names (""Amoxicillin"" not ""amoxcillin"", ""Lisinopril"" not ""lisinapril"").
5. Calculate quantity when possible: if ""twice daily for 7 days"" then quantity = 14.

Return ONLY valid JSON, no markdown code fences.";

        var response = await _geminiService.GenerateTextAsync(prompt, 0.1);

        if (!response.Success || string.IsNullOrWhiteSpace(response.Text))
        {
            _logger.LogWarning("Session {SessionId}: Orders/prescriptions extraction returned no data", session.SessionId);
            return;
        }

        // Clean and parse JSON
        var jsonText = response.Text.Trim();
        if (jsonText.StartsWith("```")) jsonText = jsonText.Split('\n', 2).Length > 1 ? jsonText.Split('\n', 2)[1] : jsonText;
        if (jsonText.EndsWith("```")) jsonText = jsonText[..^3];
        jsonText = jsonText.Trim();

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var today = DateOnly.FromDateTime(DateTime.Today);
            int orderCount = 0, rxCount = 0;

            // Save orders
            if (root.TryGetProperty("orders", out var ordersEl) && ordersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in ordersEl.EnumerateArray())
                {
                    var order = new Order
                    {
                        TenantId = tenantId,
                        PatientId = session.PatientId,
                        ProviderId = session.ProviderId,
                        EncounterId = encounterId,
                        OrderType = o.TryGetProperty("orderType", out var ot) ? ot.GetInt32() : 0,
                        Status = 0, // Draft
                        Priority = o.TryGetProperty("priority", out var pr) ? pr.GetInt32() : 0,
                        OrderDate = today,
                        DiagnosisCode = o.TryGetProperty("diagnosisCode", out var dc) && dc.ValueKind == JsonValueKind.String ? dc.GetString() : null,
                        ClinicalIndication = o.TryGetProperty("clinicalIndication", out var ci) && ci.ValueKind == JsonValueKind.String ? ci.GetString() : null,
                        Notes = o.TryGetProperty("notes", out var on) && on.ValueKind == JsonValueKind.String ? on.GetString() : null,
                        LabPanelName = o.TryGetProperty("labPanelName", out var lp) && lp.ValueKind == JsonValueKind.String ? lp.GetString() : null,
                        Modality = o.TryGetProperty("modality", out var mod) && mod.ValueKind == JsonValueKind.Number ? mod.GetInt32() : null,
                        BodyPart = o.TryGetProperty("bodyPart", out var bp) && bp.ValueKind == JsonValueKind.String ? bp.GetString() : null,
                        ContrastRequired = o.TryGetProperty("contrastRequired", out var cr) && cr.ValueKind != JsonValueKind.Null ? cr.GetBoolean() : null,
                        ReferralSpecialty = o.TryGetProperty("referralSpecialty", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString() : null,
                        ReferralReason = o.TryGetProperty("referralReason", out var rr) && rr.ValueKind == JsonValueKind.String ? rr.GetString() : null,
                        CreatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Orders.Add(order);
                    orderCount++;
                }
            }

            // Save prescriptions
            if (root.TryGetProperty("prescriptions", out var rxEl) && rxEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in rxEl.EnumerateArray())
                {
                    var rx = new Prescription
                    {
                        TenantId = tenantId,
                        PatientId = session.PatientId,
                        ProviderId = session.ProviderId,
                        EncounterId = encounterId,
                        DrugName = p.TryGetProperty("drugName", out var dn) && dn.ValueKind == JsonValueKind.String ? dn.GetString() : "Unknown",
                        GenericName = p.TryGetProperty("genericName", out var gn) && gn.ValueKind == JsonValueKind.String ? gn.GetString() : null,
                        Strength = p.TryGetProperty("strength", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null,
                        DosageForm = p.TryGetProperty("dosageForm", out var df) && df.ValueKind == JsonValueKind.Number ? df.GetInt32() : 0,
                        DoseAmount = p.TryGetProperty("doseAmount", out var da) && da.ValueKind == JsonValueKind.String ? da.GetString() : "1",
                        DoseUnit = p.TryGetProperty("doseUnit", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() : "tablet",
                        Route = p.TryGetProperty("route", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt32() : 0,
                        Frequency = p.TryGetProperty("frequency", out var fr) && fr.ValueKind == JsonValueKind.Number ? fr.GetInt32() : 0,
                        DirectionsFreeText = p.TryGetProperty("directionsFreeText", out var sig) && sig.ValueKind == JsonValueKind.String ? sig.GetString() : "As directed",
                        Quantity = p.TryGetProperty("quantity", out var qty) && qty.ValueKind == JsonValueKind.Number ? qty.GetDecimal() : 30,
                        DaysSupply = p.TryGetProperty("daysSupply", out var ds) && ds.ValueKind == JsonValueKind.Number ? ds.GetInt32() : 30,
                        Refills = p.TryGetProperty("refills", out var ref_) && ref_.ValueKind == JsonValueKind.Number ? ref_.GetInt32() : 0,
                        PharmacyName = p.TryGetProperty("pharmacyName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null,
                        PharmacyPhone = p.TryGetProperty("pharmacyPhone", out var pp) && pp.ValueKind == JsonValueKind.String ? pp.GetString() : null,
                        PharmacyAddress = p.TryGetProperty("pharmacyAddress", out var pa) && pa.ValueKind == JsonValueKind.String ? pa.GetString() : null,
                        Status = 0, // Draft
                        DiagnosisCode = p.TryGetProperty("diagnosisCode", out var rxDc) && rxDc.ValueKind == JsonValueKind.String ? rxDc.GetString() : null,
                        Notes = p.TryGetProperty("notes", out var rxN) && rxN.ValueKind == JsonValueKind.String ? rxN.GetString() : null,
                        PrescribedDate = today,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Prescriptions.Add(rx);
                    rxCount++;
                }
            }

            if (orderCount > 0 || rxCount > 0)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Session {SessionId}: Saved {OrderCount} draft orders and {RxCount} draft prescriptions",
                    session.SessionId, orderCount, rxCount);
            }
            else
            {
                _logger.LogInformation("Session {SessionId}: No orders or prescriptions found in transcription", session.SessionId);
            }
        }
        catch (JsonException jex)
        {
            _logger.LogWarning(jex, "Session {SessionId}: Failed to parse orders/prescriptions JSON", session.SessionId);
        }
    }

}

/// <summary>
/// Result DTO for chunk processing
/// </summary>
public class TranscriptionChunkResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? ChunkId { get; set; }
    public string? TranscriptionStatus { get; set; }
}

/// <summary>
/// DTO for transcription chunk data
/// </summary>
public class TranscriptionChunkDto
{
    public int ChunkId { get; set; }
    public int SessionId { get; set; }
    public int SequenceNumber { get; set; }
    public decimal? ChunkDurationSeconds { get; set; }
    public DateTime RecordedTimestamp { get; set; }
    public string? TranscriptionText { get; set; }
    public string TranscriptionStatus { get; set; } = string.Empty;
    public DateTime? TranscriptionCompletedAt { get; set; }
    public string? TranscriptionError { get; set; }
}

/// <summary>
/// Request DTO for uploading a chunk
/// </summary>
public class UploadChunkRequest
{
    public int SessionId { get; set; }
    public int SequenceNumber { get; set; }
    public decimal DurationSeconds { get; set; }
    public DateTime RecordedTimestamp { get; set; }
}

/// <summary>
/// Result DTO for SaveAndFinish operation
/// </summary>
public class SaveAndFinishRequestDto
{
    /// <summary>
    /// Template IDs selected by the provider before recording.
    /// If provided, only these templates will be used for note generation.
    /// </summary>
    public List<int>? SelectedTemplateIds { get; set; }
}

public class SaveAndFinishResultDto
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? SessionId { get; set; }
    public int? ChunkCount { get; set; }
    public int? TotalDurationSeconds { get; set; }
    public int? TranscriptionLength { get; set; }
    /// <summary>
    /// Clinical note IDs generated after successful transcription merge
    /// </summary>
    public List<int>? ClinicalNoteIds { get; set; }
}

/// <summary>
/// Result DTO for GenerateClinicalNote operation
/// </summary>
public class GenerateClinicalNoteResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? SessionId { get; set; }
    public int? TranscriptionLength { get; set; }

    /// <summary>
    /// List of generated clinical notes (one per matching template)
    /// </summary>
    public List<GeneratedNoteInfo> GeneratedNotes { get; set; } = new List<GeneratedNoteInfo>();

    // Legacy single note properties for backwards compatibility
    public int? ClinicalNoteId { get; set; }
    public int? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public string? GeneratedNote { get; set; }
    public int? NoteLength { get; set; }
}

/// <summary>
/// Information about a single generated clinical note
/// </summary>
public class GeneratedNoteInfo
{
    public int ClinicalNoteId { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string GeneratedNote { get; set; } = string.Empty;
    public int NoteLength { get; set; }
}

/// <summary>
/// Result DTO for session audio retrieval
/// </summary>
public class SessionAudioResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public byte[]? AudioData { get; set; }
    public string ContentType { get; set; } = "audio/wav";
    public int DurationSeconds { get; set; }
}

/// <summary>
/// Lightweight DTO for template selection UI
/// </summary>
public class MatchingTemplateDto
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
}
