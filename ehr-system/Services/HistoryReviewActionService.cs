using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EHR.Services;

/// <summary>
/// Centralised service for state-change and soft-delete actions on the 7
/// History Review tables (Allergies, Medications, Problems, Family History,
/// Social History, Immunization, Supplements).
///
/// Spec: rules/technical/history-review-soft-delete.md (sections 6, 8, 9).
///
/// Every method:
///   1. Loads the entity (auto-filtered to non-deleted by EF global filter)
///   2. Snapshots the row to JSON (before)
///   3. Mutates the relevant fields per the action mapping
///   4. Snapshots the row to JSON (after)
///   5. Saves the entity
///   6. Writes one AuditLog row with old/new snapshots and the reason text
///
/// Reason text is required on every method; empty/whitespace will throw.
/// Returns true on success, false if entity not found / not in this tenant.
/// </summary>
public interface IHistoryReviewActionService
{
    // Allergies
    Task<bool> InactivateAllergyAsync(int id, string reason, int currentUserId);
    Task<bool> ReactivateAllergyAsync(int id, string reason, int currentUserId);
    Task<bool> DeleteAllergyAsync(int id, string reason, int currentUserId);

    // Medications
    Task<bool> DiscontinueMedicationAsync(int id, string reason, int currentUserId);
    Task<bool> RevertDiscontinueMedicationAsync(int id, string reason, int currentUserId);
    Task<bool> MarkMedicationOnHoldAsync(int id, string reason, int currentUserId);
    Task<bool> ResumeMedicationFromHoldAsync(int id, string reason, int currentUserId);
    Task<bool> CompleteMedicationAsync(int id, string reason, int currentUserId);
    Task<bool> DeleteMedicationAsync(int id, string reason, int currentUserId);

    // Problems
    Task<bool> MarkProblemResolvedAsync(int id, string reason, int currentUserId);
    Task<bool> MarkProblemInactiveAsync(int id, string reason, int currentUserId);
    Task<bool> ReactivateProblemAsync(int id, string reason, int currentUserId);
    Task<bool> DeleteProblemAsync(int id, string reason, int currentUserId);

    // Supplements
    Task<bool> InactivateSupplementAsync(int id, string reason, int currentUserId);
    Task<bool> ReactivateSupplementAsync(int id, string reason, int currentUserId);
    Task<bool> DeleteSupplementAsync(int id, string reason, int currentUserId);

    // Delete-only sections
    Task<bool> DeleteFamilyHistoryAsync(int id, string reason, int currentUserId);
    Task<bool> DeleteSocialHistoryAsync(int id, string reason, int currentUserId);
    Task<bool> DeleteImmunizationAsync(int id, string reason, int currentUserId);
}

