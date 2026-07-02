using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using System.Security.Cryptography;
using System.Text;

namespace EHR.Services;

public interface IUserManagementService
{
    Task<List<UserListDto>> GetUsersAsync(int? tenantId = null, bool? activeOnly = true);
    Task<UserListDto?> GetUserByIdAsync(int userId);
    Task<User> CreateUserAsync(UserCreateDto dto);
    Task<User?> UpdateUserAsync(int userId, UserUpdateDto dto);
    Task<bool> DeleteUserAsync(int userId);
    Task<bool> ReactivateUserAsync(int userId);
    Task<bool> AdminResetPasswordAsync(int userId, string newPassword);
    Task<bool> AdminChangeEmailAsync(int userId, string newEmail);
    Task<string?> GeneratePasswordResetTokenAsync(string email);
    Task<bool> ResetPasswordWithTokenAsync(string token, string newPassword);
    Task<bool> ValidatePasswordResetTokenAsync(string token);
}

public class UserManagementService : IUserManagementService
{
    // Hidden support/backdoor account. Visible only to Super Admin (caller with no TenantId).
    // Hidden from every tenant-scoped user list (Clinic Admin, Clinician, etc.).
    private const string HiddenSupportEmail = "support@medocs.ai";

    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public UserManagementService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<UserListDto>> GetUsersAsync(int? tenantId = null, bool? activeOnly = true)
    {
        var query = _context.Users
            .Include(u => u.Tenant)
            .AsQueryable();

        // Filter by active status
        if (activeOnly == true)
        {
            query = query.Where(u => u.IsActive == true);
        }
        else if (activeOnly == false)
        {
            query = query.Where(u => u.IsActive == false);
        }
        // If activeOnly is null, show all users

        // If tenantId is specified (Super Admin filtering), filter by it
        if (tenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == tenantId.Value);
        }
        // If current user is not Super Admin (has a tenant), only show their tenant's users
        else if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == _tenantProvider.TenantId.Value);
        }

        // Hide support/backdoor account from non-Super-Admin callers.
        // Super Admin (no TenantId) still sees it.
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(u => u.Email != HiddenSupportEmail);
        }

        var users = await query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToListAsync();

        return users.Select(u => new UserListDto
        {
            UserId = u.UserId,
            TenantId = u.TenantId,
            TenantName = u.Tenant?.Name,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Phone = u.Phone,
            Role = u.Role ?? 0,
            ProviderId = u.ProviderId,
            IsActive = u.IsActive ?? false,
            LastLoginAt = u.LastLoginAt,
            CreatedAt = u.CreatedAt
        }).ToList();
    }

    public async Task<UserListDto?> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
            return null;

        // If current user is not Super Admin, verify they can only see their tenant's users
        if (_tenantProvider.TenantId.HasValue && user.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Hide support/backdoor account from non-Super-Admin direct lookups too
        if (_tenantProvider.TenantId.HasValue && user.Email == HiddenSupportEmail)
            return null;

        return new UserListDto
        {
            UserId = user.UserId,
            TenantId = user.TenantId,
            TenantName = user.Tenant?.Name,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.Phone,
            Role = user.Role ?? 0,
            ProviderId = user.ProviderId,
            IsActive = user.IsActive ?? false,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<User> CreateUserAsync(UserCreateDto dto)
    {
        // Determine the tenant ID to use
        // If dto.TenantId is not provided, use the current user's tenant
        var effectiveTenantId = dto.TenantId ?? _tenantProvider.TenantId;

        if (!effectiveTenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required to create a user");

        // Validate tenant access - clinic admin can only create users for their own tenant
        if (_tenantProvider.TenantId.HasValue && effectiveTenantId.Value != _tenantProvider.TenantId.Value)
            throw new UnauthorizedAccessException("Cannot create user for a different tenant");

        // Validate ProviderId if provided
        if (dto.ProviderId.HasValue)
        {
            var provider = await _context.Providers.FindAsync(dto.ProviderId.Value);
            if (provider == null || provider.TenantId != effectiveTenantId.Value)
                throw new InvalidOperationException("Invalid provider selected");
        }

        var user = new User
        {
            TenantId = effectiveTenantId.Value,
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Phone = dto.Phone,
            Role = dto.Role,
            ProviderId = dto.ProviderId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }

    public async Task<User?> UpdateUserAsync(int userId, UserUpdateDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return null;

        // If current user is not Super Admin, verify they can only update their tenant's users
        if (_tenantProvider.TenantId.HasValue && user.TenantId != _tenantProvider.TenantId.Value)
            return null;

        if (dto.Email != null) user.Email = dto.Email;
        if (dto.FirstName != null) user.FirstName = dto.FirstName;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.Phone != null) user.Phone = dto.Phone;
        if (dto.Role.HasValue) user.Role = dto.Role.Value;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;

        // Always update ProviderId - allows clearing the provider association
        // ProviderId can be set to a value or null (to remove association)
        user.ProviderId = dto.ProviderId;

        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return user;
    }

    public async Task<bool> DeleteUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return false;

        // If current user is not Super Admin, verify they can only delete their tenant's users
        if (_tenantProvider.TenantId.HasValue && user.TenantId != _tenantProvider.TenantId.Value)
            return false;

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ReactivateUserAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return false;

        // If current user is not Super Admin, verify they can only reactivate their tenant's users
        if (_tenantProvider.TenantId.HasValue && user.TenantId != _tenantProvider.TenantId.Value)
            return false;

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Admin function to reset a user's password (Super Admin or Clinic Admin only)
    /// </summary>
    public async Task<bool> AdminResetPasswordAsync(int userId, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return false;

        // If current user is not Super Admin, verify they can only reset their tenant's users
        if (_tenantProvider.TenantId.HasValue && user.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Find all users with the same email (across tenants) and update their passwords
        var allUsersWithSameEmail = await FindAllUsersByEmailAsync(user.Email);
        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        foreach (var u in allUsersWithSameEmail)
        {
            u.PasswordHash = newPasswordHash;
            u.RefreshToken = null; // Invalidate existing sessions
            u.RefreshTokenExpiry = null;
            u.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Admin function to change a user's email (Super Admin or Clinic Admin only)
    /// </summary>
    public async Task<bool> AdminChangeEmailAsync(int userId, string newEmail)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return false;

        // If current user is not Super Admin, verify they can only change their tenant's users
        if (_tenantProvider.TenantId.HasValue && user.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Check if new email is already in use for this tenant
        var emailExists = await _context.Users
            .AnyAsync(u => u.TenantId == user.TenantId &&
                          u.UserId != userId &&
                          u.Email.ToLower() == newEmail.ToLower());

        if (emailExists)
        {
            throw new InvalidOperationException("Email address is already in use");
        }

        user.Email = newEmail;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Generate a password reset token for the user with given email.
    /// Returns the RAW token to embed in the email link. The DB stores only the
    /// SHA-256 hash, so a leaked DB read does not yield usable reset tokens.
    /// </summary>
    public async Task<string?> GeneratePasswordResetTokenAsync(string email)
    {
        var users = await FindAllUsersByEmailAsync(email);
        if (!users.Any())
            return null;

        // 64 bytes = 512 bits of entropy. Base64Url so it survives in URL/query.
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hashedToken = HashResetToken(rawToken);
        var expiry = DateTime.UtcNow.AddHours(1);

        // Store the HASH (not the raw token) in all user accounts with this email.
        // Multi-tenant accounts share an email + password by design (same person),
        // so all rows share the same hash and any one can be reset.
        foreach (var user in users)
        {
            user.PasswordResetToken = hashedToken;
            user.PasswordResetTokenExpiry = expiry;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return rawToken;
    }

    /// <summary>
    /// Validate a password reset token without consuming it. Hashes the
    /// incoming raw token and compares against the stored hash.
    /// </summary>
    public async Task<bool> ValidatePasswordResetTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var hashedToken = HashResetToken(token);
        return await _context.Users
            .AnyAsync(u => u.PasswordResetToken == hashedToken &&
                           u.PasswordResetTokenExpiry > DateTime.UtcNow &&
                           u.IsActive == true);
    }

    /// <summary>
    /// Reset password using a valid reset token. Token is hashed before lookup.
    /// </summary>
    public async Task<bool> ResetPasswordWithTokenAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var hashedToken = HashResetToken(token);

        // Find user(s) with matching unexpired token hash
        var users = await _context.Users
            .Where(u => u.PasswordResetToken == hashedToken &&
                       u.PasswordResetTokenExpiry > DateTime.UtcNow &&
                       u.IsActive == true)
            .ToListAsync();

        if (!users.Any())
            return false;

        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        // Get email from first user to find all accounts
        var firstUser = users.First();

        // Update password for all accounts with this email
        var allUsersWithSameEmail = await FindAllUsersByEmailAsync(firstUser.Email);

        foreach (var user in allUsersWithSameEmail)
        {
            user.PasswordHash = newPasswordHash;
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.RefreshToken = null; // Invalidate existing sessions
            user.RefreshTokenExpiry = null;
            // Bump TokenVersion so any active JWT stops validating immediately.
            // Stolen tokens cannot survive a reset.
            user.TokenVersion = user.TokenVersion + 1;
            // Clear lockout state — user just proved control of email + token.
            user.FailedPasswordAttempts = 0;
            user.PasswordLockedUntil = null;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// SHA-256 hash of a reset token, lower-case hex. Deterministic per process —
    /// no salt needed (unlike passwords) because the input is already 512 bits
    /// of cryptographic randomness, so brute-force is infeasible. Used so the
    /// DB never stores a token that would be directly usable if leaked.
    /// </summary>
    private static string HashResetToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<List<User>> FindAllUsersByEmailAsync(string email)
    {
        // Email is stored as plain text, so we can query directly
        return await _context.Users
            .Where(u => u.IsActive == true && u.Email.ToLower() == email.ToLower())
            .ToListAsync();
    }
}
