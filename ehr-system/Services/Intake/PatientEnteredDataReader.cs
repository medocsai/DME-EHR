using System.Text.Json;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services.Intake;

/// <summary>
/// Reads patient-entered rows (Source=Patient) across clinical tables and returns
/// a grouped view for the Profile accordion and Encounter right-panel.
/// </summary>
public interface IPatientEnteredDataReader
{
    Task<PatientIntakeViewDto> GetIntakeViewAsync(int patientId);
}

public class PatientEnteredDataReader : IPatientEnteredDataReader
{
    private readonly EhrDbContext _context;
    private readonly IIntakeProgressCalculator _progress;

    public PatientEnteredDataReader(EhrDbContext context, IIntakeProgressCalculator progress)
    {
        _context = context;
        _progress = progress;
    }

    public async Task<PatientIntakeViewDto> GetIntakeViewAsync(int patientId)
    {
        var src = (int)IntakeSource.Patient;
        var view = new PatientIntakeViewDto { PatientId = patientId };

        // Pull the latest open submission once. We use it for several section-level
        // free-text answers (concerns context, cancer specify, lifestyle JSON).
        var submission = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        // Pull patient-level intake fields once (medications context columns).
        var patientLevel = await _context.Patients
            .Where(p => p.PatientId == patientId)
            .Select(p => new
            {
                p.DrugReactionHistory,
                p.PrimaryPharmacyInfo,
                p.CompoundingPharmacyInfo,
                p.HealthcareTeamNotes
            })
            .FirstOrDefaultAsync();

        // -------- Health Concerns --------
        // Per-concern rows + 5 section-level free-text answers (When last felt well,
        // What triggered, What makes better, What makes worse, Additional timeline).
        var concerns = await _context.PatientHealthConcerns
            .Where(c => c.PatientId == patientId && c.IsDeleted != true)
            .OrderBy(c => c.Priority)
            .Select(c => new IntakeSectionItemDto
            {
                Table = "PatientHealthConcerns",
                Id = c.PatientHealthConcernId,
                Label = c.Concern,
                Detail = c.Details,
                SavedAt = c.UpdatedAt ?? c.CreatedAt
            })
            .ToListAsync();
        AddIfPresent(concerns, submission?.LastFeltWell, "When last felt well", submission?.UpdatedAt ?? submission?.StartedAt);
        AddIfPresent(concerns, submission?.WhatTriggered, "What triggered", submission?.UpdatedAt ?? submission?.StartedAt);
        AddIfPresent(concerns, submission?.BetterFactors, "Makes symptoms better", submission?.UpdatedAt ?? submission?.StartedAt);
        AddIfPresent(concerns, submission?.WorseFactors, "Makes symptoms worse", submission?.UpdatedAt ?? submission?.StartedAt);
        AddIfPresent(concerns, submission?.AdditionalTimeline, "Additional history & timeline", submission?.UpdatedAt ?? submission?.StartedAt);
        view.Sections.Add(new IntakeSectionViewDto
        {
            Name = "concerns",
            Items = concerns,
            LastSavedAt = concerns.Max(i => i.SavedAt)
        });

        // Medications (patient-entered). Detail shows the patient's Notes
        // (what they actually wrote) — fall back to dosage/frequency if blank.
        var meds = await _context.PatientMedications
            .Where(m => m.PatientId == patientId && m.Source == src)
            .OrderByDescending(m => m.UpdatedAt ?? m.CreatedAt)
            .Select(m => new IntakeSectionItemDto
            {
                Table = "PatientMedications",
                Id = m.PatientMedicationId,
                Label = m.DrugName,
                Detail = !string.IsNullOrWhiteSpace(m.Notes)
                    ? m.Notes
                    : $"{m.Dosage} {m.Frequency}".Trim(),
                SavedAt = m.UpdatedAt ?? m.CreatedAt
            })
            .ToListAsync();
        // Patient-level medications context (drug reactions, pharmacies, healthcare team).
        AddIfPresent(meds, patientLevel?.DrugReactionHistory, "Drug reaction / adverse history", null);
        AddIfPresent(meds, patientLevel?.PrimaryPharmacyInfo, "Primary pharmacy", null);
        AddIfPresent(meds, patientLevel?.CompoundingPharmacyInfo, "Compounding / specialty pharmacy", null);
        AddIfPresent(meds, patientLevel?.HealthcareTeamNotes, "Current healthcare team", null);
        view.Sections.Add(new IntakeSectionViewDto { Name = "medications", Items = meds, LastSavedAt = meds.Max(i => i.SavedAt) });

        // Supplements
        var supps = await _context.PatientSupplements
            .Where(s => s.PatientId == patientId && s.IsDeleted != true)
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Select(s => new IntakeSectionItemDto
            {
                Table = "PatientSupplements",
                Id = s.PatientSupplementId,
                Label = s.SupplementName,
                Detail = s.Notes,
                SavedAt = s.UpdatedAt ?? s.CreatedAt
            })
            .ToListAsync();
        view.Sections.Add(new IntakeSectionViewDto { Name = "supplements", Items = supps, LastSavedAt = supps.Max(i => i.SavedAt) });

        // Allergies (patient-entered). Show Notes if present, else Reaction.
        var allergies = await _context.PatientAllergies
            .Where(a => a.PatientId == patientId && a.Source == src)
            .OrderByDescending(a => a.UpdatedAt ?? a.CreatedAt)
            .Select(a => new IntakeSectionItemDto
            {
                Table = "PatientAllergies",
                Id = a.PatientAllergyId,
                Label = a.AllergenName,
                Detail = !string.IsNullOrWhiteSpace(a.Notes) ? a.Notes : a.Reaction,
                SavedAt = a.UpdatedAt ?? a.CreatedAt
            })
            .ToListAsync();
        view.Sections.Add(new IntakeSectionViewDto { Name = "allergies", Items = allergies, LastSavedAt = allergies.Max(i => i.SavedAt) });

        // Problems / Medical History (patient-entered). Show Notes first
        // (checkbox + notes pattern puts the detail here); fall back to IcdCode.
        var problems = await _context.PatientProblems
            .Where(p => p.PatientId == patientId && p.Source == src)
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .Select(p => new IntakeSectionItemDto
            {
                Table = "PatientProblems",
                Id = p.PatientProblemId,
                Label = p.Description,
                Detail = !string.IsNullOrWhiteSpace(p.Notes) ? p.Notes : p.IcdCode,
                SavedAt = p.UpdatedAt ?? p.CreatedAt
            })
            .ToListAsync();
        AddIfPresent(problems, submission?.CancerSpecify, "Cancer specify", submission?.UpdatedAt ?? submission?.StartedAt);
        view.Sections.Add(new IntakeSectionViewDto { Name = "medical-history", Items = problems, LastSavedAt = problems.Max(i => i.SavedAt) });

        // Family History (patient-entered)
        var family = await _context.PatientFamilyHistories
            .Where(f => f.PatientId == patientId && f.Source == src)
            .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
            .Select(f => new IntakeSectionItemDto
            {
                Table = "PatientFamilyHistories",
                Id = f.PatientFamilyHistoryId,
                Label = $"{f.Relation}: {f.Condition}",
                Detail = f.Notes,
                SavedAt = f.UpdatedAt ?? f.CreatedAt
            })
            .ToListAsync();
        view.Sections.Add(new IntakeSectionViewDto { Name = "family-history", Items = family, LastSavedAt = family.Max(i => i.SavedAt) });

        // Lifestyle / Social (patient-entered)
        var social = await _context.PatientSocialHistories
            .Where(s => s.PatientId == patientId && s.Source == src)
            .OrderBy(s => s.Category)
            .Select(s => new IntakeSectionItemDto
            {
                Table = "PatientSocialHistories",
                Id = s.PatientSocialHistoryId,
                Label = s.Category,
                Detail = s.Description,
                SavedAt = s.UpdatedAt ?? s.CreatedAt
            })
            .ToListAsync();
        view.Sections.Add(new IntakeSectionViewDto { Name = "lifestyle", Items = social, LastSavedAt = social.Max(i => i.SavedAt) });

        // Immunizations (patient-entered). Show Notes if present, else the date.
        var imms = await _context.PatientImmunizations
            .Where(i => i.PatientId == patientId && i.Source == src)
            .OrderByDescending(i => i.AdministeredDate)
            .Select(i => new IntakeSectionItemDto
            {
                Table = "PatientImmunizations",
                Id = i.PatientImmunizationId,
                Label = i.VaccineName,
                Detail = !string.IsNullOrWhiteSpace(i.Notes)
                    ? i.Notes
                    : i.AdministeredDate.ToString(),
                SavedAt = i.CreatedAt
            })
            .ToListAsync();
        view.Sections.Add(new IntakeSectionViewDto { Name = "immunizations", Items = imms, LastSavedAt = imms.Max(i => i.SavedAt) });

        // -------- Longevity (full content, not just a placeholder) --------
        var longevity = await _context.PatientLongevityProfiles
            .Where(l => l.PatientId == patientId && l.IsDeleted != true)
            .FirstOrDefaultAsync();
        if (longevity != null)
        {
            var longSavedAt = longevity.UpdatedAt ?? longevity.CreatedAt;
            var longItems = new List<IntakeSectionItemDto>();

            // Symptom ratings: render as "Brain Fog: 7" lines
            var ratings = ParseJsonObject(longevity.SymptomRatings);
            if (ratings != null)
            {
                foreach (var prop in ratings.Value.EnumerateObject())
                {
                    longItems.Add(new IntakeSectionItemDto
                    {
                        Table = "PatientLongevityProfiles",
                        Id = longevity.PatientLongevityProfileId,
                        Label = $"Symptom: {Humanize(prop.Name)}",
                        Detail = prop.Value.ToString(),
                        SavedAt = longSavedAt
                    });
                }
            }

            AddJsonArray(longItems, longevity.Goals, "Goal", longevity.PatientLongevityProfileId, longSavedAt);
            AddJsonArray(longItems, longevity.PriorTesting, "Prior testing", longevity.PatientLongevityProfileId, longSavedAt);
            AddJsonArray(longItems, longevity.CurrentInterventions, "Current intervention", longevity.PatientLongevityProfileId, longSavedAt);
            AddJsonArray(longItems, longevity.ToxinExposure, "Toxin exposure", longevity.PatientLongevityProfileId, longSavedAt);

            AddIfPresent(longItems, longevity.BiomarkerGoals, "Biomarker goals", longSavedAt, longevity.PatientLongevityProfileId, "PatientLongevityProfiles");
            AddIfPresent(longItems, longevity.OptimalHealthVision, "Optimal health vision (5-10 yr)", longSavedAt, longevity.PatientLongevityProfileId, "PatientLongevityProfiles");
            AddIfPresent(longItems, longevity.LastBloodPanelDate?.ToString("yyyy-MM-dd"), "Last comprehensive blood panel", longSavedAt, longevity.PatientLongevityProfileId, "PatientLongevityProfiles");
            AddIfPresent(longItems, longevity.LastPhysicalDate?.ToString("yyyy-MM-dd"), "Last full physical exam", longSavedAt, longevity.PatientLongevityProfileId, "PatientLongevityProfiles");

            view.Sections.Add(new IntakeSectionViewDto
            {
                Name = "longevity",
                Items = longItems,
                LastSavedAt = longSavedAt
            });
        }

        // -------- Gender Health (new section) --------
        var genderHealth = await _context.PatientGenderHealths
            .FirstOrDefaultAsync(g => g.PatientId == patientId && g.IsDeleted != true);
        if (genderHealth != null)
        {
            var ghSavedAt = genderHealth.UpdatedAt ?? genderHealth.CreatedAt;
            var ghItems = new List<IntakeSectionItemDto>();
            AddIfPresent(ghItems, genderHealth.BiologicalSex, "Biological sex", ghSavedAt, genderHealth.PatientGenderHealthId, "PatientGenderHealths");

            // Unpack the structured JSON into readable rows.
            var data = ParseJsonObject(genderHealth.StructuredData);
            if (data != null)
            {
                FlattenGenderHealth(ghItems, data.Value, "women", "Women", genderHealth.PatientGenderHealthId, ghSavedAt);
                FlattenGenderHealth(ghItems, data.Value, "men", "Men", genderHealth.PatientGenderHealthId, ghSavedAt);
                FlattenGenderHealth(ghItems, data.Value, "sexual", "Sexual health", genderHealth.PatientGenderHealthId, ghSavedAt);
            }

            view.Sections.Add(new IntakeSectionViewDto
            {
                Name = "gender-health",
                Items = ghItems,
                LastSavedAt = ghSavedAt
            });
        }

        view.Progress = await _progress.CalculateAsync(patientId);
        return view;
    }

