using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using Stripe;

namespace EHR.Services;

/// <summary>
/// Connect-aware Stripe service.
/// All payment operations route to a specific connected account via the Stripe-Account header.
/// Patients use one Stripe customer per (patient → connected account) pairing.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Creates a Stripe customer ON the connected account for the location.
    /// Saves the customer ID to Patients.StripeCustomerId.
    /// </summary>
    Task<string> CreateCustomerForLocationAsync(int patientId, int locationId, string email, string name);

    /// <summary>
    /// Gets the patient's existing Stripe customer ID, or creates one if missing.
    /// </summary>
    Task<string> GetOrCreateCustomerAsync(int patientId, int locationId);

    /// <summary>
    /// Creates a PaymentIntent on the connected account for the location.
    /// Adds application_fee_amount and on_behalf_of for Direct Charges.
    /// Returns (client_secret, payment_intent_id, fee breakdown).
    /// </summary>
    Task<(string ClientSecret, string PaymentIntentId, FeeBreakdown Fees)> CreatePaymentIntentAsync(
        int locationId, int amountCents, string customerId, Dictionary<string, string> metadata);

    /// <summary>
    /// Creates a SetupIntent on the connected account (for saving payment methods, e.g., for installments).
    /// </summary>
    Task<string> CreateSetupIntentAsync(int locationId, string customerId);

    /// <summary>
    /// Saves a payment method to the customer on the connected account.
    /// </summary>
    Task SavePaymentMethodAsync(int locationId, string customerId, string paymentMethodId);

    /// <summary>
    /// Charges a saved payment method off-session for installment auto-charging.
    /// Routes to the connected account; calculates application fee based on amount and location.
    /// Includes idempotency key to prevent double charges.
    /// </summary>
    Task<(PaymentIntent PaymentIntent, FeeBreakdown Fees)> ChargeOffSessionAsync(
        int stripeConnectAccountId, int locationId, string customerId, string paymentMethodId,
        int amountCents, string idempotencyKey, Dictionary<string, string> metadata);

    /// <summary>
    /// Retrieves a PaymentIntent from a specific connected account.
    /// </summary>
    Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId, string stripeAccountId);

    /// <summary>
    /// Retrieves a balance transaction (for actual fee amounts after a charge completes).
    /// </summary>
    Task<BalanceTransaction?> GetBalanceTransactionAsync(string balanceTransactionId, string stripeAccountId);

    string GetPublishableKey();
}

public class StripeService : IStripeService
{
    private readonly EhrDbContext _context;
    private readonly IConfiguration _config;
    private readonly IPlatformFeeCalculator _feeCalculator;
    private readonly ILogger<StripeService> _logger;

    public StripeService(
        EhrDbContext context,
        IConfiguration config,
        IPlatformFeeCalculator feeCalculator,
        ILogger<StripeService> logger)
    {
        _context = context;
        _config = config;
        _feeCalculator = feeCalculator;
        _logger = logger;

        // Set platform API key (used for ALL Stripe API calls; Stripe-Account header overrides per call)
        StripeConfiguration.ApiKey = _config["Stripe:SecretKey"];
    }

    public string GetPublishableKey() => _config["Stripe:PublishableKey"];

    // ============================================
    // Customer management (per connected account)
    // ============================================

