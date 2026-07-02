using EHR.Services.Intake;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Pure unit tests for the IntakeVerifyCookie helper. No DB, no HTTP.
/// Covers test IDs B1, B2, B3, B7 (cookie name/value/options + tamper detection)
/// and J2 (cookie value size) from rules/technical/consent-to-intake-handoff.md.
/// </summary>
public class IntakeVerifyCookieTests
{
    private static readonly Guid Token = Guid.Parse("39f1689c-18a7-4e82-a574-aaaaaaaaaaaa");

    [Fact] // B1
    public void Name_UsesPrefixAndFirst12HexCharsOfTokenN()
    {
        var name = IntakeVerifyCookie.Name(Token);
        name.Should().StartWith("intake_verify_");
        name.Should().Be("intake_verify_" + Token.ToString("N")[..12]);
    }

    [Fact] // B1
    public void Value_IsThe32CharNFormatGuid()
    {
        var value = IntakeVerifyCookie.Value(Token);
        value.Should().Be(Token.ToString("N"));
        value.Should().HaveLength(32);
        value.Should().NotContain("-");
    }

    [Fact] // B7
    public void Matches_ReturnsTrueForCorrectGuid()
    {
        IntakeVerifyCookie.Matches(Token.ToString("N"), Token).Should().BeTrue();
    }

    [Fact] // B7
    public void Matches_ReturnsFalseForTamperedValue()
    {
        var tampered = Token.ToString("N").Substring(0, 31) + "0";
        IntakeVerifyCookie.Matches(tampered, Token).Should().BeFalse();
    }

    [Fact] // B7
    public void Matches_ReturnsFalseForDifferentGuid()
    {
        var other = Guid.NewGuid().ToString("N");
        IntakeVerifyCookie.Matches(other, Token).Should().BeFalse();
    }

    [Theory] // B7
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void Matches_ReturnsFalseForNullOrEmptyOrJunk(string val)
    {
        IntakeVerifyCookie.Matches(val, Token).Should().BeFalse();
    }

    [Fact] // B2
    public void Options_HasSecurityAttributesEnabled()
    {
        var opts = IntakeVerifyCookie.Options(isHttps: true, ttl: TimeSpan.FromMinutes(30));

        opts.HttpOnly.Should().BeTrue();
        opts.Secure.Should().BeTrue();
        opts.SameSite.Should().Be(SameSiteMode.Strict);
        opts.Path.Should().Be("/");
        opts.Expires.Should().NotBeNull();
        opts.Expires!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
    }

    [Fact] // B2
    public void Options_SecureFollowsHttpsFlag()
    {
        IntakeVerifyCookie.Options(isHttps: true, ttl: TimeSpan.FromMinutes(30)).Secure.Should().BeTrue();
        IntakeVerifyCookie.Options(isHttps: false, ttl: TimeSpan.FromMinutes(30)).Secure.Should().BeFalse();
    }

    [Fact] // B2
    public void Options_TtlIsCallerControlled()
    {
        var opts = IntakeVerifyCookie.Options(isHttps: true, ttl: TimeSpan.FromHours(4));
        opts.Expires!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(4), TimeSpan.FromSeconds(5));
    }

    [Fact] // B3 — cookie name is per-token, so a cookie issued for one patient
            //      cannot be read on another patient's URL even if the prefix
            //      collides. Keep the first 12 hex chars unique to this token.
    public void Name_IsDifferentForDifferentTokens()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        IntakeVerifyCookie.Name(t1).Should().NotBe(IntakeVerifyCookie.Name(t2));
    }

    [Fact] // J2 — cookie value < 200 bytes
    public void Value_IsWellUnder200Bytes()
    {
        IntakeVerifyCookie.Value(Token).Length.Should().BeLessThan(200);
    }

    [Fact]
    public void HandoffTtl_IsShorterThanDefaultTtl()
    {
        IntakeVerifyCookie.HandoffTtl.Should().BeLessThan(IntakeVerifyCookie.DefaultTtl);
        IntakeVerifyCookie.HandoffTtl.Should().Be(TimeSpan.FromMinutes(30));
        IntakeVerifyCookie.DefaultTtl.Should().Be(TimeSpan.FromHours(4));
    }
}
