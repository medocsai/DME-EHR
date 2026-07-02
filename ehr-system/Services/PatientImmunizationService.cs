using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientImmunizationService
{
    Task<List<PatientImmunizationDto>> GetByPatientAsync(int patientId);
    Task<PatientImmunization> CreateAsync(int patientId, PatientImmunizationCreateDto dto, int userId);
    Task<PatientImmunization?> UpdateAsync(int id, PatientImmunizationCreateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class PatientImmunizationService : IPatientImmunizationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientImmunizationService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientImmunizationDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientImmunizations
            .Include(i => i.AdministeredByProvider)
            .Where(i => i.TenantId == tenantId && i.PatientId == patientId)
            .OrderByDescending(i => i.AdministeredDate)
            .Select(i => new PatientImmunizationDto
            {
                PatientImmunizationId = i.PatientImmunizationId,
                PatientId = i.PatientId,
                EncounterId = i.EncounterId,
                VaccineName = i.VaccineName,
                CvxCode = i.CvxCode,
                AdministeredDate = i.AdministeredDate,
                LotNumber = i.LotNumber,
                Manufacturer = i.Manufacturer,
                Site = i.Site,
                AdministeredByProviderName = i.AdministeredByProvider != null
                    ? i.AdministeredByProvider.FirstName + " " + i.AdministeredByProvider.LastName
                    : null,
                Notes = i.Notes,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PatientImmunization> CreateAsync(int patientId, PatientImmunizationCreateDto dto, int userId)
    {
        var immunization = new PatientImmunization
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            VaccineName = dto.VaccineName,
            CvxCode = dto.CvxCode,
            AdministeredDate = dto.AdministeredDate,
            LotNumber = dto.LotNumber,
            Manufacturer = dto.Manufacturer,
            Site = dto.Site,
            AdministeredByProviderId = dto.AdministeredByProviderId,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.PatientImmunizations.Add(immunization);
        await _context.SaveChangesAsync();
        return immunization;
    }

    public async Task<PatientImmunization?> UpdateAsync(int id, PatientImmunizationCreateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var immunization = await _context.PatientImmunizations.FirstOrDefaultAsync(i => i.PatientImmunizationId == id && i.TenantId == tenantId);
        if (immunization == null) return null;

        immunization.VaccineName = dto.VaccineName;
        immunization.CvxCode = dto.CvxCode;
        immunization.AdministeredDate = dto.AdministeredDate;
        immunization.LotNumber = dto.LotNumber;
        immunization.Manufacturer = dto.Manufacturer;
        immunization.Site = dto.Site;
        immunization.Notes = dto.Notes;

        await _context.SaveChangesAsync();
        return immunization;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // Soft-delete only. Per rules/technical/history-review-soft-delete.md,
        // patient history rows are never hard-deleted.
        var tenantId = _tenantProvider.TenantId ?? 0;
        var immunization = await _context.PatientImmunizations.FirstOrDefaultAsync(i => i.PatientImmunizationId == id && i.TenantId == tenantId);
        if (immunization == null) return false;
        immunization.IsDeleted = true;
        immunization.DeletedAt = DateTime.UtcNow;
        immunization.DeletedReason = "Deleted via legacy endpoint (no reason captured)";
        await _context.SaveChangesAsync();
        return true;
    }
}
