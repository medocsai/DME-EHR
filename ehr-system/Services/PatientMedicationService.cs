using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientMedicationService
{
    Task<List<PatientMedicationDto>> GetByPatientAsync(int patientId);
    Task<PatientMedication> CreateAsync(int patientId, PatientMedicationCreateDto dto, int userId);
    Task<PatientMedication?> UpdateAsync(int id, PatientMedicationUpdateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class PatientMedicationService : IPatientMedicationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientMedicationService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientMedicationDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientMedications
            .Include(m => m.PrescribedByProvider)
            .Where(m => m.TenantId == tenantId && m.PatientId == patientId)
            .OrderByDescending(m => m.Status == 0) // Active first
            .ThenBy(m => m.DrugName)
            .Select(m => new PatientMedicationDto
            {
                PatientMedicationId = m.PatientMedicationId,
                PatientId = m.PatientId,
                EncounterId = m.EncounterId,
                DrugName = m.DrugName,
                Dosage = m.Dosage,
                Form = m.Form,
                Route = m.Route,
                Frequency = m.Frequency,
                Status = m.Status,
                StatusName = m.Status == 0 ? "Active" : m.Status == 1 ? "Discontinued" : m.Status == 2 ? "On Hold" : "Completed",
                StartDate = m.StartDate,
                EndDate = m.EndDate,
                PrescribedByProviderId = m.PrescribedByProviderId,
                PrescribedByProviderName = m.PrescribedByProvider != null
                    ? m.PrescribedByProvider.FirstName + " " + m.PrescribedByProvider.LastName
                    : null,
                Notes = m.Notes,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientMedication> CreateAsync(int patientId, PatientMedicationCreateDto dto, int userId)
    {
        var medication = new PatientMedication
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            DrugName = dto.DrugName,
            Dosage = dto.Dosage,
            Form = dto.Form,
            Route = dto.Route,
            Frequency = dto.Frequency,
            Status = 0, // Active
            StartDate = dto.StartDate,
            PrescribedByProviderId = dto.PrescribedByProviderId,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientMedications.Add(medication);
        await _context.SaveChangesAsync();
        return medication;
    }

    public async Task<PatientMedication?> UpdateAsync(int id, PatientMedicationUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var medication = await _context.PatientMedications.FirstOrDefaultAsync(m => m.PatientMedicationId == id && m.TenantId == tenantId);
        if (medication == null) return null;

        if (dto.DrugName != null) medication.DrugName = dto.DrugName;
        if (dto.Dosage != null) medication.Dosage = dto.Dosage;
        if (dto.Form != null) medication.Form = dto.Form;
        if (dto.Route != null) medication.Route = dto.Route;
        if (dto.Frequency != null) medication.Frequency = dto.Frequency;
        if (dto.Status.HasValue) medication.Status = dto.Status.Value;
        if (dto.StartDate.HasValue) medication.StartDate = dto.StartDate;
        if (dto.EndDate.HasValue) medication.EndDate = dto.EndDate;
        if (dto.Notes != null) medication.Notes = dto.Notes;
        medication.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return medication;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft-delete only. Per rules/technical/history-review-soft-delete.md,
        // patient history rows are never hard-deleted. Reason captured by
        // legacy callers is generic; new UI uses HistoryReviewActionService
        // which captures a real user-supplied reason.
        var tenantId = _tenantProvider.TenantId ?? 0;
        var medication = await _context.PatientMedications.FirstOrDefaultAsync(m => m.PatientMedicationId == id && m.TenantId == tenantId);
        if (medication == null) return false;
        medication.IsDeleted = true;
        medication.DeletedAt = DateTime.UtcNow;
        medication.DeletedReason = "Deleted via legacy endpoint (no reason captured)";
        medication.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
