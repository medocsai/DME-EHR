using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Services;
using Stripe;

namespace EHR.Controllers;

/// <summary>
/// Handles Stripe webhook events for both platform-level and Connect events.
/// AllowAnonymous because Stripe calls this directly — verified via webhook signature.
/// All events are stored in StripeWebhookEvents for idempotency + audit trail.
/// </summary>
[ApiController]
[Route("api/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly EhrDbContext _context;
    private readonly IPaymentService _paymentService;
    private readonly IInstallmentService _installmentService;
    private readonly IStripeConnectService _stripeConnectService;
    private readonly IStripeService _stripeService;
    private readonly IPlatformFeeCalculator _feeCalculator;
    private readonly IEmailService _emailService;
    private readonly EHR.Helpers.EncryptionHelper _encryptionHelper;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        EhrDbContext context,
        IPaymentService paymentService,
        IInstallmentService installmentService,
        IStripeConnectService stripeConnectService,
        IStripeService stripeService,
        IPlatformFeeCalculator feeCalculator,
        IEmailService emailService,
        EHR.Helpers.EncryptionHelper encryptionHelper,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _context = context;
        _paymentService = paymentService;
        _installmentService = installmentService;
        _stripeConnectService = stripeConnectService;
        _stripeService = stripeService;
        _feeCalculator = feeCalculator;
        _emailService = emailService;
        _encryptionHelper = encryptionHelper;
        _config = config;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var signatureHeader = Request.Headers["Stripe-Signature"];

        Event stripeEvent;
        try
        {
            // Try Connect webhook secret first; fall back to platform secret.
            // Distinguish: Connect events have an "account" field at the top level
            // and are signed with ConnectWebhookSecret. Platform-level events are
            // signed with WebhookSecret. Trying both lets one endpoint receive both.
            var connectSecret = _config["Stripe:ConnectWebhookSecret"];
            var platformSecret = _config["Stripe:WebhookSecret"];

            stripeEvent = TryConstructEvent(json, signatureHeader, connectSecret)
                       ?? TryConstructEvent(json, signatureHeader, platformSecret);

            if (stripeEvent == null)
            {
                _logger.LogError("Stripe webhook signature verification failed for both secrets");
                return BadRequest(new { message = "Invalid webhook signature" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Stripe webhook");
            return BadRequest(new { message = "Webhook parsing error" });
        }

        _logger.LogInformation(
            "Stripe webhook received: {EventType} ({EventId}) account={Account}",
            stripeEvent.Type, stripeEvent.Id, stripeEvent.Account ?? "platform");

        // Idempotency check — store event and skip if already processed
        var existingEvent = await _context.StripeWebhookEvents
            .FirstOrDefaultAsync(e => e.StripeEventId == stripeEvent.Id);

        if (existingEvent != null)
        {
            _logger.LogInformation(
                "Event {EventId} already received (status={Status}), skipping reprocessing",
                stripeEvent.Id, existingEvent.Status);
            return Ok();
        }

        var eventLog = new StripeWebhookEvent
        {
            StripeEventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            StripeAccountId = stripeEvent.Account,
            Payload = json,
            Status = 0, // Received
            ReceivedAt = DateTime.UtcNow
        };

        _context.StripeWebhookEvents.Add(eventLog);
        await _context.SaveChangesAsync();

        try
        {
            await DispatchEventAsync(stripeEvent);

            eventLog.Status = 1; // Processed
            eventLog.ProcessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook event {EventId}", stripeEvent.Id);

            eventLog.Status = 2; // Failed
            eventLog.ErrorMessage = ex.ToString();
            eventLog.ProcessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Return 200 to prevent Stripe from retrying for our internal errors
            // (Stripe should retry only on 5xx; we return 200 here to avoid infinite loop on bad data)
            return Ok();
        }
    }

    private Event TryConstructEvent(string json, string signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Contains("REPLACE_WITH"))
            return null;

        try
        {
            // throwOnApiVersionMismatch: false is intentional and permanent.
            // See conventions.md "Stripe.NET API Version Mismatch" for the
            // full rationale. Short version: a Stripe webhook endpoint's API
            // version is immutable after creation, but Stripe.NET upgrades
            // bump the SDK's expected API version. Without this flag, every
            // SDK upgrade breaks every existing webhook endpoint until
            // recreated. Setting it false keeps signature verification fully
            // intact (still HMAC-validated against secret) and just skips
            // the API-version equality check. Core event fields we read
            // (Id, Amount, Metadata, LatestChargeId, Account) are stable
            // across all Stripe API versions, so parsing remains safe.
            return EventUtility.ConstructEvent(json, signatureHeader, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends a receipt email after a successful Stripe payment. Called fire-and-forget
    /// from HandlePaymentIntentSucceeded so email failures never block webhook ack.
    /// Logs any errors but never throws — the payment row is already saved by the time
    /// this runs, so an email failure should not cause Stripe to retry the webhook.
    /// </summary>
    private async Task SendReceiptEmailAsync(int patientId, int tenantId, decimal amount, string paymentIntentId)
    {
        try
        {
            if (patientId <= 0)
            {
                _logger.LogWarning("Skip receipt email for {PI}: no patient id in metadata", paymentIntentId);
                return;
            }
            var patient = await _context.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PatientId == patientId && p.TenantId == tenantId);
            if (patient == null)
            {
                _logger.LogWarning("Skip receipt email for {PI}: patient {PatientId} not found", paymentIntentId, patientId);
                return;
            }
            try { _encryptionHelper.DecryptEntity(patient); } catch { /* already plaintext */ }

            if (string.IsNullOrWhiteSpace(patient.Email))
            {
                _logger.LogInformation("Skip receipt email for {PI}: patient {PatientId} has no email", paymentIntentId, patientId);
                return;
            }

            var patientName = $"{patient.FirstName} {patient.LastName}".Trim();
            // Display "Credit Card" — the underlying card type (debit vs credit) is
            // not surfaced in PaymentIntent metadata reliably enough to switch on.
            var ok = await _emailService.SendPaymentReceiptAsync(
                patient.Email, patientName, amount, "Credit Card", DateTime.UtcNow);
            if (ok)
            {
                _logger.LogInformation("Receipt email sent for {PI} to patient {PatientId}", paymentIntentId, patientId);
            }
            else
            {
                _logger.LogWarning("Receipt email returned false for {PI} (likely Email:Enabled=false or SMTP failure)", paymentIntentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt email failed for PaymentIntent {PI}; payment is already recorded.", paymentIntentId);
        }
    }

    private async Task DispatchEventAsync(Event stripeEvent)
    {
        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                await HandlePaymentIntentSucceeded(stripeEvent);
                break;

            case "payment_intent.payment_failed":
                await HandlePaymentIntentFailed(stripeEvent);
                break;

            case "charge.refunded":
                await HandleChargeRefunded(stripeEvent);
                break;

            case "charge.dispute.created":
                await HandleDisputeCreated(stripeEvent);
                break;

            case "charge.dispute.closed":
                await HandleDisputeClosed(stripeEvent);
                break;

            case "account.updated":
                await HandleAccountUpdated(stripeEvent);
                break;

            case "account.application.deauthorized":
                await HandleAccountDeauthorized(stripeEvent);
                break;

            default:
                _logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    // ============================================
    // Payment intent handlers
    // ============================================

    private async Task HandlePaymentIntentSucceeded(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null) return;

        var metadata = paymentIntent.Metadata;
        var paymentType = metadata.GetValueOrDefault("paymentType", "");
        int.TryParse(metadata.GetValueOrDefault("patientId", "0"), out var patientId);
        int.TryParse(metadata.GetValueOrDefault("tenantId", "0"), out var tenantId);
        int.TryParse(metadata.GetValueOrDefault("locationId", "0"), out var locationId);
        // appointmentId is best-effort linkage set by the portal Pay Now flow when
        // the patient has an outstanding-copay checked-in appointment. Zero means
        // no link (general patient payment, not tied to a specific visit).
        int.TryParse(metadata.GetValueOrDefault("appointmentId", "0"), out var appointmentId);
        // Metadata-supplied fee values are kept ONLY for reconciliation logging —
        // the values we actually act on are recomputed server-side below to
        // prevent fee tampering by anyone with Stripe API access.
        int.TryParse(metadata.GetValueOrDefault("clinicTotalFeeCents", "0"), out var metaClinicFee);
        int.TryParse(metadata.GetValueOrDefault("applicationFeeCents", "0"), out var metaAppFee);

        _logger.LogInformation(
            "Payment succeeded: {PaymentIntentId}, type={PaymentType}, patient={PatientId}, location={LocationId}",
            paymentIntent.Id, paymentType, patientId, locationId);

        // Look up the connected account from the location
        int? connectAccountId = null;
        if (locationId > 0)
        {
            var location = await _context.Locations.FindAsync(locationId);
            connectAccountId = location?.StripeConnectAccountId;
        }

        switch (paymentType)
        {
            case "Full":
                var amountDollars = paymentIntent.Amount / 100m;

                // Recompute fees server-side. paymentIntent.Amount is signed by
                // Stripe and trustworthy; everything else in metadata is not.
                // Detect card-present vs online from metadata's paymentMethod
                // hint (defaults to Online). Even if the hint is forged, the
                // cost difference is bounded by config and audited below.
                var methodHint = metadata.GetValueOrDefault("paymentMethod", "Online");
                var resolvedMethod = string.Equals(methodHint, "CardPresent", StringComparison.OrdinalIgnoreCase)
                    ? StripePaymentMethodType.CardPresent
                    : StripePaymentMethodType.Online;

                int serverClinicFee = metaClinicFee;
                int serverAppFee = metaAppFee;
                int serverNetToClinic = (int)paymentIntent.Amount - metaClinicFee;
                int serverStripeFee = metaClinicFee - metaAppFee;

                if (locationId > 0)
                {
                    try
                    {
                        var fees = await _feeCalculator.CalculateAsync(
                            locationId, resolvedMethod, (int)paymentIntent.Amount);
                        serverClinicFee = fees.ClinicTotalFeeCents;
                        serverAppFee = fees.ApplicationFeeCents;
                        serverNetToClinic = fees.NetToClinicCents;
                        serverStripeFee = fees.EstimatedStripeFeeCents;

                        if (serverClinicFee != metaClinicFee || serverAppFee != metaAppFee)
                        {
                            _logger.LogWarning(
                                "Fee tampering check FAILED for {PaymentIntentId}: " +
                                "metadata clinicFee={MetaClinic}/appFee={MetaApp} vs " +
                                "server-computed clinicFee={ServerClinic}/appFee={ServerApp}. " +
                                "Using server-computed values.",
                                paymentIntent.Id, metaClinicFee, metaAppFee, serverClinicFee, serverAppFee);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Server-side fee recompute failed for {PaymentIntentId}; falling back to metadata. " +
                            "Investigate before next deploy.", paymentIntent.Id);
                    }
                }

                await _paymentService.CompleteStripePaymentAsync(
                    paymentIntent.Id, patientId, tenantId, amountDollars,
                    locationId: locationId > 0 ? locationId : null,
                    stripeConnectAccountId: connectAccountId,
                    clinicTotalFeeCents: serverClinicFee,
                    stripeProcessingFeeCents: serverStripeFee,
                    applicationFeeCents: serverAppFee,
                    netToClinicCents: serverNetToClinic,
                    stripeChargeId: paymentIntent.LatestChargeId,
                    appointmentId: appointmentId > 0 ? appointmentId : null);

                // Fire-and-forget receipt email. We don't await the result for failure
                // because email delivery shouldn't block webhook acknowledgement to
                // Stripe (Stripe would retry on non-200 and create duplicate ledger
                // entries despite idempotency check). Email failures are logged inside
                // SendReceiptEmailAsync; the payment row is already saved either way.
                _ = SendReceiptEmailAsync(patientId, tenantId, amountDollars, paymentIntent.Id);
                break;

            case "FirstInstallment":
            case "Installment":
                await _installmentService.HandlePaymentSuccessAsync(
                    paymentIntent.Id, tenantId, stripeEvent.Account);
                break;

            default:
                _logger.LogWarning("Unknown payment type in metadata: {PaymentType}", paymentType);
                break;
        }
    }

    private async Task HandlePaymentIntentFailed(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null) return;

        var paymentType = paymentIntent.Metadata.GetValueOrDefault("paymentType", "");
        _logger.LogWarning("Payment failed: {Id}, type={Type}", paymentIntent.Id, paymentType);

        if (paymentType == "Installment" || paymentType == "FirstInstallment")
        {
            await _installmentService.HandlePaymentFailureAsync(paymentIntent.Id);
        }
    }

    // ============================================
    // Refund handler
    // ============================================

    private async Task HandleChargeRefunded(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Stripe.Charge;
        if (charge == null) return;

        // Find the original Payment by StripeChargeId or by PaymentIntent
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.StripeChargeId == charge.Id || p.StripePaymentIntentId == charge.PaymentIntentId);

        if (payment == null)
        {
            _logger.LogWarning("Refund webhook for unknown charge {ChargeId}", charge.Id);
            return;
        }

        // Process each refund on the charge (Stripe sends the full refund list)
        if (charge.Refunds == null) return;

        foreach (var refund in charge.Refunds.Data)
        {
            // Skip if we already have this refund
            var exists = await _context.PaymentRefunds
                .AnyAsync(r => r.StripeRefundId == refund.Id);
            if (exists) continue;

            var refundRow = new PaymentRefund
            {
                PaymentId = payment.PaymentId,
                TenantId = payment.TenantId,
                StripeRefundId = refund.Id,
                StripeChargeId = charge.Id,
                AmountCents = (int)refund.Amount,
                Reason = refund.Reason,
                Status = MapRefundStatus(refund.Status),
                RefundedAt = refund.Created,
                CreatedByExternal = true // assume from clinic dashboard
            };
            _context.PaymentRefunds.Add(refundRow);

            // Reverse ledger entry
            var reversalEntry = new PatientLedger
            {
                TenantId = payment.TenantId,
                PatientId = payment.PatientId,
                EntryType = 3, // Adjustment / refund
                PaymentId = payment.PaymentId,
                TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Amount = refund.Amount / 100m, // Positive = increases balance back
                Description = $"Refund issued: ${refund.Amount / 100m:F2}",
                CreatedAt = DateTime.UtcNow
            };
            _context.PatientLedgers.Add(reversalEntry);

            _logger.LogInformation(
                "Refund {RefundId} processed for payment {PaymentId}: ${Amount}",
                refund.Id, payment.PaymentId, refund.Amount / 100m);

            // Notify patient
            try
            {
                var patient = await _context.Patients.FindAsync(payment.PatientId);
                if (patient != null && !string.IsNullOrEmpty(patient.Email))
                {
                    // Email notification handled via dedicated method on EmailService later if needed
                    _logger.LogInformation("Refund notification sent to patient {PatientId}", payment.PatientId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send refund notification");
            }
        }

        await _context.SaveChangesAsync();
    }

    private static int MapRefundStatus(string stripeStatus) => stripeStatus switch
    {
        "succeeded" => 1,
        "failed" => 2,
        "canceled" => 3,
        _ => 0 // Pending
    };

    // ============================================
    // Dispute handlers
    // ============================================

    private async Task HandleDisputeCreated(Event stripeEvent)
    {
        var dispute = stripeEvent.Data.Object as Dispute;
        if (dispute == null) return;

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.StripeChargeId == dispute.ChargeId
                                    || p.StripePaymentIntentId == dispute.PaymentIntentId);

        if (payment == null)
        {
            _logger.LogWarning("Dispute {DisputeId} for unknown charge {ChargeId}", dispute.Id, dispute.ChargeId);
            return;
        }

        payment.HasOpenDispute = true;
        await _context.SaveChangesAsync();

        _logger.LogWarning(
            "Dispute opened for payment {PaymentId}: ${Amount} ({Reason})",
            payment.PaymentId, dispute.Amount / 100m, dispute.Reason);
    }

    private async Task HandleDisputeClosed(Event stripeEvent)
    {
        var dispute = stripeEvent.Data.Object as Dispute;
        if (dispute == null) return;

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.StripeChargeId == dispute.ChargeId
                                    || p.StripePaymentIntentId == dispute.PaymentIntentId);

        if (payment == null) return;

        payment.HasOpenDispute = false;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Dispute closed for payment {PaymentId}: status={Status}",
            payment.PaymentId, dispute.Status);
    }

    // ============================================
    // Account event handlers
    // ============================================

    private async Task HandleAccountUpdated(Event stripeEvent)
    {
        var account = stripeEvent.Data.Object as Stripe.Account;
        if (account == null) return;

        await _stripeConnectService.HandleAccountUpdatedAsync(account.Id, account);
    }

    private async Task HandleAccountDeauthorized(Event stripeEvent)
    {
        // For deauthorized, the account ID is on the event itself
        var accountId = stripeEvent.Account;
        if (string.IsNullOrEmpty(accountId)) return;

        await _stripeConnectService.HandleAccountDeauthorizedAsync(accountId);
    }
}
