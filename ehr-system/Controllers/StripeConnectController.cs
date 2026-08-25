using EHR.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Services;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// ClinicAdmin endpoints for managing Stripe Connect onboarding and account linking.
/// All endpoints require ClinicAdmin role (1) or SuperAdmin role (0).
/// </summary>
[ApiController]
[Route("api/stripe-connect")]
[Authorize(Roles = "0,1")]
public class StripeConnectController : ControllerBase
{
    private readonly EhrDbContext _context;
    private readonly IStripeConnectService _stripeConnectService;
    private readonly ILogger<StripeConnectController> _logger;

    public StripeConnectController(
        EhrDbContext context,
        IStripeConnectService stripeConnectService,
        ILogger<StripeConnectController> logger)
    {
        _context = context;
        _stripeConnectService = stripeConnectService;
        _logger = logger;
    }

    private int GetTenantId() => int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    /// <summary>
    /// Lists all connected Stripe accounts for the current tenant.
    /// Used by ClinicAdmin Settings → Payment Integration page and the
    /// Location modal "Use existing" dropdown.
    /// </summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> GetTenantAccounts()
    {
        var tenantId = GetTenantId();
        if (tenantId == 0) return Unauthorized();

        var accounts = await _stripeConnectService.GetAccountsForTenantAsync(tenantId);

        var result = accounts.Select(a => new
        {
            a.StripeConnectAccountId,
            a.StripeAccountId,
            a.DisplayName,
            a.BusinessEmail,
            a.Status,
            StatusName = ((StripeConnectAccountStatus)a.Status).ToString(),
            a.ChargesEnabled,
            a.PayoutsEnabled,
            a.DetailsSubmitted,
            a.ConnectedAt,
            a.DisconnectedAt,
            LinkedLocations = _context.Locations
                .Where(l => l.StripeConnectAccountId == a.StripeConnectAccountId)
                .Select(l => new { l.LocationId, l.Name })
                .ToList()
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Returns the Stripe Connect status for a specific location.
    /// </summary>
    [HttpGet("status/{locationId:int}")]
    public async Task<IActionResult> GetLocationStatus(int locationId)
    {
        var tenantId = GetTenantId();
        var location = await _context.Locations
            .Include(l => l.StripeConnectAccount)
            .FirstOrDefaultAsync(l => l.LocationId == locationId && l.TenantId == tenantId);

        if (location == null) return NotFound();

        if (location.StripeConnectAccount == null)
        {
            return Ok(new
            {
                Connected = false,
                Status = "NotConnected",
                LocationName = location.Name
            });
        }

        return Ok(new
        {
            Connected = true,
            Status = ((StripeConnectAccountStatus)location.StripeConnectAccount.Status).ToString(),
            location.StripeConnectAccount.DisplayName,
            location.StripeConnectAccount.ChargesEnabled,
            location.StripeConnectAccount.PayoutsEnabled,
            location.StripeConnectAccount.DetailsSubmitted,
            location.StripeConnectAccount.ConnectedAt,
            LocationName = location.Name
        });
    }

    /// <summary>
    /// Starts onboarding for a new Stripe account at a specific location.
    /// Creates a new connected account and returns a Stripe Account Link URL
    /// for the clinic to complete onboarding.
    ///
    /// If the location is already linked to a Pending account (onboarding was interrupted),
    /// this endpoint regenerates the onboarding link for the existing account — a clean
    /// "resume" behavior rather than erroring out.
    /// </summary>
    [HttpPost("start-onboarding")]
    public async Task<IActionResult> StartOnboarding([FromBody] StartOnboardingRequest request)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();
        if (tenantId == 0 || userId == 0) return Unauthorized();

        var location = await _context.Locations
            .Include(l => l.StripeConnectAccount)
            .FirstOrDefaultAsync(l => l.LocationId == request.LocationId && l.TenantId == tenantId);
        if (location == null) return NotFound(new { message = "Location not found" });

        // Resume flow: location already linked to an account
        if (location.StripeConnectAccountId != null && location.StripeConnectAccount != null)
        {
            var existing = location.StripeConnectAccount;

            // If the existing account is Active, reject — re-onboarding an active account is not allowed
            if (existing.Status == (int)StripeConnectAccountStatus.Active)
            {
                return BadRequest(new { message = "This location is already connected to an active Stripe account." });
            }

            // If Pending or Restricted, regenerate the onboarding link so the clinic can resume
            if (existing.Status == (int)StripeConnectAccountStatus.Pending
                || existing.Status == (int)StripeConnectAccountStatus.Restricted)
            {
                try
                {
                    var resumeUrl = await _stripeConnectService.GenerateOnboardingLinkAsync(existing.StripeConnectAccountId);
                    _logger.LogInformation(
                        "Resuming onboarding for location {LocationId}, existing account {AccountId}",
                        request.LocationId, existing.StripeAccountId);
                    return Ok(new { OnboardingUrl = resumeUrl, StripeConnectAccountId = existing.StripeConnectAccountId, Resumed = true });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to regenerate onboarding link for existing account {AccountId}", existing.StripeAccountId);
                    return this.ServerError(ex, "An unexpected error occurred.");
                }
            }

            // Disconnected: unlink and allow fresh start
            location.StripeConnectAccountId = null;
            await _context.SaveChangesAsync();
        }

        try
        {
            // Create a new connected account
            var account = await _stripeConnectService.CreateAccountAsync(
                tenantId, request.BusinessEmail ?? location.Phone ?? "noreply@medocs.ai", userId);

            // Link to the location immediately
            await _stripeConnectService.LinkLocationToAccountAsync(request.LocationId, account.StripeConnectAccountId);

            // Generate the onboarding link
            var url = await _stripeConnectService.GenerateOnboardingLinkAsync(account.StripeConnectAccountId);

            return Ok(new { OnboardingUrl = url, StripeConnectAccountId = account.StripeConnectAccountId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Stripe Connect onboarding for location {LocationId}", request.LocationId);
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Re-generates an onboarding link for an existing pending account
    /// (used when the previous link expired or the user clicked "refresh").
    /// </summary>
    [HttpPost("refresh-onboarding-link")]
    public async Task<IActionResult> RefreshOnboardingLink([FromBody] RefreshOnboardingRequest request)
    {
        var tenantId = GetTenantId();
        var account = await _context.StripeConnectAccounts
            .FirstOrDefaultAsync(a => a.StripeConnectAccountId == request.StripeConnectAccountId && a.TenantId == tenantId);

        if (account == null) return NotFound();

        try
        {
            var url = await _stripeConnectService.GenerateOnboardingLinkAsync(request.StripeConnectAccountId);
            return Ok(new { OnboardingUrl = url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing onboarding link for account {AccountId}", request.StripeConnectAccountId);
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Links an existing connected Stripe account (already onboarded for another
    /// location of the same tenant) to a new location.
    /// </summary>
    [HttpPost("link-existing")]
    public async Task<IActionResult> LinkExisting([FromBody] LinkExistingRequest request)
    {
        var tenantId = GetTenantId();
        var account = await _context.StripeConnectAccounts
            .FirstOrDefaultAsync(a => a.StripeConnectAccountId == request.StripeConnectAccountId && a.TenantId == tenantId);
        if (account == null) return NotFound(new { message = "Stripe account not found" });

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == request.LocationId && l.TenantId == tenantId);
        if (location == null) return NotFound(new { message = "Location not found" });

        try
        {
            await _stripeConnectService.LinkLocationToAccountAsync(request.LocationId, request.StripeConnectAccountId);
            return Ok(new { message = "Linked", LocationId = request.LocationId, StripeConnectAccountId = request.StripeConnectAccountId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking location {LocationId} to account {AccountId}",
                request.LocationId, request.StripeConnectAccountId);
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Disconnects a Stripe account from the platform.
    /// Unlinks all locations using it and marks the account as Disconnected.
    /// </summary>
    [HttpPost("disconnect/{stripeConnectAccountId:int}")]
    public async Task<IActionResult> Disconnect(int stripeConnectAccountId)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();

        var account = await _context.StripeConnectAccounts
            .FirstOrDefaultAsync(a => a.StripeConnectAccountId == stripeConnectAccountId && a.TenantId == tenantId);
        if (account == null) return NotFound();

        try
        {
            await _stripeConnectService.DisconnectAsync(stripeConnectAccountId, userId);
            return Ok(new { message = "Disconnected" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting Stripe account {AccountId}", stripeConnectAccountId);
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Refreshes the cached account status from Stripe (useful for polling
    /// during onboarding to detect when the clinic completes the flow).
    /// </summary>
    [HttpPost("refresh-status/{stripeConnectAccountId:int}")]
    public async Task<IActionResult> RefreshStatus(int stripeConnectAccountId)
    {
        var tenantId = GetTenantId();
        var account = await _context.StripeConnectAccounts
            .FirstOrDefaultAsync(a => a.StripeConnectAccountId == stripeConnectAccountId && a.TenantId == tenantId);
        if (account == null) return NotFound();

        try
        {
            var updated = await _stripeConnectService.RefreshAccountStatusAsync(stripeConnectAccountId);
            return Ok(new
            {
                updated.Status,
                StatusName = ((StripeConnectAccountStatus)updated.Status).ToString(),
                updated.ChargesEnabled,
                updated.PayoutsEnabled,
                updated.DetailsSubmitted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Stripe Connect status {AccountId}", stripeConnectAccountId);
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Generates a one-time login link to the clinic's Stripe Express/Standard dashboard.
    /// </summary>
    [HttpGet("dashboard-link/{stripeConnectAccountId:int}")]
    public async Task<IActionResult> GetDashboardLink(int stripeConnectAccountId)
    {
        var tenantId = GetTenantId();
        var account = await _context.StripeConnectAccounts
            .FirstOrDefaultAsync(a => a.StripeConnectAccountId == stripeConnectAccountId && a.TenantId == tenantId);
        if (account == null) return NotFound();

        try
        {
            var url = await _stripeConnectService.GenerateDashboardLinkAsync(stripeConnectAccountId);
            return Ok(new { Url = url });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating dashboard link {AccountId}", stripeConnectAccountId);
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }
}

// ============================================
// Request DTOs
// ============================================

public class StartOnboardingRequest
{
    public int LocationId { get; set; }
    public string BusinessEmail { get; set; }
}

public class RefreshOnboardingRequest
{
    public int StripeConnectAccountId { get; set; }
}

public class LinkExistingRequest
{
    public int LocationId { get; set; }
    public int StripeConnectAccountId { get; set; }
}
