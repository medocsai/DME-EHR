using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Moq;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 tests for PatientEnteredDataReader. The read-side view that pulls
/// patient-entered rows (Source=Patient) across clinical tables for the Profile
/// accordion + Encounter right panel.
///
/// Key contract: rows authored by the CLINIC (Source != Patient) must not leak
/// into this view (except concerns, which is any-source per the rules).
/// </summary>
[Collection("Db")]
public class PatientEnteredDataReaderTests
{
    private readonly SqlServerFixture _fx;

    private const int PatientId = 42;
    private const int TenantId = 1;

    public PatientEnteredDataReaderTests(SqlServerFixture fx) => _fx = fx;

    private static PatientEnteredDataReader NewReader(EhrDbContext db)
    {
        var progress = new Mock<IIntakeProgressCalculator>();
        progress.Setup(p => p.CalculateAsync(It.IsAny<int>()))
            .ReturnsAsync(new IntakeProgressDto { Completed = 0, Total = 7 });
        return new PatientEnteredDataReader(db, progress.Object);
    }

    private async Task SeedPatientAsync()
    {
        await using var db = _fx.CreateDbContext();
        db.Patients.Add(new Patient
        {
            PatientId = PatientId, TenantId = TenantId,
            Mrn = "MRN", FirstName = "T", LastName = "P", Gender = "M",
            DateOfBirth = new DateOnly(1985, 1, 1)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetIntakeViewAsync_EmptyPatient_ReturnsAllSectionsEmpty()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        view.PatientId.Should().Be(PatientId);
        view.Sections.Should().NotBeEmpty(); // header sections present
        view.Sections.All(s => s.Items.Count == 0).Should().BeTrue();
    }

    [Fact]
    public async Task GetIntakeViewAsync_Medications_OnlyReturnsPatientSourceRows()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientMedications.Add(new PatientMedication
            {
                PatientId = PatientId, TenantId = TenantId,
                DrugName = "FromPatient",
                Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
            });
            seed.PatientMedications.Add(new PatientMedication
            {
                PatientId = PatientId, TenantId = TenantId,
                DrugName = "FromClinic",
                Source = (int)IntakeSource.Clinic, CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        var meds = view.Sections.Single(s => s.Name == "medications");
        meds.Items.Should().HaveCount(1);
        meds.Items.Single().Label.Should().Be("FromPatient");
    }

    [Fact]
    public async Task GetIntakeViewAsync_Allergies_OnlyReturnsPatientSourceRows()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientAllergies.Add(new PatientAllergy
            {
                PatientId = PatientId, TenantId = TenantId,
                AllergenName = "Peanut", Source = (int)IntakeSource.Patient,
                IsActive = true, CreatedAt = DateTime.UtcNow
            });
            seed.PatientAllergies.Add(new PatientAllergy
            {
                PatientId = PatientId, TenantId = TenantId,
                AllergenName = "Latex", Source = (int)IntakeSource.Clinic,
                IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        var allergies = view.Sections.Single(s => s.Name == "allergies");
        allergies.Items.Should().ContainSingle(i => i.Label == "Peanut");
        allergies.Items.Should().NotContain(i => i.Label == "Latex");
    }

    [Fact]
    public async Task GetIntakeViewAsync_Concerns_IncludesAllSourcesButExcludesDeleted()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientHealthConcerns.AddRange(
                new PatientHealthConcern
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Priority = 1, Concern = "From patient",
                    Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
                },
                new PatientHealthConcern
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Priority = 2, Concern = "From clinic",
                    Source = (int)IntakeSource.Clinic, CreatedAt = DateTime.UtcNow
                },
                new PatientHealthConcern
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Priority = 3, Concern = "Deleted one",
                    Source = (int)IntakeSource.Patient, IsDeleted = true,
                    CreatedAt = DateTime.UtcNow
                });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        var concerns = view.Sections.Single(s => s.Name == "concerns");
        concerns.Items.Select(i => i.Label).Should()
            .BeEquivalentTo(new[] { "From patient", "From clinic" });
    }

    [Fact]
    public async Task GetIntakeViewAsync_Lifestyle_SortedByCategory_OnlyPatientSource()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientSocialHistories.AddRange(
                new PatientSocialHistory
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Category = "sleep", Description = "6h",
                    Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
                },
                new PatientSocialHistory
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Category = "diet", Description = "vegan",
                    Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
                },
                new PatientSocialHistory
                {
                    PatientId = PatientId, TenantId = TenantId,
                    Category = "tobacco", Description = "never",
                    Source = (int)IntakeSource.Clinic, CreatedAt = DateTime.UtcNow
                });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        var lifestyle = view.Sections.Single(s => s.Name == "lifestyle");
        lifestyle.Items.Should().HaveCount(2);
        lifestyle.Items.Select(i => i.Label).Should().Equal("diet", "sleep"); // alphabetical
    }

    [Fact]
    public async Task GetIntakeViewAsync_Longevity_ProfilePresent_SectionIncluded()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientLongevityProfiles.Add(new PatientLongevityProfile
            {
                PatientId = PatientId, TenantId = TenantId,
                OptimalHealthVision = "Healthy at 100",
                Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        view.Sections.Any(s => s.Name == "longevity").Should().BeTrue();
        view.Sections.Single(s => s.Name == "longevity").Items.Single().Detail
            .Should().Be("Healthy at 100");
    }

    [Fact]
    public async Task GetIntakeViewAsync_Longevity_NoProfile_SectionOmitted()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        view.Sections.Any(s => s.Name == "longevity").Should().BeFalse();
    }

    [Fact]
    public async Task GetIntakeViewAsync_ProgressDelegatedToCalculator()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        view.Progress.Should().NotBeNull();
        view.Progress.Total.Should().Be(7);
    }

    [Fact]
    public async Task GetIntakeViewAsync_CrossPatientIsolation_OtherPatientsRowsNotReturned()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            // Seed another patient with rows.
            seed.Patients.Add(new Patient
            {
                PatientId = 99, TenantId = TenantId,
                Mrn = "OTHER", FirstName = "O", LastName = "P", Gender = "F",
                DateOfBirth = new DateOnly(1990, 1, 1)
            });
            seed.PatientMedications.Add(new PatientMedication
            {
                PatientId = 99, TenantId = TenantId,
                DrugName = "OtherPatientMed",
                Source = (int)IntakeSource.Patient, CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var reader = NewReader(db);

        var view = await reader.GetIntakeViewAsync(PatientId);

        view.Sections.Single(s => s.Name == "medications").Items.Should().BeEmpty();
    }
}
