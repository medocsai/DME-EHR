using System.ComponentModel.DataAnnotations;
using EHR.Helpers;
using EHR.Models.Generated;
using EHR.Services;
using EHR.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EHR.Controllers;

/// <summary>
/// Patient Portal Controller - Handles both MVC views and API endpoints for the patient portal.
/// Supports two auth modes:
///   1. Legacy: DOB + SSN Last 4 (existing, kept for backward compatibility)
///   2. New: Email + Password + OTP (via admin invitation)
/// All data API endpoints require Role = 8 (Patient) and verify PatientId ownership.
/// </summary>
public class PatientPortalController : Controller
{
    private readonly IPatientPortalAuthService _portalAuthService;
    private readonly IPatientPortalInvitationService _invitationService;
    private readonly IPatientService _patientService;
    private readonly IAppointmentService _appointmentService;
    private readonly IClinicalNoteService _noteService;
    private readonly IEncounterService _encounterService;
    private readonly IPatientMedicationService _medicationService;
    private readonly IPatientAllergyService _allergyService;
    private readonly IPatientVitalService _vitalService;
    private readonly IPrescriptionService _prescriptionService;
    private readonly IOrderService _orderService;
    private readonly IAuditService _auditService;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly EhrDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<PatientPortalController> _logger;
    private readonly IProfilePictureService _profilePictureService;
    private readonly IPaymentService _paymentService;
    private readonly IStripeService _stripeService;
    private readonly IInstallmentService _installmentService;
    private readonly IPatientDocumentService _documentService;
    private readonly IPatientMessagingService _messagingService;
    private readonly IPatientMessagingNotificationService _messagingNotificationService;
    private readonly IConsentService _consentService;

    public PatientPortalController(
        IPatientPortalAuthService portalAuthService,
        IPatientPortalInvitationService invitationService,
        IPatientService patientService,
        IAppointmentService appointmentService,
        IClinicalNoteService noteService,
        IEncounterService encounterService,
        IPatientMedicationService medicationService,
        IPatientAllergyService allergyService,
        IPatientVitalService vitalService,
        IPrescriptionService prescriptionService,
        IOrderService orderService,
        IAuditService auditService,
        EncryptionHelper encryptionHelper,
        EhrDbContext context,
        IConfiguration config,
        ILogger<PatientPortalController> logger,
        IProfilePictureService profilePictureService,
        IPaymentService paymentService,
        IStripeService stripeService,
        IInstallmentService installmentService,
        IPatientDocumentService documentService,
        IPatientMessagingService messagingService,
        IPatientMessagingNotificationService messagingNotificationService,
        IConsentService consentService)
    {
        _portalAuthService = portalAuthService;
        _invitationService = invitationService;
        _patientService = patientService;
        _appointmentService = appointmentService;
        _noteService = noteService;
        _encounterService = encounterService;
        _medicationService = medicationService;
        _allergyService = allergyService;
        _vitalService = vitalService;
        _prescriptionService = prescriptionService;
        _orderService = orderService;
        _auditService = auditService;
        _encryptionHelper = encryptionHelper;
        _context = context;
        _config = config;
        _logger = logger;
        _profilePictureService = profilePictureService;
        _paymentService = paymentService;
        _stripeService = stripeService;
        _installmentService = installmentService;
        _documentService = documentService;
        _messagingService = messagingService;
        _messagingNotificationService = messagingNotificationService;
        _consentService = consentService;
    }

