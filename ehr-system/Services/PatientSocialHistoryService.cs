using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientSocialHistoryService
{
    Task<List<PatientSocialHistoryDto>> GetByPatientAsync(int patientId);
    Task<PatientSocialHistory> CreateAsync(int patientId, PatientSocialHistoryCreateDto dto, int userId);
    Task<PatientSocialHistory?> UpdateAsync(int id, PatientSocialHistoryCreateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class PatientSocialHistoryService : IPatientSocialHistoryService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientSocialHistoryService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientSocialHistoryDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientSocialHistories
            .Where(s => s.TenantId == tenantId && s.PatientId == patientId)
            .OrderBy(s => s.Category)
            .Select(s => new PatientSocialHistoryDto
            {
                PatientSocialHistoryId = s.PatientSocialHistoryId,
                PatientId = s.PatientId,
                EncounterId = s.EncounterId,
                Category = s.Category,
                Description = s.Description,
                Status = s.Status,
                Notes = s.Notes,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientSocialHistory> CreateAsync(int patientId, PatientSocialHistoryCreateDto dto, int userId)
    {
        var history = new PatientSocialHistory
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            Category = dto.Category,
            Description = dto.Description,
            Status = dto.Status,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientSocialHistories.Add(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<PatientSocialHistory?> UpdateAsync(int id, PatientSocialHistoryCreateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var history = await _context.PatientSocialHistories.FirstOrDefaultAsync(s => s.PatientSocialHistoryId == id && s.TenantId == tenantId);
        if (history == null) return null;

        if (dto.Category != null) history.Category = dto.Category;
        if (dto.Description != null) history.Description = dto.Description;
        if (dto.Status != null) history.Status = dto.Status;
        if (dto.Notes != null) history.Notes = dto.Notes;

        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft-delete only. Per rules/technical/history-review-soft-delete.md,
        // patient history rows are never hard-deleted.
        var tenantId = _tenantProvider.TenantId ?? 0;
        var history = await _context.PatientSocialHistories.FirstOrDefaultAsync(s => s.PatientSocialHistoryId == id && s.TenantId == tenantId);
        if (history == null) return false;
        history.IsDeleted = true;
        history.DeletedAt = DateTime.UtcNow;
        history.DeletedReason = "Deleted via legacy endpoint (no reason captured)";
        history.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
