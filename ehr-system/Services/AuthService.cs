using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using Microsoft.Extensions.Hosting;

namespace EHR.Services;

public interface IAuthService
{
    Task<UserLoginResponseDto?> LoginAsync(UserLoginDto loginDto);
    Task<UserLoginResponseDto?> VerifyOtpAsync(VerifyOtpDto dto);
    Task<bool> ResendOtpAsync(string email);
    Task<UserLoginResponseDto?> RefreshTokenAsync(string refreshToken);
    Task<bool> LogoutAsync(int userId);
    Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto dto);
    string GenerateToken(User user, Tenant? tenant, Location? location = null);
}

public class AuthService : IAuthService
{
    private readonly EhrDbContext _context;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly bool _isDevelopment;

    // Test accounts that always get fixed OTP 123456 (still receive the email — real inboxes).
    private static readonly HashSet<string> _testOtpEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "support@medocs.ai",
        "hmajeed@medocs.ai"
    };

    // Test domains whose addresses always get fixed OTP 123456 AND never receive an email
    // (fake addresses created for testing — there is no inbox to read the code from).
    private static readonly string[] _hardcodedOtpDomains = { "@testmd.com", "@test.com" };

    public AuthService(EhrDbContext context, IConfiguration config, IEmailService emailService, IHostEnvironment env)
    {
        _context = context;
        _config = config;
        _emailService = emailService;
        _isDevelopment = env.IsDevelopment();
    }

    public async Task<UserLoginResponseDto?> LoginAsync(UserLoginDto loginDto)
    {
        // Find all active users with matching email across all tenants
        var matchingUsers = await FindAllUsersByEmailAsync(loginDto.Email);

        // HIPAA 45 CFR 164.312(b) — every failed authentication attempt is
        // logged with IP/UA so brute-force and credential-stuffing attacks
        // are detectable. Use TenantId=null when we don't know the tenant
        // (no user found); UserId=null for the same reason.
        if (!matchingUsers.Any())
        {
            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = null,
                UserId = null,
                UserEmail = loginDto.Email,
                EntityType = "User",
                EntityId = null,
                Action = "Login_Failed_NoUser",
                NewValues = $"ip={loginDto.IpAddress};ua={Truncate(loginDto.UserAgent, 200)}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return null;
        }

        // Verify password against the first user (same email across tenants shares the same password)
        var firstUser = matchingUsers.First();

        // Password lockout window — set after 5 prior failed attempts. Block here
        // BEFORE BCrypt.Verify so an attacker cannot use the verify call as a
        // timing oracle while the account is locked.
        if (firstUser.PasswordLockedUntil.HasValue
            && firstUser.PasswordLockedUntil.Value > DateTime.UtcNow)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = firstUser.TenantId,
                UserId = firstUser.UserId,
                UserEmail = firstUser.Email,
                EntityType = "User",
                EntityId = firstUser.UserId,
                Action = "Login_Blocked_PasswordLockout",
                NewValues = $"ip={loginDto.IpAddress};ua={Truncate(loginDto.UserAgent, 200)};lockedUntil={firstUser.PasswordLockedUntil:O}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, firstUser.PasswordHash))
        {
            // Increment per-user failed-password counter. Apply the same value
            // to every row sharing this email (multi-tenant accounts) so the
            // lockout is global across the user's tenants.
            var nextAttempts = firstUser.FailedPasswordAttempts + 1;
            DateTime? lockedUntil = null;
            if (nextAttempts >= 5)
            {
                lockedUntil = DateTime.UtcNow.AddMinutes(15);
            }

            foreach (var u in matchingUsers)
            {
                u.FailedPasswordAttempts = nextAttempts;
                if (lockedUntil.HasValue) u.PasswordLockedUntil = lockedUntil.Value;
            }

            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = firstUser.TenantId,
                UserId = firstUser.UserId,
                UserEmail = firstUser.Email,
                EntityType = "User",
                EntityId = firstUser.UserId,
                Action = lockedUntil.HasValue ? "Login_PasswordLockoutStarted" : "Login_Failed_BadPassword",
                NewValues = $"ip={loginDto.IpAddress};ua={Truncate(loginDto.UserAgent, 200)};attempt={nextAttempts}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return null;
        }

        // Correct password — clear the failed-attempt counter and any
        // expired lockout window for everyone sharing this email.
        if (firstUser.FailedPasswordAttempts != 0 || firstUser.PasswordLockedUntil.HasValue)
        {
            foreach (var u in matchingUsers)
            {
                u.FailedPasswordAttempts = 0;
                u.PasswordLockedUntil = null;
            }
            await _context.SaveChangesAsync();
        }

        // Account in active OTP-failure lockout (5 wrong OTPs → 15 min lock).
        // Return null (same shape as bad password) so attackers cannot distinguish
        // "locked" from "wrong creds" via response. Legit users wait 15 min.
        if (firstUser.OtpAttempts >= 5
            && firstUser.OtpResendCooldownUntil.HasValue
            && firstUser.OtpResendCooldownUntil.Value > DateTime.UtcNow)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = firstUser.TenantId,
                UserId = firstUser.UserId,
                UserEmail = firstUser.Email,
                EntityType = "User",
                EntityId = firstUser.UserId,
                Action = "Login_Blocked_OtpLockout",
                NewValues = $"ip={loginDto.IpAddress};ua={Truncate(loginDto.UserAgent, 200)}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return null;
        }

        // Trusted device cookie — skip OTP entirely
        if (await IsDeviceTrustedAsync(matchingUsers, loginDto.DeviceToken))
        {
            var userIds = matchingUsers.Select(u => u.UserId).ToList();
            await CleanupExpiredDevicesAsync(userIds);

            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = firstUser.TenantId,
                UserId = firstUser.UserId,
                UserEmail = firstUser.Email,
                EntityType = "User",
                EntityId = firstUser.UserId,
                Action = "OTP_Bypassed_TrustedDevice",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return await CompleteLoginAsync(matchingUsers, loginDto.TenantId);
        }

        // Generate OTP — hardcoded 123456 in development, for hardcoded test accounts, or for test domains (@testmd.com, @test.com)
        var isTestAccount = _testOtpEmails.Contains(firstUser.Email);
        var isHardcodedOtpDomain = _hardcodedOtpDomains.Any(d =>
            firstUser.Email.EndsWith(d, StringComparison.OrdinalIgnoreCase));
        var otpCode = (_isDevelopment || isTestAccount || isHardcodedOtpDomain) ? "123456" : GenerateOtp();

        foreach (var user in matchingUsers)
        {
            user.OtpCode = otpCode;
            user.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            user.OtpAttempts = 0;
            user.OtpResendCooldownUntil = DateTime.UtcNow.AddSeconds(60);
        }
        await _context.SaveChangesAsync();

        // Skip email entirely for hardcoded-OTP domains (@testmd.com, @test.com) — the OTP is always 123456 and there's no inbox to read it.
        // For everyone else, EmailService handles localhost preview, blocked domains, and @testmd.com redirect (for non-OTP emails) internally.
        if (!isHardcodedOtpDomain)
        {
            await _emailService.SendOtpEmailAsync(firstUser.Email, otpCode, firstUser.FirstName);
        }

        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = firstUser.TenantId,
            UserId = firstUser.UserId,
            UserEmail = firstUser.Email,
            EntityType = "User",
            EntityId = firstUser.UserId,
            Action = "OTP_Sent",
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return new UserLoginResponseDto
        {
            UserId = firstUser.UserId,
            Email = firstUser.Email,
            FullName = firstUser.FirstName + " " + firstUser.LastName,
            Role = firstUser.Role ?? 0,
            RequiresOtpVerification = true,
            MaskedEmail = MaskEmail(firstUser.Email)
        };
    }

    public async Task<UserLoginResponseDto?> VerifyOtpAsync(VerifyOtpDto dto)
    {
        var matchingUsers = await FindAllUsersByEmailAsync(dto.Email);
        if (!matchingUsers.Any())
            return null;

        var firstUser = matchingUsers.First();

        // "VERIFIED" state = multi-tenant user picking a clinic after OTP was already verified
        if (firstUser.OtpCode == "VERIFIED")
        {
            if (firstUser.OtpExpiry < DateTime.UtcNow)
                return null;

            if (!dto.TenantId.HasValue)
                return null;

            foreach (var u in matchingUsers)
            {
                u.OtpCode = null;
                u.OtpExpiry = null;
                u.OtpAttempts = 0;
                u.OtpResendCooldownUntil = null;
            }
            await _context.SaveChangesAsync();

            var verifiedResult = await CompleteLoginAsync(matchingUsers, dto.TenantId);

            if (verifiedResult != null && !string.IsNullOrEmpty(verifiedResult.Token) && dto.RememberDevice)
            {
                verifiedResult.DeviceToken = await CreateDeviceTrustAsync(verifiedResult.UserId, dto.UserAgent);
                _context.AuditLogs.Add(new AuditLog
                {
                    TenantId = firstUser.TenantId,
                    UserId = verifiedResult.UserId,
                    UserEmail = firstUser.Email,
                    EntityType = "TrustedDevice",
                    EntityId = verifiedResult.UserId,
                    Action = "Device_Trusted",
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return verifiedResult;
        }

        // Normal OTP verification
        if (string.IsNullOrEmpty(firstUser.OtpCode))
            return null;

        if (firstUser.OtpExpiry < DateTime.UtcNow)
            return null;

        if (firstUser.OtpAttempts >= 5)
            return null;

        if (firstUser.OtpCode != dto.OtpCode)
        {
            foreach (var u in matchingUsers)
                u.OtpAttempts++;

            // 5th failure → 15-minute lockout. OtpResendCooldownUntil doubles as the
            // lockout flag (also already blocks /resend-otp). LoginAsync checks the
            // same field to refuse re-issuing OTP during lockout, which closes the
            // brute-force loop where attacker reset attempts via /login or /resend.
            var lockoutTriggered = firstUser.OtpAttempts >= 5;
            if (lockoutTriggered)
            {
                var lockoutUntil = DateTime.UtcNow.AddMinutes(15);
                foreach (var u in matchingUsers)
                    u.OtpResendCooldownUntil = lockoutUntil;
            }

            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = firstUser.TenantId,
                UserId = firstUser.UserId,
                UserEmail = firstUser.Email,
                EntityType = "User",
                EntityId = firstUser.UserId,
                Action = lockoutTriggered ? "OTP_LockoutStarted" : "OTP_Failed",
                NewValues = $"Attempt {firstUser.OtpAttempts}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return null;
        }

        // OTP valid — clear OTP fields
        foreach (var u in matchingUsers)
        {
            u.OtpCode = null;
            u.OtpExpiry = null;
            u.OtpAttempts = 0;
            u.OtpResendCooldownUntil = null;
        }
        await _context.SaveChangesAsync();

        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = firstUser.TenantId,
            UserId = firstUser.UserId,
            UserEmail = firstUser.Email,
            EntityType = "User",
            EntityId = firstUser.UserId,
            Action = "OTP_Verified",
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await CompleteLoginAsync(matchingUsers, dto.TenantId);

        // Multi-tenant selection — set VERIFIED state for a 2-minute tenant-selection window
        if (result != null && result.RequiresTenantSelection)
        {
            foreach (var u in matchingUsers)
            {
                u.OtpCode = "VERIFIED";
                u.OtpExpiry = DateTime.UtcNow.AddMinutes(2);
            }
            await _context.SaveChangesAsync();

            if (dto.RememberDevice)
                result.PendingDeviceTrust = true;
        }

        // Login fully complete on this step (single-tenant or super admin) — honour Remember Device
        if (result != null && !string.IsNullOrEmpty(result.Token) && dto.RememberDevice)
        {
            result.DeviceToken = await CreateDeviceTrustAsync(result.UserId, dto.UserAgent);
            _context.AuditLogs.Add(new AuditLog
            {
                TenantId = firstUser.TenantId,
                UserId = result.UserId,
                UserEmail = firstUser.Email,
                EntityType = "TrustedDevice",
                EntityId = result.UserId,
                Action = "Device_Trusted",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        return result;
    }

    public async Task<bool> ResendOtpAsync(string email)
    {
        var matchingUsers = await FindAllUsersByEmailAsync(email);
        if (!matchingUsers.Any())
            return false;

        var firstUser = matchingUsers.First();

        // Must have a pending OTP (not null, not VERIFIED)
        if (string.IsNullOrEmpty(firstUser.OtpCode) || firstUser.OtpCode == "VERIFIED")
            return false;

        // Server-side cooldown (60 seconds)
        if (firstUser.OtpResendCooldownUntil.HasValue && firstUser.OtpResendCooldownUntil > DateTime.UtcNow)
            return false;

        var isTestAccount = _testOtpEmails.Contains(firstUser.Email);
        var isHardcodedOtpDomain = _hardcodedOtpDomains.Any(d =>
            firstUser.Email.EndsWith(d, StringComparison.OrdinalIgnoreCase));
        var otpCode = (_isDevelopment || isTestAccount || isHardcodedOtpDomain) ? "123456" : GenerateOtp();
        foreach (var u in matchingUsers)
        {
            u.OtpCode = otpCode;
            u.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            u.OtpAttempts = 0;
            u.OtpResendCooldownUntil = DateTime.UtcNow.AddSeconds(60);
        }
        await _context.SaveChangesAsync();

        // Skip email for hardcoded-OTP domains (@testmd.com, @test.com) — nobody is reading that inbox.
        if (!isHardcodedOtpDomain)
        {
            await _emailService.SendOtpEmailAsync(firstUser.Email, otpCode, firstUser.FirstName);
        }

        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = firstUser.TenantId,
            UserId = firstUser.UserId,
            UserEmail = firstUser.Email,
            EntityType = "User",
            EntityId = firstUser.UserId,
            Action = "OTP_Resent",
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Complete the login process after OTP verification or trusted-device bypass.
    /// Handles Super Admin detection, tenant selection, location resolution, JWT generation.
    /// </summary>
    private async Task<UserLoginResponseDto?> CompleteLoginAsync(List<User> matchingUsers, int? tenantId)
    {
        var firstUser = matchingUsers.First();
        User? selectedUser = null;
        Tenant? tenant = null;

        // Super Admin (Role 0, no TenantId)
        var superAdminUser = matchingUsers.FirstOrDefault(u => u.Role == 0 && u.TenantId == null);
        if (superAdminUser != null)
        {
            selectedUser = superAdminUser;
            tenant = null;
        }
        else if (tenantId.HasValue)
        {
            selectedUser = matchingUsers.FirstOrDefault(u => u.TenantId == tenantId.Value);
            if (selectedUser == null)
                return null;

            tenant = await _context.Tenants.FindAsync(tenantId.Value);
            if (tenant?.Status != (int)TenantStatus.Active || tenant?.IsDeleted == true)
                return null;
        }
        else
        {
            var tenantUsers = matchingUsers.Where(u => u.TenantId.HasValue).ToList();

            if (tenantUsers.Count > 1)
            {
                var availableTenants = new List<TenantSelectionDto>();
                foreach (var user in tenantUsers)
                {
                    var t = await _context.Tenants.FindAsync(user.TenantId!.Value);
                    if (t != null && t.Status == (int)TenantStatus.Active && t.IsDeleted != true)
                    {
                        availableTenants.Add(new TenantSelectionDto
                        {
                            TenantId = t.TenantId,
                            Name = t.Name,
                            Subdomain = t.Subdomain
                        });
                    }
                }

                if (availableTenants.Count > 1)
                {
                    return new UserLoginResponseDto
                    {
                        UserId = firstUser.UserId,
                        Email = firstUser.Email,
                        FullName = firstUser.FirstName + " " + firstUser.LastName,
                        Role = firstUser.Role ?? 0,
                        RequiresTenantSelection = true,
                        AvailableTenants = availableTenants
                    };
                }
                else if (availableTenants.Count == 1)
                {
                    selectedUser = tenantUsers.First(u => u.TenantId == availableTenants[0].TenantId);
                    tenant = await _context.Tenants.FindAsync(availableTenants[0].TenantId);
                }
                else
                {
                    return null;
                }
            }
            else if (tenantUsers.Count == 1)
            {
                selectedUser = tenantUsers.First();
                tenant = await _context.Tenants.FindAsync(selectedUser.TenantId!.Value);
                if (tenant?.Status != (int)TenantStatus.Active || tenant?.IsDeleted == true)
                    return null;
            }
            else
            {
                return null;
            }
        }

        if (selectedUser == null)
            return null;

        // Multi-location support
        Location? defaultLocation = null;
        List<LocationSelectionDto>? availableLocations = null;

        if (tenant != null)
        {
            var locations = await _context.Locations
                .Where(l => l.TenantId == tenant.TenantId && l.IsActive == true)
                .OrderByDescending(l => l.IsPrimary)
                .ThenBy(l => l.Name)
                .ToListAsync();

            if (locations.Any())
            {
                defaultLocation = locations.FirstOrDefault(l => l.IsPrimary == true) ?? locations.First();

                availableLocations = locations.Select(l =>
                {
                    var tzId = l.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
                    return new LocationSelectionDto
                    {
                        LocationId = l.LocationId,
                        Name = l.Name,
                        IsPrimary = l.IsPrimary ?? false,
                        TimeZoneId = tzId,
                        TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(tzId)
                    };
                }).ToList();
            }
        }

        var token = GenerateToken(selectedUser, tenant, defaultLocation);
        var refreshToken = GenerateRefreshToken();

        // Store only the SHA-256 hash of the refresh token. A leaked DB read
        // (compromised backup, accidental log dump, future SQL injection)
        // no longer yields directly usable refresh tokens — the attacker
        // would need the raw value the client holds.
        selectedUser.RefreshToken = HashRefreshToken(refreshToken);
        selectedUser.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        selectedUser.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = selectedUser.TenantId,
            UserId = selectedUser.UserId,
            UserEmail = selectedUser.Email,
            EntityType = "User",
            EntityId = selectedUser.UserId,
            Action = "Login",
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var defaultTimeZoneId = defaultLocation?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        return new UserLoginResponseDto
        {
            UserId = selectedUser.UserId,
            TenantId = selectedUser.TenantId,
            TenantName = tenant?.Name,
            TenantSubdomain = tenant?.Subdomain,
            TenantHasLogo = !string.IsNullOrEmpty(tenant?.LogoUrl),
            Email = selectedUser.Email,
            FullName = selectedUser.FirstName + " " + selectedUser.LastName,
            Role = selectedUser.Role ?? 0,
            ProviderId = selectedUser.ProviderId,
            Token = token,
            RefreshToken = refreshToken,
            TokenExpiry = DateTime.UtcNow.AddMinutes(30),
            RequiresTenantSelection = false,
            LocationId = defaultLocation?.LocationId,
            LocationName = defaultLocation?.Name,
            TimeZoneId = defaultTimeZoneId,
            TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(defaultTimeZoneId),
            AvailableLocations = availableLocations
        };
    }

    public async Task<UserLoginResponseDto?> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return null;

        var hashed = HashRefreshToken(refreshToken);
        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.RefreshToken == hashed &&
                                     u.RefreshTokenExpiry > DateTime.UtcNow &&
                                     u.IsActive == true);

        if (user == null)
            return null;

        var tenant = user.Tenant;
        if (tenant != null && (tenant.Status != (int)TenantStatus.Active || tenant.IsDeleted == true))
            return null;

        Location? defaultLocation = null;
        List<LocationSelectionDto>? availableLocations = null;

        if (tenant != null)
        {
            var locations = await _context.Locations
                .Where(l => l.TenantId == tenant.TenantId && l.IsActive == true)
                .OrderByDescending(l => l.IsPrimary)
                .ThenBy(l => l.Name)
                .ToListAsync();

            if (locations.Any())
            {
                defaultLocation = locations.FirstOrDefault(l => l.IsPrimary == true) ?? locations.First();
                availableLocations = locations.Select(l =>
                {
                    var tzId = l.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
                    return new LocationSelectionDto
                    {
                        LocationId = l.LocationId,
                        Name = l.Name,
                        IsPrimary = l.IsPrimary ?? false,
                        TimeZoneId = tzId,
                        TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(tzId)
                    };
                }).ToList();
            }
        }

        var newToken = GenerateToken(user, tenant, defaultLocation);
        var newRefreshToken = GenerateRefreshToken();

        // Single-use refresh-token rotation: replace the stored hash so the
        // previous refresh token cannot be replayed. Combined with the 7-day
        // expiry window, this bounds the value of a leaked refresh token.
        user.RefreshToken = HashRefreshToken(newRefreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _context.SaveChangesAsync();

        var defaultTimeZoneId = defaultLocation?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;

        return new UserLoginResponseDto
        {
            UserId = user.UserId,
            TenantId = user.TenantId,
            TenantName = tenant?.Name,
            TenantSubdomain = tenant?.Subdomain,
            TenantHasLogo = !string.IsNullOrEmpty(tenant?.LogoUrl),
            Email = user.Email,
            FullName = user.FirstName + " " + user.LastName,
            Role = user.Role ?? 0,
            ProviderId = user.ProviderId,
            Token = newToken,
            RefreshToken = newRefreshToken,
            TokenExpiry = DateTime.UtcNow.AddMinutes(30),
            LocationId = defaultLocation?.LocationId,
            LocationName = defaultLocation?.Name,
            TimeZoneId = defaultTimeZoneId,
            TimeZoneAbbreviation = TimezoneHelper.GetTimezoneAbbreviation(defaultTimeZoneId),
            AvailableLocations = availableLocations
        };
    }

    public async Task<bool> LogoutAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return false;

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        // Bump TokenVersion so any in-flight JWT for this user immediately stops
        // validating. Stolen tokens become useless on logout.
        user.TokenVersion = user.TokenVersion + 1;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return false;

        var oldPassword = !string.IsNullOrEmpty(dto.CurrentPassword) ? dto.CurrentPassword : dto.OldPassword;

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            return false;

        // Checked AFTER the current password is verified, so an attacker who
        // does not know the current password learns nothing about the policy.
        // See Helpers/PasswordPolicy.cs.
        var policyError = EHR.Helpers.PasswordPolicy.Validate(dto.NewPassword, user.Email);
        if (policyError != null)
            throw new InvalidOperationException(policyError);

        var allUsersWithSameEmail = await FindAllUsersByEmailAsync(user.Email);
        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

        foreach (var u in allUsersWithSameEmail)
        {
            u.PasswordHash = newPasswordHash;
            u.UpdatedAt = DateTime.UtcNow;
            // Bump TokenVersion so any existing JWT stops validating after the
            // password change — stolen tokens cannot survive a reset.
            u.TokenVersion = u.TokenVersion + 1;
            // Also clear any stored refresh token
            u.RefreshToken = null;
            u.RefreshTokenExpiry = null;
            // Reset failed-attempt counter as a defensive cleanup.
            u.FailedPasswordAttempts = 0;
            u.PasswordLockedUntil = null;
        }

        await _context.SaveChangesAsync();

        return true;
    }

    public string GenerateToken(User user, Tenant? tenant, Location? location = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("UserId", user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FirstName + " " + user.LastName),
            new(ClaimTypes.Role, (user.Role ?? 0).ToString()),
            new("Role", (user.Role ?? 0).ToString()),
            // Token version — must match User.TokenVersion at validation time.
            // Bumping the DB column instantly invalidates this JWT.
            new("tv", user.TokenVersion.ToString())
        };

        if (user.TenantId.HasValue)
        {
            claims.Add(new System.Security.Claims.Claim("TenantId", user.TenantId.Value.ToString()));
        }

        if (tenant != null)
        {
            claims.Add(new System.Security.Claims.Claim("TenantSubdomain", tenant.Subdomain));
            claims.Add(new System.Security.Claims.Claim("TenantName", tenant.Name));
        }

        if (user.ProviderId.HasValue)
        {
            claims.Add(new System.Security.Claims.Claim("ProviderId", user.ProviderId.Value.ToString()));
        }

        if (location != null)
        {
            claims.Add(new System.Security.Claims.Claim("LocationId", location.LocationId.ToString()));
            claims.Add(new System.Security.Claims.Claim("LocationName", location.Name));
        }

        // 30-minute access-token lifetime — clients refresh via the rotating
        // refresh token (RefreshTokenAsync) before this expires. Short-lived
        // access tokens bound the value of a stolen JWT to at most 30 minutes
        // even if the per-user TokenVersion (D1) is not bumped in time.
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    /// <summary>
    /// SHA-256 hash of a refresh token (lower-case hex). 64-byte raw token is
    /// already 512 bits of cryptographic randomness, so a single fast hash
    /// suffices — brute-force back from the hash is infeasible. Reduces blast
    /// radius of a DB read leak: stored value is no longer directly usable.
    /// </summary>
    private static string HashRefreshToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateOtp()
    {
        var bytes = new byte[4];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var code = (BitConverter.ToUInt32(bytes, 0) % 900000) + 100000;
        return code.ToString();
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "***";
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";
        var local = parts[0];
        var masked = local.Length <= 2
            ? local[0] + "***"
            : local[0] + new string('*', local.Length - 2) + local[^1];
        return masked + "@" + parts[1];
    }

    /// <summary>
    /// Find all active users by email across all tenants.
    /// </summary>
    private async Task<List<User>> FindAllUsersByEmailAsync(string email)
    {
        return await _context.Users
            .Where(u => u.IsActive == true && u.Email.ToLower() == email.ToLower())
            .ToListAsync();
    }

    /// <summary>
    /// Check if the device token is valid for any of the matching users.
    /// </summary>
    private async Task<bool> IsDeviceTrustedAsync(List<User> matchingUsers, string deviceToken)
    {
        if (string.IsNullOrEmpty(deviceToken))
            return false;

        var tokenHash = HashDeviceToken(deviceToken);
        var userIds = matchingUsers.Select(u => u.UserId).ToList();

        return await _context.TrustedDevices
            .AnyAsync(td => userIds.Contains(td.UserId) &&
                            td.DeviceTokenHash == tokenHash &&
                            td.ExpiresAt > DateTime.UtcNow);
    }

    /// <summary>
    /// Generate a cryptographic device trust token and store its SHA256 hash. Expires in 15 days.
    /// Returns the plain token (to be set as cookie by the controller).
    /// </summary>
    private async Task<string> CreateDeviceTrustAsync(int userId, string userAgent)
    {
        var tokenBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        var plainToken = Convert.ToHexString(tokenBytes).ToLower();

        _context.TrustedDevices.Add(new TrustedDevice
        {
            UserId = userId,
            DeviceTokenHash = HashDeviceToken(plainToken),
            ExpiresAt = DateTime.UtcNow.AddDays(15),
            CreatedAt = DateTime.UtcNow,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent
        });
        await _context.SaveChangesAsync();

        return plainToken;
    }

    private static string HashDeviceToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }

    /// <summary>
    /// Cap a string at <paramref name="max"/> chars for safe storage in audit log
    /// columns (NewValues, etc.). Returns "(none)" for null/empty.
    /// </summary>
    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(none)";
        return s.Length <= max ? s : s[..max];
    }

    private async Task CleanupExpiredDevicesAsync(List<int> userIds)
    {
        var expired = await _context.TrustedDevices
            .Where(td => userIds.Contains(td.UserId) && td.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync();

        if (expired.Any())
        {
            _context.TrustedDevices.RemoveRange(expired);
            await _context.SaveChangesAsync();
        }
    }
}
