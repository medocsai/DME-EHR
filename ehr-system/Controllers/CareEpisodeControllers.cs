using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;
using Microsoft.EntityFrameworkCore;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;
using System.Security.Claims;

namespace EHR.Controllers;

// ============================================
// CARE EPISODES API
// ============================================
[ApiController]
[Route("api/care-episodes")]
[Authorize]
public class CareEpisodesController : ControllerBase
{
    private readonly IEnhancedCareEpisodeService _careEpisodeService;
    private readonly IInsuranceValidationService _insuranceValidationService;

    public CareEpisodesController(
        IEnhancedCareEpisodeService careEpisodeService,
        IInsuranceValidationService insuranceValidationService)
    {
        _careEpisodeService = careEpisodeService;
        _insuranceValidationService = insuranceValidationService;
    }

    /// <summary>
    /// Get all care episodes with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CareEpisodeListDto>>> GetCareEpisodes(
        [FromQuery] int? patientId = null,
        [FromQuery] int? status = null)
    {
        var episodes = await _careEpisodeService.GetCareEpisodesAsync(patientId, status);
        return Ok(episodes);
    }

    /// <summary>
    /// Get care episode details by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CareEpisodeDetailDto>> GetCareEpisode(int id)
    {
        try
        {
            var episode = await _careEpisodeService.GetCareEpisodeDetailAsync(id);
            if (episode == null)
                return NotFound(new { error = "Care Episode not found" });
            return Ok(episode);
        }
        catch (Exception ex)
        {
            return this.ServerError(ex, "Error loading care episode");
        }
    }

    /// <summary>
    /// Get the active care episode for a patient (Active or Overdue status)
    /// </summary>
    [HttpGet("patient/{patientId}/active")]
    public async Task<ActionResult<CareEpisodeDto>> GetActiveEpisodeForPatient(int patientId)
    {
        var episode = await _careEpisodeService.GetActiveCareEpisodeForPatientAsync(patientId);
        if (episode == null)
            return NotFound(new { error = "No active Care Episode found for this patient" });
        return Ok(episode);
    }

    /// <summary>
    /// Check if a patient can create a new care episode
    /// </summary>
    [HttpGet("patient/{patientId}/eligibility")]
    public async Task<ActionResult<CareEpisodeEligibilityDto>> CheckEligibility(int patientId)
    {
        var eligibility = await _careEpisodeService.CheckEligibilityAsync(patientId);
        return Ok(eligibility);
    }

    /// <summary>
    /// Create a new care episode
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CareEpisode>> CreateCareEpisode([FromBody] CareEpisodeCreateDto dto)
    {
        try
        {
            var episode = await _careEpisodeService.CreateCareEpisodeAsync(dto);
            return CreatedAtAction(nameof(GetCareEpisode), new { id = episode.CareEpisodeId }, episode);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing care episode
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<CareEpisode>> UpdateCareEpisode(int id, [FromBody] CareEpisodeUpdateDto dto)
    {
        var episode = await _careEpisodeService.UpdateCareEpisodeAsync(id, dto);
        if (episode == null)
            return NotFound(new { error = "Care Episode not found" });
        return Ok(episode);
    }

    /// <summary>
    /// Mark a care episode as completed
    /// </summary>
    [HttpPost("{id}/complete")]
    public async Task<ActionResult<CareEpisode>> MarkAsCompleted(int id, [FromBody] CareEpisodeCompleteDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var episode = await _careEpisodeService.MarkAsCompletedAsync(id, dto.DischargeReason, userId);
        if (episode == null)
            return NotFound(new { error = "Care Episode not found" });
        return Ok(episode);
    }

    /// <summary>
    /// Restore a completed care episode back to active status (Admin only)
    /// </summary>
    [HttpPost("{id}/restore")]
    [Authorize(Roles = "0,1")] // Only SuperAdmin and ClinicAdmin can restore
    public async Task<ActionResult<CareEpisode>> RestoreCareEpisode(int id)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var episode = await _careEpisodeService.RestoreCareEpisodeAsync(id, userId);
        if (episode == null)
            return NotFound(new { error = "Care Episode not found or cannot be restored" });
        return Ok(episode);
    }

    /// <summary>
    /// Extend the end date of a care episode
    /// </summary>
    [HttpPost("{id}/extend")]
    public async Task<ActionResult<CareEpisode>> ExtendEndDate(int id, [FromBody] DateOnly newEndDate)
    {
        var episode = await _careEpisodeService.ExtendEndDateAsync(id, newEndDate);
        if (episode == null)
            return NotFound(new { error = "Care Episode not found" });
        return Ok(episode);
    }

    /// <summary>
    /// Update visit counts for a care episode
    /// </summary>
    [HttpPost("{id}/refresh-visits")]
    public async Task<ActionResult> RefreshVisitCounts(int id)
    {
        await _careEpisodeService.UpdateVisitCountsAsync(id);
        return Ok(new { message = "Visit counts updated" });
    }

    /// <summary>
    /// Get care episode dashboard data
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<CareEpisodeDashboardDto>> GetDashboard([FromQuery] int? providerId)
    {
        var dashboard = await _careEpisodeService.GetDashboardDataAsync(providerId);
        return Ok(dashboard);
    }

    /// <summary>
    /// Get low visits remaining alerts
    /// </summary>
    [HttpGet("alerts/low-visits")]
    public async Task<ActionResult<List<CareEpisodeAlertDto>>> GetLowVisitsAlerts([FromQuery] int? threshold = null)
    {
        var actualThreshold = threshold ?? 3;
        var alerts = await _careEpisodeService.GetLowVisitsAlertsAsync(actualThreshold);
        return Ok(alerts);
    }

    /// <summary>
    /// Get no-show alerts
    /// </summary>
    [HttpGet("alerts/no-shows")]
    public async Task<ActionResult<List<NoShowAlertDto>>> GetNoShowAlerts([FromQuery] int? thresholdMinutes = null)
    {
        var actualThreshold = thresholdMinutes ?? 30;
        var alerts = await _careEpisodeService.GetNoShowAlertsAsync(actualThreshold);
        return Ok(alerts);
    }

    /// <summary>
    /// Get Care Episodes that need appointments scheduled.
    /// For Admin and Front Desk dashboard card.
    /// Returns episodes where scheduled appointments < expected visits.
    /// </summary>
    [HttpGet("require-schedule")]
    [Authorize(Roles = "0,1,3")] // SuperAdmin, ClinicAdmin, and FrontDesk
    public async Task<ActionResult<List<RequireScheduleDto>>> GetRequireSchedule()
    {
        var episodes = await _careEpisodeService.GetCareEpisodesNeedingScheduleAsync();
        return Ok(episodes);
    }
}