    // ---- helpers ----

    private static void AddIfPresent(List<IntakeSectionItemDto> items, string? value, string label, DateTime? savedAt,
        int id = 0, string table = "PatientIntakeSubmissions")
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        items.Add(new IntakeSectionItemDto
        {
            Table = table,
            Id = id,
            Label = label,
            Detail = value,
            SavedAt = savedAt
        });
    }

    private static JsonElement? ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private static void AddJsonArray(List<IntakeSectionItemDto> items, string? json, string labelPrefix, int id, DateTime? savedAt)
    {
        var el = ParseJsonObject(json);
        if (el == null || el.Value.ValueKind != JsonValueKind.Array) return;
        foreach (var item in el.Value.EnumerateArray())
        {
            var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (string.IsNullOrWhiteSpace(s)) continue;
            items.Add(new IntakeSectionItemDto
            {
                Table = "PatientLongevityProfiles",
                Id = id,
                Label = labelPrefix,
                Detail = s,
                SavedAt = savedAt
            });
        }
    }

    private static void FlattenGenderHealth(List<IntakeSectionItemDto> items, JsonElement root, string key, string prefix, int id, DateTime? savedAt)
    {
        if (!root.TryGetProperty(key, out var sub)) return;
        if (sub.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in sub.EnumerateObject())
        {
            var v = FormatJsonValue(prop.Value);
            if (string.IsNullOrWhiteSpace(v)) continue;
            items.Add(new IntakeSectionItemDto
            {
                Table = "PatientGenderHealths",
                Id = id,
                Label = $"{prefix}: {Humanize(prop.Name)}",
                Detail = v,
                SavedAt = savedAt
            });
        }
    }

    private static string FormatJsonValue(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: return el.GetString() ?? "";
            case JsonValueKind.Number: return el.ToString();
            case JsonValueKind.True: return "Yes";
            case JsonValueKind.False: return "No";
            case JsonValueKind.Array:
                var parts = new List<string>();
                foreach (var x in el.EnumerateArray())
                {
                    var s = FormatJsonValue(x);
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                }
                return string.Join(", ", parts);
            default: return "";
        }
    }

    private static string Humanize(string camel)
    {
        if (string.IsNullOrEmpty(camel)) return camel;
        var sb = new System.Text.StringBuilder();
        sb.Append(char.ToUpper(camel[0]));
        for (int i = 1; i < camel.Length; i++)
        {
            var c = camel[i];
            if (char.IsUpper(c)) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
