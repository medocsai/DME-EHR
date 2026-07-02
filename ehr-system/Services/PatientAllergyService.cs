using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientAllergyService
{
    Task<List<PatientAllergyDto>> GetByPatientAsync(int patientId);
    Task<PatientAllergy> CreateAsync(int patientId, PatientAllergyCreateDto dto, int userId);
    Task<PatientAllergy?> UpdateAsync(int id, PatientAllergyUpdateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class PatientAllergyService : IPatientAllergyService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientAllergyService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientAllergyDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientAllergies
            .Where(a => a.TenantId == tenantId && a.PatientId == patientId)
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.Severity)
            .ThenBy(a => a.AllergenName)
            .Select(a => new PatientAllergyDto
            {
                PatientAllergyId = a.PatientAllergyId,
                PatientId = a.PatientId,
                EncounterId = a.EncounterId,
                AllergenName = a.AllergenName,
                Type = a.Type,
                TypeName = a.Type == 0 ? "Drug" : a.Type == 1 ? "Food" : a.Type == 2 ? "Environmental" : "Other",
                Reaction = a.Reaction,
                Severity = a.Severity,
                SeverityName = a.Severity == 0 ? "Mild" : a.Severity == 1 ? "Moderate" : a.Severity == 2 ? "Severe" : "Life-Threatening",
                OnsetDate = a.OnsetDate,
                IsActive = a.IsActive,
                Notes = a.Notes,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientAllergy> CreateAsync(int patientId, PatientAllergyCreateDto dto, int userId)
    {
        var allergy = new PatientAllergy
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            AllergenName = dto.AllergenName,
            Type = dto.Type,
            Reaction = dto.Reaction,
            Severity = dto.Severity,
            IsActive = true,
            OnsetDate = dto.OnsetDate,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientAllergies.Add(allergy);
        await _context.SaveChangesAsync();
        return allergy;
    }

    public async Task<PatientAllergy?> UpdateAsync(int id, PatientAllergyUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var allergy = await _context.PatientAllergies.FirstOrDefaultAsync(a => a.PatientAllergyId == id && a.TenantId == tenantId);
        if (allergy == null) return null;

        if (dto.AllergenName != null) allergy.AllergenName = dto.AllergenName;
        if (dto.Type.HasValue) allergy.Type = dto.Type.Value;
        if (dto.Reaction != null) allergy.Reaction = dto.Reaction;
        if (dto.Severity.HasValue) allergy.Severity = dto.Severity.Value;
        if (dto.IsActive.HasValue) allergy.IsActive = dto.IsActive.Value;
        if (dto.OnsetDate.HasValue) allergy.OnsetDate = dto.OnsetDate;
        if (dto.Notes != null) allergy.Notes = dto.Notes;
        allergy.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return allergy;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft-delete only. Per rules/technical/history-review-soft-delete.md,
        // patient history rows are never hard-deleted. Reason captured by
        // legacy callers is generic; new UI uses HistoryReviewActionService
        // which captures a real user-supplied reason.
        var tenantId = _tenantProvider.TenantId ?? 0;
        var allergy = await _context.PatientAllergies.FirstOrDefaultAsync(a => a.PatientAllergyId == id && a.TenantId == tenantId);
        if (allergy == null) return false;
        allergy.IsDeleted = true;
        allergy.DeletedAt = DateTime.UtcNow;
        allergy.DeletedReason = "Deleted via legacy endpoint (no reason captured)";
        allergy.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