    // ============================================
    // HELPER: Extract PatientId from JWT claims
    // ============================================
    private int GetPatientId()
    {
        var claim = User.FindFirst("PatientId");
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private string? GetPatientEmail()
    {
        return User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    }

    private string GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private int GetTenantId()
    {
        var claim = User.FindFirst("TenantId");
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    private int GetLocationId()
    {
        var claim = User.FindFirst("LocationId");
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    // ============================================
    // MVC VIEW ROUTES (serve Razor pages)
    // ============================================

    /// <summary>Portal login page — legacy (DOB + SSN)</summary>
    [AllowAnonymous]
    [Route("Portal/Login")]
    public IActionResult Login()
    {
        return View("~/Views/Portal/Login.cshtml");
    }

    /// <summary>Location-branded portal login page — /Portal/{portalCode}</summary>
    [AllowAnonymous]
    [Route("Portal/{portalCode}")]
    public IActionResult PortalLogin(string portalCode)
    {
        ViewData["PortalCode"] = portalCode;
        return View("~/Views/Portal/PortalLogin.cshtml");
    }

    /// <summary>Patient self-registration page — /Portal/Register?token=xxx</summary>
    [AllowAnonymous]
    [Route("Portal/Register")]
    public IActionResult Register()
    {
        return View("~/Views/Portal/Register.cshtml");
    }

    /// <summary>Simple "Set Up Account" page for existing patients — just set password</summary>
    [AllowAnonymous]
    [Route("Portal/SetupAccount")]
    public IActionResult SetupAccount()
    {
        return View("~/Views/Portal/SetupAccount.cshtml");
    }

    /// <summary>API: Create portal account for existing patient (password only)</summary>
    [AllowAnonymous]
    [HttpPost("api/portal/auth/setup-account")]
    public async Task<IActionResult> SetupAccountApi([FromBody] SetupAccountDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Token and password are required" });

        if (dto.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters" });

        // Validate copay payment token
        var payToken = await _context.CopayPaymentTokens
            .Include(t => t.Patient)
            .FirstOrDefaultAsync(t => t.Token == dto.Token && t.ExpiresAt > DateTime.UtcNow);

        if (payToken == null)
            return BadRequest(new { message = "Invalid or expired link" });

        // Check if account already exists
        var existingAccount = await _context.PatientPortalAccounts
            .AnyAsync(a => a.PatientId == payToken.PatientId && a.IsActive == true);
        if (existingAccount)
            return BadRequest(new { message = "Account already exists. Please login instead." });

        // Get location for portal code
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.TenantId == payToken.TenantId && l.IsActive == true);

        // Decrypt patient email
        var patient = payToken.Patient;
        _context.Entry(patient).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _encryptionHelper.DecryptEntity(patient);

        // Create portal account (email lives only in Patient table — single source of truth)
        var account = new PatientPortalAccount
        {
            TenantId = payToken.TenantId,
            LocationId = location?.LocationId ?? 0,
            PatientId = payToken.PatientId,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, BCrypt.Net.BCrypt.GenerateSalt(12)),
            IsActive = true,
            RegisteredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientPortalAccounts.Add(account);
        await _context.SaveChangesAsync();

        // Generate JWT token so patient is auto-logged in (no redirect to login page)
        var tenant = await _context.Tenants.FindAsync(payToken.TenantId);
        var jwtToken = _portalAuthService.GeneratePortalToken(patient, tenant, location);

        return Ok(new {
            success = true,
            portalCode = location?.PortalCode ?? "",
            token = jwtToken,
            patientId = patient.PatientId,
            patientName = $"{patient.FirstName} {patient.LastName}".Trim(),
            tenantId = payToken.TenantId,
            message = "Account created successfully"
        });
    }

    /// <summary>API: Validate setup token — returns patient info if valid</summary>
    [AllowAnonymous]
    [HttpGet("api/portal/auth/validate-setup-token")]
    public async Task<IActionResult> ValidateSetupToken([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Ok(new { Valid = false });

        var payToken = await _context.CopayPaymentTokens
            .Include(t => t.Patient)
            .FirstOrDefaultAsync(t => t.Token == token && t.ExpiresAt > DateTime.UtcNow);

        if (payToken == null)
            return Ok(new { Valid = false, HasAccount = false });

        // Check if account already exists
        var hasAccount = await _context.PatientPortalAccounts
            .AnyAsync(a => a.PatientId == payToken.PatientId && a.IsActive == true);

        if (hasAccount)
        {
            var loc = await _context.Locations.FirstOrDefaultAsync(l => l.TenantId == payToken.TenantId && l.IsActive == true);
            return Ok(new { Valid = false, HasAccount = true, PortalCode = loc?.PortalCode ?? "" });
        }

        // Decrypt patient info
        var patient = payToken.Patient;
        _context.Entry(patient).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _encryptionHelper.DecryptEntity(patient);

        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.TenantId == payToken.TenantId && l.IsActive == true);

        return Ok(new {
            Valid = true,
            PatientName = $"{patient.FirstName} {patient.LastName}".Trim(),
            Email = patient.Email ?? "",
            ClinicName = location?.Tenant?.Name ?? location?.Name ?? ""
        });
    }

    /// <summary>Password reset page — /Portal/ResetPassword?token=xxx</summary>
    [AllowAnonymous]
    [Route("Portal/ResetPassword")]
    public IActionResult ResetPassword()
    {
        return View("~/Views/Portal/ResetPassword.cshtml");
    }

    /// <summary>
    /// Smart payment link from copay reminder emails.
    /// Routes patient to login (if account exists) or create account (if not).
    /// Always redirects to billing page after auth.
    /// /Portal/pay?token=xxx
    /// </summary>
    [AllowAnonymous]
    [Route("Portal/pay")]
    public async Task<IActionResult> PaymentLink([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login");

        var payToken = await _context.CopayPaymentTokens
            .Include(t => t.Patient)
            .FirstOrDefaultAsync(t => t.Token == token && t.ExpiresAt > DateTime.UtcNow);

        if (payToken == null)
        {
            ViewData["Error"] = "This payment link has expired. Please contact your clinic.";
            return View("~/Views/Portal/PortalLogin.cshtml");
        }

        // Find patient's location for portal code
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.TenantId == payToken.TenantId && l.IsActive == true);

        var portalCode = location?.PortalCode ?? "";

        // Decrypt patient email
        var patient = payToken.Patient;
        _context.Entry(patient).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _encryptionHelper.DecryptEntity(patient);

        // Check if patient already has a portal account
        var hasAccount = await _context.PatientPortalAccounts
            .AnyAsync(a => a.PatientId == payToken.PatientId && a.IsActive == true);

        if (hasAccount)
        {
            // Patient has account → send to login with email pre-filled, redirect to billing
            return Redirect($"/Portal/{portalCode}?email={Uri.EscapeDataString(patient.Email ?? "")}&action=billing");
        }
        else
        {
            // Patient has no account → simple "Set Up Account" page (just password)
            return Redirect($"/Portal/SetupAccount?token={token}&action=billing");
        }
    }

    /// <summary>
    /// Smart portal access link from emails (welcome, appointment, reminder).
    /// Routes patient to login (if account exists) or create account (if not).
    /// Always redirects to dashboard after auth.
    /// /Portal/access?token=xxx
    /// </summary>
    [AllowAnonymous]
    [Route("Portal/access")]
    public async Task<IActionResult> PortalAccess([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login");

        var portalToken = await _context.CopayPaymentTokens
            .Include(t => t.Patient)
            .FirstOrDefaultAsync(t => t.Token == token && t.ExpiresAt > DateTime.UtcNow);

        if (portalToken == null)
        {
            ViewData["Error"] = "This link has expired. Please contact your clinic.";
            return View("~/Views/Portal/PortalLogin.cshtml");
        }

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.TenantId == portalToken.TenantId && l.IsActive == true);

        var portalCode = location?.PortalCode ?? "";

        var patient = portalToken.Patient;
        _context.Entry(patient).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _encryptionHelper.DecryptEntity(patient);

        var hasAccount = await _context.PatientPortalAccounts
            .AnyAsync(a => a.PatientId == portalToken.PatientId && a.IsActive == true);

        if (hasAccount)
        {
            return Redirect($"/Portal/{portalCode}?email={Uri.EscapeDataString(patient.Email ?? "")}&action=dashboard");
        }
        else
        {
            return Redirect($"/Portal/SetupAccount?token={token}&action=dashboard");
        }
    }

    private static string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    /// <summary>Portal dashboard — shell page, auth via JS/localStorage JWT</summary>
    [AllowAnonymous]
    [Route("Portal/Dashboard")]
    public IActionResult Dashboard()
    {
        return View("~/Views/Portal/Dashboard.cshtml");
    }

    /// <summary>Portal booking — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Booking")]
    public IActionResult Booking()
    {
        return View("~/Views/Portal/Booking.cshtml");
    }

    /// <summary>Portal appointments — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Appointments")]
    public IActionResult Appointments()
    {
        return View("~/Views/Portal/Appointments.cshtml");
    }

    /// <summary>Portal visits page — shows encounter summaries (replaces clinical-notes page)</summary>
    [AllowAnonymous]
    [Route("Portal/Visits")]
    public IActionResult Visits()
    {
        return View("~/Views/Portal/Visits.cshtml");
    }

    /// <summary>Portal medications — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Medications")]
    public IActionResult Medications()
    {
        return View("~/Views/Portal/Medications.cshtml");
    }

    /// <summary>Portal prescriptions — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Prescriptions")]
    public IActionResult Prescriptions()
    {
        return View("~/Views/Portal/Prescriptions.cshtml");
    }

    /// <summary>Portal orders — shell page (Lab + Imaging only, no referrals)</summary>
    [AllowAnonymous]
    [Route("Portal/Orders")]
    public IActionResult Orders()
    {
        return View("~/Views/Portal/Orders.cshtml");
    }

    /// <summary>Portal allergies — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Allergies")]
    public IActionResult Allergies()
    {
        return View("~/Views/Portal/Allergies.cshtml");
    }

    /// <summary>Portal vitals — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Vitals")]
    public IActionResult Vitals()
    {
        return View("~/Views/Portal/Vitals.cshtml");
    }

    /// <summary>Portal patient profile — shell page</summary>
    [AllowAnonymous]
    [Route("Portal/Profile")]
    public IActionResult Profile()
    {
        return View("~/Views/Portal/Profile.cshtml");
    }

    /// <summary>Portal consent — sign consent forms before arriving at the clinic (2026-05).</summary>
    [AllowAnonymous]
    [Route("Portal/Consent")]
    public IActionResult Consent()
    {
        ViewData["PortalPage"] = "consent";
        return View("~/Views/Portal/Consent.cshtml");
    }

    // ============================================
    // PORTAL CONSENT API (2026-05)
    // Patient signs consent forms remotely. Kiosk then routes them through
    // "Yes, I am Here" instead of the full forms flow.
    // ============================================

    /// <summary>List the patient's upcoming appointments that don't yet have a consent on file.</summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/consent/awaiting")]
    public async Task<IActionResult> GetAwaitingConsent()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();
        var tenantId = GetTenantId();

        try
        {
            var items = await _consentService.GetPortalAwaitingConsentAsync(patientId, tenantId);
            return Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal awaiting-consent for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading consent forms." });
        }
    }

    /// <summary>Return the rendered consent templates the patient needs to sign for the given appointment.</summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/consent/templates")]
    public async Task<IActionResult> GetConsentTemplates([FromQuery] int appointmentId)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();
        var tenantId = GetTenantId();

        if (appointmentId <= 0)
            return BadRequest(new { message = "AppointmentId is required." });

        try
        {
            var templates = await _consentService.GetPortalConsentTemplatesAsync(patientId, tenantId, appointmentId);
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading consent templates for PatientId={PatientId}, AppointmentId={AppointmentId}", patientId, appointmentId);
            return StatusCode(500, new { message = "Error loading consent templates." });
        }
    }

    /// <summary>Submit signed consent forms from the portal for a specific appointment.</summary>
    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/consent/submit")]
    public async Task<IActionResult> SubmitConsent([FromBody] PortalConsentSubmitRequestDto request)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();
        var tenantId = GetTenantId();

        if (request == null || request.AppointmentId <= 0)
            return BadRequest(new PortalConsentSubmitResponseDto { Success = false, Message = "AppointmentId is required." });

        var ip = GetIpAddress();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _consentService.SubmitPortalConsentAsync(patientId, tenantId, request, ip, ua);

        // Audit row regardless of success — staff need a trail of attempts.
        await _auditService.LogAccessAsync(
            null, GetPatientEmail(),
            result.Success ? "PORTAL_CONSENT_SUBMITTED" : "PORTAL_CONSENT_SUBMIT_FAILED",
            "Appointment", request.AppointmentId,
            null, null, ip);

        return Ok(result);
    }