// ============================================
// INSURANCE VALIDATION API
// ============================================
[ApiController]
[Route("api/insurance")]
[Authorize]
public class InsuranceValidationController : ControllerBase
{
    private readonly IInsuranceValidationService _validationService;

    public InsuranceValidationController(IInsuranceValidationService validationService)
    {
        _validationService = validationService;
    }

    /// <summary>
    /// Validate insurance and get authorization data
    /// This is a mock implementation that will be replaced with real API integration
    /// </summary>
    [HttpPost("{insuranceId}/validate")]
    public async Task<ActionResult<InsuranceAuthorizationDto>> ValidateInsurance(int insuranceId)
    {
        var result = await _validationService.ValidateInsuranceAsync(insuranceId);
        if (!result.IsValid)
            return BadRequest(result);
        return Ok(result);
    }
}

// ============================================
// SYSTEM SETTINGS API
// ============================================
[ApiController]
[Route("api/settings")]
[Authorize(Roles = "0,1")] // SuperAdmin, ClinicAdmin only
public class SystemSettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settingsService;

    public SystemSettingsController(ISystemSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Get all system settings for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SystemSettingDto>>> GetAllSettings()
    {
        var settings = await _settingsService.GetAllSettingsAsync();
        return Ok(settings);
    }

    /// <summary>
    /// Get a specific setting by key
    /// </summary>
    [HttpGet("{key}")]
    public async Task<ActionResult<SystemSettingDto>> GetSetting(string key)
    {
        var setting = await _settingsService.GetSettingAsync(key);
        if (setting == null)
            return NotFound(new { error = $"Setting '{key}' not found" });
        return Ok(setting);
    }

    /// <summary>
    /// Update or create a setting
    /// </summary>
    [HttpPut("{key}")]
    public async Task<ActionResult<SystemSettingDto>> UpsertSetting(string key, [FromBody] SystemSettingUpdateDto dto)
    {
        var setting = await _settingsService.UpsertSettingAsync(key, dto);
        return Ok(setting);
    }

    /// <summary>
    /// Initialize default settings for the tenant
    /// </summary>
    [HttpPost("initialize")]
    public async Task<ActionResult> InitializeDefaults()
    {
        await _settingsService.InitializeDefaultSettingsAsync();
        return Ok(new { message = "Default settings initialized" });
    }
}

// ============================================
// ICD-10 CODE SEARCH API
// ============================================
[ApiController]
[Route("api/icd-codes")]
[Authorize]
public class IcdCodesController : ControllerBase
{
    private readonly EhrDbContext _context;

    public IcdCodesController(EhrDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Search ICD-10 codes
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult> SearchIcdCodes([FromQuery] string query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Ok(new List<object>());

        var codes = await _context.Icdcodes
            .Where(c => c.Code.Contains(query) || c.Description.Contains(query))
            .Take(take)
            .Select(c => new
            {
                Code = c.Code,
                Description = c.Description,
                DisplayText = c.Code + " - " + c.Description
            })
            .ToListAsync();

        return Ok(codes);
    }
}
