using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface IDashboardService
{
    /// <summary>
    /// Gets appointments where patient checked in but no clinical note exists.
    /// Filters by tenant, location, and optionally provider.
    /// </summary>
    Task<List<MissingNotesItemDto>> GetMissingNotesAsync(int? providerId = null, int? locationId = null);

    /// <summary>
    /// Gets clinical notes that need signature (Draft or PendingSignature status).
    /// Filters by tenant, location, and optionally provider.
    /// </summary>
    Task<List<MissingSignatureItemDto>> GetMissingSignaturesAsync(int? providerId = null, int? locationId = null);
}

public class DashboardService : IDashboardService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EncryptionHelper _encryptionHelper;

    public DashboardService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EncryptionHelper encryptionHelper)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryptionHelper = encryptionHelper;
    }

    public async Task<List<MissingNotesItemDto>> GetMissingNotesAsync(int? providerId = null, int? locationId = null)
    {
        // Query appointments where:
        // 1. Patient has checked in (Status >= CheckedIn and not Cancelled/Missed)
        // 2. No ClinicalNote record is linked to the appointment
        // AsNoTracking: read-only dashboard projection; included Patient/Provider
        // are decrypted in-place, must not be tracked.
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .Include(a => a.ClinicalNotes)
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        // Location filtering: explicit locationId > provider context > all locations
        if (locationId.HasValue)
        {
            query = query.Where(a => a.Patient.PreferredLocationId == locationId.Value);
        }
        else if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(a => a.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        // Provider filtering (for provider role users)
        if (providerId.HasValue)
        {
            query = query.Where(a => a.ProviderId == providerId.Value);
        }

        // Filter for checked-in appointments (Status >= CheckedIn) excluding Cancelled and Missed
        query = query.Where(a =>
            a.Status != null &&
            a.Status >= (int)AppointmentStatus.CheckedIn &&
            a.Status != (int)AppointmentStatus.Cancelled &&
            a.Status != (int)AppointmentStatus.Missed);

        // Filter for appointments with NO clinical notes
        query = query.Where(a => !a.ClinicalNotes.Any());

        // Only look back 90 days for missing notes
        var cutoffDate = DateTime.UtcNow.AddDays(-90);
        query = query.Where(a => a.StartTime >= cutoffDate);

        // Order by oldest first (most urgent)
        var appointments = await query
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        // Decrypt PHI fields
        foreach (var a in appointments)
        {
            if (a.Patient != null)
                _encryptionHelper.DecryptEntity(a.Patient);
            if (a.Provider != null)
                _encryptionHelper.DecryptEntity(a.Provider);
        }

        var today = DateTime.UtcNow.Date;

        return appointments.Select(a => new MissingNotesItemDto
        {
            AppointmentId = a.AppointmentId,
            PatientId = a.PatientId,
            PatientName = a.Patient != null ? $"{a.Patient.FirstName} {a.Patient.LastName}" : "",
            PatientMRN = a.Patient?.Mrn ?? "",
            PatientPhone = a.Patient?.Phone,
            PatientEmail = a.Patient?.Email,
            AppointmentDate = a.StartTime,
            Type = a.Type,
            ProviderId = a.ProviderId,
            ProviderName = a.Provider != null ? $"{a.Provider.LastName}, {a.Provider.FirstName}" : "",
            LocationId = a.LocationId,
            LocationName = a.Location?.Name,
            DaysSince = (int)(today - a.StartTime.Date).TotalDays
        }).ToList();
    }

    public async Task<List<MissingSignatureItemDto>> GetMissingSignaturesAsync(int? providerId = null, int? locationId = null)
    {
        // Query clinical notes where:
        // 1. Status is Draft (0) or PendingSignature (1)
        // 2. Note is linked to an appointment (for appointment date display)
        // AsNoTracking: read-only dashboard projection; Patient/Provider decrypted
        // in-place, must not be tracked.
        var query = _context.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.Appointment)
                .ThenInclude(a => a.Location)
            .Include(n => n.Template)
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);
        }

        // Location filtering: via patient's preferred location
        if (locationId.HasValue)
        {
            query = query.Where(n => n.Patient.PreferredLocationId == locationId.Value);
        }
        else if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(n => n.Patient.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        // Provider filtering (for provider role users)
        if (providerId.HasValue)
        {
            query = query.Where(n => n.ProviderId == providerId.Value);
        }

        // Filter for unsigned notes (Draft = 0, PendingSignature = 1)
        query = query.Where(n =>
            n.Status == (int)NoteStatus.Draft ||
            n.Status == (int)NoteStatus.PendingSignature);

        // Only look back 90 days for unsigned notes
        var cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        query = query.Where(n => n.ServiceDate >= cutoffDate);

        // Order by oldest first (most urgent)
        var notes = await query
            .OrderBy(n => n.ServiceDate)
            .ThenBy(n => n.CreatedAt)
            .ToListAsync();

        // Decrypt PHI fields
        foreach (var n in notes)
        {
            if (n.Patient != null)
                _encryptionHelper.DecryptEntity(n.Patient);
            if (n.Provider != null)
                _encryptionHelper.DecryptEntity(n.Provider);
        }

        var today = DateTime.UtcNow.Date;

        return notes.Select(n => new MissingSignatureItemDto
        {
            ClinicalNoteId = n.ClinicalNoteId,
            AppointmentId = n.AppointmentId ?? 0,
            PatientId = n.PatientId,
            PatientName = n.Patient != null ? $"{n.Patient.FirstName} {n.Patient.LastName}" : "",
            PatientMRN = n.Patient?.Mrn ?? "",
            PatientPhone = n.Patient?.Phone,
            PatientEmail = n.Patient?.Email,
            AppointmentDate = n.Appointment?.StartTime ?? n.ServiceDate.ToDateTime(TimeOnly.MinValue),
            TemplateName = n.Template?.Name,
            Type = n.Type,
            ProviderId = n.ProviderId,
            ProviderName = n.Provider != null ? $"{n.Provider.LastName}, {n.Provider.FirstName}" : "",
            LocationId = n.Appointment?.LocationId,
            LocationName = n.Appointment?.Location?.Name,
            DaysSince = (int)(today - n.ServiceDate.ToDateTime(TimeOnly.MinValue).Date).TotalDays
        }).ToList();
    }
}
