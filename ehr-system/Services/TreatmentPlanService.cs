using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface ITreatmentPlanService
{
    Task<List<TreatmentPlanDto>> GetByPatientAsync(int patientId);
    Task<TreatmentPlan> CreateAsync(int patientId, TreatmentPlanCreateDto dto, int userId);
    Task<TreatmentPlan?> UpdateAsync(int id, TreatmentPlanUpdateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class TreatmentPlanService : ITreatmentPlanService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public TreatmentPlanService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<TreatmentPlanDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.TreatmentPlans
            .Include(t => t.Provider)
            .Where(t => t.TenantId == tenantId && t.PatientId == patientId)
            .OrderByDescending(t => t.Status == 0) // Active first
            .ThenBy(t => t.ConditionName)
            .Select(t => new TreatmentPlanDto
            {
                TreatmentPlanId = t.TreatmentPlanId,
                PatientId = t.PatientId,
                ProviderId = t.ProviderId,
                ProviderName = t.Provider != null
                    ? t.Provider.FirstName + " " + t.Provider.LastName
                    : null,
                ConditionName = t.ConditionName,
                IcdCode = t.IcdCode,
                Goals = t.Goals,
                FollowUpIntervalDays = t.FollowUpIntervalDays,
                NextFollowUp = t.NextFollowUp,
                Status = t.Status,
                StatusName = t.Status == 0 ? "Active" : t.Status == 1 ? "Completed" : t.Status == 2 ? "On Hold" : "Cancelled",
                Notes = t.Notes,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<TreatmentPlan> CreateAsync(int patientId, TreatmentPlanCreateDto dto, int userId)
    {
        var plan = new TreatmentPlan
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            ProviderId = dto.ProviderId,
            ConditionName = dto.ConditionName,
            IcdCode = dto.IcdCode,
            Goals = dto.Goals,
            FollowUpIntervalDays = dto.FollowUpIntervalDays,
            NextFollowUp = dto.NextFollowUp,
            Status = 0, // Active
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.TreatmentPlans.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }

    public async Task<TreatmentPlan?> UpdateAsync(int id, TreatmentPlanUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var plan = await _context.TreatmentPlans.FirstOrDefaultAsync(t => t.TreatmentPlanId == id && t.TenantId == tenantId);
        if (plan == null) return null;

        if (dto.ConditionName != null) plan.ConditionName = dto.ConditionName;
        if (dto.IcdCode != null) plan.IcdCode = dto.IcdCode;
        if (dto.Goals != null) plan.Goals = dto.Goals;
        if (dto.FollowUpIntervalDays.HasValue) plan.FollowUpIntervalDays = dto.FollowUpIntervalDays;
        if (dto.NextFollowUp.HasValue) plan.NextFollowUp = dto.NextFollowUp;
        if (dto.Status.HasValue) plan.Status = dto.Status.Value;
        if (dto.Notes != null) plan.Notes = dto.Notes;
        plan.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return plan;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var plan = await _context.TreatmentPlans.FirstOrDefaultAsync(t => t.TreatmentPlanId == id && t.TenantId == tenantId);
        if (plan == null) return false;
        _context.TreatmentPlans.Remove(plan);
        await _context.SaveChangesAsync();
        return true;
    }
}
