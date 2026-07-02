using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IEncounterService
{
    Task<List<EncounterDto>> GetByPatientAsync(int patientId);
    Task<EncounterDto?> GetByIdAsync(int id);
    Task<EncounterDto?> GetByAppointmentIdAsync(int appointmentId);
    Task<Encounter> CreateAsync(int patientId, EncounterCreateDto dto, int userId);
    Task<Encounter?> UpdateAsync(int id, EncounterUpdateDto dto);
    Task<bool> DeleteAsync(int id);
}

public class EncounterService : IEncounterService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper? _encryptionHelper;

    public EncounterService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper? encryptionHelper = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
    }

    /// <summary>
    /// Encounter.SummaryText is encrypted at rest. Decrypt for DTO output;
    /// fall back to original string if it isn't encrypted (legacy rows or
    /// pre-encryption test data).
    /// </summary>
    private string? DecryptSummary(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return encrypted;
        if (_encryptionHelper == null) return encrypted;
        try { return _encryptionHelper.Decrypt(encrypted) ?? encrypted; }
        catch { return encrypted; }
    }

    public async Task<List<EncounterDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var list = await _context.Encounters
            .Include(e => e.Provider)
            .Include(e => e.ClinicalNotes)
            .Include(e => e.Vitals)
            .Include(e => e.Appointment)
            .Where(e => e.TenantId == tenantId && e.PatientId == patientId)
            .OrderByDescending(e => e.EncounterDate)
            .Select(e => new EncounterDto
            {
                EncounterId = e.EncounterId,
                PatientId = e.PatientId,
                ProviderId = e.ProviderId,
                ProviderName = e.Provider != null ? e.Provider.FirstName + " " + e.Provider.LastName : null,
                ProviderHasProfilePicture = e.Provider != null && !string.IsNullOrEmpty(e.Provider.ProfilePicturePath),
                AppointmentId = e.AppointmentId,
                EncounterDate = e.EncounterDate,
                ChiefComplaint = e.ChiefComplaint,
                HistoryOfPresentIllness = e.HistoryOfPresentIllness,
                ReviewOfSystems = e.ReviewOfSystems,
                PhysicalExam = e.PhysicalExam,
                Assessment = e.Assessment,
                Plan = e.Plan,
                Status = e.Status,
                StatusName = e.Status == 0 ? "Open" :
                             e.Status == 1 ? "Signed" :
                             e.Status == 2 ? "Locked" :
                             e.Status == 3 ? "Amended" : "Open",
                CreatedAt = e.CreatedAt,
                SignedAt = e.SignedAt,
                ClinicalNoteCount = e.ClinicalNotes.Count,
                ClinicalNoteStatus = e.ClinicalNotes.Any()
                    ? (e.ClinicalNotes.All(n => n.Status >= 2) ? "Signed" : "Draft")
                    : "No Note",
                VitalsCount = e.Vitals.Count,
                AppointmentType = e.Appointment != null ? ((AppointmentType)e.Appointment.Type).ToString() : null,
                IsTelehealth = e.Appointment != null ? e.Appointment.IsTelehealth : null,
                TelehealthUrl = e.Appointment != null ? e.Appointment.TelehealthUrl : null,
                CptSelections = e.CptSelections,
                IcdSelections = e.IcdSelections,
                SummaryText = e.SummaryText
            })
            .ToListAsync();

        foreach (var dto in list)
            dto.SummaryText = DecryptSummary(dto.SummaryText);
        return list;
    }

    public async Task<EncounterDto?> GetByIdAsync(int id)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var dto = await _context.Encounters
            .Include(e => e.Provider)
            .Include(e => e.ClinicalNotes)
            .Include(e => e.Vitals)
            .Include(e => e.Appointment)
            .Where(e => e.EncounterId == id && e.TenantId == tenantId)
            .Select(e => new EncounterDto
            {
                EncounterId = e.EncounterId,
                PatientId = e.PatientId,
                ProviderId = e.ProviderId,
                ProviderName = e.Provider != null ? e.Provider.FirstName + " " + e.Provider.LastName : null,
                ProviderHasProfilePicture = e.Provider != null && !string.IsNullOrEmpty(e.Provider.ProfilePicturePath),
                AppointmentId = e.AppointmentId,
                EncounterDate = e.EncounterDate,
                ChiefComplaint = e.ChiefComplaint,
                HistoryOfPresentIllness = e.HistoryOfPresentIllness,
                ReviewOfSystems = e.ReviewOfSystems,
                PhysicalExam = e.PhysicalExam,
                Assessment = e.Assessment,
                Plan = e.Plan,
                Status = e.Status,
                StatusName = e.Status == 0 ? "Open" :
                             e.Status == 1 ? "Signed" :
                             e.Status == 2 ? "Locked" :
                             e.Status == 3 ? "Amended" : "Open",
                CreatedAt = e.CreatedAt,
                SignedAt = e.SignedAt,
                ClinicalNoteCount = e.ClinicalNotes.Count,
                ClinicalNoteStatus = e.ClinicalNotes.Any()
                    ? (e.ClinicalNotes.All(n => n.Status >= 2) ? "Signed" : "Draft")
                    : "No Note",
                VitalsCount = e.Vitals.Count,
                AppointmentType = e.Appointment != null ? ((AppointmentType)e.Appointment.Type).ToString() : null,
                IsTelehealth = e.Appointment != null ? e.Appointment.IsTelehealth : null,
                TelehealthUrl = e.Appointment != null ? e.Appointment.TelehealthUrl : null,
                CptSelections = e.CptSelections,
                IcdSelections = e.IcdSelections,
                SummaryText = e.SummaryText
            })
            .FirstOrDefaultAsync();

        if (dto != null) dto.SummaryText = DecryptSummary(dto.SummaryText);
        return dto;
    }

    public async Task<EncounterDto?> GetByAppointmentIdAsync(int appointmentId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var dto = await _context.Encounters
            .Include(e => e.Provider)
            .Include(e => e.ClinicalNotes)
            .Include(e => e.Vitals)
            .Include(e => e.Appointment)
            .Where(e => e.AppointmentId == appointmentId && e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new EncounterDto
            {
                EncounterId = e.EncounterId,
                PatientId = e.PatientId,
                ProviderId = e.ProviderId,
                ProviderName = e.Provider != null ? e.Provider.FirstName + " " + e.Provider.LastName : null,
                ProviderHasProfilePicture = e.Provider != null && !string.IsNullOrEmpty(e.Provider.ProfilePicturePath),
                AppointmentId = e.AppointmentId,
                EncounterDate = e.EncounterDate,
                ChiefComplaint = e.ChiefComplaint,
                HistoryOfPresentIllness = e.HistoryOfPresentIllness,
                ReviewOfSystems = e.ReviewOfSystems,
                PhysicalExam = e.PhysicalExam,
                Assessment = e.Assessment,
                Plan = e.Plan,
                Status = e.Status,
                StatusName = e.Status == 0 ? "Open" :
                             e.Status == 1 ? "Signed" :
                             e.Status == 2 ? "Locked" :
                             e.Status == 3 ? "Amended" : "Open",
                CreatedAt = e.CreatedAt,
                SignedAt = e.SignedAt,
                ClinicalNoteCount = e.ClinicalNotes.Count,
                ClinicalNoteStatus = e.ClinicalNotes.Any()
                    ? (e.ClinicalNotes.All(n => n.Status >= 2) ? "Signed" : "Draft")
                    : "No Note",
                VitalsCount = e.Vitals.Count,
                AppointmentType = e.Appointment != null ? ((AppointmentType)e.Appointment.Type).ToString() : null,
                IsTelehealth = e.Appointment != null ? e.Appointment.IsTelehealth : null,
                TelehealthUrl = e.Appointment != null ? e.Appointment.TelehealthUrl : null,
                CptSelections = e.CptSelections,
                IcdSelections = e.IcdSelections,
                SummaryText = e.SummaryText
            })
            .FirstOrDefaultAsync();

        if (dto != null) dto.SummaryText = DecryptSummary(dto.SummaryText);
        return dto;
    }

    public async Task<Encounter> CreateAsync(int patientId, EncounterCreateDto dto, int userId)
    {
        var encounter = new Encounter
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = patientId,
            ProviderId = dto.ProviderId,
            AppointmentId = dto.AppointmentId,
            EncounterDate = dto.EncounterDate,
            ChiefComplaint = dto.ChiefComplaint,
            Status = 0, // Open
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Encounters.Add(encounter);
        await _context.SaveChangesAsync();
        return encounter;
    }

    public async Task<Encounter?> UpdateAsync(int id, EncounterUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var encounter = await _context.Encounters.FirstOrDefaultAsync(e => e.EncounterId == id && e.TenantId == tenantId);
        if (encounter == null) return null;

        if (dto.ChiefComplaint != null) encounter.ChiefComplaint = dto.ChiefComplaint;
        if (dto.HistoryOfPresentIllness != null) encounter.HistoryOfPresentIllness = dto.HistoryOfPresentIllness;
        if (dto.ReviewOfSystems != null) encounter.ReviewOfSystems = dto.ReviewOfSystems;
        if (dto.PhysicalExam != null) encounter.PhysicalExam = dto.PhysicalExam;
        if (dto.Assessment != null) encounter.Assessment = dto.Assessment;
        if (dto.Plan != null) encounter.Plan = dto.Plan;
        if (dto.CptSelections != null) encounter.CptSelections = dto.CptSelections;
        if (dto.IcdSelections != null) encounter.IcdSelections = dto.IcdSelections;
        encounter.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return encounter;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var encounter = await _context.Encounters.FirstOrDefaultAsync(e => e.EncounterId == id && e.TenantId == tenantId);
        if (encounter == null) return false;
        _context.Encounters.Remove(encounter);
        await _context.SaveChangesAsync();
        return true;
    }
}
