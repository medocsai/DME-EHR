using System.Text;
using System.Text.Json;

namespace EHR.Services;

/// <summary>
/// Response from Gemini API text generation.
/// </summary>
public class GeminiTextResponse
{
    public bool Success { get; set; }
    public string? Text { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Centralized service for all Gemini API interactions.
/// All services should use this to interact with Gemini, allowing model changes in one place.
/// </summary>
public interface IGeminiService
{
    /// <summary>
    /// Generate text content using Gemini API.
    /// </summary>
    /// <param name="prompt">The prompt to send to Gemini</param>
    /// <param name="temperature">Temperature for response generation (0.0-1.0). Default is 0.7</param>
    /// <param name="useExtendedThinking">Whether to use extended thinking mode. Default is false</param>
    /// <returns>The generated text response</returns>
    Task<GeminiTextResponse> GenerateTextAsync(string prompt, double temperature = 0.7, bool useExtendedThinking = false);

    /// <summary>
    /// Generate a multi-turn conversational response with system instruction.
    /// Used for help chatbot with conversation history.
    /// </summary>
    /// <param name="systemInstruction">System-level instruction for the model</param>
    /// <param name="conversationHistory">List of (role, text) tuples representing the conversation</param>
    /// <param name="temperature">Temperature for response generation. Default is 0.3</param>
    /// <returns>The generated text response</returns>
    Task<GeminiTextResponse> GenerateMultiTurnAsync(string systemInstruction, List<(string role, string text)> conversationHistory, double temperature = 0.3);

    /// <summary>
    /// Upload a file to Gemini for processing (used for audio transcription).
    /// </summary>
    /// <param name="filePath">Path to the file to upload</param>
    /// <param name="mimeType">MIME type of the file</param>
    /// <returns>The file URI for use in subsequent API calls</returns>
    Task<string?> UploadFileAsync(string filePath, string mimeType);

    /// <summary>
    /// Transcribe audio using Gemini API.
    /// </summary>
    /// <param name="fileUri">The file URI from UploadFileAsync</param>
    /// <param name="mimeType">MIME type of the audio file</param>
    /// <param name="customPrompt">
    /// Optional. When provided, this prompt is sent INSTEAD of the built-in
    /// "transcribe accurately" prompt. Used by callers that need to inject
    /// running context (previously transcribed text) so Gemini can keep
    /// medical terms / sentence flow consistent across chunks.
    /// </param>
    /// <returns>The transcribed text</returns>
    Task<GeminiTextResponse> TranscribeAudioAsync(string fileUri, string mimeType = "audio/wav", string? customPrompt = null);
}

public class GeminiService : IGeminiService
{
    // Centralized model configuration - change here to update all services
    private const string DefaultModel = "gemini-2.5-flash-lite";
    // Stronger model for transcription + clinical note generation (no hallucination on silence)
    private const string ThinkingModel = "gemini-2.5-flash";
    private const string BaseApiUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string UploadApiUrl = "https://generativelanguage.googleapis.com/upload/v1beta";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _apiKey = configuration["Gemini:ApiKey"] ??
            throw new InvalidOperationException("Gemini:ApiKey not found in configuration");
        _logger = logger;

        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<GeminiTextResponse> GenerateTextAsync(string prompt, double temperature = 0.4, bool useExtendedThinking = false)
    {
        try
        {
            // Use stronger model with thinking for clinical note generation, flash-lite for real-time extraction
            var model = useExtendedThinking ? ThinkingModel : DefaultModel;
            var apiUrl = $"{BaseApiUrl}/models/{model}:generateContent?key={_apiKey}";

            object requestBody;
            if (useExtendedThinking)
            {
                requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[] { new { text = prompt } }
                        }
                    },
                    generationConfig = new
                    {
                        thinkingConfig = new { thinkingBudget = -1 }
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[] { new { text = prompt } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = temperature,
                        topP = 0.8
                    }
                };
            }

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(apiUrl, jsonContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = $"API error: {response.StatusCode}"
                };
            }

            // Parse the response
            using var doc = JsonDocument.Parse(responseContent);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                _logger.LogWarning("No candidates in Gemini response");
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = "No response generated"
                };
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
            {
                _logger.LogWarning("No parts in Gemini response");
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid response format"
                };
            }

            // With thinking mode, response has multiple parts: thought parts + text part
            // Extract the last text part (skip thought parts which have no "text" property)
            string text = "";
            for (int i = parts.GetArrayLength() - 1; i >= 0; i--)
            {
                if (parts[i].TryGetProperty("text", out var tp))
                {
                    text = tp.GetString() ?? "";
                    break;
                }
            }

            if (useExtendedThinking)
                _logger.LogInformation("[Gemini] Thinking model used, parts count: {PartsCount}", parts.GetArrayLength());

            return new GeminiTextResponse
            {
                Success = true,
                Text = text
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API");
            return new GeminiTextResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string?> UploadFileAsync(string filePath, string mimeType)
    {
        try
        {
            // Normalize path to use correct OS separators (fixes mixed / and \ issues)
            filePath = Path.GetFullPath(filePath);

            // Verify file exists before attempting upload
            if (!File.Exists(filePath))
            {
                _logger.LogError("File not found for Gemini upload: {FilePath}", filePath);
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            var numBytes = fileInfo.Length;

            // Step 1: Start resumable upload
            using var startRequest = new HttpRequestMessage(HttpMethod.Post,
                $"{UploadApiUrl}/files?key={_apiKey}");
            startRequest.Headers.Add("X-Goog-Upload-Protocol", "resumable");
            startRequest.Headers.Add("X-Goog-Upload-Command", "start");
            startRequest.Headers.Add("X-Goog-Upload-Header-Content-Length", numBytes.ToString());
            startRequest.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);
            startRequest.Content = new StringContent(
                JsonSerializer.Serialize(new { file = new { display_name = Path.GetFileName(filePath) } }),
                Encoding.UTF8,
                "application/json");

            var startResponse = await _httpClient.SendAsync(startRequest);
            if (!startResponse.IsSuccessStatusCode)
            {
                var errorContent = await startResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to start Gemini upload: {StatusCode} - {ErrorContent}",
                    startResponse.StatusCode, errorContent);
                return null;
            }

            var uploadUrl = startResponse.Headers.GetValues("X-Goog-Upload-Url").FirstOrDefault();
            if (string.IsNullOrEmpty(uploadUrl))
            {
                _logger.LogError("No upload URL in Gemini response");
                return null;
            }

            // Step 2: Upload file data
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadRequest.Headers.Add("X-Goog-Upload-Offset", "0");
            uploadRequest.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
            uploadRequest.Content = new ByteArrayContent(fileBytes);
            uploadRequest.Content.Headers.ContentLength = numBytes;

            var uploadResponse = await _httpClient.SendAsync(uploadRequest);
            var uploadResponseContent = await uploadResponse.Content.ReadAsStringAsync();

            if (!uploadResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to upload file to Gemini: {StatusCode} - {Content}",
                    uploadResponse.StatusCode, uploadResponseContent);
                return null;
            }

            // Parse response to get file URI
            using var doc = JsonDocument.Parse(uploadResponseContent);
            if (doc.RootElement.TryGetProperty("file", out var fileElement) &&
                fileElement.TryGetProperty("uri", out var uriElement))
            {
                return uriElement.GetString();
            }

            _logger.LogError("No file URI in Gemini upload response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Gemini");
            return null;
        }
    }

    public async Task<GeminiTextResponse> TranscribeAudioAsync(string fileUri, string mimeType = "audio/wav", string? customPrompt = null)
    {
        try
        {
            // Use flash 2.5 for transcription — better audio understanding, no hallucination on silence
            var apiUrl = $"{BaseApiUrl}/models/{ThinkingModel}:generateContent?key={_apiKey}";

            // Caller can override the prompt (used for chunked transcription that
            // wants to feed previously-transcribed context to Gemini for continuity).
            var prompt = customPrompt ?? @"Please transcribe this audio accurately. Return only the clean transcription text, without any additional commentary or formatting.
ALWAYS transcribe and output in ENGLISH only, regardless of what language is spoken. If the speaker uses Urdu, Hindi, Spanish, Arabic, or any other language, translate to English in the transcription.
If the audio contains medical terminology, use proper medical terminology.
If you cannot understand a word, indicate it with [inaudible].
IMPORTANT: Remove all filler words (uh, um, un, hmm, ah, er, erm, mhm, you know, like) from the transcription. Return only meaningful speech.
If the audio contains only silence, background noise, or filler words with no meaningful speech, return an empty string.";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { file_data = new { mime_type = mimeType, file_uri = fileUri } }
                        }
                    }
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(apiUrl, jsonContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini transcription error: {StatusCode} - {Content}",
                    response.StatusCode, responseContent);
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = $"Transcription failed: {response.StatusCode}"
                };
            }

            // Parse the response
            using var doc = JsonDocument.Parse(responseContent);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = "No transcription generated"
                };
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
            {
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid transcription response"
                };
            }

            var text = parts[0].TryGetProperty("text", out var textProp) ? textProp.GetString() : "";

            return new GeminiTextResponse
            {
                Success = true,
                Text = text ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio with Gemini");
            return new GeminiTextResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<GeminiTextResponse> GenerateMultiTurnAsync(string systemInstruction, List<(string role, string text)> conversationHistory, double temperature = 0.3)
    {
        try
        {
            var apiUrl = $"{BaseApiUrl}/models/{DefaultModel}:generateContent?key={_apiKey}";

            // Build contents array with role-tagged messages
            var contents = conversationHistory.Select(msg => new
            {
                role = msg.role, // "user" or "model"
                parts = new[] { new { text = msg.text } }
            }).ToArray();

            var requestBody = new
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                contents = contents,
                generationConfig = new
                {
                    temperature = temperature,
                    topP = 0.8
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(apiUrl, jsonContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini multi-turn API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = $"API error: {response.StatusCode}"
                };
            }

            using var doc = JsonDocument.Parse(responseContent);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                _logger.LogWarning("No candidates in Gemini multi-turn response");
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = "No response generated"
                };
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
            {
                _logger.LogWarning("No parts in Gemini multi-turn response");
                return new GeminiTextResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid response format"
                };
            }

            var text = parts[0].TryGetProperty("text", out var textProp) ? textProp.GetString() : "";

            return new GeminiTextResponse
            {
                Success = true,
                Text = text ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Gemini multi-turn conversation");
            return new GeminiTextResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