    /// <summary>Portal documents — My Documents page</summary>
    [AllowAnonymous]
    [Route("Portal/Documents")]
    public IActionResult Documents()
    {
        return View("~/Views/Portal/Documents.cshtml");
    }

    /// <summary>Portal patient intake wizard — shell page, auth via JS/localStorage JWT</summary>
    [AllowAnonymous]
    [Route("Portal/Intake")]
    public IActionResult Intake()
    {
        return View("~/Views/Portal/Intake.cshtml");
    }

    // ============================================
    // NEW AUTH API: Email + Password + OTP
    // ============================================

    /// <summary>Get location info by portal code (for branding the login page)</summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("api/portal/location/{portalCode}")]
    public async Task<IActionResult> GetLocationInfo(string portalCode)
    {
        var info = await _invitationService.GetLocationByPortalCodeAsync(portalCode);
        if (info == null)
            return NotFound(new { message = "Invalid portal link." });
        return Ok(info);
    }

    /// <summary>Login with email + password. Returns OTP requirement on success.</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/login")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> PortalLoginApi([FromBody] PortalEmailLoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Please provide email, password, and portal code." });

        var result = await _invitationService.LoginAsync(request.Email, request.Password, request.PortalCode);
        return Ok(result);
    }

    /// <summary>Verify OTP code after successful email+password login</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/verify-otp")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> VerifyOtp([FromBody] PortalOtpVerifyRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Please provide the verification code." });

        var result = await _invitationService.VerifyOtpAsync(request.AccountId, request.Code, request.PortalCode);
        return Ok(result);
    }

    /// <summary>Resend OTP code</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/resend-otp")]
    [EnableRateLimiting("auth-resend")]
    public async Task<IActionResult> ResendOtp([FromBody] PortalResendOtpRequest request)
    {
        var sent = await _invitationService.SendOtpAsync(request.AccountId, request.PortalCode);
        return Ok(new { Success = sent, Message = sent ? "New code sent." : "Failed to send code." });
    }

    /// <summary>Self-service account setup: verify identity with LastName + DOB + ZipCode (2026-05: SSN removed).</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/setup-verify")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> SetupVerify([FromBody] PortalSetupVerifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LastName)
            || string.IsNullOrWhiteSpace(request.ZipCode)
            || string.IsNullOrWhiteSpace(request.PortalCode))
            return BadRequest(new { Success = false, Message = "Please provide all required fields." });

        var result = await _invitationService.SetupVerifyAsync(
            request.LastName, request.DateOfBirth, request.ZipCode, request.PortalCode);
        return Ok(result);
    }

    /// <summary>Self-service account setup: verify OTP and set password</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/setup-complete")]
    public async Task<IActionResult> SetupComplete([FromBody] PortalSetupCompleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { Success = false, Message = "Please provide the verification code and password." });

        if (request.Password.Length < 8)
            return BadRequest(new { Success = false, Message = "Password must be at least 8 characters." });

        var result = await _invitationService.SetupCompleteAsync(request.AccountId, request.Code, request.Password, request.PortalCode);
        return Ok(result);
    }

    /// <summary>Validate invitation token (for registration page)</summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("api/portal/invitation/validate")]
    public async Task<IActionResult> ValidateInvitation([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { Valid = false, Message = "Missing invitation token." });

        var result = await _invitationService.ValidateInvitationTokenAsync(token);
        return Ok(result);
    }

    /// <summary>Complete patient self-registration</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/invitation/register")]
    public async Task<IActionResult> CompleteRegistration([FromBody] CompleteRegistrationDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { Success = false, Message = "Please provide all required fields." });

        var result = await _invitationService.CompleteRegistrationAsync(dto);
        return Ok(result);
    }

    /// <summary>Upload profile picture during portal self-registration (anonymous, validated by patientId from registration result)</summary>
    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [Route("api/portal/registration/profile-picture/{patientId}")]
    public async Task<IActionResult> UploadRegistrationProfilePicture(int patientId, [FromForm] IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "No file provided" });

            // Verify this patient was recently registered via portal (has an active portal account created in last 10 minutes)
            var recentAccount = await _context.PatientPortalAccounts
                .AnyAsync(a => a.PatientId == patientId
                    && a.IsActive
                    && a.RegisteredAt != null
                    && a.RegisteredAt > DateTime.UtcNow.AddMinutes(-10));

            if (!recentAccount)
                return Unauthorized(new { success = false, message = "Invalid request" });

            var result = await _profilePictureService.UploadPatientProfilePictureAsync(patientId, file);
            if (result == null)
                return NotFound(new { success = false, message = "Patient not found" });

            return Ok(new { success = true, message = "Profile picture uploaded" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Portal] Profile picture upload failed for patient {PatientId}: {Message}", patientId, ex.Message);
            return StatusCode(500, new { success = false, message = "Upload failed" });
        }
    }

    /// <summary>Request password reset email</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] PortalForgotPasswordRequest request)
    {
        await _invitationService.RequestPasswordResetAsync(request.Email, request.PortalCode);
        // Always return success to prevent email enumeration
        return Ok(new { Success = true, Message = "If an account exists with that email, a reset link has been sent." });
    }

    /// <summary>Validate password reset token</summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("api/portal/auth/validate-reset-token")]
    public async Task<IActionResult> ValidateResetToken([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { Valid = false, Message = "Missing reset token." });

        var result = await _invitationService.ValidatePasswordResetTokenAsync(token);
        return Ok(result);
    }

    /// <summary>Reset password with token</summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/reset-password")]
    public async Task<IActionResult> ResetPasswordApi([FromBody] PortalResetPasswordRequest request)
    {
        var success = await _invitationService.ResetPasswordAsync(request.Token, request.NewPassword);
        return Ok(new { Success = success, Message = success ? "Password reset successful. You can now log in." : "Invalid or expired reset link." });
    }

    // ============================================
    // ADMIN: INVITATION MANAGEMENT
    // ============================================

    /// <summary>Send portal invitation to a patient (Admin only)</summary>
    [HttpPost]
    [Authorize]
    [Route("api/portal/invitations/send")]
    public async Task<IActionResult> SendInvitation([FromBody] SendInvitationDto dto)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _invitationService.SendInvitationAsync(dto, userId);
        return Ok(result);
    }

    /// <summary>Resend portal invitation (Admin only)</summary>
    [HttpPost]
    [Authorize]
    [Route("api/portal/invitations/{invitationId}/resend")]
    public async Task<IActionResult> ResendInvitation(int invitationId)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _invitationService.ResendInvitationAsync(invitationId, userId);
        return Ok(result);
    }

    /// <summary>Get all invitations for the current tenant (Admin only)</summary>
    [HttpGet]
    [Authorize]
    [Route("api/portal/invitations")]
    public async Task<IActionResult> GetInvitations([FromQuery] int? locationId = null)
    {
        var tenantId = GetTenantId();
        if (tenantId == 0) return Unauthorized();

        var invitations = await _invitationService.GetInvitationsAsync(tenantId, locationId);
        return Ok(invitations);
    }

    /// <summary>Get portal code for a location (Admin only)</summary>
    [HttpGet]
    [Authorize]
    [Route("api/portal/location-code/{locationId}")]
    public async Task<IActionResult> GetLocationPortalCode(int locationId)
    {
        var tenantId = GetTenantId();
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == locationId && l.TenantId == tenantId);
        if (location == null) return NotFound();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            PortalCode = location.PortalCode,
            PortalUrl = $"{baseUrl}/Portal/{location.PortalCode}"
        });
    }

    // ============================================
    // PORTAL DOCUMENT UPLOAD/LIST API
    // ============================================

    /// <summary>List patient's own uploaded documents</summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/documents")]
    public async Task<IActionResult> GetPortalDocuments()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        var documents = await _context.PatientDocuments
            .Where(d => d.PatientId == patientId && d.IsPatientUploaded == true && d.IsDeleted != true)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        var result = documents.Select(d =>
        {
            var fileName = d.FileName;
            try { fileName = _encryptionHelper.Decrypt(d.FileName); } catch { }
            return new
            {
                d.DocumentId,
                FileName = fileName,
                d.ContentType,
                d.FileSize,
                d.CreatedAt
            };
        });

        return Ok(result);
    }

    /// <summary>Upload document from patient portal</summary>
    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/documents/upload")]
    [RequestSizeLimit(31_457_280)] // 30MB
    public async Task<IActionResult> UploadPortalDocument([FromForm] IFormFile file, [FromForm] string? description)
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0 || tenantId == 0) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "No file provided" });

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(patientId, stream, file.FileName, file.ContentType, 5, description ?? "", 0);
        if (!result.Success)
            return BadRequest(new { success = false, message = result.Message });

        // Mark as patient-uploaded
        if (result.DocumentId > 0)
        {
            var doc = await _context.PatientDocuments.FindAsync(result.DocumentId);
            if (doc != null)
            {
                doc.IsPatientUploaded = true;
                await _context.SaveChangesAsync();
            }

            // Create system message for ALL users in tenant + send SignalR to each
            try
            {
                var docUrl = $"/api/patients/{patientId}/documents/{result.DocumentId}";
                var systemMsg = $"[DOC_UPLOAD]{file.FileName}|{file.Length}|{result.DocumentId}|{docUrl}";
                var msgResults = await _messagingService.CreateSystemMessageAsync(patientId, tenantId, systemMsg);

                if (msgResults.Any())
                {
                    // Get patient name for notifications
                    var patient = await _context.Patients.FindAsync(patientId);
                    var patientName = patient != null ? $"{patient.FirstName} {patient.LastName}".Trim() : "Patient";

                    // Send SignalR to each user
                    foreach (var mr in msgResults)
                    {
                        var unread = await _messagingService.GetUnreadSummaryForUserAsync(mr.UserId);
                        await _messagingNotificationService.SendUnreadUpdateToProviderAsync(mr.UserId, unread);
                        await _messagingNotificationService.SendNewMessageToProviderAsync(mr.UserId, new PatientMsgNewNotification
                        {
                            PatientMessageId = mr.PatientMessageId,
                            PatientConversationId = mr.PatientConversationId,
                            SenderType = "System",
                            SenderName = patientName,
                            MessagePreview = $"Uploaded: {file.FileName}",
                            CreatedAt = mr.CreatedAt
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create document upload notification for patient {PatientId}", patientId);
            }
        }

        return Ok(new { success = true, documentId = result.DocumentId, message = "Document uploaded successfully" });
    }

    /// <summary>Download patient's own document</summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/documents/{documentId}")]
    public async Task<IActionResult> DownloadPortalDocument(int documentId)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        // Verify document belongs to this patient
        var docCheck = await _context.PatientDocuments
            .FirstOrDefaultAsync(d => d.DocumentId == documentId && d.PatientId == patientId && d.IsPatientUploaded == true && d.IsDeleted != true);
        if (docCheck == null) return NotFound(new { message = "Document not found" });

        var result = await _documentService.DownloadDocumentAsync(documentId);
        if (result == null)
            return NotFound(new { message = "Document not found" });

        return File(result.Value.stream, result.Value.contentType, result.Value.fileName);
    }

    /// <summary>Delete patient's own document</summary>
    [HttpDelete]
    [Authorize(Roles = "8")]
    [Route("api/portal/documents/{documentId}")]
    public async Task<IActionResult> DeletePortalDocument(int documentId)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        // Verify the document belongs to this patient and was patient-uploaded
        var doc = await _context.PatientDocuments
            .FirstOrDefaultAsync(d => d.DocumentId == documentId && d.PatientId == patientId && d.IsPatientUploaded == true);
        if (doc == null) return NotFound(new { message = "Document not found" });

        var deleteResult = await _documentService.DeleteDocumentAsync(documentId, 0);
        return Ok(new { success = deleteResult });
    }

    // ============================================
    // LEGACY AUTH API (DOB + SSN Last 4 — kept for backward compatibility)
    // ============================================

    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/verify")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> VerifyIdentity([FromBody] PortalVerifyRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Please provide both date of birth and last 4 digits of SSN." });

        var result = await _portalAuthService.VerifyPatientAsync(request.DateOfBirth, request.SsnLast4);
        return Ok(result);
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/select-clinic")]
    public async Task<IActionResult> SelectClinic([FromBody] PortalSelectClinicRequest request)
    {
        var patient = await _context.Patients
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.PatientId == request.PatientId
                && p.TenantId == request.TenantId
                && p.IsDeleted != true);

        if (patient == null)
            return BadRequest(new { Success = false, Message = "Unable to verify. Please try again." });

        _encryptionHelper.DecryptEntity(patient);

        var locations = await _portalAuthService.GetTenantLocationsAsync(request.TenantId);

        if (locations.Count > 1)
        {
            return Ok(new PatientPortalVerifyResult
            {
                Success = true,
                RequiresLocationSelection = true,
                PatientId = patient.PatientId,
                PatientName = $"{patient.FirstName} {patient.LastName}",
                TenantId = patient.TenantId,
                AvailableLocations = locations
            });
        }

        Location? location = locations.Count == 1
            ? await _context.Locations.FindAsync(locations[0].LocationId)
            : null;

        var token = _portalAuthService.GeneratePortalToken(patient, patient.Tenant!, location);

        await _auditService.LogAccessAsync(
            null, patient.Email, "PORTAL_LOGIN", "Patient", patient.PatientId,
            null, null, GetIpAddress());

        return Ok(new PatientPortalVerifyResult
        {
            Success = true,
            Token = token,
            PatientId = patient.PatientId,
            PatientName = $"{patient.FirstName} {patient.LastName}",
            TenantId = patient.TenantId,
            TokenExpiry = DateTime.UtcNow.AddHours(4)
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("api/portal/auth/select-location")]
    public async Task<IActionResult> SelectLocation([FromBody] PortalSelectLocationRequest request)
    {
        var patient = await _context.Patients
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.PatientId == request.PatientId
                && p.TenantId == request.TenantId
                && p.IsDeleted != true);

        if (patient == null)
            return BadRequest(new { Success = false, Message = "Unable to verify. Please try again." });

        _encryptionHelper.DecryptEntity(patient);

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == request.LocationId
                && l.TenantId == request.TenantId
                && l.IsActive == true);

        var token = _portalAuthService.GeneratePortalToken(patient, patient.Tenant!, location);

        await _auditService.LogAccessAsync(
            null, patient.Email, "PORTAL_LOGIN", "Patient", patient.PatientId,
            null, null, GetIpAddress());

        return Ok(new PatientPortalVerifyResult
        {
            Success = true,
            Token = token,
            PatientId = patient.PatientId,
            PatientName = $"{patient.FirstName} {patient.LastName}",
            TenantId = patient.TenantId,
            TokenExpiry = DateTime.UtcNow.AddHours(4)
        });
    }

    // ============================================
    // DATA API ENDPOINTS (auth required, Role = 8)
    // ============================================

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var appointments = await _appointmentService.GetAppointmentsAsync(
                startDate: DateTime.Today,
                endDate: DateTime.Today.AddMonths(6),
                patientId: patientId);

            var allAppts = appointments ?? new List<AppointmentListDto>();
            var upcomingAppts = allAppts
                .Where(a => a.Status == 0 || a.Status == 1)
                .OrderBy(a => a.StartTime)
                .ToList();

            var notes = await _noteService.GetNotesAsync(patientId: patientId, status: 2);
            var noteCount = notes?.Count ?? 0;

            var medications = await _medicationService.GetByPatientAsync(patientId);
            var activeMedCount = medications?.Count(m => m.Status == 0) ?? 0;

            var allergies = await _allergyService.GetByPatientAsync(patientId);
            var allergyCount = allergies?.Count(a => a.IsActive) ?? 0;

            var nextAppt = upcomingAppts.FirstOrDefault();

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_DASHBOARD", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(new
            {
                UpcomingAppointmentsCount = upcomingAppts.Count,
                RecentNotesCount = noteCount,
                ActiveMedicationsCount = activeMedCount,
                ActiveAllergiesCount = allergyCount,
                NextAppointment = nextAppt != null ? new
                {
                    nextAppt.AppointmentId,
                    nextAppt.StartTime,
                    nextAppt.EndTime,
                    nextAppt.ProviderName,
                    nextAppt.Type,
                    nextAppt.Status,
                    nextAppt.Reason,
                    nextAppt.LocationName,
                    nextAppt.TimeZoneId,
                    nextAppt.TimeZoneAbbreviation,
                    nextAppt.DateFormatted,
                    nextAppt.StartTimeFormatted,
                    nextAppt.EndTimeFormatted,
                    nextAppt.IsTelehealth
                } : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal dashboard for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading dashboard data." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/appointments")]
    public async Task<IActionResult> GetAppointments()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var appointments = await _appointmentService.GetAppointmentsAsync(patientId: patientId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_APPOINTMENTS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(appointments?.OrderByDescending(a => a.StartTime).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal appointments for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading appointments." });
        }
    }

    // ============================================
    // PATIENT PORTAL BOOKING
    // ============================================

    /// <summary>
    /// Get available appointment slots for patient portal booking.
    /// Returns merged slots across all providers (no provider info exposed).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/appointments/available-slots")]
    public async Task<IActionResult> GetPortalAvailableSlots([FromQuery] DateTime date, [FromQuery] int? providerId = null, [FromQuery] int? duration = null)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var tenantId = GetTenantId();
            int? locationId = GetLocationId();
            if (locationId == 0) locationId = null;

            // Duration: caller-supplied value wins (longevity = 60 / 45). Otherwise pull
            // the tenant-wide DefaultPatientBookingDuration system setting (default 30).
            int durationMinutes;
            if (duration.HasValue && duration.Value >= 5 && duration.Value <= 240)
            {
                durationMinutes = duration.Value;
            }
            else
            {
                var durationSetting = await _context.SystemSettings
                    .Where(s => s.TenantId == tenantId && s.SettingKey == "DefaultPatientBookingDuration")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync();
                durationMinutes = int.TryParse(durationSetting, out var d) ? d : 30;
            }

            // Get clinic timezone for accurate "today" calculation
            var location = locationId.HasValue
                ? await _context.Locations.FindAsync(locationId.Value)
                : await _context.Locations.FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive == true);
            var clinicTimeZoneId = location?.TimeZoneId ?? Helpers.TimezoneHelper.DefaultTimeZoneId;
            var clinicNow = Helpers.TimezoneHelper.ConvertFromUtc(DateTime.UtcNow, clinicTimeZoneId);
            var clinicTodayStr = clinicNow.ToString("yyyy-MM-dd");
            var clinicTimeZoneAbbr = Helpers.TimezoneHelper.GetTimezoneAbbreviation(clinicTimeZoneId, DateTime.UtcNow);

            var slots = await _appointmentService.GetPortalAvailableSlotsAsync(tenantId, locationId, date, durationMinutes, providerId);

            return Ok(new { slots, durationMinutes, clinicToday = clinicTodayStr, clinicTimeZone = clinicTimeZoneAbbr });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal available slots for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading available time slots." });
        }
    }

    /// <summary>
    /// List providers a patient can pick when booking on the portal. Returns
    /// active providers who have a schedule at the patient's preferred location.
    /// Photos are served separately via /api/providers/{id}/profile-picture.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/providers")]
    public async Task<IActionResult> GetPortalBookableProviders()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var tenantId = GetTenantId();
            int? locationId = GetLocationId();
            if (locationId == 0) locationId = null;

            var providers = await _appointmentService.GetPortalBookableProvidersAsync(tenantId, locationId);
            return Ok(providers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal providers for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading providers." });
        }
    }

    /// <summary>
    /// Book an appointment from the patient portal.
    /// Honors dto.ProviderId when set; otherwise picks a random available provider.
    /// Honors dto.Type when set; otherwise defaults to FollowUpVisit.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/appointments/book")]
    public async Task<IActionResult> BookPortalAppointment([FromBody] PortalBookAppointmentDto dto)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var tenantId = GetTenantId();
            int? locationId = GetLocationId();
            if (locationId == 0) locationId = null;

            var appointment = await _appointmentService.BookPortalAppointmentAsync(patientId, tenantId, locationId, dto);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_BOOK_APPOINTMENT", "Appointment", appointment.AppointmentId,
                null, $"Patient booked appointment for {dto.StartTime:g}", GetIpAddress());

            return Ok(new { appointmentId = appointment.AppointmentId, message = "Appointment booked successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error booking portal appointment for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error booking appointment. Please try again." });
        }
    }

    /// <summary>
    /// Patient-facing list of visits (encounters). Returns one row per closed encounter
    /// that has an AI-generated summary. Open encounters and encounters without a summary
    /// are excluded — patients see only finalized visits with patient-friendly content.
    /// Spec: rules/technical/encounter-summary.md
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/visits")]
    public async Task<IActionResult> GetVisits()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var encounters = await _encounterService.GetByPatientAsync(patientId);

            // Closed only (Status >= 1: Signed/Locked/Amended). Open encounters never appear.
            var visits = (encounters ?? new List<EncounterDto>())
                .Where(e => e.Status >= 1)
                .OrderByDescending(e => e.EncounterDate)
                .Select(e => new
                {
                    e.EncounterId,
                    e.EncounterDate,
                    e.ProviderId,
                    e.ProviderName,
                    e.ProviderHasProfilePicture,
                    e.AppointmentType,
                    HasSummary = !string.IsNullOrWhiteSpace(e.SummaryText)
                })
                .ToList();

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_VISITS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(visits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal visits for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading visits." });
        }
    }

    /// <summary>
    /// Patient-facing detail for one visit. Returns the AI-generated, plain-language
    /// summary plus visit metadata (date, provider, type). Never returns the raw
    /// clinical note HTML — patients do not see provider documentation.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/visits/{encounterId}/summary")]
    public async Task<IActionResult> GetVisitSummary(int encounterId)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var encounter = await _encounterService.GetByIdAsync(encounterId);

            // Ownership + closed-only check
            if (encounter == null || encounter.PatientId != patientId || encounter.Status < 1)
                return NotFound(new { message = "Visit not found." });

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_VISIT_SUMMARY", "Encounter", encounterId,
                null, null, GetIpAddress());

            return Ok(new
            {
                encounter.EncounterId,
                encounter.EncounterDate,
                encounter.ProviderName,
                encounter.AppointmentType,
                encounter.SummaryText,
                HasSummary = !string.IsNullOrWhiteSpace(encounter.SummaryText)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal visit summary {EncounterId} for PatientId={PatientId}", encounterId, patientId);
            return StatusCode(500, new { message = "Error loading visit summary." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/medications")]
    public async Task<IActionResult> GetMedications()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var medications = await _medicationService.GetByPatientAsync(patientId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_MEDICATIONS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(medications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal medications for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading medications." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/prescriptions")]
    public async Task<IActionResult> GetPortalPrescriptions()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var prescriptions = await _prescriptionService.GetByPatientAsync(patientId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_PRESCRIPTIONS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal prescriptions for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading prescriptions." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/orders")]
    public async Task<IActionResult> GetPortalOrders()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            // Fetch all orders then exclude Referrals (OrderType 2).
            // Patient portal shows Lab (0) and Imaging (1) only.
            var orders = await _orderService.GetByPatientAsync(patientId);
            var filtered = orders.Where(o => o.OrderType != 2).ToList();

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_ORDERS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal orders for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading orders." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/allergies")]
    public async Task<IActionResult> GetAllergies()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var allergies = await _allergyService.GetByPatientAsync(patientId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_ALLERGIES", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(allergies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal allergies for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading allergies." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/vitals")]
    public async Task<IActionResult> GetVitals()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var vitals = await _vitalService.GetByPatientAsync(patientId);
            var recent = vitals?.Take(20).ToList();

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_VITALS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(recent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal vitals for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading vitals." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/profile")]
    public async Task<IActionResult> GetProfile()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var patient = await _patientService.GetPatientByIdAsync(patientId);

            if (patient == null)
                return NotFound(new { message = "Patient not found." });

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_PROFILE", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(patient);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading portal profile for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading profile." });
        }
    }

    // ============================================
    // PORTAL: Patient self-service profile picture
    // ============================================
    // Patients can upload/replace/remove ONLY their own profile picture from
    // the portal (per product rule: profile pic is the only field a patient
    // can edit on themselves). The PatientId is derived from the portal JWT —
    // never accepted from the URL or body — so a patient cannot change another
    // patient's avatar. Reads go through the existing AllowAnonymous endpoint
    // `/api/patients/{id}/profile-picture` (the `<img>` tag can't send a JWT).
    [HttpPost]
    [Authorize(Roles = "8")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [Route("api/portal/me/profile-picture")]
    public async Task<IActionResult> UploadMyProfilePicture([FromForm] IFormFile file)
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "No file provided" });

        try
        {
            var result = await _profilePictureService.UploadPatientProfilePictureAsync(patientId, file);
            if (result == null)
                return NotFound(new { success = false, message = "Patient not found" });

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_UPLOAD_PROFILE_PICTURE", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Portal] Profile picture upload failed for PatientId={PatientId}", patientId);
            return StatusCode(500, new { success = false, message = "Upload failed" });
        }
    }

    [HttpDelete]
    [Authorize(Roles = "8")]
    [Route("api/portal/me/profile-picture")]
    public async Task<IActionResult> DeleteMyProfilePicture()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var removed = await _profilePictureService.DeletePatientProfilePictureAsync(patientId);
            if (!removed)
                return NotFound(new { success = false, message = "No profile picture to remove" });

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_DELETE_PROFILE_PICTURE", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(new { success = true, message = "Profile picture removed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Portal] Profile picture delete failed for PatientId={PatientId}", patientId);
            return StatusCode(500, new { success = false, message = "Delete failed" });
        }
    }

    // ============================================
    // BILLING - MVC View
    // ============================================

    [Route("Portal/Billing")]
    public IActionResult Billing()
    {
        ViewData["PortalPage"] = "billing";
        return View("~/Views/Portal/Billing.cshtml");
    }

    /// <summary>Separate Stripe payment page — standalone, JS reads auth from localStorage</summary>
    [AllowAnonymous]
    [Route("Portal/Payment")]
    public IActionResult Payment()
    {
        return View("~/Views/Portal/Payment.cshtml");
    }

    /// <summary>Installment plan setup page — standalone, JS reads auth from localStorage</summary>
    [AllowAnonymous]
    [Route("Portal/InstallmentSetup")]
    public IActionResult InstallmentSetup()
    {
        return View("~/Views/Portal/InstallmentSetup.cshtml");
    }

    /// <summary>Payment success page with receipt</summary>
    [AllowAnonymous]
    [Route("Portal/PaymentSuccess")]
    public async Task<IActionResult> PaymentSuccess(
        [FromQuery] string payment_intent,
        [FromQuery] string stripe_account)
    {
        string patientName = "Patient";
        string clinicName = "";

        try
        {
            _logger.LogInformation("PaymentSuccess called with payment_intent={PI} stripe_account={Acct}",
                payment_intent, stripe_account);

            // Look up Payment row for this PaymentIntent. If the webhook has already
            // landed (typical) we use those values, no Stripe API roundtrip needed.
            var paymentRow = await _context.Payments
                .Include(p => p.StripeConnectAccount)
                .FirstOrDefaultAsync(p => p.StripePaymentIntentId == payment_intent);

            if (paymentRow != null)
            {
                ViewData["AmountPaid"] = paymentRow.Amount.ToString("0.00");
                ViewData["TransactionId"] = paymentRow.StripePaymentIntentId;
                ViewData["TransactionDate"] = (paymentRow.CreatedAt ?? DateTime.UtcNow).ToString("MMMM d, yyyy");
            }
            else if (!string.IsNullOrEmpty(payment_intent) && !string.IsNullOrEmpty(stripe_account))
            {
                // Webhook hasn't fired yet — race condition: Stripe redirects the user
                // faster than the webhook lands. Fetch the PaymentIntent directly from
                // Stripe API using the connected account header passed in the redirect
                // URL. The stripe_account ID is not a secret (visible in the browser
                // network tab anyway) and we use it only to read, never to mutate.
                try
                {
                    var pi = await _stripeService.GetPaymentIntentAsync(payment_intent, stripe_account);
                    ViewData["AmountPaid"] = (pi.Amount / 100m).ToString("0.00");
                    ViewData["TransactionId"] = pi.Id;
                    ViewData["TransactionDate"] = pi.Created.ToString("MMMM d, yyyy");
                }
                catch (Exception fetchEx)
                {
                    _logger.LogWarning(fetchEx,
                        "Failed to fetch PaymentIntent {PI} from Stripe API as fallback", payment_intent);
                    ViewData["AmountPaid"] = "0.00";
                    ViewData["TransactionId"] = payment_intent;
                    ViewData["TransactionDate"] = DateTime.Now.ToString("MMMM d, yyyy");
                }
            }
            else
            {
                // Defensive placeholder — neither local row nor enough info to fetch.
                ViewData["AmountPaid"] = "0.00";
                ViewData["TransactionId"] = payment_intent ?? "N/A";
                ViewData["TransactionDate"] = DateTime.Now.ToString("MMMM d, yyyy");
            }

            // Pull patient/tenant info from local DB (available only once webhook has
            // written the Payment row). If not available, leave defaults — receipt
            // page is informational, so missing name is acceptable in the race window.
            if (paymentRow != null)
            {
                var patient = await _context.Patients.FindAsync(paymentRow.PatientId);
                if (patient != null)
                {
                    _context.Entry(patient).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    try { _encryptionHelper.DecryptEntity(patient); } catch { }
                    patientName = $"{patient.FirstName} {patient.LastName}".Trim();
                }
                var tenant = await _context.Tenants.FindAsync(paymentRow.TenantId);
                clinicName = tenant?.Name ?? "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading payment success page for {PI}", payment_intent);
            ViewData["AmountPaid"] = "0.00";
            ViewData["TransactionId"] = payment_intent ?? "N/A";
            ViewData["TransactionDate"] = DateTime.Now.ToString("MMMM d, yyyy");
        }

        ViewData["PatientName"] = patientName;
        ViewData["ClinicName"] = clinicName;

        return View("~/Views/Portal/PaymentSuccess.cshtml");
    }

    /// <summary>Payment failure page</summary>
    [AllowAnonymous]
    [Route("Portal/PaymentFailed")]
    public IActionResult PaymentFailed()
    {
        return View("~/Views/Portal/PaymentFailed.cshtml");
    }

    // ============================================
    // BILLING - API Endpoints (Patient Portal)
    // ============================================

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/balance")]
    public async Task<IActionResult> GetBillingBalance()
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var balance = await _paymentService.GetPortalBalanceAsync(patientId, tenantId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_BALANCE", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(balance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading billing balance for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading billing information." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/transactions")]
    public async Task<IActionResult> GetBillingTransactions()
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var transactions = await _paymentService.GetPatientTransactionsAsync(patientId, tenantId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_VIEW_TRANSACTIONS", "Patient", patientId,
                null, null, GetIpAddress());

            return Ok(transactions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading transactions for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading transactions." });
        }
    }

    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/create-payment-intent")]
    public async Task<IActionResult> CreatePaymentIntent([FromBody] StripePaymentCreateDto dto)
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var patient = await _context.Patients
                .Include(p => p.PreferredLocation).ThenInclude(l => l.StripeConnectAccount)
                .FirstOrDefaultAsync(p => p.PatientId == patientId);
            if (patient == null) return NotFound();

            // Patient must have a preferred location with a connected Stripe account
            var locationId = patient.PreferredLocationId;
            if (locationId == null || patient.PreferredLocation == null)
            {
                return Conflict(new { message = "Your clinic location is not configured for online payments." });
            }

            if (patient.PreferredLocation.StripeConnectAccountId == null
                || patient.PreferredLocation.StripeConnectAccount == null
                || patient.PreferredLocation.StripeConnectAccount.Status != (int)StripeConnectAccountStatus.Active)
            {
                return Conflict(new
                {
                    message = "Online payments are temporarily unavailable. Please contact the clinic directly."
                });
            }

            if (dto.Amount <= 0)
                return BadRequest(new { message = "Amount must be greater than zero." });

            // Decrypt patient email/name for Stripe customer creation
            try { _encryptionHelper.DecryptEntity(patient); } catch { /* already plaintext */ }

            // Get or create Stripe customer ON the connected account
            var customerId = await _stripeService.GetOrCreateCustomerAsync(patientId, locationId.Value);

            // Best-effort link this payment to the most recent checked-in appointment
            // that still has an outstanding copay. Heuristic chosen because the portal
            // Pay Now flow doesn't pick a specific charge (patient enters any amount);
            // the most likely intent is to clear the copay for the visit they were
            // just checked in for. If no such appointment exists, AppointmentId stays
            // null and the payment is recorded as a general patient payment.
            var linkedAppointment = await _context.Appointments
                .Where(a => a.PatientId == patientId
                    && a.TenantId == tenantId
                    && a.Status >= (int)AppointmentStatus.CheckedIn
                    && a.CopayDue.HasValue && a.CopayDue > 0
                    && (a.CopayCollected == null || a.CopayCollected < a.CopayDue))
                .OrderByDescending(a => a.StartTime)
                .Select(a => new { a.AppointmentId })
                .FirstOrDefaultAsync();

            var metadata = new Dictionary<string, string>
            {
                { "paymentType", "Full" },
                { "patientId", patientId.ToString() },
                { "tenantId", tenantId.ToString() },
                { "locationId", locationId.Value.ToString() }
            };
            if (linkedAppointment != null)
            {
                metadata["appointmentId"] = linkedAppointment.AppointmentId.ToString();
            }

            var amountCents = (int)Math.Round(dto.Amount * 100);
            var (clientSecret, paymentIntentId, fees) = await _stripeService.CreatePaymentIntentAsync(
                locationId.Value, amountCents, customerId, metadata);

            return Ok(new
            {
                ClientSecret = clientSecret,
                PublishableKey = _stripeService.GetPublishableKey(),
                StripeAccountId = patient.PreferredLocation.StripeConnectAccount.StripeAccountId
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment intent for PatientId={PatientId}: {Message}", patientId, ex.Message);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "emails_sent", "_stripe_errors.log"), $"{DateTime.Now}: {ex}\n\n"); } catch { }
            return this.ServerError(ex, "An unexpected error occurred.");
        }
    }

    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/setup-intent")]
    public async Task<IActionResult> CreateSetupIntent()
    {
        var patientId = GetPatientId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var patient = await _context.Patients
                .Include(p => p.PreferredLocation).ThenInclude(l => l.StripeConnectAccount)
                .FirstOrDefaultAsync(p => p.PatientId == patientId);
            if (patient == null) return NotFound();

            var locationId = patient.PreferredLocationId;
            if (locationId == null
                || patient.PreferredLocation?.StripeConnectAccountId == null
                || patient.PreferredLocation.StripeConnectAccount?.Status != (int)StripeConnectAccountStatus.Active)
            {
                return Conflict(new { message = "Online payments are temporarily unavailable." });
            }

            try { _encryptionHelper.DecryptEntity(patient); } catch { /* already plaintext */ }

            var customerId = await _stripeService.GetOrCreateCustomerAsync(patientId, locationId.Value);
            var clientSecret = await _stripeService.CreateSetupIntentAsync(locationId.Value, customerId);

            return Ok(new
            {
                ClientSecret = clientSecret,
                PublishableKey = _stripeService.GetPublishableKey(),
                StripeAccountId = patient.PreferredLocation.StripeConnectAccount.StripeAccountId
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating setup intent for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error setting up payment method." });
        }
    }

    [HttpGet]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/installment-plan")]
    public async Task<IActionResult> GetInstallmentPlan()
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0) return Unauthorized();

        try
        {
            var plan = await _installmentService.GetActivePlanByPatientAsync(patientId, tenantId);
            return Ok(plan); // null if no active plan
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading installment plan for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error loading payment plan." });
        }
    }

    [HttpPost]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/installment-plan")]
    public async Task<IActionResult> CreateInstallmentPlan([FromBody] InstallmentPlanCreateDto dto)
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0) return Unauthorized();

        try
        {
            // If LocationId not provided, fall back to patient's preferred location
            if (dto.LocationId <= 0)
            {
                var patient = await _context.Patients.FindAsync(patientId);
                if (patient?.PreferredLocationId == null)
                {
                    return BadRequest(new { message = "Location is required for installment plans." });
                }
                dto.LocationId = patient.PreferredLocationId.Value;
            }

            var plan = await _installmentService.CreatePlanAsync(patientId, tenantId, dto);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_CREATE_INSTALLMENT_PLAN", "Patient", patientId,
                null, $"Amount: {dto.TotalAmount}, Installments: {dto.NumberOfInstallments}, LocationId: {dto.LocationId}", GetIpAddress());

            return Ok(plan);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating installment plan for PatientId={PatientId}", patientId);
            return StatusCode(500, new { message = "Error creating payment plan." });
        }
    }

    [HttpDelete]
    [Authorize(Roles = "8")]
    [Route("api/portal/billing/installment-plan/{planId}")]
    public async Task<IActionResult> CancelInstallmentPlan(int planId)
    {
        var patientId = GetPatientId();
        var tenantId = GetTenantId();
        if (patientId == 0) return Unauthorized();

        try
        {
            await _installmentService.CancelPlanAsync(planId, tenantId);

            await _auditService.LogAccessAsync(
                null, GetPatientEmail(), "PORTAL_CANCEL_INSTALLMENT_PLAN", "Patient", patientId,
                null, $"PlanId: {planId}", GetIpAddress());

            return Ok(new { message = "Payment plan cancelled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling installment plan {PlanId}", planId);
            return StatusCode(500, new { message = "Error cancelling payment plan." });
        }
    }
}

