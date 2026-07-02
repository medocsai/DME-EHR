using System.Text.Json;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EHR.Services.Intake;

/// <summary>
/// Routes a section payload to the correct clinical table, stamps provenance,
/// updates PatientIntakeSubmission (SectionsTouched + UpdatedAt), and returns fresh progress.
///
/// Sections supported: demographics, concerns, medical-history, lifestyle,
/// medications, supplements, allergies, family-history, immunizations,
/// gender-health, longevity.
///
/// Payload is a JsonElement to keep section-specific shapes flexible without N DTOs.
/// </summary>
public interface IIntakeSubmissionService
{
    Task<IntakeSubmissionResultDto> SaveSectionAsync(
        int patientId,
        int tenantId,
        string sectionName,
        JsonElement payload,
        IntakeChannel channel,
        string ipAddress,
        string userAgent);

    Task<bool> DeletePatientEnteredRowAsync(int patientId, string table, int rowId);

    Task<IntakeSubmissionResultDto> FinalizeAsync(int patientId, int tenantId);
}

public class IntakeSubmissionService : IIntakeSubmissionService
{
    private readonly EhrDbContext _context;
    private readonly IProvenanceTagger _provenance;
    private readonly IIntakeProgressCalculator _progress;
    private readonly EncryptionHelper _encryption;
    private readonly ILogger<IntakeSubmissionService> _logger;

    public IntakeSubmissionService(
        EhrDbContext context,
        IProvenanceTagger provenance,
        IIntakeProgressCalculator progress,
        EncryptionHelper encryption,
        ILogger<IntakeSubmissionService> logger)
    {
        _context = context;
        _provenance = provenance;
        _progress = progress;
        _encryption = encryption;
        _logger = logger;
    }

    private static readonly HashSet<string> ValidSections = new(StringComparer.Ordinal)
    {
        "demographics", "concerns", "medical-history", "lifestyle",
        "medications", "supplements", "allergies", "family-history",
        "immunizations", "gender-health", "longevity"
    };

