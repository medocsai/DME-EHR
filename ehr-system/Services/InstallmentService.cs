using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface IInstallmentService
{
    Task<InstallmentPlanDto> CreatePlanAsync(int patientId, int tenantId, InstallmentPlanCreateDto dto, int? createdBy = null);
    Task<InstallmentPlanDto> GetActivePlanByPatientAsync(int patientId, int tenantId);
    Task ProcessDueInstallmentsAsync();
    Task HandlePaymentSuccessAsync(string stripePaymentIntentId, int tenantId, string stripeAccountId = null);
    Task HandlePaymentFailureAsync(string stripePaymentIntentId);
    Task CancelPlanAsync(int planId, int tenantId);
}

/// <summary>
/// Connect-aware installment plan service.
/// Each plan is scoped to a Location and routes charges to that location's Stripe Connect account.
/// Background processor uses optimistic concurrency locks to prevent double-charging.
/// All state changes write to InstallmentPlanAuditLog for forensic debugging.
/// </summary>
public class InstallmentService : IInstallmentService
{
    private readonly EhrDbContext _context;
    private readonly IStripeService _stripeService;
    private readonly IEmailService _emailService;
    private readonly ILogger<InstallmentService> _logger;

    private static readonly TimeSpan StaleLockThreshold = TimeSpan.FromMinutes(5);

    public InstallmentService(
        EhrDbContext context,
        IStripeService stripeService,
        IEmailService emailService,
        ILogger<InstallmentService> logger)
    {
        _context = context;
        _stripeService = stripeService;
        _emailService = emailService;
        _logger = logger;
    }

    // ============================================
    // Plan creation
    // ============================================