    public async Task<string> CreateCustomerForLocationAsync(int patientId, int locationId, string email, string name)
    {
        var stripeAccountId = await GetStripeAccountIdForLocationAsync(locationId);

        var requestOptions = new RequestOptions { StripeAccount = stripeAccountId };

        var service = new CustomerService();
        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = name,
            Metadata = new Dictionary<string, string>
            {
                { "patientId", patientId.ToString() },
                { "platform", "MEDOCS" }
            }
        }, requestOptions);

        // Save StripeCustomerId to patient record
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient != null)
        {
            patient.StripeCustomerId = customer.Id;
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Created Stripe customer {CustomerId} for patient {PatientId} on account {AccountId}",
            customer.Id, patientId, stripeAccountId);

        return customer.Id;
    }

    public async Task<string> GetOrCreateCustomerAsync(int patientId, int locationId)
    {
        var patient = await _context.Patients.FindAsync(patientId)
            ?? throw new InvalidOperationException($"Patient {patientId} not found");

        if (!string.IsNullOrEmpty(patient.StripeCustomerId))
        {
            return patient.StripeCustomerId;
        }

        var fullName = $"{patient.FirstName} {patient.LastName}".Trim();
        return await CreateCustomerForLocationAsync(patientId, locationId, patient.Email, fullName);
    }

    // ============================================
    // PaymentIntents (Direct Charges with platform fee)
    // ============================================

    public async Task<(string ClientSecret, string PaymentIntentId, FeeBreakdown Fees)> CreatePaymentIntentAsync(
        int locationId, int amountCents, string customerId, Dictionary<string, string> metadata)
    {
        var stripeAccountId = await GetStripeAccountIdForLocationAsync(locationId);
        var fees = await _feeCalculator.CalculateAsync(locationId, StripePaymentMethodType.Online, amountCents);

        // Add location/payment type to metadata
        metadata["locationId"] = locationId.ToString();
        metadata["paymentMethodType"] = ((int)StripePaymentMethodType.Online).ToString();
        metadata["clinicTotalFeeCents"] = fees.ClinicTotalFeeCents.ToString();
        metadata["applicationFeeCents"] = fees.ApplicationFeeCents.ToString();

        var requestOptions = new RequestOptions { StripeAccount = stripeAccountId };

        var service = new PaymentIntentService();
        var paymentIntent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = amountCents,
            Currency = "usd",
            Customer = customerId,
            Metadata = metadata,
            ApplicationFeeAmount = fees.ApplicationFeeCents > 0 ? fees.ApplicationFeeCents : null,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            }
        }, requestOptions);

        _logger.LogInformation(
            "Created PaymentIntent {Id} on account {Account}: amount={Amount} fee={Fee}",
            paymentIntent.Id, stripeAccountId, amountCents, fees.ApplicationFeeCents);

        return (paymentIntent.ClientSecret, paymentIntent.Id, fees);
    }

    public async Task<string> CreateSetupIntentAsync(int locationId, string customerId)
    {
        var stripeAccountId = await GetStripeAccountIdForLocationAsync(locationId);
        var requestOptions = new RequestOptions { StripeAccount = stripeAccountId };

        var service = new SetupIntentService();
        var setupIntent = await service.CreateAsync(new SetupIntentCreateOptions
        {
            Customer = customerId,
            AutomaticPaymentMethods = new SetupIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            }
        }, requestOptions);

        _logger.LogInformation(
            "Created SetupIntent {Id} for customer {Customer} on account {Account}",
            setupIntent.Id, customerId, stripeAccountId);

        return setupIntent.ClientSecret;
    }

    public async Task SavePaymentMethodAsync(int locationId, string customerId, string paymentMethodId)
    {
        var stripeAccountId = await GetStripeAccountIdForLocationAsync(locationId);
        var requestOptions = new RequestOptions { StripeAccount = stripeAccountId };

        var pmService = new PaymentMethodService();
        await pmService.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions
        {
            Customer = customerId
        }, requestOptions);

        var customerService = new CustomerService();
        await customerService.UpdateAsync(customerId, new CustomerUpdateOptions
        {
            InvoiceSettings = new CustomerInvoiceSettingsOptions
            {
                DefaultPaymentMethod = paymentMethodId
            }
        }, requestOptions);

        _logger.LogInformation(
            "Saved payment method {Pm} for customer {Customer} on account {Account}",
            paymentMethodId, customerId, stripeAccountId);
    }

    // ============================================
    // Off-session charges (installments)
    // ============================================

    public async Task<(PaymentIntent PaymentIntent, FeeBreakdown Fees)> ChargeOffSessionAsync(
        int stripeConnectAccountId, int locationId, string customerId, string paymentMethodId,
        int amountCents, string idempotencyKey, Dictionary<string, string> metadata)
    {
        var account = await _context.StripeConnectAccounts.FindAsync(stripeConnectAccountId)
            ?? throw new InvalidOperationException($"StripeConnectAccount {stripeConnectAccountId} not found");

        if (account.Status != (int)StripeConnectAccountStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot charge — Stripe account {account.StripeAccountId} status is {(StripeConnectAccountStatus)account.Status}");
        }

        var fees = await _feeCalculator.CalculateAsync(locationId, StripePaymentMethodType.Online, amountCents);

        metadata["locationId"] = locationId.ToString();
        metadata["paymentMethodType"] = ((int)StripePaymentMethodType.Online).ToString();
        metadata["clinicTotalFeeCents"] = fees.ClinicTotalFeeCents.ToString();
        metadata["applicationFeeCents"] = fees.ApplicationFeeCents.ToString();

        var requestOptions = new RequestOptions
        {
            StripeAccount = account.StripeAccountId,
            IdempotencyKey = idempotencyKey
        };

        var service = new PaymentIntentService();
        var paymentIntent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = amountCents,
            Currency = "usd",
            Customer = customerId,
            PaymentMethod = paymentMethodId,
            OffSession = true,
            Confirm = true,
            Metadata = metadata,
            ApplicationFeeAmount = fees.ApplicationFeeCents > 0 ? fees.ApplicationFeeCents : null
        }, requestOptions);

        _logger.LogInformation(
            "Off-session charge {Id} on account {Account}: amount={Amount} fee={Fee} idempotency={Key}",
            paymentIntent.Id, account.StripeAccountId, amountCents, fees.ApplicationFeeCents, idempotencyKey);

        return (paymentIntent, fees);
    }

    // ============================================
    // Lookups
    // ============================================

    public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId, string stripeAccountId)
    {
        var requestOptions = new RequestOptions { StripeAccount = stripeAccountId };
        var service = new PaymentIntentService();
        return await service.GetAsync(paymentIntentId, requestOptions: requestOptions);
    }

    public async Task<BalanceTransaction?> GetBalanceTransactionAsync(string balanceTransactionId, string stripeAccountId)
    {
        if (string.IsNullOrEmpty(balanceTransactionId))
            return null;

        try
        {
            var requestOptions = new RequestOptions { StripeAccount = stripeAccountId };
            var service = new BalanceTransactionService();
            return await service.GetAsync(balanceTransactionId, requestOptions: requestOptions);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch balance transaction {Id}", balanceTransactionId);
            return null;
        }
    }

    // ============================================
    // Internal helpers
    // ============================================

    private async Task<string> GetStripeAccountIdForLocationAsync(int locationId)
    {
        var location = await _context.Locations
            .Include(l => l.StripeConnectAccount)
            .FirstOrDefaultAsync(l => l.LocationId == locationId)
            ?? throw new InvalidOperationException($"Location {locationId} not found");

        if (location.StripeConnectAccountId == null || location.StripeConnectAccount == null)
        {
            throw new InvalidOperationException(
                $"Location {locationId} ({location.Name}) does not have a connected Stripe account. Payment is not available at this location.");
        }

        if (location.StripeConnectAccount.Status != (int)StripeConnectAccountStatus.Active)
        {
            throw new InvalidOperationException(
                $"Location {locationId} ({location.Name}) Stripe account is not active (status: {(StripeConnectAccountStatus)location.StripeConnectAccount.Status}).");
        }

        return location.StripeConnectAccount.StripeAccountId;
    }
}