// ============================================
// REQUEST DTOs
// ============================================

public class PortalVerifyRequest
{
    [Required]
    public DateOnly DateOfBirth { get; set; }

    [Required]
    [StringLength(4, MinimumLength = 4)]
    public string SsnLast4 { get; set; } = string.Empty;
}

public class PortalSelectClinicRequest
{
    [Required]
    public int PatientId { get; set; }

    [Required]
    public int TenantId { get; set; }
}

public class PortalSelectLocationRequest
{
    [Required]
    public int PatientId { get; set; }

    [Required]
    public int TenantId { get; set; }

    [Required]
    public int LocationId { get; set; }
}

public class PortalEmailLoginRequest
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string PortalCode { get; set; } = string.Empty;
}

public class PortalOtpVerifyRequest
{
    [Required]
    public int AccountId { get; set; }

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string PortalCode { get; set; } = string.Empty;
}

public class PortalResendOtpRequest
{
    [Required]
    public int AccountId { get; set; }

    [Required]
    public string PortalCode { get; set; } = string.Empty;
}

public class PortalForgotPasswordRequest
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PortalCode { get; set; } = string.Empty;
}

public class PortalResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}

public class PortalSetupVerifyRequest
{
    // Identity verification for portal Set Up Account flow (as of 2026-05):
    // LastName + DateOfBirth + ZipCode. SSN was removed.
    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public DateOnly DateOfBirth { get; set; }

    [Required(ErrorMessage = "ZIP code is required")]
    [StringLength(10, MinimumLength = 5)]
    public string ZipCode { get; set; } = string.Empty;

    [Required]
    public string PortalCode { get; set; } = string.Empty;
}

public class PortalSetupCompleteRequest
{
    [Required]
    public int AccountId { get; set; }

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string PortalCode { get; set; } = string.Empty;
}