    public async Task<InstallmentPlanDto> CreatePlanAsync(
        int patientId, int tenantId, InstallmentPlanCreateDto dto, int? createdBy = null)
    {
        if (dto.LocationId <= 0)
            throw new InvalidOperationException("LocationId is required for installment plans");

        // Validate location has an active connected Stripe account
        var location = await _context.Locations
            .Include(l => l.StripeConnectAccount)
            .FirstOrDefaultAsync(l => l.LocationId == dto.LocationId && l.TenantId == tenantId)
            ?? throw new InvalidOperationException("Location not found");

        if (location.StripeConnectAccountId == null || location.StripeConnectAccount == null)
        {
            throw new InvalidOperationException(
                $"Stripe is not connected at location {location.Name}. Installment plans require a connected payment account.");
        }

        if (location.StripeConnectAccount.Status != (int)StripeConnectAccountStatus.Active)
        {
            throw new InvalidOperationException(
                $"Stripe account at {location.Name} is not active. Status: {(StripeConnectAccountStatus)location.StripeConnectAccount.Status}");
        }

        var patient = await _context.Patients.FindAsync(patientId)
            ?? throw new InvalidOperationException("Patient not found");

        // Get or create customer on the connected account
        var stripeCustomerId = await _stripeService.GetOrCreateCustomerAsync(patientId, dto.LocationId);

        // Save payment method to that customer
        if (!string.IsNullOrEmpty(dto.StripePaymentMethodId))
        {
            await _stripeService.SavePaymentMethodAsync(dto.LocationId, stripeCustomerId, dto.StripePaymentMethodId);
        }

        // Calculate installment amounts
        var amounts = CalculateInstallments(dto.TotalAmount, dto.NumberOfInstallments);

        var plan = new InstallmentPlan
        {
            TenantId = tenantId,
            PatientId = patientId,
            LocationId = dto.LocationId,
            StripeConnectAccountId = location.StripeConnectAccountId,
            TotalAmount = dto.TotalAmount,
            NumberOfInstallments = dto.NumberOfInstallments,
            Status = (int)InstallmentPlanStatus.Active,
            StripeCustomerId = stripeCustomerId,
            StripePaymentMethodId = dto.StripePaymentMethodId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _context.InstallmentPlans.Add(plan);
        await _context.SaveChangesAsync();

        // Create installment details
        for (int i = 0; i < dto.NumberOfInstallments; i++)
        {
            var detail = new InstallmentDetail
            {
                PlanId = plan.PlanId,
                TenantId = tenantId,
                InstallmentNumber = i + 1,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(i)),
                Amount = amounts[i],
                Status = (int)InstallmentDetailStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _context.InstallmentDetails.Add(detail);
        }
        await _context.SaveChangesAsync();

        await WriteAuditAsync(plan.PlanId, null, tenantId, "Created", null,
            (int)InstallmentPlanStatus.Active,
            $"Plan created at location {location.Name} ({dto.NumberOfInstallments} installments, total ${dto.TotalAmount})",
            "User", createdBy);

        // Charge first installment immediately
        var firstDetail = await _context.InstallmentDetails
            .Where(d => d.PlanId == plan.PlanId && d.InstallmentNumber == 1)
            .FirstAsync();

        try
        {
            var amountCents = (int)Math.Round(amounts[0] * 100);
            var idempotencyKey = $"installment-{firstDetail.DetailId}-attempt-1";

            var (paymentIntent, fees) = await _stripeService.ChargeOffSessionAsync(
                stripeConnectAccountId: location.StripeConnectAccountId.Value,
                locationId: dto.LocationId,
                customerId: stripeCustomerId,
                paymentMethodId: dto.StripePaymentMethodId,
                amountCents: amountCents,
                idempotencyKey: idempotencyKey,
                metadata: new Dictionary<string, string>
                {
                    { "paymentType", "FirstInstallment" },
                    { "planId", plan.PlanId.ToString() },
                    { "detailId", firstDetail.DetailId.ToString() },
                    { "patientId", patientId.ToString() },
                    { "tenantId", tenantId.ToString() }
                });

            firstDetail.StripePaymentIntentId = paymentIntent.Id;
            await _context.SaveChangesAsync();

            await WriteAuditAsync(plan.PlanId, firstDetail.DetailId, tenantId, "ChargeAttempted",
                (int)InstallmentDetailStatus.Pending, (int)InstallmentDetailStatus.Pending,
                $"First installment charge attempted: PaymentIntent={paymentIntent.Id}",
                "System", null);

            // If immediately succeeded, process synchronously (webhook will skip via idempotency check)
            if (paymentIntent.Status == "succeeded")
            {
                await HandlePaymentSuccessAsync(paymentIntent.Id, tenantId, location.StripeConnectAccount.StripeAccountId);
            }
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Failed to charge first installment for plan {PlanId}", plan.PlanId);
            firstDetail.Status = (int)InstallmentDetailStatus.Failed;
            firstDetail.RetryCount = 1;
            await _context.SaveChangesAsync();

            await WriteAuditAsync(plan.PlanId, firstDetail.DetailId, tenantId, "ChargeFailed",
                (int)InstallmentDetailStatus.Pending, (int)InstallmentDetailStatus.Failed,
                $"First installment charge failed: {ex.Message}",
                "System", null);

            await SendChargeFailedEmailAsync(plan, firstDetail);

            throw new InvalidOperationException("Payment failed. Please try again or use a different card.");
        }

        return await GetPlanDto(plan.PlanId);
    }

    public async Task<InstallmentPlanDto> GetActivePlanByPatientAsync(int patientId, int tenantId)
    {
        var plan = await _context.InstallmentPlans
            .Include(p => p.InstallmentDetails)
            .Where(p => p.PatientId == patientId && p.TenantId == tenantId && p.Status == (int)InstallmentPlanStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (plan == null) return null;
        return MapPlanToDto(plan);
    }

    // ============================================
    // Background processor (with concurrency lock)
    // ============================================

    public async Task ProcessDueInstallmentsAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Step 1: Release stale locks (older than 5 minutes — assume previous run died)
        await ReleaseStaleLocksAsync();

        // Step 2: Find candidate plans with due installments
        var candidatePlans = await _context.InstallmentPlans
            .Where(p => p.Status == (int)InstallmentPlanStatus.Active
                     && p.ProcessingLockId == null
                     && p.InstallmentDetails.Any(d =>
                         d.Status == (int)InstallmentDetailStatus.Pending && d.DueDate <= today))
            .Select(p => p.PlanId)
            .ToListAsync();

        _logger.LogInformation("Found {Count} candidate plans with due installments", candidatePlans.Count);

        foreach (var planId in candidatePlans)
        {
            // Step 3: Try to acquire lock atomically
            var lockId = Guid.NewGuid();
            var rowsAffected = await _context.InstallmentPlans
                .Where(p => p.PlanId == planId && p.ProcessingLockId == null && p.Status == (int)InstallmentPlanStatus.Active)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.ProcessingLockId, lockId)
                    .SetProperty(p => p.ProcessingLockedAt, DateTime.UtcNow));

            if (rowsAffected == 0)
            {
                // Another process picked it up first
                continue;
            }

            try
            {
                await ProcessSinglePlanAsync(planId, today);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing plan {PlanId}", planId);
            }
            finally
            {
                // Always release lock
                await _context.InstallmentPlans
                    .Where(p => p.PlanId == planId && p.ProcessingLockId == lockId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(p => p.ProcessingLockId, (Guid?)null)
                        .SetProperty(p => p.ProcessingLockedAt, (DateTime?)null));
            }
        }
    }

    private async Task ReleaseStaleLocksAsync()
    {
        var threshold = DateTime.UtcNow - StaleLockThreshold;
        var released = await _context.InstallmentPlans
            .Where(p => p.ProcessingLockId != null && p.ProcessingLockedAt < threshold)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ProcessingLockId, (Guid?)null)
                .SetProperty(p => p.ProcessingLockedAt, (DateTime?)null));

        if (released > 0)
        {
            _logger.LogWarning("Released {Count} stale processing locks", released);
        }
    }

    private async Task ProcessSinglePlanAsync(int planId, DateOnly today)
    {
        var plan = await _context.InstallmentPlans
            .Include(p => p.InstallmentDetails)
            .Include(p => p.StripeConnectAccount)
            .Include(p => p.Location)
            .FirstOrDefaultAsync(p => p.PlanId == planId);

        if (plan == null) return;

        // Validate Stripe account is still connected and active
        if (plan.StripeConnectAccountId == null || plan.StripeConnectAccount == null
            || plan.StripeConnectAccount.Status != (int)StripeConnectAccountStatus.Active)
        {
            _logger.LogWarning(
                "Plan {PlanId} skipped: Stripe account {AccountId} is not active",
                plan.PlanId, plan.StripeConnectAccountId);

            await WriteAuditAsync(plan.PlanId, null, plan.TenantId, "ChargeFailed", null, null,
                "Stripe account no longer active — plan paused",
                "System", null);

            return;
        }

        var dueDetails = plan.InstallmentDetails
            .Where(d => d.Status == (int)InstallmentDetailStatus.Pending && d.DueDate <= today)
            .OrderBy(d => d.InstallmentNumber)
            .ToList();

        foreach (var detail in dueDetails)
        {
            await ChargeInstallmentAsync(plan, detail);
        }
    }

    private async Task ChargeInstallmentAsync(InstallmentPlan plan, InstallmentDetail detail)
    {
        if (string.IsNullOrEmpty(plan.StripeCustomerId) || string.IsNullOrEmpty(plan.StripePaymentMethodId))
        {
            _logger.LogWarning("Plan {PlanId} missing Stripe info, marking installment {DetailId} as failed",
                plan.PlanId, detail.DetailId);
            await HandleInstallmentFailureAsync(plan, detail, "Missing Stripe customer or payment method");
            return;
        }

        try
        {
            var amountCents = (int)Math.Round(detail.Amount * 100);
            var idempotencyKey = $"installment-{detail.DetailId}-attempt-{detail.RetryCount + 1}";

            await WriteAuditAsync(plan.PlanId, detail.DetailId, plan.TenantId, "ChargeAttempted",
                detail.Status, detail.Status,
                $"Background charge attempt {detail.RetryCount + 1}",
                "System", null);

            var (paymentIntent, fees) = await _stripeService.ChargeOffSessionAsync(
                stripeConnectAccountId: plan.StripeConnectAccountId.Value,
                locationId: plan.LocationId.Value,
                customerId: plan.StripeCustomerId,
                paymentMethodId: plan.StripePaymentMethodId,
                amountCents: amountCents,
                idempotencyKey: idempotencyKey,
                metadata: new Dictionary<string, string>
                {
                    { "paymentType", "Installment" },
                    { "planId", plan.PlanId.ToString() },
                    { "detailId", detail.DetailId.ToString() },
                    { "patientId", plan.PatientId.ToString() },
                    { "tenantId", plan.TenantId.ToString() }
                });

            detail.StripePaymentIntentId = paymentIntent.Id;
            await _context.SaveChangesAsync();

            // If synchronously succeeded, process now (webhook will be idempotent)
            if (paymentIntent.Status == "succeeded")
            {
                await HandlePaymentSuccessAsync(paymentIntent.Id, plan.TenantId, plan.StripeConnectAccount.StripeAccountId);
            }
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Failed to charge installment {DetailId}", detail.DetailId);
            await HandleInstallmentFailureAsync(plan, detail, ex.Message);
        }
    }

    // ============================================
    // Webhook handlers (called from StripeWebhookController)
    // ============================================

    public async Task HandlePaymentSuccessAsync(string stripePaymentIntentId, int tenantId, string stripeAccountId = null)
    {
        var detail = await _context.InstallmentDetails
            .Include(d => d.Plan).ThenInclude(p => p.StripeConnectAccount)
            .FirstOrDefaultAsync(d => d.StripePaymentIntentId == stripePaymentIntentId);

        if (detail == null)
        {
            _logger.LogWarning("PaymentIntent {Id} not found in InstallmentDetails", stripePaymentIntentId);
            return;
        }

        if (detail.Status == (int)InstallmentDetailStatus.Paid)
        {
            _logger.LogInformation("Installment {DetailId} already marked Paid — skipping", detail.DetailId);
            return; // Already processed
        }

        var oldStatus = detail.Status;
        detail.Status = (int)InstallmentDetailStatus.Paid;
        detail.PaidAt = DateTime.UtcNow;

        // Pull actual fees from balance transaction
        FeeBreakdown actualFees = null;
        try
        {
            var paymentIntent = await _stripeService.GetPaymentIntentAsync(
                stripePaymentIntentId,
                detail.Plan.StripeConnectAccount?.StripeAccountId ?? stripeAccountId);

            if (paymentIntent.LatestChargeId != null)
            {
                // Use metadata for our calculated fee, balance transaction for actual Stripe fee
                int.TryParse(paymentIntent.Metadata.GetValueOrDefault("clinicTotalFeeCents", "0"), out var clinicTotalFee);
                int.TryParse(paymentIntent.Metadata.GetValueOrDefault("applicationFeeCents", "0"), out var appFee);

                actualFees = new FeeBreakdown
                {
                    ClinicTotalFeeCents = clinicTotalFee,
                    ApplicationFeeCents = appFee,
                    EstimatedStripeFeeCents = clinicTotalFee - appFee,
                    NetToClinicCents = (int)(detail.Amount * 100) - clinicTotalFee
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve fee breakdown for {Id}", stripePaymentIntentId);
        }

        // Create Payment record
        var payment = new Payment
        {
            TenantId = tenantId,
            PatientId = detail.Plan.PatientId,
            LocationId = detail.Plan.LocationId,
            StripeConnectAccountId = detail.Plan.StripeConnectAccountId,
            PaymentMethodType = (int)StripePaymentMethodType.Online,
            Type = (int)PaymentType.SelfPay,
            Method = (int)PaymentMethod.CreditCard,
            Amount = detail.Amount,
            StripePaymentIntentId = stripePaymentIntentId,
            InstallmentDetailId = detail.DetailId,
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = (int)PaymentStatus.Completed,
            Notes = $"Installment {detail.InstallmentNumber} of {detail.Plan.NumberOfInstallments}",
            CreatedAt = DateTime.UtcNow,
            ClinicTotalFeeCents = actualFees?.ClinicTotalFeeCents,
            StripeProcessingFeeCents = actualFees?.EstimatedStripeFeeCents,
            ApplicationFeeCents = actualFees?.ApplicationFeeCents,
            NetToClinicCents = actualFees?.NetToClinicCents
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        detail.PaymentId = payment.PaymentId;

        // Ledger entry
        var ledgerEntry = new PatientLedger
        {
            TenantId = tenantId,
            PatientId = detail.Plan.PatientId,
            EntryType = 2, // Payment
            PaymentId = payment.PaymentId,
            TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount = -detail.Amount,
            Description = $"Patient Payment - Installment {detail.InstallmentNumber}/{detail.Plan.NumberOfInstallments} (Online)",
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientLedgers.Add(ledgerEntry);
        await _context.SaveChangesAsync();

        await WriteAuditAsync(detail.PlanId, detail.DetailId, tenantId, "ChargeSucceeded",
            oldStatus, (int)InstallmentDetailStatus.Paid,
            $"Payment {payment.PaymentId} created for ${detail.Amount}",
            "Webhook", null);

        // Send patient email — installment paid
        await SendInstallmentPaidEmailAsync(detail.Plan, detail);

        // Check if all paid → mark plan completed
        var allPaid = await _context.InstallmentDetails
            .Where(d => d.PlanId == detail.PlanId)
            .AllAsync(d => d.Status == (int)InstallmentDetailStatus.Paid);

        if (allPaid)
        {
            detail.Plan.Status = (int)InstallmentPlanStatus.Completed;
            await _context.SaveChangesAsync();

            await WriteAuditAsync(detail.PlanId, null, tenantId, "Completed",
                (int)InstallmentPlanStatus.Active, (int)InstallmentPlanStatus.Completed,
                "All installments paid",
                "System", null);

            await SendPlanCompletedEmailAsync(detail.Plan);
            _logger.LogInformation("Installment plan {PlanId} completed — all paid", detail.PlanId);
        }

        _logger.LogInformation("Installment {DetailId} paid: ${Amount}", detail.DetailId, detail.Amount);
    }

    public async Task HandlePaymentFailureAsync(string stripePaymentIntentId)
    {
        var detail = await _context.InstallmentDetails
            .Include(d => d.Plan)
            .FirstOrDefaultAsync(d => d.StripePaymentIntentId == stripePaymentIntentId);

        if (detail == null) return;
        await HandleInstallmentFailureAsync(detail.Plan, detail, "Webhook reported payment failure");
    }

    public async Task CancelPlanAsync(int planId, int tenantId)
    {
        var plan = await _context.InstallmentPlans
            .Include(p => p.InstallmentDetails)
            .FirstOrDefaultAsync(p => p.PlanId == planId && p.TenantId == tenantId)
            ?? throw new InvalidOperationException("Plan not found");

        var oldStatus = plan.Status;
        plan.Status = (int)InstallmentPlanStatus.Cancelled;

        foreach (var detail in plan.InstallmentDetails.Where(d => d.Status == (int)InstallmentDetailStatus.Pending))
        {
            detail.Status = (int)InstallmentDetailStatus.Failed;
        }

        await _context.SaveChangesAsync();

        await WriteAuditAsync(planId, null, tenantId, "Cancelled",
            oldStatus, (int)InstallmentPlanStatus.Cancelled,
            "Plan cancelled",
            "User", null);

        _logger.LogInformation("Cancelled installment plan {PlanId}", planId);
    }

    // ============================================
    // Internal helpers
    // ============================================

    private async Task HandleInstallmentFailureAsync(InstallmentPlan plan, InstallmentDetail detail, string reason)
    {
        var oldStatus = detail.Status;
        detail.RetryCount++;

        if (detail.RetryCount >= 3)
        {
            detail.Status = (int)InstallmentDetailStatus.Delinquent;
            _logger.LogWarning("Installment {DetailId} marked delinquent after 3 retries", detail.DetailId);

            await WriteAuditAsync(plan.PlanId, detail.DetailId, plan.TenantId, "Delinquent",
                oldStatus, (int)InstallmentDetailStatus.Delinquent,
                $"Marked delinquent after 3 failed attempts. Last reason: {reason}",
                "System", null);

            // Check if plan should be marked Defaulted
            await _context.SaveChangesAsync(); // Save the delinquent status first

            var delinquentCount = await _context.InstallmentDetails
                .Where(d => d.PlanId == plan.PlanId && d.Status == (int)InstallmentDetailStatus.Delinquent)
                .CountAsync();

            if (delinquentCount >= 2)
            {
                var oldPlanStatus = plan.Status;
                plan.Status = (int)InstallmentPlanStatus.Defaulted;
                await _context.SaveChangesAsync();

                await WriteAuditAsync(plan.PlanId, null, plan.TenantId, "Defaulted",
                    oldPlanStatus, (int)InstallmentPlanStatus.Defaulted,
                    $"Plan defaulted: {delinquentCount} delinquent installments",
                    "System", null);

                _logger.LogWarning("Plan {PlanId} marked as Defaulted", plan.PlanId);
                await SendPlanDefaultedEmailAsync(plan);
            }
        }
        else
        {
            detail.Status = (int)InstallmentDetailStatus.Failed;
            detail.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)); // Retry +5 days
            await _context.SaveChangesAsync();

            await WriteAuditAsync(plan.PlanId, detail.DetailId, plan.TenantId, "ChargeFailed",
                oldStatus, (int)InstallmentDetailStatus.Failed,
                $"Charge failed (attempt {detail.RetryCount}): {reason}. Retry on {detail.DueDate}",
                "System", null);

            _logger.LogWarning("Installment {DetailId} failed, retry {RetryCount}, next attempt {DueDate}",
                detail.DetailId, detail.RetryCount, detail.DueDate);
        }

        // Always email the patient on failure
        await SendChargeFailedEmailAsync(plan, detail);
    }

    private List<decimal> CalculateInstallments(decimal totalAmount, int numberOfInstallments)
    {
        var baseAmount = Math.Floor(totalAmount / numberOfInstallments * 100) / 100;
        var remainder = totalAmount - (baseAmount * numberOfInstallments);

        var amounts = new List<decimal>();
        for (int i = 0; i < numberOfInstallments; i++)
        {
            amounts.Add(i == numberOfInstallments - 1 ? baseAmount + remainder : baseAmount);
        }
        return amounts;
    }

    private async Task<InstallmentPlanDto> GetPlanDto(int planId)
    {
        var plan = await _context.InstallmentPlans
            .Include(p => p.InstallmentDetails)
            .FirstAsync(p => p.PlanId == planId);
        return MapPlanToDto(plan);
    }

    private static InstallmentPlanDto MapPlanToDto(InstallmentPlan plan)
    {
        var details = plan.InstallmentDetails.OrderBy(d => d.InstallmentNumber).ToList();
        var amountPaid = details.Where(d => d.Status == (int)InstallmentDetailStatus.Paid).Sum(d => d.Amount);
        var nextDue = details
            .Where(d => d.Status == (int)InstallmentDetailStatus.Pending || d.Status == (int)InstallmentDetailStatus.Failed)
            .OrderBy(d => d.DueDate)
            .FirstOrDefault();

        return new InstallmentPlanDto
        {
            PlanId = plan.PlanId,
            TotalAmount = plan.TotalAmount,
            NumberOfInstallments = plan.NumberOfInstallments,
            Status = plan.Status,
            AmountPaid = amountPaid,
            AmountRemaining = plan.TotalAmount - amountPaid,
            NextDueDate = nextDue?.DueDate,
            CreatedAt = plan.CreatedAt ?? DateTime.UtcNow,
            Details = details.Select(d => new InstallmentDetailDto
            {
                DetailId = d.DetailId,
                InstallmentNumber = d.InstallmentNumber,
                DueDate = d.DueDate,
                Amount = d.Amount,
                Status = d.Status,
                PaidAt = d.PaidAt
            }).ToList()
        };
    }

    private async Task WriteAuditAsync(
        int planId, int? detailId, int tenantId,
        string action, int? oldStatus, int? newStatus,
        string details, string actorType, int? actorId)
    {
        try
        {
            _context.InstallmentPlanAuditLogs.Add(new InstallmentPlanAuditLog
            {
                PlanId = planId,
                DetailId = detailId,
                TenantId = tenantId,
                Action = action,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Details = details,
                ActorType = actorType,
                ActorId = actorId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for plan {PlanId}", planId);
            // Don't throw — audit failure shouldn't break the main flow
        }
    }

    // ============================================
    // Email notifications
    // ============================================

    private async Task SendInstallmentPaidEmailAsync(InstallmentPlan plan, InstallmentDetail detail)
    {
        try
        {
            var patient = await _context.Patients.FindAsync(plan.PatientId);
            if (patient == null || string.IsNullOrEmpty(patient.Email)) return;

            var subject = $"Payment received: Installment {detail.InstallmentNumber} of {plan.NumberOfInstallments}";
            var body = $@"
<p>Hi {patient.FirstName},</p>
<p>We've successfully processed your installment payment of <strong>${detail.Amount:F2}</strong>.</p>
<p>This was installment {detail.InstallmentNumber} of {plan.NumberOfInstallments} on your payment plan.</p>
<p>Thank you!</p>
<p><em>MEDOCS</em></p>";

            await _emailService.SendEmailAsync(patient.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send installment paid email for detail {DetailId}", detail.DetailId);
        }
    }

    private async Task SendChargeFailedEmailAsync(InstallmentPlan plan, InstallmentDetail detail)
    {
        try
        {
            var patient = await _context.Patients.FindAsync(plan.PatientId);
            if (patient == null || string.IsNullOrEmpty(patient.Email)) return;

            var subject = "Action required: We couldn't process your installment payment";
            var body = $@"
<p>Hi {patient.FirstName},</p>
<p>We tried to process your installment payment of <strong>${detail.Amount:F2}</strong> but it didn't go through.</p>
<p>This may be due to:</p>
<ul>
<li>Insufficient funds</li>
<li>Expired card</li>
<li>Card declined by your bank</li>
</ul>
<p>Please update your payment method in your patient portal, or contact your clinic to make a manual payment.</p>
<p>We'll automatically try again in 5 days.</p>
<p><em>MEDOCS</em></p>";

            await _emailService.SendEmailAsync(patient.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send charge failed email for detail {DetailId}", detail.DetailId);
        }
    }

    private async Task SendPlanCompletedEmailAsync(InstallmentPlan plan)
    {
        try
        {
            var patient = await _context.Patients.FindAsync(plan.PatientId);
            if (patient == null || string.IsNullOrEmpty(patient.Email)) return;

            var subject = "Your payment plan is complete!";
            var body = $@"
<p>Hi {patient.FirstName},</p>
<p>Great news — you've completed all <strong>{plan.NumberOfInstallments}</strong> installments on your payment plan.</p>
<p>Total paid: <strong>${plan.TotalAmount:F2}</strong></p>
<p>Thank you for choosing to pay over time. Your account is now in good standing.</p>
<p><em>MEDOCS</em></p>";

            await _emailService.SendEmailAsync(patient.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send plan completed email for plan {PlanId}", plan.PlanId);
        }
    }

    private async Task SendPlanDefaultedEmailAsync(InstallmentPlan plan)
    {
        try
        {
            var patient = await _context.Patients.FindAsync(plan.PatientId);
            if (patient != null && !string.IsNullOrEmpty(patient.Email))
            {
                var subject = "Your payment plan needs attention";
                var body = $@"
<p>Hi {patient.FirstName},</p>
<p>Multiple installment payments on your plan have failed. Your plan has been marked as defaulted.</p>
<p>Please contact your clinic to discuss next steps for resolving your outstanding balance.</p>
<p><em>MEDOCS</em></p>";

                await _emailService.SendEmailAsync(patient.Email, subject, body);
            }

            // Also notify ClinicAdmin (find admins for this tenant)
            var admins = await _context.Users
                .Where(u => u.TenantId == plan.TenantId && u.Role == 1) // ClinicAdmin role
                .ToListAsync();
            // Note: User.Role is nullable int; explicit equality is OK in EF query

            foreach (var admin in admins)
            {
                if (string.IsNullOrEmpty(admin.Email)) continue;

                var subject = $"Installment plan defaulted for patient {plan.PatientId}";
                var body = $@"
<p>An installment plan has been marked as defaulted.</p>
<p>Plan ID: {plan.PlanId}<br/>
Patient ID: {plan.PatientId}<br/>
Total amount: ${plan.TotalAmount:F2}<br/>
Number of installments: {plan.NumberOfInstallments}</p>
<p>Please follow up with the patient.</p>
<p><em>MEDOCS</em></p>";

                await _emailService.SendEmailAsync(admin.Email, subject, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send plan defaulted notifications for plan {PlanId}", plan.PlanId);
        }
    }
}
