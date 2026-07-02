using System.Text.RegularExpressions;
using System.Web;

namespace EHR.Services;

/// <summary>
/// Request model for asking the help assistant a question.
/// </summary>
public class HelpAskRequest
{
    public string Question { get; set; } = "";
    public List<ConversationMessage>? History { get; set; }
}

/// <summary>
/// A single message in the conversation history.
/// </summary>
public class ConversationMessage
{
    public string Role { get; set; } = "user"; // "user" or "model"
    public string Text { get; set; } = "";
}

/// <summary>
/// Response from the help assistant.
/// </summary>
public class HelpAskResponse
{
    public bool Success { get; set; }
    public string Answer { get; set; } = "";
    public bool IsFeatureRequest { get; set; }
    public string? FeatureRequestSuggestion { get; set; }
}

/// <summary>
/// Request model for submitting a feature request.
/// </summary>
public class FeatureRequestSubmission
{
    public string Description { get; set; } = "";
}

/// <summary>
/// Provides the user guide markdown content. Loaded once at startup and cached.
/// </summary>
public interface IUserGuideProvider
{
    string GetUserGuide();
}

/// <summary>
/// Singleton that loads and caches the IMEHR user guide markdown at startup.
/// </summary>
public class UserGuideProvider : IUserGuideProvider
{
    private readonly string _userGuideContent;
    private readonly ILogger<UserGuideProvider> _logger;

    public UserGuideProvider(ILogger<UserGuideProvider> logger, IWebHostEnvironment env)
    {
        _logger = logger;

        // Try multiple paths to find the user guide
        var paths = new[]
        {
            Path.Combine(env.ContentRootPath, "..", "Docs", "IMEHR_UserGuide.md"),
            Path.Combine(env.ContentRootPath, "Docs", "IMEHR_UserGuide.md"),
            Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "docs", "IMEHR_UserGuide.md")
        };

        _userGuideContent = "";
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                _userGuideContent = File.ReadAllText(fullPath);
                _logger.LogInformation("User guide loaded from {Path} ({Length} chars)", fullPath, _userGuideContent.Length);
                return;
            }
        }

        _logger.LogWarning("IMEHR_UserGuide.md not found. AI assistant will have limited knowledge.");
    }

    public string GetUserGuide() => _userGuideContent;
}

/// <summary>
/// Service interface for the IMEHR help assistant.
/// </summary>
public interface IIMEHRHelpService
{
    Task<HelpAskResponse> AskAsync(string question, List<ConversationMessage>? history,
        string userName, string userRole, string userEmail);
    Task<(bool Success, string Message)> SubmitFeatureRequestAsync(string description,
        string userName, string userRole, string userEmail);
}

/// <summary>
/// Orchestrates the MEDOCS AI help assistant using Gemini for Q&A
/// and EmailService for feature request submissions.
/// </summary>
public class IMEHRHelpService : IIMEHRHelpService
{
    private readonly IGeminiService _geminiService;
    private readonly IUserGuideProvider _userGuideProvider;
    private readonly IEmailService _emailService;
    private readonly ILogger<IMEHRHelpService> _logger;

    private const string FeatureRequestTag = "[FEATURE_REQUEST]";
    private const string FeatureRequestEmail = "contact@medocs.ai";
    private const int MaxConversationTurns = 20;

