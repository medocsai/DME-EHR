using System.Text.Json;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Stage 3 DB-touching tests for IntakeSubmissionService — the core write path
/// for patient-entered clinical data.
///
/// Contracts proven:
/// - Every section write stamps Source=Patient + IntakeSubmissionId on new rows
///   (the "re-tag immutability rule" from Stage 2 gets tested HERE at the caller,
///   because the tagger itself is indiscriminate).
/// - Submissions are opened once and reused across section saves in the same attempt.
/// - FinalizeAsync flips SubmittedAt and rejects when no open submission exists.
/// - Demographics encrypts address/city/state/zip; phone/email stay plain.
/// - Unknown section names return failure without writing.
/// - DeletePatientEnteredRow only touches Source=Patient rows, scoped to patientId.
/// - SectionsTouched is persisted as a JSON array.
/// </summary>
[Collection("Db")]
public class IntakeSubmissionServiceTests
{
    private readonly SqlServerFixture _fx;

    private const int PatientId = 42;
    private const int TenantId = 1;

    public IntakeSubmissionServiceTests(SqlServerFixture fx) => _fx = fx;

    // ---------- helpers ----------

    private IntakeSubmissionService NewService(EhrDbContext db, out Mock<IIntakeProgressCalculator> progressMock)
    {
        progressMock = new Mock<IIntakeProgressCalculator>();
        progressMock.Setup(p => p.CalculateAsync(It.IsAny<int>()))
            .ReturnsAsync(new IntakeProgressDto { Completed = 1, Total = 7 });

        return new IntakeSubmissionService(
            db,
            new ProvenanceTagger(),
            progressMock.Object,
            TestEncryptionHelper.Create(),
            NullLogger<IntakeSubmissionService>.Instance);
    }

