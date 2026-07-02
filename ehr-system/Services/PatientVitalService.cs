using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPatientVitalService
{
    Task<List<PatientVitalDto>> GetByPatientAsync(int patientId);
    Task<PatientVital> CreateAsync(int patientId, PatientVitalCreateDto dto, int userId);
    Task<PatientVital?> UpdateAsync(int id, PatientVitalCreateDto dto, int userId);
    Task<bool> DeleteAsync(int id);
}

public class PatientVitalService : IPatientVitalService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public PatientVitalService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task<List<PatientVitalDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        return await _context.PatientVitals
            .Where(v => v.TenantId == tenantId && v.PatientId == patientId)
            .OrderByDescending(v => v.RecordedAt)
            .Select(v => new PatientVitalDto
            {
                PatientVitalId = v.PatientVitalId,
                PatientId = v.PatientId,
                EncounterId = v.EncounterId,
                RecordedAt = v.RecordedAt,
                SystolicBp = v.SystolicBp,
                DiastolicBp = v.DiastolicBp,
                BloodPressure = v.SystolicBp != null && v.DiastolicBp != null
                    ? v.SystolicBp.Value.ToString() + "/" + v.DiastolicBp.Value.ToString()
                    : null,
                HeartRate = v.HeartRate,
                RespiratoryRate = v.RespiratoryRate,
                Temperature = v.Temperature,
                SpO2 = v.SpO2,
                Weight = v.Weight,
                Height = v.Height,
                Bmi = v.Bmi,
                Notes = v.Notes
            })
            .ToListAsync();
    }

    public async Task<PatientVital> CreateAsync(int patientId, PatientVitalCreateDto dto, int userId)
    {
        var vital = new PatientVital
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            EncounterId = dto.EncounterId,
            SystolicBp = dto.SystolicBp,
            DiastolicBp = dto.DiastolicBp,
            HeartRate = dto.HeartRate,
            RespiratoryRate = dto.RespiratoryRate,
            Temperature = dto.Temperature,
            SpO2 = dto.SpO2,
            Height = dto.Height,
            Weight = dto.Weight,
            Bmi = CalculateBmi(dto.Height, dto.Weight),
            RecordedAt = DateTime.UtcNow,
            RecordedByUserId = userId,
            Notes = dto.Notes
        };
        _context.PatientVitals.Add(vital);
        await _context.SaveChangesAsync();
        return vital;
    }

    public async Task<PatientVital?> UpdateAsync(int id, PatientVitalCreateDto dto, int userId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var vital = await _context.PatientVitals.FirstOrDefaultAsync(v => v.PatientVitalId == id && v.TenantId == tenantId);
        if (vital == null) return null;

        vital.SystolicBp = dto.SystolicBp;
        vital.DiastolicBp = dto.DiastolicBp;
        vital.HeartRate = dto.HeartRate;
        vital.RespiratoryRate = dto.RespiratoryRate;
        vital.Temperature = dto.Temperature;
        vital.SpO2 = dto.SpO2;
        vital.Height = dto.Height;
        vital.Weight = dto.Weight;
        vital.Bmi = CalculateBmi(dto.Height, dto.Weight);
        vital.Notes = dto.Notes;
        vital.RecordedByUserId = userId;

        await _context.SaveChangesAsync();
        return vital;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var vital = await _context.PatientVitals.FirstOrDefaultAsync(v => v.PatientVitalId == id && v.TenantId == tenantId);
        if (vital == null) return false;
        _context.PatientVitals.Remove(vital);
        await _context.SaveChangesAsync();
        return true;
    }

    private static decimal? CalculateBmi(decimal? height, decimal? weight)
    {
        if (!height.HasValue || !weight.HasValue || height.Value == 0) return null;
        // BMI = (weight in lbs / (height in inches)^2) * 703
        return Math.Round((weight.Value / (height.Value * height.Value)) * 703, 1);
    }
}
