using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using EHR.Data;
using EHR.Helpers;
using EHR.Models;
using EHR.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EHR.Controllers;

/// <summary>
/// Serves the /KioskSetup page. This page is used by the Android tablet app
/// to let clinic admins sign in, pick a location, and open that location's kiosk.
/// The page calls the API endpoints below; admin session is held in a HttpOnly
/// cookie so the WebView never touches localStorage.
/// </summary>
[AllowAnonymous]
public class KioskSetupController : Controller
{
    [Route("KioskSetup")]
    public IActionResult Index()
    {
        return View("~/Views/KioskSetup/Index.cshtml");
    }
}

/// <summary>
/// API endpoints supporting the /KioskSetup flow. Lives in its own controller
/// so the existing AuthController, KioskController, and Program.cs JWT setup
/// are untouched.
/// </summary>
[ApiController]
[Route("api/kiosksetup")]
public class KioskSetupApiController : ControllerBase
{
    /// <summary>Cookie used to hold the admin JWT for the kiosk-setup flow.</summary>
    public const string AuthCookieName = "imehr_kiosk_auth";

    private readonly EhrDbContext _context;
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;
    private readonly ILogger<KioskSetupApiController> _logger;

    public KioskSetupApiController(
        EhrDbContext context,
        IAuthService authService,
        IConfiguration config,
        ILogger<KioskSetupApiController> logger)
    {
        _context = context;
        _authService = authService;
        _config = config;
        _logger = logger;
    }

    // ======================================================================
    // Public (no auth required)
    // ======================================================================

