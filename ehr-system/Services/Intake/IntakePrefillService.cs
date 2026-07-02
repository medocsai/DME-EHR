using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services.Intake;

/// <summary>
/// Returns pre-fill data for any intake section, by name, for a given patient.
/// Used by both the portal flow (authed patient via JWT) and the tablet flow
/// (anonymous + verify cookie). Both controllers resolve the patientId in
/// their own way and then call this service.
///
/// Section dispatch mirrors IIntakeSubmissionService.SaveSectionAsync — same
/// switch-by-name pattern keeps add/save/prefill symmetrical.
/// </summary>
public interface IIntakePrefillService
{
    /// <summary>
    /// Returns a section-specific anonymous object suitable for direct JSON
    /// serialization to the client. Throws ArgumentException for unknown
    /// section names.
    /// </summary>
    Task<object> GetAsync(string sectionName, int patientId);
}

public class IntakePrefillService : IIntakePrefillService
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryption;

    public IntakePrefillService(EhrDbContext context, EncryptionHelper encryption)
    {
        _context = context;
        _encryption = encryption;
    }

    public Task<object> GetAsync(string sectionName, int patientId)
    {
        var name = (sectionName ?? string.Empty).Trim().ToLowerInvariant();
        return name switch
        {
            "demographics"     => GetDemographicsAsync(patientId),
            "concerns"         => GetConcernsAsync(patientId),
            "medical-history"  => GetMedicalHistoryAsync(patientId),
            "lifestyle"        => GetLifestyleAsync(patientId),
            "medications"      => GetMedicationsAsync(patientId),
            "gender-health"    => GetGenderHealthAsync(patientId),
            "longevity"        => GetLongevityAsync(patientId),
            _ => throw new ArgumentException($"Unknown intake prefill section: {sectionName}", nameof(sectionName))
        };
    }

    // ============================================================
    // Demographics
    // ============================================================
    private async Task<object> GetDemographicsAsync(int patientId)
    {
        var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == patientId);
        if (p == null) return new { };

        // Decrypt all configured PHI fields on the entity in-place.
        // Same pattern AppointmentService / patient list views use.
        // EncryptionHelper.IsEncrypted() guard means plaintext rows pass through
        // unchanged — handles mixed legacy / encrypted data safely.
        // Spec: rules/CRITICAL.md (PHI encryption is mandatory).
        _encryption.DecryptEntity(p);

        // SSN last-4: SsnEncrypted is the cipher-text column name. Decrypt then
        // pull the last 4 digits for masked display.
        string ssnLast4 = null;
        if (!string.IsNullOrEmpty(p.SsnEncrypted))
        {
            try
            {
                var ssn = _encryption.Decrypt(p.SsnEncrypted) ?? string.Empty;
                var digits = new string(ssn.Where(char.IsDigit).ToArray());
                if (digits.Length >= 4) ssnLast4 = digits[^4..];
            }
            catch { ssnLast4 = null; }
        }

        return new
        {
            firstName = p.FirstName,
            lastName = p.LastName,
            dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"),
            gender = p.Gender,
            email = p.Email,
            ssnLast4 = ssnLast4,
            phone = p.Phone,
            address = p.Address,
            city = p.City,
            state = p.State,
            zipCode = p.ZipCode,
            emergencyContactName = p.EmergencyContactName,
            emergencyContactRelation = p.EmergencyContactRelation,
            emergencyContactPhone = p.EmergencyContactPhone,
            emergencyContactAltPhone = p.EmergencyContactAltPhone
        };
    }

    // ============================================================
    // Health Concerns
    // ============================================================
    private async Task<object> GetConcernsAsync(int patientId)
    {
        var items = await _context.PatientHealthConcerns
            .Where(c => c.PatientId == patientId && c.IsDeleted != true)
            .OrderBy(c => c.Priority)
            .Select(c => new { priority = c.Priority, concern = c.Concern, details = c.Details })
            .ToListAsync();

        var sub = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new
            {
                s.LastFeltWell,
                s.WhatTriggered,
                s.BetterFactors,
                s.WorseFactors,
                s.AdditionalTimeline
            })
            .FirstOrDefaultAsync();

        return new
        {
            items,
            lastFeltWell = sub?.LastFeltWell,
            whatTriggered = sub?.WhatTriggered,
            betterFactors = sub?.BetterFactors,
            worseFactors = sub?.WorseFactors,
            additionalTimeline = sub?.AdditionalTimeline
        };
    }

    // ============================================================
    // Medical History
    // ============================================================
    private async Task<object> GetMedicalHistoryAsync(int patientId)
    {
        var src = (int)IntakeSource.Patient;

        var problems = await _context.PatientProblems
            .Where(p => p.PatientId == patientId && p.Source == src)
            .Select(p => new { description = p.Description, notes = p.Notes })
            .ToListAsync();

        var family = await _context.PatientFamilyHistories
            .Where(f => f.PatientId == patientId && f.Source == src)
            .Select(f => new { relation = f.Relation, condition = f.Condition, notes = f.Notes })
            .ToListAsync();

        var immunizations = await _context.PatientImmunizations
            .Where(i => i.PatientId == patientId && i.Source == src)
            .Select(i => new { vaccineName = i.VaccineName, administeredDate = i.AdministeredDate, notes = i.Notes })
            .ToListAsync();

        var allergies = await _context.PatientAllergies
            .Where(a => a.PatientId == patientId && a.Source == src)
            .Select(a => new { allergenName = a.AllergenName, notes = a.Notes })
            .ToListAsync();

        var cancerSpecify = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.CancerSpecify)
            .FirstOrDefaultAsync();

        return new
        {
            problems,
            family,
            immunizations,
            allergies,
            cancerSpecify
        };
    }

    // ============================================================
    // Lifestyle
    // ============================================================
    private async Task<object> GetLifestyleAsync(int patientId)
    {
        var json = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.LifestyleData)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(json)) return new { };

        try
        {
            // Return parsed JSON so the client receives an object directly
            // (matches the legacy Content(json, "application/json") behavior).
            return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        }
        catch
        {
            return new { };
        }
    }

    // ============================================================
    // Medications & Supplements
    // ============================================================
    private async Task<object> GetMedicationsAsync(int patientId)
    {
        var src = (int)IntakeSource.Patient;

        var meds = await _context.PatientMedications
            .Where(m => m.PatientId == patientId && m.Source == src)
            .Select(m => new { drugName = m.DrugName, notes = m.Notes })
            .ToListAsync();

        var supps = await _context.PatientSupplements
            .Where(s => s.PatientId == patientId && s.Source == src && s.IsDeleted != true)
            .Select(s => new { supplementName = s.SupplementName, notes = s.Notes })
            .ToListAsync();

        var patient = await _context.Patients
            .Where(p => p.PatientId == patientId)
            .Select(p => new
            {
                p.DrugReactionHistory,
                p.PrimaryPharmacyInfo,
                p.CompoundingPharmacyInfo,
                p.HealthcareTeamNotes
            })
            .FirstOrDefaultAsync();

        return new
        {
            medications = meds,
            supplements = supps,
            drugReactionHistory = patient?.DrugReactionHistory,
            primaryPharmacyInfo = patient?.PrimaryPharmacyInfo,
            compoundingPharmacyInfo = patient?.CompoundingPharmacyInfo,
            healthcareTeamNotes = patient?.HealthcareTeamNotes
        };
    }

    // ============================================================
    // Gender Health
    // ============================================================
    private async Task<object> GetGenderHealthAsync(int patientId)
    {
        var gh = await _context.PatientGenderHealths
            .FirstOrDefaultAsync(g => g.PatientId == patientId && g.IsDeleted != true);

        var demoGender = await _context.Patients
            .Where(p => p.PatientId == patientId)
            .Select(p => p.Gender)
            .FirstOrDefaultAsync();

        object parsed = null;
        if (gh != null && !string.IsNullOrWhiteSpace(gh.StructuredData))
        {
            try { parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(gh.StructuredData); }
            catch { parsed = null; }
        }

        return new
        {
            biologicalSex = gh?.BiologicalSex,
            demographicsGender = demoGender,
            structuredData = parsed
        };
    }

    // ============================================================
    // Longevity
    // ============================================================
    private async Task<object> GetLongevityAsync(int patientId)
    {
        var p = await _context.PatientLongevityProfiles
            .FirstOrDefaultAsync(l => l.PatientId == patientId && l.IsDeleted != true);
        if (p == null) return new { };

        object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json); }
            catch { return null; }
        }

        return new
        {
            symptomRatings = Parse(p.SymptomRatings),
            goals = Parse(p.Goals),
            priorTesting = Parse(p.PriorTesting),
            currentInterventions = Parse(p.CurrentInterventions),
            toxinExposure = Parse(p.ToxinExposure),
            biomarkerGoals = p.BiomarkerGoals,
            optimalHealthVision = p.OptimalHealthVision,
            lastBloodPanel = p.LastBloodPanelDate?.ToString("yyyy-MM-dd"),
            lastPhysical = p.LastPhysicalDate?.ToString("yyyy-MM-dd")
        };
    }
}
