using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface IPrescriptionService
{
    Task<List<PrescriptionDto>> GetAllAsync(int? patientId, int? status, DateOnly? dateFrom, DateOnly? dateTo);
    Task<List<PrescriptionDto>> GetByPatientAsync(int patientId);
    Task<PrescriptionDto?> GetByIdAsync(int id);
    Task<Prescription> CreateAsync(PrescriptionCreateDto dto, int userId);
    Task<Prescription?> UpdateAsync(int id, PrescriptionUpdateDto dto);
    Task<bool> CancelAsync(int id, int userId);
}

public class PrescriptionService : IPrescriptionService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryptionHelper;

    public PrescriptionService(EhrDbContext context, ITenantProvider tenantProvider, EncryptionHelper encryptionHelper)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
    }

    public async Task<List<PrescriptionDto>> GetAllAsync(int? patientId, int? status, DateOnly? dateFrom, DateOnly? dateTo)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        // AsNoTracking: read-only list. We load entities first (not .Select → SQL
        // projection) so we can DecryptEntity the Patient/Provider navigation
        // properties in memory before mapping to DTOs. Previous implementation
        // used .Select(p => MapToDto(p)) which sent the encrypted FirstName/
        // LastName columns straight through to the DTO as plaintext strings.
        var query = _context.Prescriptions
            .AsNoTracking()
            .Include(p => p.Patient)
            .Include(p => p.Provider)
            .Where(p => p.TenantId == tenantId);

        if (patientId.HasValue)
            query = query.Where(p => p.PatientId == patientId.Value);
        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);
        if (dateFrom.HasValue)
            query = query.Where(p => p.PrescribedDate >= dateFrom.Value);
        if (dateTo.HasValue)
            query = query.Where(p => p.PrescribedDate <= dateTo.Value);

        var prescriptions = await query
            .OrderByDescending(p => p.PrescribedDate)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();

        foreach (var p in prescriptions)
        {
            if (p.Patient != null) _encryptionHelper.DecryptEntity(p.Patient);
            if (p.Provider != null) _encryptionHelper.DecryptEntity(p.Provider);
        }

        return prescriptions.Select(MapToDto).ToList();
    }

    public async Task<List<PrescriptionDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        // AsNoTracking + in-memory decrypt before DTO mapping — same rationale.
        var prescriptions = await _context.Prescriptions
            .AsNoTracking()
            .Include(p => p.Patient)
            .Include(p => p.Provider)
            .Where(p => p.TenantId == tenantId && p.PatientId == patientId)
            .OrderByDescending(p => p.PrescribedDate)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();

        foreach (var p in prescriptions)
        {
            if (p.Patient != null) _encryptionHelper.DecryptEntity(p.Patient);
            if (p.Provider != null) _encryptionHelper.DecryptEntity(p.Provider);
        }

        return prescriptions.Select(MapToDto).ToList();
    }

    public async Task<PrescriptionDto?> GetByIdAsync(int id)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        // AsNoTracking + in-memory decrypt before DTO mapping — same rationale.
        var prescription = await _context.Prescriptions
            .AsNoTracking()
            .Include(p => p.Patient)
            .Include(p => p.Provider)
            .Where(p => p.TenantId == tenantId && p.PrescriptionId == id)
            .FirstOrDefaultAsync();

        if (prescription == null) return null;

        if (prescription.Patient != null) _encryptionHelper.DecryptEntity(prescription.Patient);
        if (prescription.Provider != null) _encryptionHelper.DecryptEntity(prescription.Provider);

        return MapToDto(prescription);
    }

    public async Task<Prescription> CreateAsync(PrescriptionCreateDto dto, int userId)
    {
        var prescription = new Prescription
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = dto.PatientId,
            ProviderId = dto.ProviderId,
            EncounterId = dto.EncounterId,
            DrugName = dto.DrugName,
            GenericName = dto.GenericName,
            NDCCode = dto.NDCCode,
            RxNormCode = dto.RxNormCode,
            Strength = dto.Strength,
            DosageForm = dto.DosageForm,
            Quantity = dto.Quantity,
            DaysSupply = dto.DaysSupply,
            DoseAmount = dto.DoseAmount,
            DoseUnit = dto.DoseUnit,
            Route = dto.Route,
            Frequency = dto.Frequency,
            DirectionsFreeText = dto.DirectionsFreeText,
            Refills = dto.Refills,
            DAW = dto.DAW,
            PharmacyName = dto.PharmacyName,
            PharmacyPhone = dto.PharmacyPhone,
            PharmacyAddress = dto.PharmacyAddress,
            Status = dto.Status,
            IsControlledSubstance = dto.IsControlledSubstance,
            DEASchedule = dto.DEASchedule,
            DiagnosisCode = dto.DiagnosisCode,
            PrescribedDate = DateOnly.FromDateTime(DateTime.Today),
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Prescriptions.Add(prescription);
        await _context.SaveChangesAsync();
        return prescription;
    }

    public async Task<Prescription?> UpdateAsync(int id, PrescriptionUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var prescription = await _context.Prescriptions
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PrescriptionId == id);

        if (prescription == null) return null;

        if (dto.Status.HasValue) prescription.Status = dto.Status.Value;

        // Full draft edit fields
        if (dto.PatientId.HasValue) prescription.PatientId = dto.PatientId.Value;
        if (dto.ProviderId.HasValue) prescription.ProviderId = dto.ProviderId.Value;
        if (dto.DrugName != null) prescription.DrugName = dto.DrugName;
        if (dto.GenericName != null) prescription.GenericName = dto.GenericName;
        if (dto.NDCCode != null) prescription.NDCCode = dto.NDCCode;
        if (dto.Strength != null) prescription.Strength = dto.Strength;
        if (dto.DosageForm.HasValue) prescription.DosageForm = dto.DosageForm.Value;
        if (dto.Quantity.HasValue) prescription.Quantity = dto.Quantity.Value;
        if (dto.DaysSupply.HasValue) prescription.DaysSupply = dto.DaysSupply.Value;
        if (dto.DoseAmount != null) prescription.DoseAmount = dto.DoseAmount;
        if (dto.DoseUnit != null) prescription.DoseUnit = dto.DoseUnit;
        if (dto.Route.HasValue) prescription.Route = dto.Route.Value;
        if (dto.Frequency.HasValue) prescription.Frequency = dto.Frequency.Value;
        if (dto.DirectionsFreeText != null) prescription.DirectionsFreeText = dto.DirectionsFreeText;
        if (dto.Refills.HasValue) prescription.Refills = dto.Refills.Value;
        if (dto.DAW.HasValue) prescription.DAW = dto.DAW.Value;
        if (dto.PharmacyName != null) prescription.PharmacyName = dto.PharmacyName;
        if (dto.PharmacyPhone != null) prescription.PharmacyPhone = dto.PharmacyPhone;
        if (dto.PharmacyAddress != null) prescription.PharmacyAddress = dto.PharmacyAddress;
        if (dto.IsControlledSubstance.HasValue) prescription.IsControlledSubstance = dto.IsControlledSubstance.Value;
        if (dto.DEASchedule.HasValue) prescription.DEASchedule = dto.DEASchedule.Value;
        if (dto.DiagnosisCode != null) prescription.DiagnosisCode = dto.DiagnosisCode;
        if (dto.Notes != null) prescription.Notes = dto.Notes;
        prescription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return prescription;
    }

    public async Task<bool> CancelAsync(int id, int userId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var prescription = await _context.Prescriptions
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PrescriptionId == id);

        if (prescription == null) return false;

        prescription.Status = (int)PrescriptionStatus.Cancelled;
        prescription.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    private static PrescriptionDto MapToDto(Prescription p) => new()
    {
        PrescriptionId = p.PrescriptionId,
        PatientId = p.PatientId,
        PatientName = p.Patient != null ? p.Patient.FirstName + " " + p.Patient.LastName : "",
        ProviderId = p.ProviderId,
        ProviderName = p.Provider != null ? p.Provider.FirstName + " " + p.Provider.LastName : "",
        EncounterId = p.EncounterId,
        DrugName = p.DrugName,
        GenericName = p.GenericName,
        NDCCode = p.NDCCode,
        RxNormCode = p.RxNormCode,
        Strength = p.Strength,
        DosageForm = p.DosageForm,
        DosageFormName = EnumHelper.GetDosageFormName(p.DosageForm),
        Quantity = p.Quantity,
        DaysSupply = p.DaysSupply,
        DoseAmount = p.DoseAmount,
        DoseUnit = p.DoseUnit,
        Route = p.Route,
        RouteName = EnumHelper.GetMedicationRouteName(p.Route),
        Frequency = p.Frequency,
        FrequencyName = EnumHelper.GetMedicationFrequencyName(p.Frequency),
        DirectionsFreeText = p.DirectionsFreeText,
        Refills = p.Refills,
        DAW = p.DAW,
        PharmacyName = p.PharmacyName,
        PharmacyPhone = p.PharmacyPhone,
        PharmacyAddress = p.PharmacyAddress,
        Status = p.Status,
        StatusName = EnumHelper.GetPrescriptionStatusName(p.Status),
        IsControlledSubstance = p.IsControlledSubstance,
        DEASchedule = p.DEASchedule,
        DiagnosisCode = p.DiagnosisCode,
        PrescribedDate = p.PrescribedDate,
        ExpirationDate = p.ExpirationDate,
        Notes = p.Notes,
        CreatedAt = p.CreatedAt
    };
}
