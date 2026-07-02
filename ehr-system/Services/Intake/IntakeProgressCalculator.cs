using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services.Intake;

/// <summary>
/// Computes "X of N sections complete" live from clinical tables + PatientIntakeSubmission.
/// Progress is NEVER stored (per Stock Problem). Recalculate on every call.
/// Section-complete definitions per rules/technical/patient-intake.md section 5.
/// </summary>
public interface IIntakeProgressCalculator
{
    Task<IntakeProgressDto> CalculateAsync(int patientId);
}

public class IntakeProgressCalculator : IIntakeProgressCalculator
{
    private readonly EhrDbContext _context;

    public IntakeProgressCalculator(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<IntakeProgressDto> CalculateAsync(int patientId)
    {
        var patient = await _context.Patients
            .Where(p => p.PatientId == patientId)
            .Select(p => new { p.PatientId, p.PreferredLocationId, p.DateOfBirth, p.Phone, p.Email })
            .FirstOrDefaultAsync();

        if (patient == null)
            return new IntakeProgressDto { Completed = 0, Total = 7 };

        // Most-recent submission drives SectionsTouched + SubmittedAt checks.
        var submission = await _context.PatientIntakeSubmissions
            .Where(s => s.PatientId == patientId && s.IsDeleted != true)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        var touched = ParseSectionsTouched(submission?.SectionsTouched);

        // Longevity intake section is gated by the patient's CURRENT-OR-NEXT
        // active appointment. Rule: if the patient has no active appointment →
        // hide the section. If they do, only show it when that appointment is
        // one of the two longevity types (10, 11).
        //
        // "Active" means Status IN (Scheduled, Confirmed, CheckedIn, InProgress).
        // We deliberately do NOT filter by StartTime > now — an InProgress visit
        // happening RIGHT NOW (StartTime an hour ago, Status=InProgress) still
        // counts as the patient's current visit context. Completed/Cancelled/
        // NoShow/Rescheduled/Missed are excluded by the Status filter alone.
        // (Stock Problem: compute the gate from the source records — Appointments
        // — not from a stored boolean flag on Location.)
        var nextAppointmentType = await _context.Appointments
            .Where(a => a.PatientId == patientId
                && (a.Status == (int)AppointmentStatus.Scheduled
                    || a.Status == (int)AppointmentStatus.Confirmed
                    || a.Status == (int)AppointmentStatus.CheckedIn
                    || a.Status == (int)AppointmentStatus.InProgress))
            .OrderBy(a => a.StartTime)
            .Select(a => (int?)a.Type)
            .FirstOrDefaultAsync();

        var longevityEnabled = nextAppointmentType.HasValue
            && (nextAppointmentType.Value == (int)AppointmentType.NewLongevityPatient
                || nextAppointmentType.Value == (int)AppointmentType.FollowUpLongevityPatient);

        var intakeSubmissionIdFilter = submission?.PatientIntakeSubmissionId;

        // --- Section evaluations ---
        var sections = new List<IntakeSectionStatusDto>();

        // 1. Demographics: DOB + (phone OR email)
        var hasPhoneOrEmail = !string.IsNullOrWhiteSpace(patient.Phone) || !string.IsNullOrWhiteSpace(patient.Email);
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "demographics",
            Filled = patient.DateOfBirth != default && hasPhoneOrEmail,
            Timestamp = submission?.UpdatedAt ?? submission?.StartedAt
        });

        // 2. Health Concerns: >= 1 row
        var concernCount = await _context.PatientHealthConcerns
            .CountAsync(c => c.PatientId == patientId && c.IsDeleted != true);
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "concerns",
            Filled = concernCount >= 1,
            Timestamp = null
        });

        // 3. Medical History: touched OR any patient-entered problem/allergy/family row
        var hasMedHistoryRow = await _context.PatientProblems
                .AnyAsync(p => p.PatientId == patientId && p.Source == (int)IntakeSource.Patient)
            || await _context.PatientAllergies
                .AnyAsync(a => a.PatientId == patientId && a.Source == (int)IntakeSource.Patient)
            || await _context.PatientFamilyHistories
                .AnyAsync(f => f.PatientId == patientId && f.Source == (int)IntakeSource.Patient);
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "medical-history",
            Filled = touched.Contains("medical-history") || hasMedHistoryRow,
            Timestamp = null
        });

        // 4. Lifestyle: >= 3 social-history categories from patient
        var lifestyleCategories = await _context.PatientSocialHistories
            .Where(s => s.PatientId == patientId && s.Source == (int)IntakeSource.Patient && !string.IsNullOrEmpty(s.Category))
            .Select(s => s.Category)
            .Distinct()
            .CountAsync();
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "lifestyle",
            Filled = lifestyleCategories >= 3,
            Timestamp = null
        });

        // 5. Medications & Supplements: touched is enough (zero meds valid)
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "medications",
            Filled = touched.Contains("medications") || touched.Contains("supplements"),
            Timestamp = null
        });

        // 6. Gender Health: touched is enough
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "gender-health",
            Filled = touched.Contains("gender-health"),
            Timestamp = null
        });

        // 7. Review & Finish (always present) — completed only when SubmittedAt set
        sections.Add(new IntakeSectionStatusDto
        {
            Name = "review",
            Filled = submission?.SubmittedAt != null,
            Timestamp = submission?.SubmittedAt
        });

        // 8. Longevity (only if flag on). Inserted before "review" for UI ordering.
        if (longevityEnabled)
        {
            var longevity = await _context.PatientLongevityProfiles
                .Where(l => l.PatientId == patientId && l.IsDeleted != true)
                .Select(l => new { l.SymptomRatings, l.Goals, l.UpdatedAt, l.CreatedAt })
                .FirstOrDefaultAsync();

            var longevityFilled = longevity != null
                && (!string.IsNullOrWhiteSpace(longevity.SymptomRatings)
                    || !string.IsNullOrWhiteSpace(longevity.Goals));

            // Insert just before review
            sections.Insert(sections.Count - 1, new IntakeSectionStatusDto
            {
                Name = "longevity",
                Filled = longevityFilled,
                Timestamp = longevity?.UpdatedAt ?? longevity?.CreatedAt
            });
        }

        return new IntakeProgressDto
        {
            Total = sections.Count,
            Completed = sections.Count(s => s.Filled),
            Sections = sections,
            CurrentSubmissionId = intakeSubmissionIdFilter,
            SubmittedAt = submission?.SubmittedAt
        };
    }

    private static HashSet<string> ParseSectionsTouched(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            return new HashSet<string>(arr ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
