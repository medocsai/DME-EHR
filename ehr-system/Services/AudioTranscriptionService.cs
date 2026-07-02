namespace EHR.Services;

/// <summary>
/// AudioTranscriptionService -- the Independent Contractor for "audio in, raw text out."
///
/// Why it exists:
///   Multiple features need to turn microphone audio into the speaker's
///   actual words: Care Notes (now), future clinical-note dictation, future
///   patient-intake voice. The plumbing is identical: upload audio ->
///   transcribe -> return raw text. This contractor owns the plumbing.
///
/// Hierarchy (each guy hires the next):
///   Caller (e.g. CareNotesController)
///     -> AudioTranscriptionService     (this class)
///        - hires GeminiService         (Gemini API plumbing)
///   Caller passes:  audio file path + mimeType + optional previousText
///                   (running context so chunk N stays consistent with N-1)
///                   + optional patientId (currently unused for transcription
///                   only, kept for future per-patient prompt tuning)
///   Caller gets:    raw transcribed text (the speaker's words, nothing more)
///
/// What this contractor does NOT do:
///   - No polishing, no rewriting, no SOAP framing -- if a caller wants that
///     they own that step. The contractor's job is faithful transcription.
///   - No PHI scrub on output (the output IS the speaker's words; scrubbing
///     before storage is the caller's choice). The scrubber is still hired
///     downstream by features that send transcribed text back to AI for
///     further processing -- not relevant here.
/// </summary>
public interface IAudioTranscriptionService
{
    /// <summary>
    /// Transcribe one audio chunk to raw text. <paramref name="previousText"/>
    /// is fed to Gemini as running context so the new chunk's transcription
    /// stays consistent with what came before (medical terms, names, sentence
    /// flow). It is NOT used to rewrite earlier text.
    /// </summary>
    Task<TranscriptionResult> TranscribeChunkAsync(
        string audioFilePath,
        string mimeType,
        string? previousText,
        int? patientId);
}

public class TranscriptionResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class AudioTranscriptionService : IAudioTranscriptionService
{
    private readonly IGeminiService _gemini;
    private readonly ILogger<AudioTranscriptionService> _logger;

    public AudioTranscriptionService(
        IGeminiService gemini,
        ILogger<AudioTranscriptionService> logger)
    {
        _gemini = gemini;
        _logger = logger;
    }

    // ====================================================================
    // Chunk transcribe: audio + (optional) running context -> raw text.
    // No polish, no rewriting -- faithful transcription only.
    // ====================================================================
    public async Task<TranscriptionResult> TranscribeChunkAsync(
        string audioFilePath,
        string mimeType,
        string? previousText,
        int? patientId)
    {
        try
        {
            // Step 1: upload via GeminiService.
            var fileUri = await _gemini.UploadFileAsync(audioFilePath, mimeType);
            if (string.IsNullOrEmpty(fileUri))
                return Fail("Audio upload failed.");

            // Step 2: build a context-aware prompt if previousText is provided.
            // We keep the prompt SMALL -- prepend the last ~600 chars of previous
            // transcription as context so Gemini can keep medical terms / sentence
            // flow consistent across chunks. We do NOT scrub previousText here:
            // it was already produced by Gemini, may contain patient names, and
            // sending it back is no worse than the original audio (which had
            // those names spoken aloud).
            string? customPrompt = null;
            if (!string.IsNullOrWhiteSpace(previousText))
            {
                var ctx = previousText!.Length > 600
                    ? previousText[^600..]
                    : previousText;

                customPrompt =
                    "You are continuing a real-time transcription. Below is the text " +
                    "transcribed so far -- use it as context for terminology, names, " +
                    "and sentence flow. Then transcribe the NEW audio chunk only.\n\n" +
                    "PREVIOUS TRANSCRIPT (for context, do NOT repeat):\n" +
                    ctx + "\n\n" +
                    "Now transcribe ONLY the new audio chunk that follows. Return clean English text. " +
                    "Use proper medical terminology. Remove filler words (uh, um, hmm, etc.). " +
                    "If the new audio is silence or filler only, return an empty string. " +
                    "Do NOT include any preamble, quotes, or commentary.";
            }

            var raw = await _gemini.TranscribeAudioAsync(fileUri, mimeType, customPrompt);
            if (!raw.Success)
                return Fail(raw.ErrorMessage ?? "Transcription failed.");

            return Ok((raw.Text ?? string.Empty).Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TranscribeChunkAsync failed");
            return Fail("Transcription error.");
        }
    }

    private static TranscriptionResult Ok(string text) =>
        new() { Success = true, Text = text };

    private static TranscriptionResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