    private async Task<int> SeedPatientAsync(int patientId = PatientId)
    {
        await using var db = _fx.CreateDbContext();
        db.Patients.Add(new Patient
        {
            PatientId = patientId,
            TenantId = TenantId,
            Mrn = $"MRN-{patientId}",
            FirstName = "Test",
            LastName = "Patient",
            Gender = "M",
            DateOfBirth = new DateOnly(1985, 6, 15)
        });
        await db.SaveChangesAsync();
        return patientId;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // ---------- SaveSectionAsync: section dispatch ----------

    [Fact]
    public async Task SaveSectionAsync_UnknownSection_ReturnsFailureWithoutWriting()
    {
        // Section name is validated BEFORE the submission is opened, so unknown
        // sections don't leave orphan PatientIntakeSubmission rows behind.
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var result = await svc.SaveSectionAsync(PatientId, TenantId, "not-a-section",
            Json("{}"), IntakeChannel.Portal, "10.0.0.1", "UA");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown section");

        // No submission row written.
        await using var verify = _fx.CreateDbContext();
        verify.PatientIntakeSubmissions.Count(s => s.PatientId == PatientId).Should().Be(0);
    }

    [Fact]
    public async Task SaveSectionAsync_FirstCall_OpensSubmission_SubsequentCallsReuseIt()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var first = await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"Headaches","priority":1}]}"""),
            IntakeChannel.Portal, "10.0.0.1", "UA");

        var second = await svc.SaveSectionAsync(PatientId, TenantId, "allergies",
            Json("""{"items":[{"allergenName":"Peanut"}]}"""),
            IntakeChannel.Portal, "10.0.0.1", "UA");

        first.SubmissionId.Should().Be(second.SubmissionId);
        await using var verify = _fx.CreateDbContext();
        verify.PatientIntakeSubmissions.Count(s => s.PatientId == PatientId).Should().Be(1);
    }

    [Fact]
    public async Task SaveSectionAsync_SectionsTouched_StoredAsJsonArray()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"X"}]}"""), IntakeChannel.Portal, "ip", "ua");
        await svc.SaveSectionAsync(PatientId, TenantId, "allergies",
            Json("""{"items":[{"allergenName":"Y"}]}"""), IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var sub = verify.PatientIntakeSubmissions.Single(s => s.PatientId == PatientId);
        var touched = JsonSerializer.Deserialize<string[]>(sub.SectionsTouched!);
        touched.Should().Contain(new[] { "concerns", "allergies" });
    }

    // ---------- Concerns ----------

    [Fact]
    public async Task SaveSectionAsync_Concerns_TagsRowsWithPatientSourceAndSubmissionId()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var result = await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"Headache","priority":1,"severity":4}]}"""),
            IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var row = verify.PatientHealthConcerns.Single(c => c.PatientId == PatientId && c.IsDeleted != true);
        row.Concern.Should().Be("Headache");
        row.Source.Should().Be((int)IntakeSource.Patient);
        row.IntakeSubmissionId.Should().Be(result.SubmissionId);
    }

    [Fact]
    public async Task SaveSectionAsync_Concerns_SecondSaveSoftDeletesFirstPatientEnteredSet()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"First"}]}"""), IntakeChannel.Portal, "ip", "ua");
        await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"Second"}]}"""), IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        verify.PatientHealthConcerns.Count(c => c.PatientId == PatientId && c.IsDeleted != true)
            .Should().Be(1);
        verify.PatientHealthConcerns.Single(c => c.PatientId == PatientId && c.IsDeleted != true)
            .Concern.Should().Be("Second");
        verify.PatientHealthConcerns.Count(c => c.PatientId == PatientId && c.IsDeleted == true)
            .Should().Be(1);
    }

    [Fact]
    public async Task SaveSectionAsync_Concerns_SkipsItemsWithBlankConcern()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":""},{"concern":"Real concern"},{"concern":"   "}]}"""),
            IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        verify.PatientHealthConcerns.Count(c => c.PatientId == PatientId && c.IsDeleted != true)
            .Should().Be(1);
    }

    // ---------- Demographics (encryption + integrity contract) ----------
    // Spec: CRITICAL.md ("PHI Fields Must Always Be Encrypted at Rest").
    // After save, ALL configured PHI fields must be ciphertext, not plaintext.
    // Tests below decrypt with a fresh helper (same fixed test key) to verify
    // the round-trip: client value → ciphertext on disk → decrypted = original.

    [Fact]
    public async Task SaveSectionAsync_Demographics_EncryptsAllConfiguredPhiFields()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);
        var enc = TestEncryptionHelper.Create();

        var payload = """{"firstName":"New","lastName":"Surname","phone":"555-1234","address":"1 Main St","city":"Boston","state":"MA","zipCode":"02101","emergencyContactName":"Jane","emergencyContactRelation":"Spouse","emergencyContactPhone":"555-1111"}""";

        var result = await svc.SaveSectionAsync(PatientId, TenantId, "demographics",
            Json(payload), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var p = verify.Patients.Single(x => x.PatientId == PatientId);

        // None of the configured PHI fields may be stored as plaintext.
        var phiFields = new (string raw, string stored)[]
        {
            ("New",        p.FirstName),
            ("Surname",    p.LastName),
            ("555-1234",   p.Phone),
            ("1 Main St",  p.Address),
            ("Boston",     p.City),
            ("MA",         p.State),
            ("02101",      p.ZipCode),
            ("Jane",       p.EmergencyContactName),
            ("Spouse",     p.EmergencyContactRelation),
            ("555-1111",   p.EmergencyContactPhone)
        };
        foreach (var (raw, stored) in phiFields)
        {
            stored.Should().NotBeNullOrEmpty();
            stored.Should().NotBe(raw, "PHI must never be persisted as plaintext (CRITICAL.md rule 1)");
            enc.IsEncrypted(stored).Should().BeTrue($"stored value for '{raw}' must look like ciphertext");
            enc.Decrypt(stored).Should().Be(raw);
        }

        // Gender is NOT in the encryption config (low-risk identifier), so it
        // round-trips as plaintext. Documents the boundary explicitly.
        // (Not asserted here because Gender wasn't in the payload; see the
        // separate WritesNameGenderAndEmergencyContact test for that.)
    }

    [Fact]
    public async Task SaveSectionAsync_Demographics_IgnoresReadOnlyFields_DobEmailSsn()
    {
        // Even if the client sends dateOfBirth, email, or ssn, the service must not
        // overwrite them. These are patient-identity fields tied to the account.
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-42",
                FirstName = "Original", LastName = "Name", Gender = "M",
                DateOfBirth = new DateOnly(1985, 6, 15),
                Email = "original@example.com",
                SsnEncrypted = "existing-encrypted-ssn"
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);
        var enc = TestEncryptionHelper.Create();

        var payload = """{"dateOfBirth":"1999-12-31","email":"hacker@bad.com","ssn":"999-99-9999","phone":"555-9999"}""";
        var result = await svc.SaveSectionAsync(PatientId, TenantId, "demographics",
            Json(payload), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var p = verify.Patients.Single(x => x.PatientId == PatientId);

        // DOB: plain DateOnly column, must not change.
        p.DateOfBirth.Should().Be(new DateOnly(1985, 6, 15));

        // The seed used a placeholder "existing-encrypted-ssn" that is actually
        // a plaintext string (not real ciphertext). EncryptEntity sees it as
        // plaintext and re-encrypts it. The MEANING (the SSN representation)
        // is preserved — what matters for the read-only contract is that
        // SAVE did not replace it with the payload's "999-99-9999".
        enc.Decrypt(p.SsnEncrypted).Should().Be("existing-encrypted-ssn");

        // Email is in the encryption config, so seed's plaintext value gets
        // re-encrypted by EncryptEntity on save. The read-only contract is
        // about MEANING (the patient's email didn't change to the attacker's),
        // not about column-bytes. Decrypt to verify the meaning is preserved.
        // The original seed was plaintext "original@example.com"; after the
        // first save it lands as ciphertext but still decodes to the same
        // address. Critically, it must NOT be the payload's "hacker@bad.com".
        // Spec: CRITICAL.md (PHI fields encrypted at rest).
        enc.IsEncrypted(p.Email).Should().BeTrue();
        enc.Decrypt(p.Email).Should().Be("original@example.com");

        // Phone IS writable, but stored encrypted (CRITICAL.md rule 1).
        enc.IsEncrypted(p.Phone).Should().BeTrue();
        enc.Decrypt(p.Phone).Should().Be("555-9999");
    }

    [Fact]
    public async Task SaveSectionAsync_Demographics_WritesNameGenderAndEmergencyContact()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);
        var enc = TestEncryptionHelper.Create();

        var payload = """{"firstName":"New","lastName":"Surname","gender":"F","emergencyContactName":"Jane","emergencyContactRelation":"Spouse","emergencyContactPhone":"555-1111","emergencyContactAltPhone":"555-2222"}""";
        var result = await svc.SaveSectionAsync(PatientId, TenantId, "demographics",
            Json(payload), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var p = verify.Patients.Single(x => x.PatientId == PatientId);

        // PHI fields: stored encrypted, decrypt to original.
        enc.Decrypt(p.FirstName).Should().Be("New");
        enc.Decrypt(p.LastName).Should().Be("Surname");
        enc.Decrypt(p.EmergencyContactName).Should().Be("Jane");
        enc.Decrypt(p.EmergencyContactRelation).Should().Be("Spouse");
        enc.Decrypt(p.EmergencyContactPhone).Should().Be("555-1111");

        // Gender is NOT in encryption config (low-risk identifier), stored plain.
        p.Gender.Should().Be("F");

        // EmergencyContactAltPhone is NOT registered in EncryptionConfiguration —
        // documents the boundary. (Future: consider adding it, see CRITICAL.md
        // section "Known plaintext PHI debt to clean up".)
        p.EmergencyContactAltPhone.Should().Be("555-2222");
    }

    [Fact]
    public async Task SaveSectionAsync_Demographics_EmptySubmission_DoesNotBlankExistingPhi()
    {
        // Regression test for a real data-loss bug: previously, a Save & Continue
        // submitted with empty fields would overwrite the patient's encrypted
        // FirstName / LastName / Phone with empty strings, wiping real data.
        // Now blank/whitespace fields are treated as "patient did not edit" and
        // existing values are preserved. Spec: CRITICAL.md (PHI integrity).
        await _fx.ResetAsync();
        var enc = TestEncryptionHelper.Create();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-42",
                FirstName = enc.Encrypt("Kamal"),
                LastName = enc.Encrypt("Khan"),
                Phone = enc.Encrypt("555-0000"),
                Gender = "M",
                DateOfBirth = new DateOnly(1995, 2, 1),
                Email = "kamal@test.com"
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        // All fields submitted as empty strings (the wizard's auto-save behavior
        // when patient hits Save & Continue without typing).
        var payload = """{"firstName":"","lastName":"","phone":"","address":"","city":"","emergencyContactName":""}""";
        var result = await svc.SaveSectionAsync(PatientId, TenantId, "demographics",
            Json(payload), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var p = verify.Patients.Single(x => x.PatientId == PatientId);

        // Existing encrypted values must round-trip unchanged.
        enc.Decrypt(p.FirstName).Should().Be("Kamal");
        enc.Decrypt(p.LastName).Should().Be("Khan");
        enc.Decrypt(p.Phone).Should().Be("555-0000");
    }

    [Fact]
    public async Task SaveSectionAsync_Demographics_WhitespaceOnlyFields_DoesNotOverwrite()
    {
        // Whitespace-only input should not count as a real edit either.
        await _fx.ResetAsync();
        var enc = TestEncryptionHelper.Create();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Patients.Add(new Patient
            {
                PatientId = PatientId, TenantId = TenantId, Mrn = "MRN-42",
                FirstName = enc.Encrypt("Kamal"),
                LastName = enc.Encrypt("Khan"),
                Gender = "M",
                DateOfBirth = new DateOnly(1995, 2, 1),
                Email = "kamal@test.com"
            });
            await seed.SaveChangesAsync();
        }
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var payload = """{"firstName":"   ","lastName":"\t"}""";
        var result = await svc.SaveSectionAsync(PatientId, TenantId, "demographics",
            Json(payload), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var p = verify.Patients.Single(x => x.PatientId == PatientId);
        enc.Decrypt(p.FirstName).Should().Be("Kamal");
        enc.Decrypt(p.LastName).Should().Be("Khan");
    }

    [Fact]
    public async Task SaveSectionAsync_UnknownPatient_ReturnsFailureCleanly()
    {
        // Patient existence is validated BEFORE the submission is opened, so
        // a bad patientId returns a clean failure DTO instead of throwing
        // DbUpdateException from the submission insert.
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var result = await svc.SaveSectionAsync(999, TenantId, "demographics",
            Json("""{"phone":"x"}"""), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");

        // No submission row written.
        await using var verify = _fx.CreateDbContext();
        verify.PatientIntakeSubmissions.Count(s => s.PatientId == 999).Should().Be(0);
    }

    // ---------- Medical history (problems + family nested) ----------

    [Fact]
    public async Task SaveSectionAsync_MedicalHistory_WritesProblemsAndFamilyHistory_AllTaggedPatient()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var payload = """{"problems":[{"description":"Hypertension","icdCode":"I10","status":1}],"family":[{"relation":"Mother","condition":"Diabetes","ageAtOnset":55}]}""";

        var result = await svc.SaveSectionAsync(PatientId, TenantId, "medical-history",
            Json(payload), IntakeChannel.Portal, "ip", "ua");

        result.Success.Should().BeTrue();
        await using var verify = _fx.CreateDbContext();
        var prob = verify.PatientProblems.Single(p => p.PatientId == PatientId);
        prob.IcdCode.Should().Be("I10");
        prob.Source.Should().Be((int)IntakeSource.Patient);
        prob.IntakeSubmissionId.Should().Be(result.SubmissionId);

        var fam = verify.PatientFamilyHistories.Single(f => f.PatientId == PatientId);
        fam.Relation.Should().Be("Mother");
        fam.Source.Should().Be((int)IntakeSource.Patient);
    }

    // ---------- Lifestyle (upsert per category) ----------

    [Fact]
    public async Task SaveSectionAsync_Lifestyle_UpsertsPerCategory_NoDuplicates()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "lifestyle",
            Json("""{"sleep":"6 hours","diet":"Vegan"}"""), IntakeChannel.Portal, "ip", "ua");
        // Second save updates, does NOT duplicate.
        await svc.SaveSectionAsync(PatientId, TenantId, "lifestyle",
            Json("""{"sleep":"8 hours"}"""), IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var rows = verify.PatientSocialHistories
            .Where(s => s.PatientId == PatientId && s.Source == (int)IntakeSource.Patient).ToList();
        rows.Count.Should().Be(2);
        rows.Single(r => r.Category == "sleep").Description.Should().Be("8 hours");
        rows.Single(r => r.Category == "diet").Description.Should().Be("Vegan");
    }

    // ---------- Medications / Supplements / Allergies / Immunizations ----------

    [Fact]
    public async Task SaveSectionAsync_Medications_TagsEachRow()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "medications",
            Json("""{"items":[{"drugName":"Lisinopril","dosage":"10mg","frequency":"QD"}]}"""),
            IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var m = verify.PatientMedications.Single(x => x.PatientId == PatientId);
        m.DrugName.Should().Be("Lisinopril");
        m.Source.Should().Be((int)IntakeSource.Patient);
    }

    [Fact]
    public async Task SaveSectionAsync_Allergies_TagsEachRow()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "allergies",
            Json("""{"items":[{"allergenName":"Peanut","severity":3}]}"""),
            IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var a = verify.PatientAllergies.Single(x => x.PatientId == PatientId);
        a.AllergenName.Should().Be("Peanut");
        a.Source.Should().Be((int)IntakeSource.Patient);
    }

    [Fact]
    public async Task SaveSectionAsync_Immunizations_ParsesDateOrFallsBackToToday()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "immunizations",
            Json("""{"items":[{"vaccineName":"Flu","administeredDate":"2026-01-15"},{"vaccineName":"COVID","administeredDate":"not-a-date"}]}"""),
            IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var rows = verify.PatientImmunizations.Where(i => i.PatientId == PatientId).ToList();
        rows.Count.Should().Be(2);
        rows.Single(r => r.VaccineName == "Flu").AdministeredDate.Should().Be(new DateOnly(2026, 1, 15));
        rows.Single(r => r.VaccineName == "COVID").AdministeredDate
            .Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // ---------- Longevity (upsert on single profile row) ----------

    [Fact]
    public async Task SaveSectionAsync_Longevity_CreatesProfileFirstTime_UpdatesSecondTime()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "longevity",
            Json("""{"biomarkerGoals":"v1","optimalHealthVision":"feel great"}"""),
            IntakeChannel.Portal, "ip", "ua");
        await svc.SaveSectionAsync(PatientId, TenantId, "longevity",
            Json("""{"biomarkerGoals":"v2"}"""), IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var profiles = verify.PatientLongevityProfiles.Where(l => l.PatientId == PatientId).ToList();
        profiles.Count.Should().Be(1); // upsert, not insert
        profiles[0].BiomarkerGoals.Should().Be("v2");
        profiles[0].OptimalHealthVision.Should().Be("feel great"); // second save didn't override missing field
    }

    // ---------- FinalizeAsync ----------

    [Fact]
    public async Task FinalizeAsync_OpenSubmission_SetsSubmittedAt()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        // Open a submission via a section save.
        var seed = await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"x"}]}"""), IntakeChannel.Portal, "ip", "ua");

        var result = await svc.FinalizeAsync(PatientId, TenantId);

        result.Success.Should().BeTrue();
        result.SubmissionId.Should().Be(seed.SubmissionId);
        await using var verify = _fx.CreateDbContext();
        verify.PatientIntakeSubmissions.Single(s => s.PatientIntakeSubmissionId == seed.SubmissionId)
            .SubmittedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FinalizeAsync_NoOpenSubmission_ReturnsFailure()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var result = await svc.FinalizeAsync(PatientId, TenantId);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No open submission");
    }

    // ---------- DeletePatientEnteredRowAsync ----------

    [Fact]
    public async Task DeletePatientEnteredRowAsync_PatientEnteredAllergy_RemovesRow()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "allergies",
            Json("""{"items":[{"allergenName":"Peanut"}]}"""), IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var rowId = verify.PatientAllergies.Single(a => a.PatientId == PatientId).PatientAllergyId;

        var ok = await svc.DeletePatientEnteredRowAsync(PatientId, "PatientAllergies", rowId);

        ok.Should().BeTrue();
        await using var verify2 = _fx.CreateDbContext();
        verify2.PatientAllergies.Count(a => a.PatientId == PatientId).Should().Be(0);
    }

    [Fact]
    public async Task DeletePatientEnteredRowAsync_ClinicEnteredAllergy_DoesNotDelete()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        // Seed a clinic-entered (Source=Clinic) row directly.
        await using (var seed = _fx.CreateDbContext())
        {
            seed.PatientAllergies.Add(new PatientAllergy
            {
                PatientId = PatientId,
                TenantId = TenantId,
                AllergenName = "Latex",
                Source = (int)IntakeSource.Clinic,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);
        var rowId = db.PatientAllergies.Single(a => a.PatientId == PatientId).PatientAllergyId;

        var ok = await svc.DeletePatientEnteredRowAsync(PatientId, "PatientAllergies", rowId);

        ok.Should().BeFalse();
        await using var verify = _fx.CreateDbContext();
        verify.PatientAllergies.Count(a => a.PatientId == PatientId).Should().Be(1);
    }

    [Fact]
    public async Task DeletePatientEnteredRowAsync_HealthConcern_SoftDeletes()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        await svc.SaveSectionAsync(PatientId, TenantId, "concerns",
            Json("""{"items":[{"concern":"X"}]}"""), IntakeChannel.Portal, "ip", "ua");

        await using var verify = _fx.CreateDbContext();
        var rowId = verify.PatientHealthConcerns.Single(c => c.PatientId == PatientId && c.IsDeleted != true)
            .PatientHealthConcernId;

        var ok = await svc.DeletePatientEnteredRowAsync(PatientId, "PatientHealthConcerns", rowId);

        ok.Should().BeTrue();
        await using var verify2 = _fx.CreateDbContext();
        verify2.PatientHealthConcerns.Count(c => c.PatientId == PatientId && c.IsDeleted != true)
            .Should().Be(0);
        verify2.PatientHealthConcerns.Count(c => c.PatientId == PatientId && c.IsDeleted == true)
            .Should().Be(1);
    }

    [Fact]
    public async Task DeletePatientEnteredRowAsync_UnknownTable_ReturnsFalse()
    {
        await _fx.ResetAsync();
        await SeedPatientAsync();
        await using var db = _fx.CreateDbContext();
        var svc = NewService(db, out _);

        var ok = await svc.DeletePatientEnteredRowAsync(PatientId, "NotATable", 1);

        ok.Should().BeFalse();
    }
}