    public async Task<IntakeSubmissionResultDto> SaveSectionAsync(
        int patientId,
        int tenantId,
        string sectionName,
        JsonElement payload,
        IntakeChannel channel,
        string ipAddress,
        string userAgent)
    {
        sectionName = (sectionName ?? "").Trim().ToLowerInvariant();

        // Validate section name BEFORE touching the DB so unknown sections don't
        // leave orphan PatientIntakeSubmission rows behind.
        if (!ValidSections.Contains(sectionName))
            return new IntakeSubmissionResultDto { Success = false, Message = $"Unknown section: {sectionName}" };

        // Validate patient exists BEFORE opening submission so a bad patientId
        // returns a clean failure DTO instead of bubbling a DbUpdateException.
        var patientExists = await _context.Patients
            .AnyAsync(p => p.PatientId == patientId && p.TenantId == tenantId);
        if (!patientExists)
            return new IntakeSubmissionResultDto { Success = false, Message = "Patient not found." };

        var submission = await GetOrCreateOpenSubmissionAsync(patientId, tenantId, channel, ipAddress, userAgent);

        try
        {
            switch (sectionName)
            {
                case "demographics":
                    await SaveDemographicsAsync(patientId, tenantId, payload);
                    break;
                case "concerns":
                    await SaveConcernsAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "medical-history":
                    await SaveMedicalHistoryAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "lifestyle":
                    await SaveLifestyleAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "medications":
                    await SaveMedicationsAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "supplements":
                    await SaveSupplementsAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "allergies":
                    await SaveAllergiesAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "family-history":
                    await SaveFamilyHistoryAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "immunizations":
                    await SaveImmunizationsAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "gender-health":
                    await SaveGenderHealthAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
                case "longevity":
                    await SaveLongevityAsync(patientId, tenantId, payload, submission.PatientIntakeSubmissionId);
                    break;
            }

            MarkSectionTouched(submission, sectionName);
            submission.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var prog = await _progress.CalculateAsync(patientId);
            return new IntakeSubmissionResultDto
            {
                Success = true,
                SubmissionId = submission.PatientIntakeSubmissionId,
                Progress = prog
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intake section save failed: patient {PatientId}, section {Section}", patientId, sectionName);
            return new IntakeSubmissionResultDto { Success = false, Message = "Save failed." };
        }
    }

    public async Task<bool> DeletePatientEnteredRowAsync(int patientId, string table, int rowId)
    {
        var patientSource = (int)IntakeSource.Patient;
        switch ((table ?? "").Trim())
        {
            case "PatientHealthConcerns":
                {
                    var r = await _context.PatientHealthConcerns
                        .FirstOrDefaultAsync(x => x.PatientHealthConcernId == rowId && x.PatientId == patientId);
                    if (r == null) return false;
                    r.IsDeleted = true;
                    r.UpdatedAt = DateTime.UtcNow;
                    break;
                }
            case "PatientSupplements":
                {
                    var r = await _context.PatientSupplements
                        .FirstOrDefaultAsync(x => x.PatientSupplementId == rowId && x.PatientId == patientId);
                    if (r == null) return false;
                    r.IsDeleted = true;
                    r.UpdatedAt = DateTime.UtcNow;
                    break;
                }
            case "PatientMedications":
                {
                    var r = await _context.PatientMedications
                        .FirstOrDefaultAsync(x => x.PatientMedicationId == rowId && x.PatientId == patientId && x.Source == patientSource);
                    if (r == null) return false;
                    _context.PatientMedications.Remove(r);
                    break;
                }
            case "PatientAllergies":
                {
                    var r = await _context.PatientAllergies
                        .FirstOrDefaultAsync(x => x.PatientAllergyId == rowId && x.PatientId == patientId && x.Source == patientSource);
                    if (r == null) return false;
                    _context.PatientAllergies.Remove(r);
                    break;
                }
            case "PatientProblems":
                {
                    var r = await _context.PatientProblems
                        .FirstOrDefaultAsync(x => x.PatientProblemId == rowId && x.PatientId == patientId && x.Source == patientSource);
                    if (r == null) return false;
                    _context.PatientProblems.Remove(r);
                    break;
                }
            case "PatientFamilyHistories":
                {
                    var r = await _context.PatientFamilyHistories
                        .FirstOrDefaultAsync(x => x.PatientFamilyHistoryId == rowId && x.PatientId == patientId && x.Source == patientSource);
                    if (r == null) return false;
                    _context.PatientFamilyHistories.Remove(r);
                    break;
                }
            case "PatientSocialHistories":
                {
                    var r = await _context.PatientSocialHistories
                        .FirstOrDefaultAsync(x => x.PatientSocialHistoryId == rowId && x.PatientId == patientId && x.Source == patientSource);
                    if (r == null) return false;
                    _context.PatientSocialHistories.Remove(r);
                    break;
                }
            case "PatientImmunizations":
                {
                    var r = await _context.PatientImmunizations
                        .FirstOrDefaultAsync(x => x.PatientImmunizationId == rowId && x.PatientId == patientId && x.Source == patientSource);
                    if (r == null) return false;
                    _context.PatientImmunizations.Remove(r);
                    break;
                }
            default:
                return false;
        }
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IntakeSubmissionResultDto> FinalizeAsync(int patientId, int tenantId)
    {
        var submission = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.SubmittedAt == null && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        if (submission == null)
        {
            return new IntakeSubmissionResultDto { Success = false, Message = "No open submission to finalize." };
        }

        submission.SubmittedAt = DateTime.UtcNow;
        submission.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var prog = await _progress.CalculateAsync(patientId);
        return new IntakeSubmissionResultDto
        {
            Success = true,
            SubmissionId = submission.PatientIntakeSubmissionId,
            Progress = prog
        };
    }

    // ---------- helpers ----------

    private async Task<PatientIntakeSubmission> GetOrCreateOpenSubmissionAsync(
        int patientId, int tenantId, IntakeChannel channel, string ipAddress, string userAgent)
    {
        var existing = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.SubmittedAt == null && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        if (existing != null) return existing;

        var sub = new PatientIntakeSubmission
        {
            PatientId = patientId,
            TenantId = tenantId,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            SourceChannel = (int)channel,
            IpAddress = ipAddress,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            SectionsTouched = "[]"
        };
        _context.PatientIntakeSubmissions.Add(sub);
        await _context.SaveChangesAsync();
        return sub;
    }

    private static void MarkSectionTouched(PatientIntakeSubmission submission, string sectionName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(submission.SectionsTouched))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(submission.SectionsTouched) ?? Array.Empty<string>();
                foreach (var s in arr) set.Add(s);
            }
            catch { }
        }
        set.Add(sectionName);
        submission.SectionsTouched = JsonSerializer.Serialize(set.ToArray());
    }

    private static string GetStr(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }

    private static int? GetInt(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var j)) return j;
        return null;
    }

    private static JsonElement? GetArray(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.Array ? p : (JsonElement?)null;
    }

    // ---------- per-section savers ----------

    private async Task SaveDemographicsAsync(int patientId, int tenantId, JsonElement payload)
    {
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId && p.TenantId == tenantId);
        if (patient == null) return;

        // ── Read-only fields (enforced server-side regardless of what client sends) ──
        // - DateOfBirth: clinical identifier, set at patient creation
        // - Email: tied to the portal account
        // - SsnEncrypted / SsnLast4Hash: captured at patient creation
        // Any values sent for these keys are silently ignored.

        // ── Existing values must already be plaintext for EncryptEntity to be
        // safely re-applied at the end. The patient row may currently have
        // ciphertext blobs (per the encryption config), so decrypt first. After
        // applying the patient-supplied edits, EncryptEntity re-encrypts in
        // place.  IsEncrypted-guards inside Decrypt/EncryptEntity make these
        // calls safe even on rows that are partly plaintext or fully empty.
        // ──────────────────────────────────────────────────────────────
        _encryption.DecryptEntity(patient);

        // ── Empty/whitespace = "patient did not edit this field" ──────
        // Never blank out existing PHI from a blank submission. Required
        // identity fields (FirstName, LastName) are particularly sensitive —
        // a stray Save & Continue with empty fields previously WIPED real
        // names. If a patient genuinely needs to remove an optional field
        // (e.g. address), front desk handles that via the clinic form.
        // Spec: CRITICAL.md (PHI integrity).
        // ──────────────────────────────────────────────────────────────
        void Apply(Action<string> setter, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) setter(value);
        }

        Apply(v => patient.FirstName               = v, GetStr(payload, "firstName"));
        Apply(v => patient.LastName                = v, GetStr(payload, "lastName"));
        Apply(v => patient.Gender                  = v, GetStr(payload, "gender"));
        Apply(v => patient.Phone                   = v, GetStr(payload, "phone"));
        Apply(v => patient.Address                 = v, GetStr(payload, "address"));
        Apply(v => patient.City                    = v, GetStr(payload, "city"));
        Apply(v => patient.State                   = v, GetStr(payload, "state"));
        Apply(v => patient.ZipCode                 = v, GetStr(payload, "zipCode"));
        Apply(v => patient.EmergencyContactName    = v, GetStr(payload, "emergencyContactName"));
        Apply(v => patient.EmergencyContactRelation= v, GetStr(payload, "emergencyContactRelation"));
        Apply(v => patient.EmergencyContactPhone   = v, GetStr(payload, "emergencyContactPhone"));
        Apply(v => patient.EmergencyContactAltPhone= v, GetStr(payload, "emergencyContactAltPhone"));
        patient.UpdatedAt = DateTime.UtcNow;

        // ── Re-encrypt all configured PHI fields on the entity in one pass ──
        // Mirrors the clinic patient-form save path. EncryptEntity:
        //   • encrypts plaintext values for fields registered in EncryptionConfiguration
        //   • leaves already-ciphertext values untouched (IsEncrypted guard)
        //   • leaves unregistered fields alone (e.g. Gender)
        // Spec: CRITICAL.md ("PHI Fields Must Always Be Encrypted at Rest").
        // ──────────────────────────────────────────────────────────────
        _encryption.EncryptEntity(patient);
    }

    private async Task SaveConcernsAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        // --- Section-level free-text answers saved on PatientIntakeSubmission ---
        var lastFeltWell = GetStr(payload, "lastFeltWell");
        var whatTriggered = GetStr(payload, "whatTriggered");
        var betterFactors = GetStr(payload, "betterFactors");
        var worseFactors = GetStr(payload, "worseFactors");
        var additionalTimeline = GetStr(payload, "additionalTimeline");

        if (lastFeltWell != null || whatTriggered != null || betterFactors != null
            || worseFactors != null || additionalTimeline != null)
        {
            var submission = await _context.PatientIntakeSubmissions
                .FirstOrDefaultAsync(s => s.PatientIntakeSubmissionId == submissionId);
            if (submission != null)
            {
                if (lastFeltWell != null) submission.LastFeltWell = lastFeltWell;
                if (whatTriggered != null) submission.WhatTriggered = whatTriggered;
                if (betterFactors != null) submission.BetterFactors = betterFactors;
                if (worseFactors != null) submission.WorseFactors = worseFactors;
                if (additionalTimeline != null) submission.AdditionalTimeline = additionalTimeline;
                submission.UpdatedAt = DateTime.UtcNow;
            }
        }

        // --- Per-concern rows in PatientHealthConcerns ---
        var items = GetArray(payload, "items");
        if (items == null) return;

        // Soft-delete previous patient-entered concerns so wizard re-save replaces.
        var existing = await _context.PatientHealthConcerns
            .Where(c => c.PatientId == patientId && c.IsDeleted != true && c.Source == (int)IntakeSource.Patient)
            .ToListAsync();
        foreach (var ex in existing)
        {
            ex.IsDeleted = true;
            ex.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var item in items.Value.EnumerateArray())
        {
            var concern = GetStr(item, "concern");
            if (string.IsNullOrWhiteSpace(concern)) continue;

            var entity = new PatientHealthConcern
            {
                PatientId = patientId,
                TenantId = tenantId,
                Priority = GetInt(item, "priority") ?? 1,
                Concern = concern,
                Details = GetStr(item, "details"),
                Severity = GetInt(item, "severity"),
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientHealthConcerns.Add(entity);
        }
    }

    private async Task SaveMedicalHistoryAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        // Section 3 free-text: Cancer specify (type + year)
        var cancerSpecify = GetStr(payload, "cancerSpecify");
        if (cancerSpecify != null)
        {
            var submission = await _context.PatientIntakeSubmissions
                .FirstOrDefaultAsync(s => s.PatientIntakeSubmissionId == submissionId);
            if (submission != null)
            {
                submission.CancerSpecify = cancerSpecify;
                submission.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Problems: soft-delete existing patient-entered rows, reinsert from payload
        // (both checkbox-selected and manual entries arrive in the same "problems" array).
        var problems = GetArray(payload, "problems");
        if (problems != null)
        {
            var existingProblems = await _context.PatientProblems
                .Where(p => p.PatientId == patientId && p.Source == (int)IntakeSource.Patient)
                .ToListAsync();
            foreach (var old in existingProblems) _context.PatientProblems.Remove(old);

            foreach (var item in problems.Value.EnumerateArray())
            {
                var desc = GetStr(item, "description");
                if (string.IsNullOrWhiteSpace(desc)) continue;

                var entity = new PatientProblem
                {
                    PatientId = patientId,
                    TenantId = tenantId,
                    Description = desc,
                    IcdCode = GetStr(item, "icdCode"),
                    Status = GetInt(item, "status") ?? 0,
                    Notes = GetStr(item, "notes"),
                    CreatedAt = DateTime.UtcNow
                };
                _provenance.Tag(entity, IntakeSource.Patient, submissionId);
                _context.PatientProblems.Add(entity);
            }
        }

        // Family history: soft-delete existing patient-entered rows, reinsert from payload.
        var family = GetArray(payload, "family");
        if (family != null)
        {
            var existingFamily = await _context.PatientFamilyHistories
                .Where(f => f.PatientId == patientId && f.Source == (int)IntakeSource.Patient)
                .ToListAsync();
            foreach (var old in existingFamily) _context.PatientFamilyHistories.Remove(old);

            foreach (var item in family.Value.EnumerateArray())
            {
                var rel = GetStr(item, "relation");
                var cond = GetStr(item, "condition");
                // For checkbox-driven family, relation may be empty — treat as generic "family history of X".
                if (string.IsNullOrWhiteSpace(cond)) continue;

                var entity = new PatientFamilyHistory
                {
                    PatientId = patientId,
                    TenantId = tenantId,
                    Relation = string.IsNullOrWhiteSpace(rel) ? "Unspecified relative" : rel,
                    Condition = cond,
                    AgeAtOnset = GetInt(item, "ageAtOnset"),
                    Notes = GetStr(item, "notes"),
                    CreatedAt = DateTime.UtcNow
                };
                _provenance.Tag(entity, IntakeSource.Patient, submissionId);
                _context.PatientFamilyHistories.Add(entity);
            }
        }
    }

    private async Task SaveLifestyleAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        // 1. Persist the full structured JSON on PatientIntakeSubmission so the
        //    wizard can pre-fill checkboxes/bands on return visits.
        var submission = await _context.PatientIntakeSubmissions
            .FirstOrDefaultAsync(s => s.PatientIntakeSubmissionId == submissionId);
        if (submission != null)
        {
            submission.LifestyleData = payload.GetRawText();
            submission.UpdatedAt = DateTime.UtcNow;
        }

        // 2. Also write human-readable summaries to PatientSocialHistory so the
        //    Profile accordion and Encounter panel (which read from the clinical
        //    table) see the same data. Upsert one row per patient+category.
        var categorySummaries = BuildLifestyleSummaries(payload);
        foreach (var (cat, desc) in categorySummaries)
        {
            if (string.IsNullOrWhiteSpace(desc)) continue;

            var existing = await _context.PatientSocialHistories
                .FirstOrDefaultAsync(s => s.PatientId == patientId
                    && s.Category == cat
                    && s.Source == (int)IntakeSource.Patient);

            if (existing != null)
            {
                existing.Description = desc;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.IntakeSubmissionId = submissionId;
            }
            else
            {
                var entity = new PatientSocialHistory
                {
                    PatientId = patientId,
                    TenantId = tenantId,
                    Category = cat,
                    Description = desc,
                    CreatedAt = DateTime.UtcNow
                };
                _provenance.Tag(entity, IntakeSource.Patient, submissionId);
                _context.PatientSocialHistories.Add(entity);
            }
        }
    }

    /// <summary>
    /// Flatten the structured lifestyle payload into per-category human-readable
    /// strings for PatientSocialHistory. Unknown categories pass through as-is.
    /// </summary>
    private static IEnumerable<(string Category, string Description)> BuildLifestyleSummaries(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) yield break;

        // Categories and how to format them. Values may be strings, numbers, or arrays of strings.
        foreach (var prop in payload.EnumerateObject())
        {
            var parts = new List<string>();
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var sub in prop.Value.EnumerateObject())
                {
                    var s = FormatJsonValue(sub.Value);
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add($"{sub.Name}: {s}");
                }
            }
            else
            {
                var s = FormatJsonValue(prop.Value);
                if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
            }

            var desc = string.Join(" | ", parts);
            if (!string.IsNullOrWhiteSpace(desc)) yield return (prop.Name, desc);
        }
    }

    private static string FormatJsonValue(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: return el.GetString() ?? string.Empty;
            case JsonValueKind.Number: return el.ToString();
            case JsonValueKind.True: return "Yes";
            case JsonValueKind.False: return "No";
            case JsonValueKind.Array:
                var parts = new List<string>();
                foreach (var item in el.EnumerateArray())
                {
                    var s = FormatJsonValue(item);
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                }
                return string.Join(", ", parts);
            default: return string.Empty;
        }
    }

    private async Task SaveMedicationsAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        // Patient-level extras (persist across intake sessions).
        var drugReactionHistory = GetStr(payload, "drugReactionHistory");
        var primaryPharmacy = GetStr(payload, "primaryPharmacyInfo");
        var compoundingPharmacy = GetStr(payload, "compoundingPharmacyInfo");
        var healthcareTeam = GetStr(payload, "healthcareTeamNotes");

        if (drugReactionHistory != null || primaryPharmacy != null
            || compoundingPharmacy != null || healthcareTeam != null)
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId);
            if (patient != null)
            {
                if (drugReactionHistory != null) patient.DrugReactionHistory = drugReactionHistory;
                if (primaryPharmacy != null) patient.PrimaryPharmacyInfo = primaryPharmacy;
                if (compoundingPharmacy != null) patient.CompoundingPharmacyInfo = compoundingPharmacy;
                if (healthcareTeam != null) patient.HealthcareTeamNotes = healthcareTeam;
                patient.UpdatedAt = DateTime.UtcNow;
            }
        }

        var items = GetArray(payload, "items");
        if (items == null) { await Task.CompletedTask; return; }

        // Soft-delete (hard-delete actually, matching Allergies pattern) existing
        // patient-entered medications so re-save replaces cleanly.
        var existing = await _context.PatientMedications
            .Where(m => m.PatientId == patientId && m.Source == (int)IntakeSource.Patient)
            .ToListAsync();
        foreach (var old in existing) _context.PatientMedications.Remove(old);

        foreach (var item in items.Value.EnumerateArray())
        {
            var name = GetStr(item, "drugName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entity = new PatientMedication
            {
                PatientId = patientId,
                TenantId = tenantId,
                DrugName = name,
                Dosage = GetStr(item, "dosage"),
                Frequency = GetStr(item, "frequency"),
                Route = GetStr(item, "route"),
                Form = GetStr(item, "form"),
                Status = GetInt(item, "status") ?? 0,
                Notes = GetStr(item, "notes"),
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientMedications.Add(entity);
        }
    }

    private async Task SaveSupplementsAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        var items = GetArray(payload, "items");
        if (items == null) { await Task.CompletedTask; return; }

        // Soft-delete existing patient-entered supplements so re-save replaces cleanly.
        var existing = await _context.PatientSupplements
            .Where(s => s.PatientId == patientId && s.Source == (int)IntakeSource.Patient && s.IsDeleted != true)
            .ToListAsync();
        foreach (var old in existing)
        {
            old.IsDeleted = true;
            old.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var item in items.Value.EnumerateArray())
        {
            var name = GetStr(item, "supplementName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entity = new PatientSupplement
            {
                PatientId = patientId,
                TenantId = tenantId,
                SupplementName = name,
                Notes = GetStr(item, "notes"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientSupplements.Add(entity);
        }
    }

    private async Task SaveAllergiesAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        var items = GetArray(payload, "items");
        if (items == null) { await Task.CompletedTask; return; }

        foreach (var item in items.Value.EnumerateArray())
        {
            var name = GetStr(item, "allergenName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entity = new PatientAllergy
            {
                PatientId = patientId,
                TenantId = tenantId,
                AllergenName = name,
                Reaction = GetStr(item, "reaction"),
                Severity = GetInt(item, "severity") ?? 0,
                Type = GetInt(item, "type") ?? 0,
                Notes = GetStr(item, "notes"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientAllergies.Add(entity);
        }
    }

    private async Task SaveFamilyHistoryAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        var items = GetArray(payload, "items");
        if (items == null) { await Task.CompletedTask; return; }

        foreach (var item in items.Value.EnumerateArray())
        {
            var rel = GetStr(item, "relation");
            var cond = GetStr(item, "condition");
            if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(cond)) continue;

            var entity = new PatientFamilyHistory
            {
                PatientId = patientId,
                TenantId = tenantId,
                Relation = rel,
                Condition = cond,
                AgeAtOnset = GetInt(item, "ageAtOnset"),
                Notes = GetStr(item, "notes"),
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientFamilyHistories.Add(entity);
        }
    }

    private async Task SaveImmunizationsAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        var items = GetArray(payload, "items");
        if (items == null) { await Task.CompletedTask; return; }

        // Soft-delete existing patient-entered immunizations so checkbox re-save replaces.
        var existing = await _context.PatientImmunizations
            .Where(i => i.PatientId == patientId && i.Source == (int)IntakeSource.Patient)
            .ToListAsync();
        foreach (var old in existing) _context.PatientImmunizations.Remove(old);

        foreach (var item in items.Value.EnumerateArray())
        {
            var name = GetStr(item, "vaccineName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var dateStr = GetStr(item, "administeredDate");
            DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
            if (DateOnly.TryParse(dateStr, out var parsed)) date = parsed;

            var entity = new PatientImmunization
            {
                PatientId = patientId,
                TenantId = tenantId,
                VaccineName = name,
                CvxCode = GetStr(item, "cvxCode"),
                AdministeredDate = date,
                LotNumber = GetStr(item, "lotNumber"),
                Manufacturer = GetStr(item, "manufacturer"),
                Site = GetStr(item, "site"),
                Notes = GetStr(item, "notes"),
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientImmunizations.Add(entity);
        }
    }

    private async Task SaveLongevityAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        var existing = await _context.PatientLongevityProfiles
            .FirstOrDefaultAsync(l => l.PatientId == patientId);

        var symptomRatings = payload.TryGetProperty("symptomRatings", out var sr) ? sr.GetRawText() : null;
        var goals = payload.TryGetProperty("goals", out var g) ? g.GetRawText() : null;
        var priorTesting = payload.TryGetProperty("priorTesting", out var pt) ? pt.GetRawText() : null;
        var interventions = payload.TryGetProperty("currentInterventions", out var ci) ? ci.GetRawText() : null;
        var toxin = payload.TryGetProperty("toxinExposure", out var te) ? te.GetRawText() : null;
        var biomarker = GetStr(payload, "biomarkerGoals");
        var vision = GetStr(payload, "optimalHealthVision");
        var lastBloodPanel = ParseDateOnly(GetStr(payload, "lastBloodPanel"));
        var lastPhysical = ParseDateOnly(GetStr(payload, "lastPhysical"));

        if (existing == null)
        {
            var entity = new PatientLongevityProfile
            {
                PatientId = patientId,
                TenantId = tenantId,
                SymptomRatings = symptomRatings,
                Goals = goals,
                PriorTesting = priorTesting,
                CurrentInterventions = interventions,
                ToxinExposure = toxin,
                BiomarkerGoals = biomarker,
                OptimalHealthVision = vision,
                LastBloodPanelDate = lastBloodPanel,
                LastPhysicalDate = lastPhysical,
                CreatedAt = DateTime.UtcNow
            };
            _provenance.Tag(entity, IntakeSource.Patient, submissionId);
            _context.PatientLongevityProfiles.Add(entity);
        }
        else
        {
            if (symptomRatings != null) existing.SymptomRatings = symptomRatings;
            if (goals != null) existing.Goals = goals;
            if (priorTesting != null) existing.PriorTesting = priorTesting;
            if (interventions != null) existing.CurrentInterventions = interventions;
            if (toxin != null) existing.ToxinExposure = toxin;
            if (biomarker != null) existing.BiomarkerGoals = biomarker;
            if (vision != null) existing.OptimalHealthVision = vision;
            if (lastBloodPanel.HasValue) existing.LastBloodPanelDate = lastBloodPanel;
            if (lastPhysical.HasValue) existing.LastPhysicalDate = lastPhysical;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.IntakeSubmissionId = submissionId;
        }
    }

    private static DateOnly? ParseDateOnly(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateOnly.TryParse(s, out var d) ? d : null;
    }

    private async Task SaveGenderHealthAsync(int patientId, int tenantId, JsonElement payload, int submissionId)
    {
        // Upsert the single PatientGenderHealth row per patient. Store biological
        // sex as a discrete column (for later filtered queries) and the rest of
        // the structured answers as JSON — avoids sprawling nullable columns.
        var biologicalSex = GetStr(payload, "biologicalSex");
        var existing = await _context.PatientGenderHealths
            .FirstOrDefaultAsync(g => g.PatientId == patientId && g.IsDeleted != true);

        var dataJson = payload.GetRawText();

        if (existing == null)
        {
            var entity = new PatientGenderHealth
            {
                PatientId = patientId,
                TenantId = tenantId,
                BiologicalSex = biologicalSex,
                StructuredData = dataJson,
                Source = (int)IntakeSource.Patient,
                IntakeSubmissionId = submissionId,
                CreatedAt = DateTime.UtcNow
            };
            _context.PatientGenderHealths.Add(entity);
        }
        else
        {
            if (biologicalSex != null) existing.BiologicalSex = biologicalSex;
            existing.StructuredData = dataJson;
            existing.IntakeSubmissionId = submissionId;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }
}
