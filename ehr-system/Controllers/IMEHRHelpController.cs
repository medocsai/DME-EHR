using EHR.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EHR.Controllers;

/// <summary>
/// API controller for the MEDOCS AI help assistant and documentation.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IMEHRHelpController : ControllerBase
{
    private readonly IIMEHRHelpService _helpService;
    private readonly IUserGuideProvider _userGuideProvider;
    private readonly ILogger<IMEHRHelpController> _logger;

    public IMEHRHelpController(
        IIMEHRHelpService helpService,
        IUserGuideProvider userGuideProvider,
        ILogger<IMEHRHelpController> logger)
    {
        _helpService = helpService;
        _userGuideProvider = userGuideProvider;
        _logger = logger;
    }

    /// <summary>
    /// Ask the AI help assistant a question.
    /// Supports multi-turn conversation via history parameter.
    /// </summary>
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] HelpAskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { success = false, answer = "Please enter a question." });

        if (request.Question.Length > 1000)
            return BadRequest(new { success = false, answer = "Question is too long. Please keep it under 1000 characters." });

        // Extract user info from JWT claims
        var userName = User.FindFirst("FirstName")?.Value ?? User.FindFirst("name")?.Value ?? "User";
        var userRole = User.FindFirst("Role")?.Value ?? User.FindFirst("role")?.Value ?? "";
        var userEmail = User.FindFirst("Email")?.Value ?? User.FindFirst("email")?.Value ?? "";

        var result = await _helpService.AskAsync(
            request.Question,
            request.History,
            userName,
            userRole,
            userEmail);

        return Ok(new
        {
            success = result.Success,
            answer = result.Answer,
            isFeatureRequest = result.IsFeatureRequest,
            featureRequestSuggestion = result.FeatureRequestSuggestion
        });
    }

    /// <summary>
    /// Get the raw user guide markdown for the documentation page.
    /// </summary>
    [HttpGet("guide")]
    public IActionResult GetGuide()
    {
        var content = _userGuideProvider.GetUserGuide();

        if (string.IsNullOrEmpty(content))
            return NotFound(new { content = "" });

        return Ok(new { content });
    }

    /// <summary>
    /// Submit a feature request via email.
    /// </summary>
    [HttpPost("feature-request")]
    public async Task<IActionResult> SubmitFeatureRequest([FromBody] FeatureRequestSubmission request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { success = false, message = "Please provide a description." });

        if (request.Description.Length > 2000)
            return BadRequest(new { success = false, message = "Description is too long. Please keep it under 2000 characters." });

        var userName = User.FindFirst("FirstName")?.Value ?? User.FindFirst("name")?.Value ?? "User";
        var userRole = User.FindFirst("Role")?.Value ?? User.FindFirst("role")?.Value ?? "";
        var userEmail = User.FindFirst("Email")?.Value ?? User.FindFirst("email")?.Value ?? "";

        var (success, message) = await _helpService.SubmitFeatureRequestAsync(
            request.Description, userName, userRole, userEmail);

        return Ok(new { success, message });
    }
}
