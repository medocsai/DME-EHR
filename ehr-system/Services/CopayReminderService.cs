using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface ICopayReminderService
{
    /// <summary>
    /// Send copay reminder emails to all patients with outstanding balance and no active installment plan.
    /// Returns count of reminders sent.
    /// </summary>
    Task<int> SendCopayRemindersAsync(int tenantId);
}

public class CopayReminderService : ICopayReminderService
{
    private readonly EhrDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IPaymentService _paymentService;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IConfiguration _config;
    private readonly ILogger<CopayReminderService> _logger;

    public CopayReminderService(
        EhrDbContext context,
        IEmailService emailService,
        IPaymentService paymentService,
        EncryptionHelper encryptionHelper,
        IConfiguration config,
        ILogger<CopayReminderService> logger)
    {
        _context = context;
        _emailService = emailService;
        _paymentService = paymentService;
        _encryptionHelper = encryptionHelper;
        _config = config;
        _logger = logger;
    }

    public async Task<int> SendCopayRemindersAsync(int tenantId)
    {
        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";

        // Get all patients for this tenant who have an email
        var patients = await _context.Patients
            .Where(p => p.TenantId == tenantId && p.IsDeleted != true && !string.IsNullOrEmpty(p.Email))
            .ToListAsync();

        // Get patients with active installment plans — they don't get reminders
        var patientsWithActivePlans = await _context.InstallmentPlans
            .Where(p => p.TenantId == tenantId && p.Status == (int)InstallmentPlanStatus.Active)
            .Select(p => p.PatientId)
            .Distinct()
            .ToListAsync();

        var sentCount = 0;

        foreach (var patient in patients)
        {
            // Skip patients on active installment plans
            if (patientsWithActivePlans.Contains(patient.PatientId))
                continue;

            // Calculate patient-facing balance (only what patient actually owes — not insurance portion)
            PortalBalanceDto balance;
            try
            {
                balance = await _paymentService.GetPortalBalanceAsync(patient.PatientId, tenantId);
            }
            catch { continue; }

            if (balance == null || balance.CurrentBalance <= 0)
                continue;

            // Decrypt patient info for email
            _context.Entry(patient).State = EntityState.Detached;
            _encryptionHelper.DecryptEntity(patient);

            if (string.IsNullOrWhiteSpace(patient.Email))
                continue;

            // Get location portal code for this patient
            var location = await _context.Locations
                .Include(l => l.Tenant)
                .Include(l => l.StripeConnectAccount)
                .FirstOrDefaultAsync(l => l.LocationId == (patient.PreferredLocationId ?? 0) && l.TenantId == tenantId);

            if (location == null)
            {
                // Fallback to first active location with a connected Stripe account
                location = await _context.Locations
                    .Include(l => l.Tenant)
                    .Include(l => l.StripeConnectAccount)
                    .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive == true);
            }

            if (location == null) continue;

            // Skip patients whose location does not have an active connected Stripe account
            // (Online payment link in the email would lead to a blocked portal)
            if (location.StripeConnectAccountId == null
                || location.StripeConnectAccount == null
                || location.StripeConnectAccount.Status != (int)StripeConnectAccountStatus.Active)
            {
                _logger.LogInformation(
                    "Skipping copay reminder for patient {PatientId} — location {LocationId} ({Name}) has no active Stripe account",
                    patient.PatientId, location.LocationId, location.Name);
                continue;
            }

            // Generate a payment token (valid for 7 days)
            var token = Guid.NewGuid().ToString("N");
            var paymentToken = new CopayPaymentToken
            {
                TenantId = tenantId,
                PatientId = patient.PatientId,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.CopayPaymentTokens.Add(paymentToken);
            await _context.SaveChangesAsync();

            // Build the smart portal link
            var portalCode = location.PortalCode ?? "";
            var payUrl = $"{baseUrl}/Portal/pay?token={token}";

            var patientName = $"{patient.FirstName} {patient.LastName}".Trim();
            var clinicName = location.Tenant?.Name ?? location.Name ?? "Your Healthcare Provider";

            // Send the copay reminder email
            await _emailService.SendBalanceNotificationAsync(
                patient.Email,
                patientName,
                balance.CurrentBalance,
                payUrl);

            sentCount++;
            _logger.LogInformation("Copay reminder sent: PatientId={PatientId}, EmailMasked={EmailMasked}, balance={Balance}",
                patient.PatientId, PhiLog.MaskEmail(patient.Email), balance.CurrentBalance);
        }

        _logger.LogInformation("Copay reminders sent: {Count} for tenant {TenantId}", sentCount, tenantId);
        return sentCount;
    }
}
