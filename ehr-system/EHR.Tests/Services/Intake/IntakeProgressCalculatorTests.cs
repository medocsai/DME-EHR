using System.Text.Json;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Moq;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 tests for IntakeProgressCalculator. Stock-Problem core: progress is
/// computed live from clinical rows + SectionsTouched JSON, never stored.
///
/// Section-complete rules (per rules/technical/patient-intake.md):
/// - demographics: DOB set + (phone OR email)
/// - concerns: >= 1 row (any source)
/// - medical-history: SectionsTouched has "medical-history" OR any Patient-source row
/// - lifestyle: >= 3 distinct Patient-source social-history categories
/// - medications: SectionsTouched has "medications" OR "supplements"
/// - gender-health: SectionsTouched has "gender-health"
/// - longevity: only present when LongevityFeatureGate.IsEnabled; filled when
///   SymptomRatings or Goals is non-empty
/// - review: only filled when SubmittedAt set
/// </summary>
[Collection("Db")]
public class IntakeProgressCalculatorTests
{
    private readonly SqlServerFixture _fx;

    private const int PatientId = 42;
    private const int TenantId = 1;
    private const int LocationId = 10;

    public IntakeProgressCalculatorTests(SqlServerFixture fx) => _fx = fx;

    private IntakeProgressCalculator NewCalc(EhrDbContext db, bool longevityEnabled = false)
    {
        var gate = new Mock<ILongevityFeatureGate>();
        gate.Setup(g => g.IsEnabledAsync(It.IsAny<int>())).ReturnsAsync(longevityEnabled);
        return new IntakeProgressCalculator(db, gate.Object);
    }

    private async Task SeedPatientAsync(
        string? phone = null,
        string? email = null,
        int? preferredLocationId = null,
        DateOnly? dob = null)
    {
        await using var db = _fx.CreateDbContext();
        db.Patients.Add(new Patient
        {
            PatientId = PatientId,
            TenantId = TenantId,
            Mrn = "MRN-42",
            FirstName = "Test",
            LastName = "Patient",
            Gender = "M",
            DateOfBirth = dob ?? new DateOnly(1985, 6, 15),
            Phone = phone,
            Email = email,
            PreferredLocationId = preferredLocationId
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedSubmissionAsync(string[] sectionsTouched, DateTime? submittedAt = null)
    {
        await using var db = _fx.CreateDbContext();
        db.PatientIntakeSubmissions.Add(new PatientIntakeSubmission
        {
            PatientId = PatientId,
            TenantId = TenantId,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            SectionsTouched = JsonSerializer.Serialize(sectionsTouched),
            SourceChannel = (int)IntakeChannel.Portal,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            SubmittedAt = submittedAt
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CalculateAsync_PatientDoesNotExist_ReturnsZeroOfSeven()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(patientId: 999);

        result.Completed.Should().Be(0);
        result.Total.Should().Be(7);
    }

    [Fact]
    public async Task CalculateAsync_BareMinimumPatient_NoSubmission_SevenSectionsZeroCompleted()
    {
        await _fx.ResetAsync();
        // Patient exists but no phone/email/submission/clinical rows → all sections empty.
        await SeedPatientAsync(phone: null, email: null);
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Total.Should().Be(7);
        result.Completed.Should().Be(0);
        result.Sections.Should().HaveCount(7);
    }

    [Fact]
    public async Task CalculateAsync_Demographics_RequiresDobAndPhoneOrEmail()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync(phone: "555-1234");
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "demographics").Filled.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateAsync_Demographics_MissingPhoneAndEmail_NotFilled()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync(phone: null, email: null);
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "demographics").Filled.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_Concerns_AnyRowCountsRegardlessOfSource()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            // Source=Clinic still counts for concerns (rule: ">=1 row").
            seed.PatientHealthConcerns.Add(new PatientHealthConcern
            {
                PatientId = PatientId, TenantId = TenantId,
                Priority = 1, Concern = "Clinic entry",
                Source = (int)IntakeSource.Clinic, CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "concerns").Filled.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateAsync_MedicalHistory_TouchedAloneCounts()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await SeedSubmissionAsync(new[] { "medical-history" });
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "medical-history").Filled.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateAsync_MedicalHistory_PatientSourceProblemRowAloneCounts()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientProblems.Add(new PatientProblem
            {
                PatientId = PatientId, TenantId = TenantId,
                Description = "Asthma", Source = (int)IntakeSource.Patient,
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "medical-history").Filled.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateAsync_Lifestyle_RequiresAtLeastThreeDistinctCategories()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientSocialHistories.AddRange(
                new PatientSocialHistory { PatientId = PatientId, TenantId = TenantId, Category = "sleep", Description = "6h", Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow },
                new PatientSocialHistory { PatientId = PatientId, TenantId = TenantId, Category = "diet", Description = "vegan", Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "lifestyle").Filled.Should().BeFalse(); // only 2 categories
    }

    [Fact]
    public async Task CalculateAsync_Lifestyle_ThreeCategoriesFilled()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            foreach (var cat in new[] { "sleep", "diet", "exercise" })
            {
                seed.PatientSocialHistories.Add(new PatientSocialHistory
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Category = cat, Description = "desc",
                    Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
                });
            }
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "lifestyle").Filled.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateAsync_Review_OnlyFilledWhenSubmittedAtSet()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await SeedSubmissionAsync(new[] { "concerns" }); // not submitted
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "review").Filled.Should().BeFalse();
        result.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task CalculateAsync_Review_FilledWhenSubmittedAtSet()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await SeedSubmissionAsync(new[] { "concerns" }, submittedAt: DateTime.UtcNow);
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "review").Filled.Should().BeTrue();
        result.SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CalculateAsync_LongevityDisabled_SectionNotInList()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync(preferredLocationId: LocationId);
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db, longevityEnabled: false);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Any(s => s.Name == "longevity").Should().BeFalse();
        result.Total.Should().Be(7);
    }

    [Fact]
    public async Task CalculateAsync_LongevityEnabled_SectionAppears_FilledWhenProfileHasContent()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync(preferredLocationId: LocationId);
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientLongevityProfiles.Add(new PatientLongevityProfile
            {
                PatientId = PatientId, TenantId = TenantId,
                Goals = "Live to 120",
                Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db, longevityEnabled: true);

        var result = await calc.CalculateAsync(PatientId);

        result.Total.Should().Be(8); // 7 base + longevity
        result.Sections.Single(s => s.Name == "longevity").Filled.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateAsync_LongevityEnabled_NoProfile_SectionPresentButNotFilled()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync(preferredLocationId: LocationId);
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db, longevityEnabled: true);

        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "longevity").Filled.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_InvalidSectionsTouchedJson_TreatedAsEmpty()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        // Seed submission with garbage JSON in SectionsTouched.
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientIntakeSubmissions.Add(new PatientIntakeSubmission
            {
                PatientId = PatientId, TenantId = TenantId,
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                SectionsTouched = "not-valid-json",
                SourceChannel = (int)IntakeChannel.Portal,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        // Should not throw; sections dependent on touched should be false.
        var result = await calc.CalculateAsync(PatientId);

        result.Sections.Single(s => s.Name == "medical-history").Filled.Should().BeFalse();
        result.Sections.Single(s => s.Name == "gender-health").Filled.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateAsync_CurrentSubmissionIdReflectsLatestOpenSubmission()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await SeedSubmissionAsync(new[] { "concerns" });
        await using var db = _fx.CreateDbContext();
        var calc = NewCalc(db);

        var result = await calc.CalculateAsync(PatientId);

        result.CurrentSubmissionId.Should().NotBeNull();
    }
}
