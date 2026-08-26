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

        // One query for every user's grants rather than one per user. The list
        // is small, but a per-row lookup here is the classic way a user screen
        // becomes slow the month a customer hires ten people.
        var grants = await LoadLocationGrantsAsync(users.Select(u => u.UserId).ToList());

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
            IsActive = u.IsActive ?? false,
            LastLoginAt = u.LastLoginAt,
            CreatedAt = u.CreatedAt,
            Locations = grants.TryGetValue(u.UserId, out var mine) ? mine : new List<UserLocationDto>()
        }).ToList();
    }

    /// <summary>
    /// Every branch grant for the given users, keyed by user.
    ///
    /// Reads dbo.vUserLocations. Raw SQL for the same reason the writes are:
    /// the grant table is deliberately not in the generated EF model.
    /// </summary>
    private async Task<Dictionary<int, List<UserLocationDto>>> LoadLocationGrantsAsync(List<int> userIds)
    {
        var byUser = new Dictionary<int, List<UserLocationDto>>();
        if (userIds.Count == 0) return byUser;

        // Ids are ints already parsed as ints and read from the database, so
        // there is nothing from the request in this string.
        var ids = string.Join(",", userIds);

        var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT UserId, LocationId, LocationName FROM dbo.vUserLocations " +
            $"WHERE UserId IN ({ids}) AND LocationActive = 1 ORDER BY LocationName";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var userId = reader.GetInt32(0);
            if (!byUser.TryGetValue(userId, out var list))
                byUser[userId] = list = new List<UserLocationDto>();

            list.Add(new UserLocationDto
            {
                LocationId = reader.GetInt32(1),
                Name = reader.GetString(2)
            });
        }

        return byUser;
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

        // A staff account holds an entire tenant's PHI. Until 2026-08-25 there
        // was no password rule here at all, so an administrator could be created
        // with the password "a". See Helpers/PasswordPolicy.cs.
        var createPolicyError = EHR.Helpers.PasswordPolicy.Validate(dto.Password, dto.Email);
        if (createPolicyError != null)
            throw new InvalidOperationException(createPolicyError);

        var user = new User
        {
            TenantId = effectiveTenantId.Value,
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Phone = dto.Phone,
            Role = dto.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await ReplaceLocationGrantsAsync(user, dto.LocationIds);

        return user;
    }

    /// <summary>
    /// Replace which branches a user may work in.
    ///
    /// WHY IT LIVES HERE
    /// "Manage users" includes "which depots they work at". Splitting it into
    /// its own service would mean two places that both have to remember the
    /// rule below.
    ///
    /// THE RULE
    /// A restricted role with no grants sees NOTHING (see
    /// Services/DmeLocationScope.cs), so creating one without a branch produces
    /// an account that can sign in and do nothing, and nothing on screen would
    /// say why. Refuse instead.
    ///
    /// Roles 0 and 1 bypass scoping, so grants are meaningless for them and any
    /// supplied list is ignored rather than written and left to rot.
    ///
    /// WHY RAW SQL
    /// dbo.UserLocations is not in the generated EF model, and adding it there
    /// would mean regenerating a model this product otherwise leaves alone.
    /// Every value below is a bound parameter.
    /// </summary>
    private async Task ReplaceLocationGrantsAsync(User user, List<int> locationIds)
    {
        const int SuperAdminRole = 0, ClinicAdminRole = 1;
        if (user.Role is SuperAdminRole or ClinicAdminRole) return;
        if (locationIds == null) return;   // null means "leave grants alone"

        // Only branches of this user's own tenant. A posted id from another
        // supplier is dropped rather than trusted: the grant table has no
        // TenantId of its own to protect it.
        var valid = await _context.Locations
            .Where(l => l.TenantId == user.TenantId && l.IsActive == true && locationIds.Contains(l.LocationId))
            .Select(l => l.LocationId)
            .ToListAsync();

        if (valid.Count == 0)
            throw new InvalidOperationException(
                "Assign at least one location. A user with none can sign in but sees no customers, " +
                "orders or claims at all.");

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.UserLocations WHERE UserId = {user.UserId}");

        foreach (var locationId in valid)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO dbo.UserLocations (UserId, LocationId) VALUES ({user.UserId}, {locationId})");
        }
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

        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Null leaves the existing grants alone, so an update that only changes
        // a phone number cannot silently revoke every branch.
        await ReplaceLocationGrantsAsync(user, dto.LocationIds);

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

        var resetPolicyError = EHR.Helpers.PasswordPolicy.Validate(newPassword, user.Email);
        if (resetPolicyError != null)
            throw new InvalidOperationException(resetPolicyError);

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

        // Get email from first user to find all accounts
        var firstUser = users.First();

        var tokenResetPolicyError = EHR.Helpers.PasswordPolicy.Validate(newPassword, firstUser.Email);
        if (tokenResetPolicyError != null)
            throw new InvalidOperationException(tokenResetPolicyError);

        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

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
