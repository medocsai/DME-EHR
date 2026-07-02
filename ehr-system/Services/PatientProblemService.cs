using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientProblemService
{
    Task<List<PatientProblemDto>> GetByPatientAsync(int patientId);
    Task<PatientProblem> CreateAsync(int patientId, PatientProblemCreateDto dto, int userId);
    Task<PatientProblem?> UpdateAsync(int id, PatientProblemUpdateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class PatientProblemService : IPatientProblemService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientProblemService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientProblemDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientProblems
            .Where(p => p.TenantId == tenantId && p.PatientId == patientId)
            .OrderByDescending(p => p.Status == 0) // Active first
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new PatientProblemDto
            {
                PatientProblemId = p.PatientProblemId,
                PatientId = p.PatientId,
                EncounterId = p.EncounterId,
                IcdCode = p.IcdCode,
                Description = p.Description,
                Status = p.Status,
                StatusName = p.Status == 0 ? "Active" : p.Status == 1 ? "Resolved" : "Inactive",
                OnsetDate = p.OnsetDate,
                ResolvedDate = p.ResolvedDate,
                Notes = p.Notes,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientProblem> CreateAsync(int patientId, PatientProblemCreateDto dto, int userId)
    {
        var problem = new PatientProblem
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            IcdCode = dto.IcdCode,
            Description = dto.Description,
            Status = 0, // Active
            OnsetDate = dto.OnsetDate,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientProblems.Add(problem);
        await _context.SaveChangesAsync();
        return problem;
    }

    public async Task<PatientProblem?> UpdateAsync(int id, PatientProblemUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var problem = await _context.PatientProblems.FirstOrDefaultAsync(p => p.PatientProblemId == id && p.TenantId == tenantId);
        if (problem == null) return null;

        if (dto.IcdCode != null) problem.IcdCode = dto.IcdCode;
        if (dto.Description != null) problem.Description = dto.Description;
        if (dto.Status.HasValue) problem.Status = dto.Status.Value;
        if (dto.OnsetDate.HasValue) problem.OnsetDate = dto.OnsetDate;
        if (dto.ResolvedDate.HasValue) problem.ResolvedDate = dto.ResolvedDate;
        if (dto.Notes != null) problem.Notes = dto.Notes;
        problem.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return problem;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft-delete only. Per rules/technical/history-review-soft-delete.md,
        // patient history rows are never hard-deleted.
        var tenantId = _tenantProvider.TenantId ?? 0;
        var problem = await _context.PatientProblems.FirstOrDefaultAsync(p => p.PatientProblemId == id && p.TenantId == tenantId);
        if (problem == null) return false;
        problem.IsDeleted = true;
        problem.DeletedAt = DateTime.UtcNow;
        problem.DeletedReason = "Deleted via legacy endpoint (no reason captured)";
        problem.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
