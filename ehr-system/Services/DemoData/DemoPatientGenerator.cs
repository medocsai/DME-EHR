using EHR.Helpers;
using EHR.Models.Generated;
using static EHR.Services.DemoData.DemoDataConstants;

namespace EHR.Services.DemoData;

/// <summary>
/// Generates 200 demo Patients with clinical history:
/// Insurance, Problems, Allergies, Medications, Family/Social History, Immunizations.
/// </summary>
public class DemoPatientGenerator
{
    private readonly EhrDbContext _db;
    private readonly EncryptionHelper _enc;
    private readonly IBlindIndexService _blindIndex;

    public DemoPatientGenerator(EhrDbContext db, EncryptionHelper enc, IBlindIndexService blindIndex)
    {
        _db = db;
        _enc = enc;
        _blindIndex = blindIndex;
    }

    public record PatientResult(List<Patient> Patients, List<Insurance> Insurances);

    public async Task<PatientResult> GenerateAsync(int tenantId, List<Provider> providers, List<Location> locations, int createdByUserId)
    {
        var patients = new List<Patient>();
        var insurances = new List<Insurance>();

        // ── Find the next MRN number ────────────────────────────────
        var lastPatient = _db.Patients
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PatientId)
            .FirstOrDefault();
        var nextMrn = (lastPatient?.PatientId ?? 0) + 1;

        // Store plaintext data before encryption (needed for search index + insurance subscriber)
        var plaintextNames = new Dictionary<int, (string First, string Last, string Phone)>();

