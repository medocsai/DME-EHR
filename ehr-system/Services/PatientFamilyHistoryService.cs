using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientFamilyHistoryService
{
    Task<List<PatientFamilyHistoryDto>> GetByPatientAsync(int patientId);
    Task<PatientFamilyHistory> CreateAsync(int patientId, PatientFamilyHistoryCreateDto dto, int userId);
    Task<PatientFamilyHistory?> UpdateAsync(int id, PatientFamilyHistoryCreateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class PatientFamilyHistoryService : IPatientFamilyHistoryService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientFamilyHistoryService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientFamilyHistoryDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientFamilyHistories
            .Where(f => f.TenantId == tenantId && f.PatientId == patientId)
            .OrderBy(f => f.Relation)
            .ThenBy(f => f.Condition)
            .Select(f => new PatientFamilyHistoryDto
            {
                PatientFamilyHistoryId = f.PatientFamilyHistoryId,
                PatientId = f.PatientId,
                EncounterId = f.EncounterId,
                Relation = f.Relation,
                Condition = f.Condition,
                AgeAtOnset = f.AgeAtOnset,
                IsDeceased = f.IsDeceased,
                Notes = f.Notes,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientFamilyHistory> CreateAsync(int patientId, PatientFamilyHistoryCreateDto dto, int userId)
    {
        var history = new PatientFamilyHistory
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            Relation = dto.Relation,
            Condition = dto.Condition,
            AgeAtOnset = dto.AgeAtOnset,
            IsDeceased = dto.IsDeceased,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientFamilyHistories.Add(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<PatientFamilyHistory?> UpdateAsync(int id, PatientFamilyHistoryCreateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var history = await _context.PatientFamilyHistories.FirstOrDefaultAsync(f => f.PatientFamilyHistoryId == id && f.TenantId == tenantId);
        if (history == null) return null;

        if (dto.Relation != null) history.Relation = dto.Relation;
        if (dto.Condition != null) history.Condition = dto.Condition;
        if (dto.AgeAtOnset.HasValue) history.AgeAtOnset = dto.AgeAtOnset;
        if (dto.IsDeceased.HasValue) history.IsDeceased = dto.IsDeceased;
        if (dto.Notes != null) history.Notes = dto.Notes;

        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft-delete only. Per rules/technical/history-review-soft-delete.md,
        // patient history rows are never hard-deleted.
        var tenantId = _tenantProvider.TenantId ?? 0;
        var history = await _context.PatientFamilyHistories.FirstOrDefaultAsync(f => f.PatientFamilyHistoryId == id && f.TenantId == tenantId);
        if (history == null) return false;
        history.IsDeleted = true;
        history.DeletedAt = DateTime.UtcNow;
        history.DeletedReason = "Deleted via legacy endpoint (no reason captured)";
        history.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
