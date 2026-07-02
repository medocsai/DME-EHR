using EHR.Helpers;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Pure-logic tests for PatientIdentityMatcher — the tablet-verify identity gate.
/// 2026-05: switched from SSN-last4 to LastName + ZipCode (DOB still required).
/// Uses EF InMemory for the DbContext and a real EncryptionHelper. Decrypt() is
/// safe on plaintext values (returns input unchanged), so the seed stores
/// plaintext strings to keep the test simple.
/// </summary>
public class PatientIdentityMatcherTests
{
    private readonly EncryptionHelper _encryption = TestEncryptionHelper.Create();

    private static readonly DateOnly Dob = new(1985, 6, 15);
    private const string CorrectLastName = "Patient";
    private const string CorrectZip = "12345";

    private EhrDbContext SeededDb(Action<Patient>? mutate = null)
    {
        var db = InMemoryDbFactory.Create();
        var patient = new Patient
        {
            PatientId = 42,
            TenantId = 1,
            Mrn = "MRN-42",
            FirstName = "Test",
            LastName = CorrectLastName,
            Gender = "M",
            DateOfBirth = Dob,
            ZipCode = CorrectZip,
            IsDeleted = false
        };
        mutate?.Invoke(patient);
        db.Patients.Add(patient);
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task MatchesAsync_AllFieldsMatch_ReturnsTrue()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, CorrectLastName, Dob, CorrectZip);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MatchesAsync_LastNameWrong_ReturnsFalse()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, "WrongName", Dob, CorrectZip);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MatchesAsync_DobWrong_ReturnsFalse()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, CorrectLastName, new DateOnly(1990, 1, 1), CorrectZip);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MatchesAsync_ZipWrong_ReturnsFalse()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, CorrectLastName, Dob, "99999");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MatchesAsync_PatientDoesNotExist_ReturnsFalse()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(999, CorrectLastName, Dob, CorrectZip);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MatchesAsync_PatientIsDeleted_ReturnsFalse()
    {
        using var db = SeededDb(p => p.IsDeleted = true);
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, CorrectLastName, Dob, CorrectZip);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MatchesAsync_LastNameCaseInsensitive_ReturnsTrue()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, "PATIENT", Dob, CorrectZip);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MatchesAsync_LastNameWithWhitespace_Matches()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, "  Patient  ", Dob, CorrectZip);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MatchesAsync_ZipNineDigitWithDash_TakesFirstFive()
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, CorrectLastName, Dob, "12345-6789");

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "12345")]
    [InlineData("   ", "12345")]
    [InlineData(null, "12345")]
    [InlineData("Patient", "")]
    [InlineData("Patient", "   ")]
    [InlineData("Patient", null)]
    [InlineData("Patient", "123")] // zip too short
    public async Task MatchesAsync_EmptyOrShortInput_ReturnsFalse(string? lastName, string? zip)
    {
        using var db = SeededDb();
        var matcher = new PatientIdentityMatcher(db, _encryption);

        var result = await matcher.MatchesAsync(42, lastName!, Dob, zip!);

        result.Should().BeFalse();
    }
}
