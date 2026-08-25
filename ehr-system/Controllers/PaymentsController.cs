using EHR.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Controllers;

/// <summary>
/// Controller for payment management.
/// Handles patient and insurance payment recording and balance tracking.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ICopayReminderService _copayReminderService;
    private readonly EhrDbContext _context;

    public PaymentsController(IPaymentService paymentService, ICopayReminderService copayReminderService, EhrDbContext context)
    {
        _paymentService = paymentService;
        _copayReminderService = copayReminderService;
        _context = context;
    }

    /// <summary>
    /// Get payments with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PaymentListDto>>> GetPayments(
        [FromQuery] int? patientId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var payments = await _paymentService.GetPaymentsAsync(patientId, startDate, endDate);
        return Ok(payments);
    }

    /// <summary>
    /// Create a new payment.
    /// Card payments require the patient's location to have an active connected Stripe account.
    /// Cash/check payments are not blocked.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,3,4")]
    public async Task<ActionResult<Payment>> CreatePayment([FromBody] PaymentCreateDto dto)
    {
        // Block card payments if the patient's location does not have an active connected Stripe account
        if (dto.Method == (int)PaymentMethod.CreditCard || dto.Method == (int)PaymentMethod.DebitCard)
        {
            var patient = await _context.Patients
                .Include(p => p.PreferredLocation).ThenInclude(l => l.StripeConnectAccount)
                .FirstOrDefaultAsync(p => p.PatientId == dto.PatientId);

            if (patient?.PreferredLocation == null
                || patient.PreferredLocation.StripeConnectAccountId == null
                || patient.PreferredLocation.StripeConnectAccount == null
                || patient.PreferredLocation.StripeConnectAccount.Status != (int)StripeConnectAccountStatus.Active)
            {
                return Conflict(new
                {
                    message = "Card payments are not available at this location. Stripe is not connected. " +
                              "Ask ClinicAdmin to connect in Settings → Payment Integration."
                });
            }
        }

        var payment = await _paymentService.CreatePaymentAsync(dto);
        return Ok(payment);
    }

    /// <summary>
    /// Get patient account balance.
    /// </summary>
    [HttpGet("patient/{patientId}/balance")]
    public async Task<ActionResult<PatientBalanceDto>> GetPatientBalance(int patientId)
    {
        try
        {
            var balance = await _paymentService.GetPatientBalanceAsync(patientId);
            return Ok(balance);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get patient outstanding copay/portal balance (includes copay dues from appointments).
    /// </summary>
    [HttpGet("patient/{patientId}/outstanding")]
    public async Task<IActionResult> GetPatientOutstandingBalance(int patientId)
    {
        try
        {
            var tenantId = int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
            var balance = await _paymentService.GetPortalBalanceAsync(patientId, tenantId);
            return Ok(balance);
        }
        catch (Exception ex)
        {
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Get full patient ledger (staff/biller view) with calculated running balance.
    /// </summary>
    [HttpGet("patient/{patientId}/ledger")]
    [Authorize(Roles = "0,1,4")]
    public async Task<IActionResult> GetPatientLedger(int patientId)
    {
        try
        {
            var tenantId = int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
            var ledger = await _paymentService.GetPatientLedgerAsync(patientId, tenantId);
            return Ok(ledger);
        }
        catch (Exception ex)
        {
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Manually trigger copay reminder emails for all patients with outstanding balance.
    /// Skips patients with active installment plans.
    /// On localhost: writes HTML files to emails_sent/ folder.
    /// </summary>
    [HttpPost("send-copay-reminders")]
    [Authorize(Roles = "0,1,4")]
    public async Task<IActionResult> SendCopayReminders()
    {
        try
        {
            var tenantId = int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
            var sentCount = await _copayReminderService.SendCopayRemindersAsync(tenantId);
            return Ok(new { message = $"Sent {sentCount} copay reminder(s)", count = sentCount });
        }
        catch (Exception ex)
        {
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Get paginated list of patients with outstanding copay balances.
    /// </summary>
    [HttpGet("copay-balances")]
    [Authorize(Roles = "0,1,4")]
    public async Task<IActionResult> GetCopayBalances([FromQuery] string search = "", [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        try
        {
            var tenantId = int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
            var result = await _paymentService.GetCopayBalancesAsync(tenantId, search, page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }
}
