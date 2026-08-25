using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services;
using System.Security.Claims;

namespace EHR.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserManagementService _userManagementService;
    private readonly IEmailService _emailService;
    private readonly EhrDbContext _context;
    private readonly ILogger<AuthController> _logger;

    // Cookie name for the 15-day trusted-device token (Remember Me)
    private const string DeviceTrustCookieName = "__medocs_dt";

    public AuthController(IAuthService authService, IUserManagementService userManagementService, IEmailService emailService, EhrDbContext context, ILogger<AuthController> logger)
    {
        _authService = authService;
        _userManagementService = userManagementService;
        _emailService = emailService;
        _context = context;
        _logger = logger;
    }
    
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<UserLoginResponseDto>> Login([FromBody] UserLoginDto dto)
    {
        try
        {
            // Inject device trust cookie, user agent, and IP from request.
            // IP/UA are always overwritten server-side — never trust client-supplied values.
            if (Request.Cookies.TryGetValue(DeviceTrustCookieName, out var deviceCookie))
                dto.DeviceToken = deviceCookie;
            dto.UserAgent = Request.Headers.UserAgent.ToString();
            dto.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            var result = await _authService.LoginAsync(dto);
            if (result == null)
                return Unauthorized(new { message = "Invalid email or password" });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for {EmailHash}", PhiLog.Hash(dto.Email));
            return StatusCode(500, new { message = "An error occurred during login. Please try again." });
        }
    }

    /// <summary>
    /// Verify OTP code. Second step of the login flow after email+password.
    /// If RememberDevice is true, sets the __medocs_dt cookie (15-day expiry).
    /// </summary>
    [HttpPost("verify-otp")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<UserLoginResponseDto>> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { message = "Email is required" });

            // OtpCode may be empty when this is a tenant-selection call after OTP was already verified
            // (multi-tenant user picking a clinic — service layer handles the "VERIFIED" state via TenantId).
            if (!dto.TenantId.HasValue && string.IsNullOrWhiteSpace(dto.OtpCode))
                return BadRequest(new { message = "Email and verification code are required" });

            dto.UserAgent = Request.Headers.UserAgent.ToString();
            dto.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            var result = await _authService.VerifyOtpAsync(dto);
            if (result == null)
                return Unauthorized(new { message = "Invalid or expired verification code" });

            // Set device trust cookie if a token was generated (Remember Me checked + login complete)
            if (!string.IsNullOrEmpty(result.DeviceToken))
            {
                Response.Cookies.Append(DeviceTrustCookieName, result.DeviceToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps, // false on localhost HTTP, true in production HTTPS
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(15),
                    Path = "/api/auth"
                });
                result.DeviceToken = null; // Never return raw token in response body
            }

            // Mirror the JWT into an HttpOnly session cookie so server-rendered
            // Razor pages can identify the caller. A page navigation sends no
            // Authorization header, so the cookie is the only way those pages
            // can be protected at all. See Helpers/SessionCookie.cs.
            // Not issued while the user still has a clinic to pick — at that
            // point the token in `result` is not yet a full session token.
            if (!result.RequiresTenantSelection && !string.IsNullOrEmpty(result.Token))
            {
                SessionCookie.Issue(Response, result.Token,
                    result.TokenExpiry ?? DateTime.UtcNow.AddMinutes(30), Request.IsHttps);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTP verification failed for {EmailHash}", PhiLog.Hash(dto.Email));
            return StatusCode(500, new { message = "An error occurred during verification. Please try again." });
        }
    }

    /// <summary>
    /// Resend OTP code. Enforced 60-second server-side cooldown.
    /// Always returns success to prevent email enumeration.
    /// </summary>
    [HttpPost("resend-otp")]
    [EnableRateLimiting("auth-resend")]
    public async Task<ActionResult> ResendOtp([FromBody] ResendOtpDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { message = "Email is required" });

            await _authService.ResendOtpAsync(dto.Email);

            // Always return success to prevent email enumeration
            return Ok(new { message = "If a pending verification exists, a new code has been sent." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTP resend failed for {EmailHash}", PhiLog.Hash(dto.Email));
            return Ok(new { message = "If a pending verification exists, a new code has been sent." });
        }
    }
    
    [HttpPost("refresh")]
    public async Task<ActionResult<UserLoginResponseDto>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken);
        if (result == null)
            return Unauthorized(new { message = "Invalid or expired refresh token" });

        // Roll the page session forward with the SPA session. The JWT lives 30
        // minutes; without this the cookie would expire mid-session and the
        // Razor pages would bounce to login while the SPA kept working.
        if (!string.IsNullOrEmpty(result.Token))
        {
            SessionCookie.Issue(Response, result.Token,
                result.TokenExpiry ?? DateTime.UtcNow.AddMinutes(30), Request.IsHttps);
        }

        return Ok(result);
    }
    
    /// <summary>
    /// End the session. The policy scheme accepts header or cookie, so a
    /// page-only session can still log itself out. LogoutAsync bumps
    /// TokenVersion, which revokes the header token and the cookie token alike
    /// because both run the same validation.
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        await _authService.LogoutAsync(userId);
        SessionCookie.Clear(Response, Request.IsHttps);
        return Ok(new { message = "Logged out successfully" });
    }
    
    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        bool result;
        try
        {
            result = await _authService.ChangePasswordAsync(userId, dto);
        }
        catch (InvalidOperationException ex)
        {
            // The password policy message is our own text, written to be shown
            // to the user ("Password must be at least 12 characters."). Letting
            // it fall through to the generic 500 handler would tell them only
            // that something went wrong, which is how people end up retrying
            // the same rejected password.
            return BadRequest(new { message = ex.Message });
        }

        if (!result)
            return BadRequest(new { message = "Current password is incorrect" });

        return Ok(new { message = "Password changed successfully" });
    }
    
    [Authorize]
    [HttpGet("me")]
    public ActionResult<object> GetCurrentUser()
    {
        return Ok(new
        {
            UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Email = User.FindFirst(ClaimTypes.Email)?.Value,
            Name = User.FindFirst(ClaimTypes.Name)?.Value,
            Role = User.FindFirst("Role")?.Value,
            TenantId = User.FindFirst("TenantId")?.Value,
            TenantName = User.FindFirst("TenantName")?.Value,
            TenantSubdomain = User.FindFirst("TenantSubdomain")?.Value
        });
    }

    /// <summary>
    /// Request a password reset email
    /// </summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-forgot")]
    public async Task<ActionResult<ForgotPasswordResponseDto>> ForgotPassword([FromBody] ForgotPasswordRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new ForgotPasswordResponseDto { Success = false, Message = "Email is required" });

        // Generate reset token
        var token = await _userManagementService.GeneratePasswordResetTokenAsync(dto.Email);

        // Always return success to prevent email enumeration attacks
        // But only send email if user exists
        if (token != null)
        {
            // Get user's first name for personalized email
            var user = await _userManagementService.GetUsersAsync();
            var matchingUser = user.FirstOrDefault(u => string.Equals(u.Email, dto.Email, StringComparison.OrdinalIgnoreCase));
            var firstName = matchingUser?.FirstName ?? "User";

            // Send password reset email
            await _emailService.SendPasswordResetEmailAsync(dto.Email, token, firstName);
        }

        return Ok(new ForgotPasswordResponseDto
        {
            Success = true,
            Message = "If an account with that email exists, a password reset link has been sent."
        });
    }

    /// <summary>
    /// Validate a password reset token (does not consume it)
    /// </summary>
    [HttpGet("validate-reset-token")]
    public async Task<ActionResult> ValidateResetToken([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Ok(new { Valid = false, Message = "No reset token provided." });

        var valid = await _userManagementService.ValidatePasswordResetTokenAsync(token);
        if (!valid)
            return Ok(new { Valid = false, Message = "This password reset link is invalid or has expired." });

        return Ok(new { Valid = true });
    }

    /// <summary>
    /// Reset password using a valid token
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequest(new { message = "Reset token is required" });

        // One rule, one place. This used to be an inline "length < 8" check that
        // disagreed with the policy the service enforces.
        var policyError = EHR.Helpers.PasswordPolicy.Validate(dto.NewPassword);
        if (policyError != null)
            return BadRequest(new { message = policyError });

        var result = await _userManagementService.ResetPasswordWithTokenAsync(dto.Token, dto.NewPassword);

        if (!result)
            return BadRequest(new { message = "Invalid or expired reset token" });

        return Ok(new { message = "Password reset successfully. You can now log in with your new password." });
    }
}

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
