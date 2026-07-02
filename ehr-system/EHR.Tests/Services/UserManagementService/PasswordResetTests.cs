using System.Security.Cryptography;
using System.Text;
using EHR.Models.Generated;
using EHR.Services;
using EHR.Tests.TestHelpers;
using FluentAssertions;

namespace EHR.Tests.Services.UserManagementServiceTests;

/// <summary>
/// DB-touching tests for the password reset trail in UserManagementService:
///   1) GeneratePasswordResetTokenAsync  — issues a token + expiry
///   2) ValidatePasswordResetTokenAsync  — non-consuming token validation
///   3) ResetPasswordWithTokenAsync      — consumes token, rehashes password,
///                                          invalidates refresh sessions
///
/// Edge cases covered: unknown email, inactive users, expired tokens, already-
/// consumed tokens, empty/null tokens, multi-tenant accounts sharing one email
/// (all accounts must be updated atomically — that's the documented behavior of
/// FindAllUsersByEmailAsync).
/// </summary>
[Collection("Db")]
public class PasswordResetTests
{
    private readonly SqlServerFixture _fx;

    public PasswordResetTests(SqlServerFixture fx) => _fx = fx;

    private static User NewUser(int id, string email, bool isActive = true,
        string? resetToken = null, DateTime? resetExpiry = null,
        string? refreshToken = null) => new()
    {
        UserId = id,
        TenantId = 1,
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword#1"),
        FirstName = "Test",
        LastName = "User",
        Phone = "555-0000",
        Role = 2,
        IsActive = isActive,
        PasswordResetToken = resetToken,
        PasswordResetTokenExpiry = resetExpiry,
        RefreshToken = refreshToken,
        RefreshTokenExpiry = refreshToken != null ? DateTime.UtcNow.AddDays(7) : null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private UserManagementService NewService(EhrDbContext db) =>
        new(db, new TenantProvider { TenantId = 1 });

    /// <summary>
    /// Mirror of UserManagementService.HashResetToken so tests can seed the DB
    /// with the same hash the production code would write — and so we can
    /// assert against the stored hash from a known raw value.
    /// </summary>
    private static string HashResetTokenLikeService(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // -------------------------------------------------------------------------
    // GeneratePasswordResetTokenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerateToken_UnknownEmail_ReturnsNull()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        var token = await svc.GeneratePasswordResetTokenAsync("nobody@example.com");

        token.Should().BeNull();
    }

    [Fact]
    public async Task GenerateToken_InactiveUser_ReturnsNull()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "inactive@md.com", isActive: false));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var token = await NewService(db).GeneratePasswordResetTokenAsync("inactive@md.com");

        token.Should().BeNull();
    }

    [Fact]
    public async Task GenerateToken_ActiveUser_PersistsTokenAndOneHourExpiry()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com"));
            await seed.SaveChangesAsync();
        }

        var before = DateTime.UtcNow;
        string? token;
        await using (var db = _fx.CreateDbContext())
        {
            token = await NewService(db).GeneratePasswordResetTokenAsync("user@md.com");
        }
        var after = DateTime.UtcNow;

        token.Should().NotBeNullOrWhiteSpace();

        await using var verify = _fx.CreateDbContext();
        var u = verify.Users.Single(x => x.UserId == 1);
        // DB stores SHA-256(raw token), not the raw token itself, so a
        // leaked DB read yields no usable reset tokens. The raw token is
        // returned to the caller (for embedding in the email link).
        u.PasswordResetToken.Should().Be(HashResetTokenLikeService(token!));
        u.PasswordResetToken.Should().NotBe(token);
        u.PasswordResetTokenExpiry.Should().NotBeNull();
        // Expiry should be ~1 hour from now (allow a window for clock drift)
        u.PasswordResetTokenExpiry!.Value.Should().BeOnOrAfter(before.AddHours(1).AddSeconds(-5));
        u.PasswordResetTokenExpiry!.Value.Should().BeOnOrBefore(after.AddHours(1).AddSeconds(5));
    }

    [Fact]
    public async Task GenerateToken_EmailMatchIsCaseInsensitive()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "Mixed.Case@Md.Com"));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var token = await NewService(db).GeneratePasswordResetTokenAsync("mixed.case@md.com");

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateToken_SameEmailMultipleTenants_AllAccountsGetSameToken()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                TenantId = 2, Name = "Tenant 2", Subdomain = "t2",
                Phone = "0", Email = "t2@t.com", Address = "x", City = "x", State = "x"
            });
            seed.Users.Add(NewUser(1, "shared@md.com"));
            var u2 = NewUser(2, "shared@md.com"); u2.TenantId = 2;
            seed.Users.Add(u2);
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var token = await NewService(db).GeneratePasswordResetTokenAsync("shared@md.com");

        await using var verify = _fx.CreateDbContext();
        var users = verify.Users.Where(u => u.Email == "shared@md.com").ToList();
        users.Should().HaveCount(2);
        // DB stores the same SHA-256 hash for every account sharing this email;
        // the raw token is the one returned to the caller.
        users.Select(u => u.PasswordResetToken).Distinct().Should().ContainSingle()
            .Which.Should().Be(HashResetTokenLikeService(token!));
    }

    [Fact]
    public async Task GenerateToken_CalledTwice_RotatesToken()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com"));
            await seed.SaveChangesAsync();
        }

        await using var db1 = _fx.CreateDbContext();
        var t1 = await NewService(db1).GeneratePasswordResetTokenAsync("user@md.com");

        await using var db2 = _fx.CreateDbContext();
        var t2 = await NewService(db2).GeneratePasswordResetTokenAsync("user@md.com");

        t1.Should().NotBeNull();
        t2.Should().NotBeNull();
        t2.Should().NotBe(t1, "second request must invalidate the first token");
    }

    // -------------------------------------------------------------------------
    // ValidatePasswordResetTokenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateToken_NullOrEmpty_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db);

        (await svc.ValidatePasswordResetTokenAsync(null!)).Should().BeFalse();
        (await svc.ValidatePasswordResetTokenAsync("")).Should().BeFalse();
        (await svc.ValidatePasswordResetTokenAsync("   ")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToken_UnknownToken_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: "real-token", resetExpiry: DateTime.UtcNow.AddMinutes(30)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        (await NewService(db).ValidatePasswordResetTokenAsync("not-the-token"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToken_ExpiredToken_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: "expired-token", resetExpiry: DateTime.UtcNow.AddMinutes(-1)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        (await NewService(db).ValidatePasswordResetTokenAsync("expired-token"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToken_InactiveUser_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com", isActive: false,
                resetToken: "tok", resetExpiry: DateTime.UtcNow.AddMinutes(30)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        (await NewService(db).ValidatePasswordResetTokenAsync("tok"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToken_ValidActiveToken_ReturnsTrueAndDoesNotConsume()
    {
        const string rawToken = "good-tok";
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            // Seed the DB with the HASH (matching production behavior) but
            // hand the RAW token to the service.
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: HashResetTokenLikeService(rawToken),
                resetExpiry: DateTime.UtcNow.AddMinutes(45)));
            await seed.SaveChangesAsync();
        }

        await using (var db = _fx.CreateDbContext())
        {
            var ok = await NewService(db).ValidatePasswordResetTokenAsync(rawToken);
            ok.Should().BeTrue();
        }

        await using var verify = _fx.CreateDbContext();
        var u = verify.Users.Single(x => x.UserId == 1);
        u.PasswordResetToken.Should().Be(HashResetTokenLikeService(rawToken),
            "validation must NOT consume the token — that's the reset call's job");
        u.PasswordResetTokenExpiry.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // ResetPasswordWithTokenAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Reset_ValidToken_UpdatesPasswordClearsTokenAndInvalidatesSession()
    {
        const string rawToken = "tok-1";
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: HashResetTokenLikeService(rawToken),
                resetExpiry: DateTime.UtcNow.AddMinutes(30),
                refreshToken: "old-refresh"));
            await seed.SaveChangesAsync();
        }

        bool result;
        await using (var db = _fx.CreateDbContext())
        {
            result = await NewService(db).ResetPasswordWithTokenAsync(rawToken, "NewPassword#1");
        }

        result.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var u = verify.Users.Single(x => x.UserId == 1);

        BCrypt.Net.BCrypt.Verify("NewPassword#1", u.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("OldPassword#1", u.PasswordHash).Should().BeFalse();
        u.PasswordResetToken.Should().BeNull();
        u.PasswordResetTokenExpiry.Should().BeNull();
        u.RefreshToken.Should().BeNull("active sessions must be invalidated on reset");
        u.RefreshTokenExpiry.Should().BeNull();
    }

    [Fact]
    public async Task Reset_UnknownToken_ReturnsFalseAndKeepsOriginalPassword()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: "real", resetExpiry: DateTime.UtcNow.AddMinutes(30)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var ok = await NewService(db).ResetPasswordWithTokenAsync("wrong", "NewPassword#1");

        ok.Should().BeFalse();

        await using var verify = _fx.CreateDbContext();
        var u = verify.Users.Single(x => x.UserId == 1);
        BCrypt.Net.BCrypt.Verify("OldPassword#1", u.PasswordHash).Should().BeTrue();
        u.PasswordResetToken.Should().Be("real");
    }

    [Fact]
    public async Task Reset_ExpiredToken_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: "expired", resetExpiry: DateTime.UtcNow.AddMinutes(-1)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var ok = await NewService(db).ResetPasswordWithTokenAsync("expired", "NewPassword#1");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_TokenAlreadyConsumed_ReturnsFalseOnSecondCall()
    {
        const string rawToken = "one-shot";
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com",
                resetToken: HashResetTokenLikeService(rawToken),
                resetExpiry: DateTime.UtcNow.AddMinutes(30)));
            await seed.SaveChangesAsync();
        }

        await using (var db = _fx.CreateDbContext())
        {
            (await NewService(db).ResetPasswordWithTokenAsync(rawToken, "First#Password1"))
                .Should().BeTrue();
        }

        await using (var db2 = _fx.CreateDbContext())
        {
            (await NewService(db2).ResetPasswordWithTokenAsync(rawToken, "Second#Password1"))
                .Should().BeFalse();
        }

        await using var verify = _fx.CreateDbContext();
        var u = verify.Users.Single(x => x.UserId == 1);
        BCrypt.Net.BCrypt.Verify("First#Password1", u.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("Second#Password1", u.PasswordHash).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_InactiveUser_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "user@md.com", isActive: false,
                resetToken: "tok", resetExpiry: DateTime.UtcNow.AddMinutes(30)));
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        (await NewService(db).ResetPasswordWithTokenAsync("tok", "NewPassword#1"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Reset_SameEmailMultipleTenants_UpdatesAllAccounts()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                TenantId = 2, Name = "Tenant 2", Subdomain = "t2",
                Phone = "0", Email = "t2@t.com", Address = "x", City = "x", State = "x"
            });
            const string rawSharedToken = "shared-tok";
            var hashedSharedToken = HashResetTokenLikeService(rawSharedToken);
            var sharedExpiry = DateTime.UtcNow.AddMinutes(30);
            seed.Users.Add(NewUser(1, "shared@md.com",
                resetToken: hashedSharedToken, resetExpiry: sharedExpiry,
                refreshToken: "rt-1"));
            var u2 = NewUser(2, "shared@md.com",
                resetToken: hashedSharedToken, resetExpiry: sharedExpiry,
                refreshToken: "rt-2");
            u2.TenantId = 2;
            seed.Users.Add(u2);
            await seed.SaveChangesAsync();
        }

        await using (var db = _fx.CreateDbContext())
        {
            (await NewService(db).ResetPasswordWithTokenAsync("shared-tok", "NewPassword#1"))
                .Should().BeTrue();
        }

        await using var verify = _fx.CreateDbContext();
        var users = verify.Users.Where(u => u.Email == "shared@md.com").ToList();
        users.Should().HaveCount(2);
        foreach (var u in users)
        {
            BCrypt.Net.BCrypt.Verify("NewPassword#1", u.PasswordHash).Should().BeTrue();
            u.PasswordResetToken.Should().BeNull();
            u.PasswordResetTokenExpiry.Should().BeNull();
            u.RefreshToken.Should().BeNull();
        }
    }

    // -------------------------------------------------------------------------
    // End-to-end happy path: generate -> validate -> reset
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EndToEnd_GenerateValidateReset_FullTrailWorks()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Users.Add(NewUser(1, "trail@md.com", refreshToken: "active-session"));
            await seed.SaveChangesAsync();
        }

        // Step 1: generate
        string? token;
        await using (var db = _fx.CreateDbContext())
        {
            token = await NewService(db).GeneratePasswordResetTokenAsync("trail@md.com");
        }
        token.Should().NotBeNullOrWhiteSpace();

        // Step 2: validate (does not consume)
        await using (var db = _fx.CreateDbContext())
        {
            (await NewService(db).ValidatePasswordResetTokenAsync(token!)).Should().BeTrue();
        }

        // Step 3: reset (consumes)
        await using (var db = _fx.CreateDbContext())
        {
            (await NewService(db).ResetPasswordWithTokenAsync(token!, "BrandNew#Pwd1"))
                .Should().BeTrue();
        }

        // Step 4: token can no longer be validated, password is updated, sessions cleared
        await using (var db = _fx.CreateDbContext())
        {
            (await NewService(db).ValidatePasswordResetTokenAsync(token!)).Should().BeFalse();
        }
        await using var verify = _fx.CreateDbContext();
        var u = verify.Users.Single(x => x.UserId == 1);
        BCrypt.Net.BCrypt.Verify("BrandNew#Pwd1", u.PasswordHash).Should().BeTrue();
        u.RefreshToken.Should().BeNull();
    }
}