public class HistoryReviewActionService : IHistoryReviewActionService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    public HistoryReviewActionService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    // ============================================================
    // ALLERGIES
    // ============================================================

    public Task<bool> InactivateAllergyAsync(int id, string reason, int currentUserId) =>
        _ChangeAllergyAsync(id, reason, currentUserId, "Inactivate", a =>
        {
            a.IsActive = false;
            a.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> ReactivateAllergyAsync(int id, string reason, int currentUserId) =>
        _ChangeAllergyAsync(id, reason, currentUserId, "Reactivate", a =>
        {
            a.IsActive = true;
            a.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> DeleteAllergyAsync(int id, string reason, int currentUserId) =>
        _ChangeAllergyAsync(id, reason, currentUserId, "Delete", a =>
        {
            a.IsDeleted = true;
            a.DeletedAt = DateTime.UtcNow;
            a.DeletedByUserId = currentUserId;
            a.DeletedReason = reason;
        });

    private async Task<bool> _ChangeAllergyAsync(int id, string reason, int currentUserId, string action, Action<PatientAllergy> mutate)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;

        // For Reactivate/Delete we may need rows that are inactive (not deleted, just IsActive=false).
        // EF query filter only hides IsDeleted=true, so plain query is fine.
        var entity = await _context.PatientAllergies
            .FirstOrDefaultAsync(a => a.PatientAllergyId == id && a.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_AllergySnapshot(entity), JsonOpts);
        mutate(entity);
        var after = JsonSerializer.Serialize(_AllergySnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientAllergy", entity.PatientAllergyId, action, before, after, reason, currentUserId);
        return true;
    }

    // ============================================================
    // MEDICATIONS
    // ============================================================

    public Task<bool> DiscontinueMedicationAsync(int id, string reason, int currentUserId) =>
        _ChangeMedicationAsync(id, reason, currentUserId, "Discontinue", m =>
        {
            m.Status = 1; // Discontinued
            m.EndDate = DateOnly.FromDateTime(DateTime.UtcNow);
            m.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> RevertDiscontinueMedicationAsync(int id, string reason, int currentUserId) =>
        _ChangeMedicationAsync(id, reason, currentUserId, "RevertDiscontinue", m =>
        {
            m.Status = 0; // Active
            m.EndDate = null;
            m.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> MarkMedicationOnHoldAsync(int id, string reason, int currentUserId) =>
        _ChangeMedicationAsync(id, reason, currentUserId, "MarkOnHold", m =>
        {
            m.Status = 2; // OnHold
            m.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> ResumeMedicationFromHoldAsync(int id, string reason, int currentUserId) =>
        _ChangeMedicationAsync(id, reason, currentUserId, "ResumeFromHold", m =>
        {
            m.Status = 0; // Active
            m.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> CompleteMedicationAsync(int id, string reason, int currentUserId) =>
        _ChangeMedicationAsync(id, reason, currentUserId, "Complete", m =>
        {
            m.Status = 3; // Completed
            m.EndDate = DateOnly.FromDateTime(DateTime.UtcNow);
            m.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> DeleteMedicationAsync(int id, string reason, int currentUserId) =>
        _ChangeMedicationAsync(id, reason, currentUserId, "Delete", m =>
        {
            m.IsDeleted = true;
            m.DeletedAt = DateTime.UtcNow;
            m.DeletedByUserId = currentUserId;
            m.DeletedReason = reason;
        });

    private async Task<bool> _ChangeMedicationAsync(int id, string reason, int currentUserId, string action, Action<PatientMedication> mutate)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;

        var entity = await _context.PatientMedications
            .FirstOrDefaultAsync(m => m.PatientMedicationId == id && m.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_MedicationSnapshot(entity), JsonOpts);
        mutate(entity);
        var after = JsonSerializer.Serialize(_MedicationSnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientMedication", entity.PatientMedicationId, action, before, after, reason, currentUserId);
        return true;
    }

    // ============================================================
    // PROBLEMS
    // ============================================================

    public Task<bool> MarkProblemResolvedAsync(int id, string reason, int currentUserId) =>
        _ChangeProblemAsync(id, reason, currentUserId, "MarkResolved", p =>
        {
            p.Status = 1; // Resolved
            p.ResolvedDate = DateOnly.FromDateTime(DateTime.UtcNow);
            p.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> MarkProblemInactiveAsync(int id, string reason, int currentUserId) =>
        _ChangeProblemAsync(id, reason, currentUserId, "MarkInactive", p =>
        {
            p.Status = 2; // Inactive
            p.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> ReactivateProblemAsync(int id, string reason, int currentUserId) =>
        _ChangeProblemAsync(id, reason, currentUserId, "Reactivate", p =>
        {
            p.Status = 0; // Active
            p.ResolvedDate = null;
            p.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> DeleteProblemAsync(int id, string reason, int currentUserId) =>
        _ChangeProblemAsync(id, reason, currentUserId, "Delete", p =>
        {
            p.IsDeleted = true;
            p.DeletedAt = DateTime.UtcNow;
            p.DeletedByUserId = currentUserId;
            p.DeletedReason = reason;
        });

    private async Task<bool> _ChangeProblemAsync(int id, string reason, int currentUserId, string action, Action<PatientProblem> mutate)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;

        var entity = await _context.PatientProblems
            .FirstOrDefaultAsync(p => p.PatientProblemId == id && p.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_ProblemSnapshot(entity), JsonOpts);
        mutate(entity);
        var after = JsonSerializer.Serialize(_ProblemSnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientProblem", entity.PatientProblemId, action, before, after, reason, currentUserId);
        return true;
    }

    // ============================================================
    // SUPPLEMENTS
    // ============================================================

    public Task<bool> InactivateSupplementAsync(int id, string reason, int currentUserId) =>
        _ChangeSupplementAsync(id, reason, currentUserId, "Inactivate", s =>
        {
            s.IsActive = false;
            s.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> ReactivateSupplementAsync(int id, string reason, int currentUserId) =>
        _ChangeSupplementAsync(id, reason, currentUserId, "Reactivate", s =>
        {
            s.IsActive = true;
            s.UpdatedAt = DateTime.UtcNow;
        });

    public Task<bool> DeleteSupplementAsync(int id, string reason, int currentUserId) =>
        _ChangeSupplementAsync(id, reason, currentUserId, "Delete", s =>
        {
            s.IsDeleted = true;
            s.DeletedAt = DateTime.UtcNow;
            s.DeletedByUserId = currentUserId;
            s.DeletedReason = reason;
        });

    private async Task<bool> _ChangeSupplementAsync(int id, string reason, int currentUserId, string action, Action<PatientSupplement> mutate)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;

        var entity = await _context.PatientSupplements
            .FirstOrDefaultAsync(s => s.PatientSupplementId == id && s.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_SupplementSnapshot(entity), JsonOpts);
        mutate(entity);
        var after = JsonSerializer.Serialize(_SupplementSnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientSupplement", entity.PatientSupplementId, action, before, after, reason, currentUserId);
        return true;
    }

    // ============================================================
    // DELETE-ONLY SECTIONS
    // ============================================================

    public async Task<bool> DeleteFamilyHistoryAsync(int id, string reason, int currentUserId)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;
        var entity = await _context.PatientFamilyHistories
            .FirstOrDefaultAsync(f => f.PatientFamilyHistoryId == id && f.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_FamilyHistorySnapshot(entity), JsonOpts);
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedByUserId = currentUserId;
        entity.DeletedReason = reason;
        entity.UpdatedAt = DateTime.UtcNow;
        var after = JsonSerializer.Serialize(_FamilyHistorySnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientFamilyHistory", entity.PatientFamilyHistoryId, "Delete", before, after, reason, currentUserId);
        return true;
    }

    public async Task<bool> DeleteSocialHistoryAsync(int id, string reason, int currentUserId)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;
        var entity = await _context.PatientSocialHistories
            .FirstOrDefaultAsync(s => s.PatientSocialHistoryId == id && s.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_SocialHistorySnapshot(entity), JsonOpts);
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedByUserId = currentUserId;
        entity.DeletedReason = reason;
        entity.UpdatedAt = DateTime.UtcNow;
        var after = JsonSerializer.Serialize(_SocialHistorySnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientSocialHistory", entity.PatientSocialHistoryId, "Delete", before, after, reason, currentUserId);
        return true;
    }

    public async Task<bool> DeleteImmunizationAsync(int id, string reason, int currentUserId)
    {
        _RequireReason(reason);
        var tenantId = _tenantProvider.TenantId ?? 0;
        var entity = await _context.PatientImmunizations
            .FirstOrDefaultAsync(i => i.PatientImmunizationId == id && i.TenantId == tenantId);
        if (entity == null) return false;

        var before = JsonSerializer.Serialize(_ImmunizationSnapshot(entity), JsonOpts);
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedByUserId = currentUserId;
        entity.DeletedReason = reason;
        var after = JsonSerializer.Serialize(_ImmunizationSnapshot(entity), JsonOpts);

        await _context.SaveChangesAsync();
        await _WriteAuditAsync("PatientImmunization", entity.PatientImmunizationId, "Delete", before, after, reason, currentUserId);
        return true;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static void _RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required for all History Review actions.", nameof(reason));
    }

    /// <summary>
    /// Writes a single AuditLog row using raw SQL (mirrors AuditService.LogAccessAsync
    /// pattern: avoids flushing other tracked entities in the shared DbContext).
    /// Stores the reason text in the Changes column.
    /// </summary>
    private async Task _WriteAuditAsync(string entityType, int entityId, string action, string? oldJson, string? newJson, string reason, int userId)
    {
        var tenantId = _tenantProvider.TenantId;
        var timestamp = DateTime.UtcNow;
        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO AuditLogs (TenantId, UserId, UserEmail, Action, EntityType, EntityId, OldValues, NewValues, Changes, IpAddress, Timestamp)
                VALUES ({tenantId}, {userId}, {(string?)null}, {action}, {entityType}, {entityId}, {oldJson}, {newJson}, {reason}, {"HistoryReviewAction"}, {timestamp})");
        }
        catch (Exception ex)
        {
            var snapshot = new
            {
                TenantId = tenantId,
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValues = oldJson,
                NewValues = newJson,
                Reason = reason,
                Timestamp = timestamp
            };
            Console.Error.WriteLine($"AUDIT LOG FAILURE (HistoryReviewAction): {JsonSerializer.Serialize(snapshot)} - Error: {ex.Message}");
        }
    }

    // Snapshot helpers — capture the fields that matter for recovery / auditing.
    // Avoid serialising navigation properties (would pull half the DB).

    private static object _AllergySnapshot(PatientAllergy a) => new
    {
        a.PatientAllergyId, a.TenantId, a.PatientId, a.EncounterId,
        a.AllergenName, a.Type, a.Reaction, a.Severity, a.OnsetDate,
        a.IsActive, a.Notes, a.CreatedByUserId, a.CreatedAt, a.UpdatedAt,
        a.Source, a.IntakeSubmissionId,
        a.IsDeleted, a.DeletedAt, a.DeletedByUserId, a.DeletedReason
    };

    private static object _MedicationSnapshot(PatientMedication m) => new
    {
        m.PatientMedicationId, m.TenantId, m.PatientId, m.EncounterId,
        m.DrugName, m.Dosage, m.Form, m.Route, m.Frequency,
        m.Status, m.StartDate, m.EndDate,
        m.PrescribedByProviderId, m.Notes, m.CreatedByUserId, m.CreatedAt, m.UpdatedAt,
        m.Source, m.IntakeSubmissionId,
        m.IsDeleted, m.DeletedAt, m.DeletedByUserId, m.DeletedReason
    };

    private static object _ProblemSnapshot(PatientProblem p) => new
    {
        p.PatientProblemId, p.TenantId, p.PatientId, p.EncounterId,
        p.IcdCode, p.Description, p.Status, p.OnsetDate, p.ResolvedDate,
        p.Notes, p.CreatedByUserId, p.CreatedAt, p.UpdatedAt,
        p.Source, p.IntakeSubmissionId,
        p.IsDeleted, p.DeletedAt, p.DeletedByUserId, p.DeletedReason
    };

    private static object _FamilyHistorySnapshot(PatientFamilyHistory f) => new
    {
        f.PatientFamilyHistoryId, f.TenantId, f.PatientId, f.EncounterId,
        f.Relation, f.Condition, f.AgeAtOnset, f.IsDeceased,
        f.Notes, f.CreatedByUserId, f.CreatedAt, f.UpdatedAt,
        f.Source, f.IntakeSubmissionId,
        f.IsDeleted, f.DeletedAt, f.DeletedByUserId, f.DeletedReason
    };

    private static object _SocialHistorySnapshot(PatientSocialHistory s) => new
    {
        s.PatientSocialHistoryId, s.TenantId, s.PatientId, s.EncounterId,
        s.Category, s.Description, s.Status, s.Notes,
        s.CreatedByUserId, s.CreatedAt, s.UpdatedAt,
        s.Source, s.IntakeSubmissionId,
        s.IsDeleted, s.DeletedAt, s.DeletedByUserId, s.DeletedReason
    };

    private static object _ImmunizationSnapshot(PatientImmunization i) => new
    {
        i.PatientImmunizationId, i.TenantId, i.PatientId, i.EncounterId,
        i.VaccineName, i.CvxCode, i.AdministeredDate,
        i.LotNumber, i.Manufacturer, i.Site,
        i.AdministeredByProviderId, i.Notes, i.CreatedByUserId, i.CreatedAt,
        i.Source, i.IntakeSubmissionId,
        i.IsDeleted, i.DeletedAt, i.DeletedByUserId, i.DeletedReason
    };

    private static object _SupplementSnapshot(PatientSupplement s) => new
    {
        s.PatientSupplementId, s.TenantId, s.PatientId,
        s.SupplementName, s.Notes, s.IsActive,
        s.Source, s.IntakeSubmissionId, s.CreatedAt, s.UpdatedAt,
        s.IsDeleted, s.DeletedAt, s.DeletedByUserId, s.DeletedReason
    };
}