    /// <summary>
    /// Authenticates the admin and, on success, stores the JWT in a
    /// HttpOnly cookie scoped to this browser/WebView.
    /// </summary>
    [HttpPost("sign-in")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskSetupSignInResponseDto>> SignIn([FromBody] UserLoginDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { success = false, message = "Email and password are required." });

        UserLoginResponseDto? result;
        try
        {
            result = await _authService.LoginAsync(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KioskSetup sign-in failed for {EmailHash}", PhiLog.Hash(dto.Email));
            return StatusCode(500, new { success = false, message = "Sign in failed. Please try again." });
        }

        if (result == null)
            return Unauthorized(new { success = false, message = "Invalid email or password." });

        if (result.RequiresTenantSelection)
        {
            return Ok(new KioskSetupSignInResponseDto
            {
                Success = false,
                RequiresTenantSelection = true,
                Message = "This account belongs to multiple clinics. Please sign in through the web app to select one."
            });
        }

        // Backend requires OTP verification — forward the state to the client.
        // No cookie is set yet; that happens after the OTP is verified.
        if (result.RequiresOtpVerification)
        {
            return Ok(new KioskSetupSignInResponseDto
            {
                Success = false,
                RequiresOtpVerification = true,
                Email = result.Email,
                Message = "A verification code has been sent to your email."
            });
        }

        if (string.IsNullOrEmpty(result.Token))
            return Unauthorized(new { success = false, message = "Authentication did not return a session token." });

        // Persist the JWT in a HttpOnly cookie. The browser/WebView will
        // automatically send it on subsequent requests so no JS storage is needed.
        AppendAuthCookie(result.Token, result.TokenExpiry);

        return Ok(new KioskSetupSignInResponseDto
        {
            Success = true,
            UserId = result.UserId,
            Email = result.Email,
            FullName = result.FullName,
            Role = result.Role,
            TenantId = result.TenantId,
            TenantName = result.TenantName
        });
    }

    /// <summary>
    /// Verifies the OTP that follows a sign-in attempt. On success the
    /// session JWT is issued and stored in the kiosk auth cookie, so the
    /// following /me and /locations requests succeed just like a direct
    /// sign-in would.
    /// </summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<KioskSetupSignInResponseDto>> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.OtpCode))
            return BadRequest(new { success = false, message = "Email and verification code are required." });

        dto.UserAgent = Request.Headers.UserAgent.ToString();

        UserLoginResponseDto? result;
        try
        {
            result = await _authService.VerifyOtpAsync(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KioskSetup OTP verify failed for {EmailHash}", PhiLog.Hash(dto.Email));
            return StatusCode(500, new { success = false, message = "Verification failed. Please try again." });
        }

        if (result == null)
            return Unauthorized(new { success = false, message = "Invalid or expired verification code." });

        if (string.IsNullOrEmpty(result.Token))
            return Unauthorized(new { success = false, message = "Verification did not return a session token." });

        AppendAuthCookie(result.Token, result.TokenExpiry);

        // Never leak the device-trust token to the client — the kiosk flow
        // does not use Remember-Device and we clear this field defensively.
        result.DeviceToken = null;

        return Ok(new KioskSetupSignInResponseDto
        {
            Success = true,
            UserId = result.UserId,
            Email = result.Email,
            FullName = result.FullName,
            Role = result.Role,
            TenantId = result.TenantId,
            TenantName = result.TenantName
        });
    }

    /// <summary>Requests a new OTP code to be sent to the user's email.</summary>
    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<ActionResult> ResendOtp([FromBody] ResendOtpDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { success = false, message = "Email is required." });

        try
        {
            await _authService.ResendOtpAsync(dto.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KioskSetup OTP resend failed for {EmailHash}", PhiLog.Hash(dto.Email));
        }

        // Always return success to avoid email enumeration.
        return Ok(new { success = true, message = "If a pending verification exists, a new code has been sent." });
    }

    /// <summary>Clears the kiosk-setup auth cookie.</summary>
    [HttpPost("sign-out")]
    [AllowAnonymous]
    public ActionResult SignOut()
    {
        ClearAuthCookie();
        return Ok(new { success = true });
    }

    // ======================================================================
    // Authenticated (cookie-based)
    // ======================================================================

    /// <summary>Current signed-in admin's basic profile (for UI header/masked email).</summary>
    [HttpGet("me")]
    [KioskSetupAuthorize]
    public ActionResult Me()
    {
        return Ok(new
        {
            UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Email = User.FindFirst(ClaimTypes.Email)?.Value,
            FullName = User.FindFirst(ClaimTypes.Name)?.Value,
            Role = User.FindFirst("Role")?.Value,
            TenantId = User.FindFirst("TenantId")?.Value,
            TenantName = User.FindFirst("TenantName")?.Value
        });
    }

    /// <summary>
    /// Returns the tenant's active locations together with each location's
    /// kiosk token so the client can redirect into the correct kiosk.
    /// </summary>
    [HttpGet("locations")]
    [KioskSetupAuthorize]
    public async Task<ActionResult<IEnumerable<KioskSetupLocationDto>>> GetLocations()
    {
        var tenantId = GetTenantId();
        if (tenantId == 0)
            return Unauthorized(new { success = false, message = "No tenant associated with this account." });

        // Tenant-wide (LocationId == null) consent templates cover every location.
        // Location-specific templates only cover that LocationId.
        var hasTenantWideForm = await _context.ConsentFormTemplates
            .AnyAsync(t => t.TenantId == tenantId
                        && t.LocationId == null
                        && t.IsActive == true
                        && t.IsDeleted == false);

        var locationsWithForm = await _context.ConsentFormTemplates
            .Where(t => t.TenantId == tenantId
                     && t.LocationId != null
                     && t.IsActive == true
                     && t.IsDeleted == false)
            .Select(t => t.LocationId!.Value)
            .Distinct()
            .ToListAsync();

        var rows = await _context.Locations
            .Where(l => l.TenantId == tenantId && l.IsActive == true)
            .OrderByDescending(l => l.IsPrimary == true)
            .ThenBy(l => l.Name)
            .Select(l => new
            {
                l.LocationId,
                l.Name,
                l.Address,
                l.City,
                l.State,
                l.IsPrimary,
                KioskToken = _context.LocationKioskSettings
                    .Where(k => k.LocationId == l.LocationId)
                    .Select(k => new { k.KioskToken, k.IsEnabled })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = rows.Select(r => new KioskSetupLocationDto
        {
            LocationId = r.LocationId,
            Name = r.Name ?? string.Empty,
            Address = string.Join(", ", new[] { r.Address, r.City, r.State }
                .Where(s => !string.IsNullOrWhiteSpace(s))),
            IsPrimary = r.IsPrimary == true,
            KioskToken = r.KioskToken?.KioskToken,
            KioskEnabled = r.KioskToken?.IsEnabled == true,
            HasConsentForm = hasTenantWideForm || locationsWithForm.Contains(r.LocationId)
        });

        return Ok(result);
    }

    /// <summary>
    /// Verifies the current admin's password. Used by the in-kiosk logout
    /// modal to prove the person clicking sign-out is the admin. On success
    /// the auth cookie is cleared so a refresh returns to /KioskSetup.
    /// </summary>
    [HttpPost("confirm-password")]
    [KioskSetupAuthorize]
    public async Task<ActionResult> ConfirmPassword([FromBody] KioskSetupConfirmPasswordDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.Password))
            return BadRequest(new { success = false, message = "Password is required." });

        var userId = GetUserId();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
            return Unauthorized(new { success = false, message = "Account not found." });

        var ok = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!ok)
            return Ok(new { success = false, message = "Incorrect password." });

        // Sign-out after successful password verification
        ClearAuthCookie();
        return Ok(new { success = true });
    }

