using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Helpers;

/// <summary>
/// WHY:           Compute patient intake form status for a set of patients in one query, so any
///                endpoint serving a patient list can include it on its DTO.
///                Stock Problem: status is NEVER stored, always computed from PatientIntakeSubmission.
/// WHO CALLS ME:  AppointmentService.GetAppointmentsAsync (single enrichment point — feeds Dashboard,
///                CareEpisode list, and Calendar in one shot).
///                Future: any service that returns a patient list and wants to show the chip.
/// WHAT I RETURN: Dictionary keyed by patientId, value = IntakeStatusDto.
/// HOW TO HIRE:   _intakeStatus.GetForPatientsAsync(patientIds)
/// SPEC:          rules/technical/intake-status-indicator.md
/// </summary>
public interface IIntakeStatusHelper
{
    Task<Dictionary<int, IntakeStatusDto>> GetForPatientsAsync(IEnumerable<int> patientIds);
}

public class IntakeStatusHelper : IIntakeStatusHelper
{
    private readonly EhrDbContext _context;

    public IntakeStatusHelper(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<int, IntakeStatusDto>> GetForPatientsAsync(IEnumerable<int> patientIds)
    {
        var ids = patientIds?.Distinct().ToList() ?? new List<int>();
        var result = new Dictionary<int, IntakeStatusDto>();
        if (ids.Count == 0) return result;

        // Pull only the columns we need. One query for all patients in scope.
        var rows = await _context.PatientIntakeSubmissions
            .Where(s => ids.Contains(s.PatientId) && s.IsDeleted != true)
            .Select(s => new
            {
                s.PatientId,
                s.SubmittedAt,
                s.StartedAt,
                s.UpdatedAt
            })
            .ToListAsync();

        // Latest submission per patient wins (newest StartedAt).
        var latestPerPatient = rows
            .GroupBy(r => r.PatientId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.StartedAt).First()
            );

        foreach (var pid in ids)
        {
            if (!latestPerPatient.TryGetValue(pid, out var latest))
            {
                result[pid] = new IntakeStatusDto
                {
                    Status = (int)IntakeStatusValue.NotSubmitted,
                    SubmittedAt = null,
                    LastEditedAt = null
                };
                continue;
            }

            var status = latest.SubmittedAt.HasValue
                ? IntakeStatusValue.Submitted
                : IntakeStatusValue.InProgress;

            result[pid] = new IntakeStatusDto
            {
                Status = (int)status,
                SubmittedAt = latest.SubmittedAt,
                LastEditedAt = latest.UpdatedAt ?? latest.StartedAt
            };
        }

        return result;
    }
}

/// <summary>
/// 0 = Not Submitted (no submission row exists for this patient).
/// 1 = In Progress (latest submission has SubmittedAt = null).
/// 2 = Submitted (latest submission has SubmittedAt set).
/// </summary>
public enum IntakeStatusValue
{
    NotSubmitted = 0,
    InProgress = 1,
    Submitted = 2
}

public class IntakeStatusDto
{
    /// <summary>0=NotSubmitted, 1=InProgress, 2=Submitted. See IntakeStatusValue.</summary>
    public int Status { get; set; }

    /// <summary>UTC timestamp when patient submitted. Null until submitted.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>UTC timestamp of last update to the submission row, fallback to StartedAt.</summary>
    public DateTime? LastEditedAt { get; set; }
}