        // ── Generate 200 Patients ───────────────────────────────────
        for (int i = 0; i < 200; i++)
        {
            var isMale = Chance(48);
            var firstName = isMale ? Pick(FirstNamesMale) : Pick(FirstNamesFemale);
            var lastName = Pick(LastNames);
            // Age weighted toward 40-70 for IM clinic
            var age = Chance(15) ? Between(18, 39) :
                      Chance(60) ? Between(40, 70) :
                      Between(71, 88);
            var dob = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-age).AddDays(-Between(0, 365)));

            var patient = new Patient
            {
                TenantId = tenantId,
                Mrn = $"IM-{nextMrn + i:D6}",
                FirstName = firstName,
                LastName = lastName,
                DateOfBirth = dob,
                Gender = isMale ? "Male" : "Female",
                Phone = $"(512) 555-{Between(1000, 9999)}",
                Email = $"{firstName.ToLower()}.{lastName.ToLower()}{i}@test.com",
                Address = $"{Between(100, 9999)} {Pick(new[] { "Oak", "Elm", "Main", "Cedar", "Park", "Lake", "Hill", "Spring", "River", "Maple" })} {Pick(new[] { "St", "Ave", "Blvd", "Dr", "Ln", "Ct" })}",
                City = "Austin",
                State = "TX",
                ZipCode = $"787{Between(10, 99)}",
                EmergencyContactName = $"{Pick(FirstNamesMale)} {lastName}",
                EmergencyContactPhone = $"(512) 555-{Between(1000, 9999)}",
                EmergencyContactRelation = Pick(new[] { "Spouse", "Parent", "Sibling", "Child", "Friend" }),
                PreferredProviderId = providers[Rng.Next(providers.Count)].ProviderId,
                PreferredLocationId = locations.Count > 0 ? locations[Rng.Next(locations.Count)].LocationId : null,
                IsDeleted = false,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
            };
            // Store plaintext data before encryption
            plaintextNames[i] = (firstName, lastName, patient.Phone);
            _enc.EncryptEntity(patient);
            _db.Patients.Add(patient);
            patients.Add(patient);
        }
        await _db.SaveChangesAsync();

        // ── Build search index for all patients (using plaintext names) ──
        for (int idx = 0; idx < patients.Count; idx++)
        {
            var p = patients[idx];
            var names = plaintextNames[idx];
            // Create a temporary patient with plaintext fields for indexing
            var indexPatient = new Patient
            {
                PatientId = p.PatientId,
                TenantId = tenantId,
                FirstName = names.First,
                LastName = names.Last,
                Mrn = p.Mrn,
                Phone = names.Phone,
                Email = $"{names.First.ToLower()}.{names.Last.ToLower()}{idx}@test.com",
                PreferredLocationId = p.PreferredLocationId
            };
            await _blindIndex.IndexPatientAsync(indexPatient);
        }

        // ── Insurance (85% of patients, some get 2) ─────────────────
        for (int idx = 0; idx < patients.Count; idx++)
        {
            var patient = patients[idx];
            if (!Chance(85)) continue;

            var names = plaintextNames[idx];
            var payer = Pick(Payers);
            var relationship = Chance(80) ? "Self" : Pick(new[] { "Spouse", "Child", "Other" });
            var subFirst = relationship == "Self" ? names.First : Pick(FirstNamesMale);
            var subLast = names.Last;

            var ins = new Insurance
            {
                TenantId = tenantId,
                PatientId = patient.PatientId,
                PayerName = payer.PayerName,
                PayerId = payer.PayerId,
                PolicyNumber = $"POL{Between(100000, 999999)}",
                GroupNumber = $"GRP{Between(1000, 9999)}",
                SubscriberName = $"{subFirst} {subLast}",
                SubscriberFirstName = subFirst,
                SubscriberLastName = subLast,
                SubscriberRelationship = relationship,
                Type = 0, // Primary
                IsActive = true,
                EligibilityStatus = 1, // Eligible
                Copay = payer.Copay,
                Coinsurance = payer.PayerName.Contains("Medicare") ? 20m : Between(10, 30),
                DeductibleTotal = payer.PayerName.Contains("Medicare") ? 233m : Between(500, 3000),
                DeductibleMet = BetweenDec(0, 500),
                InNetwork = true,
                InsuranceCategory = payer.PayerName.Contains("Workers") ? 1 : 0,
                CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
            };
            _enc.EncryptEntity(ins);
            _db.Insurances.Add(ins);
            insurances.Add(ins);

            // 15% get secondary insurance
            if (Chance(15))
            {
                var sec = Pick(Payers);
                while (sec.PayerName == payer.PayerName) sec = Pick(Payers);
                var secIns = new Insurance
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    PayerName = sec.PayerName,
                    PayerId = sec.PayerId,
                    PolicyNumber = $"POL{Between(100000, 999999)}",
                    GroupNumber = $"GRP{Between(1000, 9999)}",
                    SubscriberFirstName = subFirst,
                    SubscriberLastName = subLast,
                    SubscriberRelationship = relationship,
                    Type = 1, // Secondary
                    IsActive = true,
                    EligibilityStatus = 1,
                    Copay = sec.Copay,
                    InsuranceCategory = 0,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                };
                _enc.EncryptEntity(secIns);
                _db.Insurances.Add(secIns);
            }
        }
        await _db.SaveChangesAsync();

        // ── Patient Problems (40% get 1-4) ──────────────────────────
        foreach (var patient in patients)
        {
            if (!Chance(40)) continue;
            var count = Between(1, 4);
            var used = new HashSet<int>();
            for (int j = 0; j < count; j++)
            {
                int idx;
                do { idx = Rng.Next(IcdCodes.Length); } while (used.Contains(idx));
                used.Add(idx);
                var icd = IcdCodes[idx];
                _db.PatientProblems.Add(new PatientProblem
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    IcdCode = icd.Code,
                    Description = icd.Desc,
                    Status = Chance(85) ? 0 : 1, // 85% Active, 15% Resolved
                    OnsetDate = RandomDate(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-5)), DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3))),
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                });
            }
        }

        // ── Allergies (30% get 1-2) ─────────────────────────────────
        foreach (var patient in patients)
        {
            if (!Chance(30)) continue;
            var count = Between(1, 2);
            var used = new HashSet<int>();
            for (int j = 0; j < count; j++)
            {
                int idx;
                do { idx = Rng.Next(Allergies.Length); } while (used.Contains(idx));
                used.Add(idx);
                var a = Allergies[idx];
                _db.PatientAllergies.Add(new PatientAllergy
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    AllergenName = a.Allergen,
                    Type = a.Type,
                    Reaction = a.Reaction,
                    Severity = a.Severity,
                    IsActive = true,
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                });
            }
        }

        // ── Current Medications (35% get 1-3) ───────────────────────
        foreach (var patient in patients)
        {
            if (!Chance(35)) continue;
            var count = Between(1, 3);
            var used = new HashSet<int>();
            for (int j = 0; j < count; j++)
            {
                int idx;
                do { idx = Rng.Next(Drugs.Length); } while (used.Contains(idx));
                used.Add(idx);
                var d = Drugs[idx];
                _db.PatientMedications.Add(new PatientMedication
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    DrugName = d.Drug,
                    Dosage = d.Strength,
                    Frequency = d.Freq.ToString(),
                    Status = 0, // Active
                    StartDate = RandomDate(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)), DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1))),
                    PrescribedByProviderId = providers[Rng.Next(providers.Count)].ProviderId,
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                });
            }
        }

        // ── Family History (25% get 1-2) ────────────────────────────
        foreach (var patient in patients)
        {
            if (!Chance(25)) continue;
            var count = Between(1, 2);
            var used = new HashSet<int>();
            for (int j = 0; j < count; j++)
            {
                int idx;
                do { idx = Rng.Next(FamilyHistory.Length); } while (used.Contains(idx));
                used.Add(idx);
                var fh = FamilyHistory[idx];
                _db.PatientFamilyHistories.Add(new PatientFamilyHistory
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    Relation = fh.Relation,
                    Condition = fh.Condition,
                    AgeAtOnset = Between(40, 70),
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                });
            }
        }

        // ── Social History (40% get 1-3 categories) ─────────────────
        foreach (var patient in patients)
        {
            if (!Chance(40)) continue;
            var count = Between(1, 3);
            var used = new HashSet<int>();
            for (int j = 0; j < count; j++)
            {
                int idx;
                do { idx = Rng.Next(SocialHistory.Length); } while (used.Contains(idx));
                used.Add(idx);
                var sh = SocialHistory[idx];
                _db.PatientSocialHistories.Add(new PatientSocialHistory
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    Category = sh.Category,
                    Description = Pick(sh.Options),
                    Status = "Current",
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                });
            }
        }

        // ── Immunizations (20% get 1-2) ─────────────────────────────
        foreach (var patient in patients)
        {
            if (!Chance(20)) continue;
            var count = Between(1, 2);
            var used = new HashSet<int>();
            for (int j = 0; j < count; j++)
            {
                int idx;
                do { idx = Rng.Next(Immunizations.Length); } while (used.Contains(idx));
                used.Add(idx);
                var imm = Immunizations[idx];
                _db.PatientImmunizations.Add(new PatientImmunization
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    VaccineName = imm.Vaccine,
                    CvxCode = imm.Cvx,
                    AdministeredDate = RandomDate(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)), DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1))),
                    AdministeredByProviderId = providers[Rng.Next(providers.Count)].ProviderId,
                    CreatedByUserId = createdByUserId,
                    CreatedAt = DateTime.UtcNow.AddDays(-Between(30, 180))
                });
            }
        }

        await _db.SaveChangesAsync();
        return new PatientResult(patients, insurances);
    }
}