    public IMEHRHelpService(
        IGeminiService geminiService,
        IUserGuideProvider userGuideProvider,
        IEmailService emailService,
        ILogger<IMEHRHelpService> logger)
    {
        _geminiService = geminiService;
        _userGuideProvider = userGuideProvider;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<HelpAskResponse> AskAsync(string question, List<ConversationMessage>? history,
        string userName, string userRole, string userEmail)
    {
        try
        {
            var systemInstruction = BuildSystemInstruction(userRole);

            // Build conversation messages for multi-turn
            var conversationMessages = new List<(string role, string text)>();

            if (history != null && history.Count > 0)
            {
                // Trim to last N turns to manage context size
                var trimmed = history.Count > MaxConversationTurns
                    ? history.Skip(history.Count - MaxConversationTurns).ToList()
                    : history;

                foreach (var msg in trimmed)
                {
                    conversationMessages.Add((msg.Role, msg.Text));
                }
            }

            // Add the current question
            conversationMessages.Add(("user", question));

            var result = await _geminiService.GenerateMultiTurnAsync(systemInstruction, conversationMessages, temperature: 0.3);

            if (!result.Success)
            {
                _logger.LogError("Gemini API error for help question: {Error}", result.ErrorMessage);
                return new HelpAskResponse
                {
                    Success = false,
                    Answer = "I'm sorry, I'm having trouble right now. Please try again in a moment."
                };
            }

            var answer = result.Text ?? "";
            var isFeatureRequest = answer.Contains(FeatureRequestTag, StringComparison.OrdinalIgnoreCase);

            if (isFeatureRequest)
            {
                answer = answer.Replace(FeatureRequestTag, "", StringComparison.OrdinalIgnoreCase).Trim();
            }

            return new HelpAskResponse
            {
                Success = true,
                Answer = answer,
                IsFeatureRequest = isFeatureRequest,
                FeatureRequestSuggestion = isFeatureRequest ? question : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in help assistant");
            return new HelpAskResponse
            {
                Success = false,
                Answer = "I'm sorry, something went wrong. Please try again."
            };
        }
    }

    public async Task<(bool Success, string Message)> SubmitFeatureRequestAsync(
        string description, string userName, string userRole, string userEmail)
    {
        try
        {
            var subject = "IMEHR Feature Request";
            var body = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <div style='background: linear-gradient(135deg, #6366f1, #4f46e5); padding: 20px; border-radius: 8px 8px 0 0;'>
        <h2 style='color: white; margin: 0;'>IMEHR Feature Request</h2>
    </div>
    <div style='padding: 24px; border: 1px solid #e5e7eb; border-top: none; border-radius: 0 0 8px 8px;'>
        <p><strong>From:</strong> {HttpUtility.HtmlEncode(userName)}</p>
        <p><strong>Email:</strong> {HttpUtility.HtmlEncode(userEmail)}</p>
        <p><strong>Role:</strong> {HttpUtility.HtmlEncode(userRole)}</p>
        <p><strong>Date:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>
        <hr style='border: none; border-top: 1px solid #e5e7eb; margin: 16px 0;'>
        <h3 style='color: #1f2937;'>Feature Request</h3>
        <p style='color: #374151; line-height: 1.6;'>{HttpUtility.HtmlEncode(description)}</p>
    </div>
    <p style='color: #9ca3af; font-size: 12px; text-align: center; margin-top: 16px;'>
        Sent via MEDOCS AI Help Assistant — IMEHR
    </p>
</div>";

            var sent = await _emailService.SendEmailAsync(FeatureRequestEmail, subject, body, isHtml: true);

            return sent
                ? (true, "Your feature request has been sent to the IMEHR team. Thank you!")
                : (false, "Failed to send feature request. Please email contact@medocs.ai directly.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting feature request");
            return (false, "Failed to send feature request. Please email contact@medocs.ai directly.");
        }
    }

    private string BuildSystemInstruction(string userRole)
    {
        var userGuide = _userGuideProvider.GetUserGuide();

        var roleName = userRole switch
        {
            "0" => "Super Admin",
            "1" => "Clinic Admin",
            "2" => "Clinician",
            "3" => "Front Desk",
            "4" => "Biller",
            "5" => "Read Only",
            "6" => "Medical Assistant",
            "7" => "Nurse",
            _ => "User"
        };

        return $@"You are MEDOCS AI, the built-in help assistant for IMEHR — an Internal Medicine Electronic Health Records system.

RULES:
1. Answer ONLY questions about using IMEHR. If asked about anything unrelated, politely redirect: ""I can only help with IMEHR questions. What would you like to know about using the system?""
2. Be concise, friendly, and provide step-by-step instructions when explaining how to do something. Use numbered steps for procedures.
3. The current user's role is ""{roleName}"". Tailor your answers to what they can access. Do NOT mention features or actions that are unavailable to their role.
4. If the user asks about a feature that does NOT exist in the documentation below, include the tag {FeatureRequestTag} in your response and let them know the feature isn't currently available. Offer to send their suggestion to the development team.
5. Never reveal that you are powered by Gemini, an AI model, or a language model. You are ""MEDOCS AI"", the IMEHR help assistant.
6. Never provide medical, legal, or financial advice. You only help with using the IMEHR software.
7. Format responses with markdown: use **bold** for emphasis, numbered lists for steps, and bullet points for lists.
8. Keep answers under 300 words unless the user asks for detailed instructions.
9. Remember the conversation history — refer back to previous questions when relevant.
10. For role-specific guidance:
    - Super Admin: Full access to everything including clinic settings (name, address, NPI, logo, Tax ID)
    - Clinic Admin: Can manage users, locations, providers, consent forms. CANNOT modify clinic settings (name, address, NPI, logo) — that is Super Admin only. If a Clinic Admin asks about changing clinic info, tell them to contact contact@medocs.ai
    - Clinician: Full clinical access including signing notes, prescribing, ordering
    - Front Desk: Appointments, check-in, patient demographics
    - Biller: Billing, insurance, financial reports
    - Medical Assistant & Nurse: Vitals, Chief Complaint, HPI entry; view-only for clinical notes/orders/Rx
    - Read Only: View-only access everywhere
11. When a user asks ""what should I do first?"", ""how do I get started?"", or similar first-time questions, refer to Section 2 (Quick Start by Role) for role-specific onboarding steps, and Section 3 (Complete Clinic Workflow) for the full patient visit flow from clinic setup to encounter close.
12. The complete clinic workflow is: Super Admin configures clinic info → Clinic Admin sets up providers/staff/locations → Front Desk adds patients & creates appointments → Patient checks in (consent form or manual) → MA/Nurse starts visit, enters vitals, history & CC/HPI → Clinician reviews, writes clinical note, creates orders & prescriptions → Clinician or MA/Nurse closes the encounter. Explain this flow when users ask about the overall process.

DOCUMENTATION:
{userGuide}";
    }
}