    // ======================================================================
    // Helpers
    // ======================================================================

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : 0;

    private int GetTenantId() =>
        int.TryParse(User.FindFirst("TenantId")?.Value, out var t) ? t : 0;

    private void AppendAuthCookie(string jwt, DateTime? expiry)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiry ?? DateTimeOffset.UtcNow.AddHours(8)
        };
        Response.Cookies.Append(AuthCookieName, jwt, cookieOptions);
    }

    private void ClearAuthCookie()
    {
        Response.Cookies.Delete(AuthCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }
}

/// <summary>
/// Authorize attribute for the kiosk-setup cookie flow. Validates the JWT
/// stored in the "imehr_kiosk_auth" cookie and populates HttpContext.User.
/// Does NOT touch the application-wide JwtBearer configuration.
/// </summary>
public class KioskSetupAuthorizeAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var cookie = context.HttpContext.Request.Cookies[KioskSetupApiController.AuthCookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "Not signed in." });
            return;
        }

        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var jwtKey = config["Jwt:Key"] ?? "YourSecretKeyHere12345678901234567890";
        var issuer = config["Jwt:Issuer"] ?? "IMEHR";
        var audience = config["Jwt:Audience"] ?? "IMEHRUsers";
        var key = Encoding.UTF8.GetBytes(jwtKey);

        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var principal = tokenHandler.ValidateToken(cookie, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            context.HttpContext.User = principal;
        }
        catch (SecurityTokenExpiredException)
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "Session expired. Please sign in again." });
        }
        catch
        {
            context.Result = new UnauthorizedObjectResult(new { success = false, message = "Invalid session." });
        }

        base.OnActionExecuting(context);
    }
}

// ==========================================================================
// DTOs
// ==========================================================================

public class KioskSetupSignInResponseDto
{
    public bool Success { get; set; }
    public bool RequiresTenantSelection { get; set; }
    public bool RequiresOtpVerification { get; set; }
    public string? Message { get; set; }
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Role { get; set; }
    public int? TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
}

public class KioskSetupLocationDto
{
    public int LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public string? KioskToken { get; set; }
    public bool KioskEnabled { get; set; }

    /// <summary>
    /// True if a tenant-wide active consent form template exists OR
    /// a location-specific template exists for this LocationId.
    /// False means this location cannot run the kiosk until a consent
    /// form is created in the admin panel.
    /// </summary>
    public bool HasConsentForm { get; set; }
}

public class KioskSetupConfirmPasswordDto
{
    public string Password { get; set; } = string.Empty;
}
