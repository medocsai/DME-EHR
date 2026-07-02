using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace EHR.Services;

public enum StripeConnectAccountStatus
{
    Pending = 0,
    Active = 1,
    Restricted = 2,
    Disconnected = 3
}

public interface IStripeConnectService
{
    /// <summary>
    /// Creates a new Stripe Standard connected account for a tenant and returns the local DB row.
    /// Caller should then call GenerateOnboardingLinkAsync to start the onboarding flow.
    /// </summary>
    Task<StripeConnectAccount> CreateAccountAsync(int tenantId, string businessEmail, int connectedByUserId);

    /// <summary>
    /// Generates a Stripe Account Link URL for completing onboarding.
    /// The link is single-use and expires after a short time.
    /// </summary>
    Task<string> GenerateOnboardingLinkAsync(int stripeConnectAccountId);

    /// <summary>
    /// Generates a one-time login link to the clinic's Stripe Express/Standard dashboard.
    /// Standard accounts redirect to dashboard.stripe.com.
    /// </summary>
    Task<string> GenerateDashboardLinkAsync(int stripeConnectAccountId);

    /// <summary>
    /// Refreshes account status by querying Stripe API. Updates DB.
    /// </summary>
    Task<StripeConnectAccount> RefreshAccountStatusAsync(int stripeConnectAccountId);

    /// <summary>
    /// Disconnects (deauthorizes) the account from our platform.
    /// Calls Stripe API to revoke access, marks DB row as Disconnected.
    /// </summary>
    Task DisconnectAsync(int stripeConnectAccountId, int userId);

    Task<List<StripeConnectAccount>> GetAccountsForTenantAsync(int tenantId);

    Task<StripeConnectAccount?> GetByLocationAsync(int locationId);

    Task<StripeConnectAccount?> GetByStripeAccountIdAsync(string stripeAccountId);

    /// <summary>
    /// Links an existing connected account to a location (e.g., when adding a new location to a tenant).
    /// </summary>
    Task LinkLocationToAccountAsync(int locationId, int stripeConnectAccountId);

    /// <summary>
    /// Unlinks a location from its current Stripe account (sets FK to null).
    /// Does NOT disconnect the account itself.
    /// </summary>
    Task UnlinkLocationAsync(int locationId);

    // Webhook handlers
    Task HandleAccountUpdatedAsync(string stripeAccountId, Stripe.Account stripeAccount);
    Task HandleAccountDeauthorizedAsync(string stripeAccountId);
}

public class StripeConnectService : IStripeConnectService
{
    private readonly EhrDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeConnectService> _logger;

