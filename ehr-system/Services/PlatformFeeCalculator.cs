using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

public enum StripePaymentMethodType
{
    Online = 0,
    CardPresent = 1,
    Ach = 2
}

public class FeeBreakdown
{
    /// <summary>Total clinic-facing fee in cents (e.g., 3.9% + $0.50 of amount).</summary>
    public int ClinicTotalFeeCents { get; set; }

    /// <summary>Estimated Stripe processing fee in cents (used for application_fee calculation).</summary>
    public int EstimatedStripeFeeCents { get; set; }

    /// <summary>Application fee in cents (MEDOCS profit). What we pass to Stripe.</summary>
    public int ApplicationFeeCents { get; set; }

    /// <summary>Net amount clinic will receive in cents.</summary>
    public int NetToClinicCents { get; set; }
}

public interface IPlatformFeeCalculator
{
    /// <summary>
    /// Calculates the fee breakdown for a given amount, location, and payment method type.
    /// Resolves: Location override → Global default from appsettings.json.
    /// </summary>
    Task<FeeBreakdown> CalculateAsync(int locationId, StripePaymentMethodType paymentType, int amountCents);
}

public class PlatformFeeCalculator : IPlatformFeeCalculator
{
    private readonly EhrDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformFeeCalculator> _logger;

    public PlatformFeeCalculator(EhrDbContext context, IConfiguration config, ILogger<PlatformFeeCalculator> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    public async Task<FeeBreakdown> CalculateAsync(int locationId, StripePaymentMethodType paymentType, int amountCents)
    {
        var location = await _context.Locations.FirstOrDefaultAsync(l => l.LocationId == locationId)
            ?? throw new InvalidOperationException($"Location {locationId} not found");

        decimal clinicPercent;
        int clinicFlat;
        decimal stripePercent;
        int stripeFlat;

        if (paymentType == StripePaymentMethodType.CardPresent)
        {
            clinicPercent = location.CardPresentFeePercent
                ?? _config.GetValue<decimal>("PlatformFees:DefaultCardPresentFeePercent");
            clinicFlat = location.CardPresentFeeFlatCents
                ?? _config.GetValue<int>("PlatformFees:DefaultCardPresentFeeFlatCents");
            stripePercent = _config.GetValue<decimal>("PlatformFees:StripeCardPresentFeePercent");
            stripeFlat = _config.GetValue<int>("PlatformFees:StripeCardPresentFeeFlatCents");
        }
        else
        {
            // Online (default for Phase 1)
            clinicPercent = location.OnlineFeePercent
                ?? _config.GetValue<decimal>("PlatformFees:DefaultOnlineFeePercent");
            clinicFlat = location.OnlineFeeFlatCents
                ?? _config.GetValue<int>("PlatformFees:DefaultOnlineFeeFlatCents");
            stripePercent = _config.GetValue<decimal>("PlatformFees:StripeOnlineFeePercent");
            stripeFlat = _config.GetValue<int>("PlatformFees:StripeOnlineFeeFlatCents");
        }

        // Total clinic-facing fee in cents
        var clinicTotal = (int)Math.Round(amountCents * clinicPercent / 100m) + clinicFlat;

        // Stripe's estimated fee
        var stripeFee = (int)Math.Round(amountCents * stripePercent / 100m) + stripeFlat;

        // Application fee = clinic total - stripe fee
        var appFee = clinicTotal - stripeFee;

        if (appFee < 0)
        {
            _logger.LogWarning(
                "Application fee negative for location {LocationId} amount {Amount}: clinic={Clinic}, stripe={Stripe}, app={App}. Setting to 0.",
                locationId, amountCents, clinicTotal, stripeFee, appFee);
            appFee = 0;
        }

        return new FeeBreakdown
        {
            ClinicTotalFeeCents = clinicTotal,
            EstimatedStripeFeeCents = stripeFee,
            ApplicationFeeCents = appFee,
            NetToClinicCents = amountCents - clinicTotal
        };
    }
}