    public StripeConnectService(EhrDbContext context, IConfiguration config, ILogger<StripeConnectService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;

        // Ensure platform API key is set (StripeService also sets this in its constructor)
        if (string.IsNullOrEmpty(StripeConfiguration.ApiKey))
        {
            StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];
        }
    }

    public async Task<StripeConnectAccount> CreateAccountAsync(int tenantId, string businessEmail, int connectedByUserId)
    {
        // Check if tenant already has any accounts
        var existing = await _context.StripeConnectAccounts
            .Where(a => a.TenantId == tenantId && a.Status != (int)StripeConnectAccountStatus.Disconnected)
            .ToListAsync();

        var service = new AccountService();
        var account = await service.CreateAsync(new AccountCreateOptions
        {
            Type = "standard",
            Email = businessEmail,
            Country = "US",
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            },
            Metadata = new Dictionary<string, string>
            {
                { "tenantId", tenantId.ToString() },
                { "platform", "MEDOCS" }
            }
        });

        var dbRow = new StripeConnectAccount
        {
            TenantId = tenantId,
            StripeAccountId = account.Id,
            DisplayName = account.BusinessProfile?.Name ?? account.Email ?? "Pending",
            BusinessEmail = account.Email,
            Country = account.Country ?? "US",
            DefaultCurrency = account.DefaultCurrency ?? "usd",
            Status = (int)StripeConnectAccountStatus.Pending,
            ChargesEnabled = account.ChargesEnabled,
            PayoutsEnabled = account.PayoutsEnabled,
            DetailsSubmitted = account.DetailsSubmitted,
            ConnectedAt = DateTime.UtcNow,
            ConnectedByUserId = connectedByUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.StripeConnectAccounts.Add(dbRow);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created Stripe Connect account {AccountId} for tenant {TenantId}",
            account.Id, tenantId);

        return dbRow;
    }

    public async Task<string> GenerateOnboardingLinkAsync(int stripeConnectAccountId)
    {
        var account = await _context.StripeConnectAccounts.FindAsync(stripeConnectAccountId)
            ?? throw new InvalidOperationException($"StripeConnectAccount {stripeConnectAccountId} not found");

        var returnUrl = _config["Stripe:OnboardingReturnUrl"];
        var refreshUrl = _config["Stripe:OnboardingRefreshUrl"];

        var service = new AccountLinkService();
        var link = await service.CreateAsync(new AccountLinkCreateOptions
        {
            Account = account.StripeAccountId,
            RefreshUrl = refreshUrl,
            ReturnUrl = returnUrl,
            Type = "account_onboarding"
        });

        _logger.LogInformation("Generated onboarding link for account {AccountId}", account.StripeAccountId);
        return link.Url;
    }

    public async Task<string> GenerateDashboardLinkAsync(int stripeConnectAccountId)
    {
        var account = await _context.StripeConnectAccounts.FindAsync(stripeConnectAccountId)
            ?? throw new InvalidOperationException($"StripeConnectAccount {stripeConnectAccountId} not found");

        // Standard Connect accounts use dashboard.stripe.com directly with their own credentials.
        // No special login link is needed; we just send them to their account dashboard.
        return $"https://dashboard.stripe.com/{account.StripeAccountId}";
    }

    public async Task<StripeConnectAccount> RefreshAccountStatusAsync(int stripeConnectAccountId)
    {
        var dbRow = await _context.StripeConnectAccounts.FindAsync(stripeConnectAccountId)
            ?? throw new InvalidOperationException($"StripeConnectAccount {stripeConnectAccountId} not found");

        var service = new AccountService();
        var stripeAccount = await service.GetAsync(dbRow.StripeAccountId);

        UpdateAccountStateFromStripe(dbRow, stripeAccount);
        await _context.SaveChangesAsync();

        return dbRow;
    }

    public async Task DisconnectAsync(int stripeConnectAccountId, int userId)
    {
        var dbRow = await _context.StripeConnectAccounts.FindAsync(stripeConnectAccountId)
            ?? throw new InvalidOperationException($"StripeConnectAccount {stripeConnectAccountId} not found");

        // Best-effort: ask Stripe to disconnect on their side
        try
        {
            var service = new OAuthTokenService();
            await service.DeauthorizeAsync(new OAuthDeauthorizeOptions
            {
                ClientId = _config["Stripe:PlatformAccountId"],
                StripeUserId = dbRow.StripeAccountId
            });
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Failed to deauthorize at Stripe (will mark disconnected anyway): {AccountId}", dbRow.StripeAccountId);
        }

        dbRow.Status = (int)StripeConnectAccountStatus.Disconnected;
        dbRow.DisconnectedAt = DateTime.UtcNow;
        dbRow.UpdatedAt = DateTime.UtcNow;

        // Unlink any locations using this account
        var linkedLocations = await _context.Locations
            .Where(l => l.StripeConnectAccountId == stripeConnectAccountId)
            .ToListAsync();
        foreach (var loc in linkedLocations)
        {
            loc.StripeConnectAccountId = null;
        }

        await _context.SaveChangesAsync();

        _logger.LogWarning("Disconnected Stripe account {AccountId}, unlinked {Count} locations",
            dbRow.StripeAccountId, linkedLocations.Count);
    }

    public async Task<List<StripeConnectAccount>> GetAccountsForTenantAsync(int tenantId)
    {
        return await _context.StripeConnectAccounts
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.ConnectedAt)
            .ToListAsync();
    }

    public async Task<StripeConnectAccount?> GetByLocationAsync(int locationId)
    {
        var location = await _context.Locations
            .Include(l => l.StripeConnectAccount)
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        return location?.StripeConnectAccount;
    }

    public async Task<StripeConnectAccount?> GetByStripeAccountIdAsync(string stripeAccountId)
    {
        return await _context.StripeConnectAccounts
            .FirstOrDefaultAsync(a => a.StripeAccountId == stripeAccountId);
    }

    public async Task LinkLocationToAccountAsync(int locationId, int stripeConnectAccountId)
    {
        var location = await _context.Locations.FindAsync(locationId)
            ?? throw new InvalidOperationException($"Location {locationId} not found");

        var account = await _context.StripeConnectAccounts.FindAsync(stripeConnectAccountId)
            ?? throw new InvalidOperationException($"StripeConnectAccount {stripeConnectAccountId} not found");

        if (account.TenantId != location.TenantId)
            throw new InvalidOperationException("Cannot link a Stripe account from a different tenant");

        location.StripeConnectAccountId = stripeConnectAccountId;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Linked location {LocationId} to Stripe account {AccountId}",
            locationId, account.StripeAccountId);
    }

    public async Task UnlinkLocationAsync(int locationId)
    {
        var location = await _context.Locations.FindAsync(locationId)
            ?? throw new InvalidOperationException($"Location {locationId} not found");

        location.StripeConnectAccountId = null;
        await _context.SaveChangesAsync();
    }

    public async Task HandleAccountUpdatedAsync(string stripeAccountId, Stripe.Account stripeAccount)
    {
        var dbRow = await GetByStripeAccountIdAsync(stripeAccountId);
        if (dbRow == null)
        {
            _logger.LogWarning("Received account.updated for unknown account {AccountId}", stripeAccountId);
            return;
        }

        UpdateAccountStateFromStripe(dbRow, stripeAccount);
        dbRow.LastWebhookAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task HandleAccountDeauthorizedAsync(string stripeAccountId)
    {
        var dbRow = await GetByStripeAccountIdAsync(stripeAccountId);
        if (dbRow == null)
        {
            _logger.LogWarning("Received deauthorize for unknown account {AccountId}", stripeAccountId);
            return;
        }

        dbRow.Status = (int)StripeConnectAccountStatus.Disconnected;
        dbRow.DisconnectedAt = DateTime.UtcNow;
        dbRow.LastWebhookAt = DateTime.UtcNow;
        dbRow.UpdatedAt = DateTime.UtcNow;

        // Unlink locations
        var locations = await _context.Locations
            .Where(l => l.StripeConnectAccountId == dbRow.StripeConnectAccountId)
            .ToListAsync();
        foreach (var loc in locations)
        {
            loc.StripeConnectAccountId = null;
        }

        await _context.SaveChangesAsync();

        _logger.LogWarning("Account {AccountId} deauthorized via webhook, unlinked {Count} locations",
            stripeAccountId, locations.Count);
    }

    private void UpdateAccountStateFromStripe(StripeConnectAccount dbRow, Stripe.Account stripeAccount)
    {
        dbRow.ChargesEnabled = stripeAccount.ChargesEnabled;
        dbRow.PayoutsEnabled = stripeAccount.PayoutsEnabled;
        dbRow.DetailsSubmitted = stripeAccount.DetailsSubmitted;
        dbRow.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(stripeAccount.BusinessProfile?.Name))
        {
            dbRow.DisplayName = stripeAccount.BusinessProfile.Name;
        }

        if (!string.IsNullOrEmpty(stripeAccount.Email))
        {
            dbRow.BusinessEmail = stripeAccount.Email;
        }

        // Status logic
        if (dbRow.Status == (int)StripeConnectAccountStatus.Disconnected)
        {
            // Don't override Disconnected
            return;
        }

        if (stripeAccount.ChargesEnabled && stripeAccount.PayoutsEnabled && stripeAccount.DetailsSubmitted)
        {
            dbRow.Status = (int)StripeConnectAccountStatus.Active;
        }
        else if (stripeAccount.Requirements?.DisabledReason != null)
        {
            dbRow.Status = (int)StripeConnectAccountStatus.Restricted;
        }
        else
        {
            dbRow.Status = (int)StripeConnectAccountStatus.Pending;
        }
    }
}
