using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Hubs;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

/// <summary>
/// CareNoteService -- the "Hired Guy" for everything Care Notes.
///
/// Why it exists:
///   One owner for create / read / edit / soft-delete / mark-seen / unseen-count
///   for the Care Notes feature. Keeps callers (controllers, future modules) clean.
///
/// PHI handling:
///   CareNote.Content is registered as encrypted in EncryptionConfiguration.
///   - Save flow: EncryptionHelper.EncryptEntity(note) before SaveChangesAsync.
///   - Read flow: EncryptionHelper.DecryptEntity(note) before returning.
///   This service also touches Patient (to render patient name in the unseen
///   dropdown) -- those fields are also encrypted, so DecryptEntity is called.
///
/// Tenancy:
///   All queries filter on ITenantProvider.TenantId. Cross-tenant reads/writes
///   silently return null/false, never throw.
/// </summary>
public interface ICareNoteService
{
    Task<List<CareNoteDto>> GetForPatientAsync(int patientId);
    Task<CareNoteDto?> CreateAsync(int patientId, CareNoteCreateDto dto, int userId, string userName);
    Task<CareNoteDto?> UpdateAsync(int careNoteId, CareNoteUpdateDto dto, int userId, string userName);
    Task<bool> SoftDeleteAsync(int careNoteId, int userId, string userName);

    // Provider notification surface
    Task<int> GetUnseenCountForProviderAsync(int providerId);
    Task<List<CareNoteUnseenDto>> GetUnseenForProviderAsync(int providerId, int take = 20);

    // Auto-mark when the assigned provider opens the patient profile Care Notes tab
    Task<int> MarkPatientNotesSeenAsync(int patientId, int providerId);
}

public class CareNoteService : ICareNoteService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryption;
    private readonly ICareNotesNotifier _notifier;

    public CareNoteService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryption,
        ICareNotesNotifier notifier)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryption = encryption;
        _notifier = notifier;
    }

    // ====================================================================
    // READ: all notes for a patient (Care Notes tab)
    // ====================================================================
    public async Task<List<CareNoteDto>> GetForPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return new List<CareNoteDto>();

        var notes = await _context.CareNotes
            .Where(n => n.PatientId == patientId
                     && n.TenantId == tenantId.Value
                     && !n.IsDeleted)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        // Decrypt Content on each note (PHI)
        foreach (var n in notes) _encryption.DecryptEntity(n);

        // Resolve provider names (if any). One small lookup per call.
        var forIds = notes
            .Where(n => n.ForProviderId.HasValue)
            .Select(n => n.ForProviderId!.Value)
            .Distinct()
            .ToList();

        var providerNames = await BuildProviderNameMapAsync(forIds);

        return notes.Select(n => ToDto(n, providerNames)).ToList();
    }

    // ====================================================================
    // CREATE
    // ====================================================================
    public async Task<CareNoteDto?> CreateAsync(int patientId, CareNoteCreateDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Content)) return null;

        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return null;

        // Verify patient belongs to tenant
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.PatientId == patientId
                                   && p.TenantId == tenantId.Value
                                   && p.IsDeleted != true);
        if (patient == null) return null;

        // Verify provider (if supplied) belongs to tenant
        int? forProviderId = null;
        if (dto.ForProviderId.HasValue)
        {
            var prov = await _context.Providers
                .FirstOrDefaultAsync(p => p.ProviderId == dto.ForProviderId.Value
                                       && p.TenantId == tenantId.Value);
            if (prov != null) forProviderId = prov.ProviderId;
        }

        var note = new CareNote
        {
            TenantId = tenantId.Value,
            PatientId = patientId,
            Content = dto.Content.Trim(),
            ForProviderId = forProviderId,
            CreatedByUserId = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.UtcNow,
            IsEdited = false,
            IsDeleted = false
        };

        // CRITICAL: encrypt PHI before persisting
        _encryption.EncryptEntity(note);

        _context.CareNotes.Add(note);
        await _context.SaveChangesAsync();

        // Decrypt back for the response
        _encryption.DecryptEntity(note);

        // Real-time push: if a provider was named, their unseen count just went up.
        // Send the FRESH count so the client doesn't have to ask.
        if (forProviderId.HasValue)
        {
            var newCount = await GetUnseenCountForProviderAsync(forProviderId.Value);
            await _notifier.NotifyUnseenChangedAsync(forProviderId.Value, newCount);
        }

        var providerNames = forProviderId.HasValue
            ? await BuildProviderNameMapAsync(new List<int> { forProviderId.Value })
            : new Dictionary<int, string>();

        return ToDto(note, providerNames);
    }

    // ====================================================================
    // UPDATE (creator only)
    // ====================================================================
    public async Task<CareNoteDto?> UpdateAsync(int careNoteId, CareNoteUpdateDto dto, int userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(dto.Content)) return null;

        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return null;

        var note = await _context.CareNotes
            .FirstOrDefaultAsync(n => n.CareNoteId == careNoteId
                                   && n.TenantId == tenantId.Value
                                   && !n.IsDeleted);
        if (note == null) return null;

        // Owner-only edit
        if (note.CreatedByUserId != userId) return null;

        // Validate provider if changed
        int? forProviderId = null;
        if (dto.ForProviderId.HasValue)
        {
            var prov = await _context.Providers
                .FirstOrDefaultAsync(p => p.ProviderId == dto.ForProviderId.Value
                                       && p.TenantId == tenantId.Value);
            if (prov != null) forProviderId = prov.ProviderId;
        }

        // Capture the OLD target so we can notify them too if it changed
        var oldForProviderId = note.ForProviderId;

        // Apply update
        note.Content = dto.Content.Trim();
        note.ForProviderId = forProviderId;
        note.IsEdited = true;
        note.EditedByUserId = userId;
        note.EditedByName = userName;
        note.EditedAt = DateTime.UtcNow;

        // If reassigning, prior "seen" stamp no longer reflects the new target
        // Reset seen state so the new target provider sees it.
        note.SeenByProviderId = null;
        note.SeenAt = null;

        _encryption.EncryptEntity(note);
        await _context.SaveChangesAsync();
        _encryption.DecryptEntity(note);

        // Real-time pushes:
        //   - The new target's count went up (or stayed same if same person).
        //   - If reassigned, the previous target's count went down.
        if (forProviderId.HasValue)
        {
            var c = await GetUnseenCountForProviderAsync(forProviderId.Value);
            await _notifier.NotifyUnseenChangedAsync(forProviderId.Value, c);
        }
        if (oldForProviderId.HasValue && oldForProviderId.Value != forProviderId)
        {
            var c = await GetUnseenCountForProviderAsync(oldForProviderId.Value);
            await _notifier.NotifyUnseenChangedAsync(oldForProviderId.Value, c);
        }

        var providerNames = forProviderId.HasValue
            ? await BuildProviderNameMapAsync(new List<int> { forProviderId.Value })
            : new Dictionary<int, string>();

        return ToDto(note, providerNames);
    }

    // ====================================================================
    // SOFT DELETE (creator only)
    // ====================================================================
    public async Task<bool> SoftDeleteAsync(int careNoteId, int userId, string userName)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return false;

        var note = await _context.CareNotes
            .FirstOrDefaultAsync(n => n.CareNoteId == careNoteId
                                   && n.TenantId == tenantId.Value
                                   && !n.IsDeleted);
        if (note == null) return false;

        // Owner-only delete
        if (note.CreatedByUserId != userId) return false;

        // Capture the assigned provider BEFORE flipping flags, so we know who
        // to ping if their count just went down.
        var wasUnseenForProvider = note.ForProviderId.HasValue && !note.SeenByProviderId.HasValue
            ? note.ForProviderId
            : null;

        note.IsDeleted = true;
        note.DeletedByUserId = userId;
        note.DeletedByName = userName;
        note.DeletedAt = DateTime.UtcNow;

        // CRITICAL: even though we only flip flags, the entity's Content is in-memory
        // plaintext if it was loaded before. EF tracks all columns -- we must
        // re-encrypt so an UPDATE doesn't downgrade ciphertext to plaintext.
        _encryption.EncryptEntity(note);
        await _context.SaveChangesAsync();

        if (wasUnseenForProvider.HasValue)
        {
            var c = await GetUnseenCountForProviderAsync(wasUnseenForProvider.Value);
            await _notifier.NotifyUnseenChangedAsync(wasUnseenForProvider.Value, c);
        }
        return true;
    }

    // ====================================================================
    // PROVIDER NOTIFICATION: count
    // ====================================================================
    public async Task<int> GetUnseenCountForProviderAsync(int providerId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return 0;

        return await _context.CareNotes.CountAsync(n =>
            n.TenantId == tenantId.Value
            && n.ForProviderId == providerId
            && n.SeenByProviderId == null
            && !n.IsDeleted);
    }

    // ====================================================================
    // PROVIDER NOTIFICATION: dropdown list
    // ====================================================================
    public async Task<List<CareNoteUnseenDto>> GetUnseenForProviderAsync(int providerId, int take = 20)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return new List<CareNoteUnseenDto>();

        // Pull notes joined with patient (need MRN + name for the row)
        var rows = await (
            from n in _context.CareNotes
            where n.TenantId == tenantId.Value
                  && n.ForProviderId == providerId
                  && n.SeenByProviderId == null
                  && !n.IsDeleted
            join p in _context.Patients on n.PatientId equals p.PatientId
            orderby n.CreatedAt descending
            select new { Note = n, Patient = p }
        ).Take(take).ToListAsync();

        var result = new List<CareNoteUnseenDto>(rows.Count);
        foreach (var row in rows)
        {
            // Decrypt PHI (note content + patient names)
            _encryption.DecryptEntity(row.Note);
            _encryption.DecryptEntity(row.Patient);

            var preview = row.Note.Content ?? string.Empty;
            if (preview.Length > 120) preview = preview.Substring(0, 120) + "...";

            result.Add(new CareNoteUnseenDto
            {
                CareNoteId = row.Note.CareNoteId,
                PatientId = row.Note.PatientId,
                PatientName = $"{row.Patient.FirstName} {row.Patient.LastName}".Trim(),
                PatientMrn = row.Patient.Mrn ?? string.Empty,
                Preview = preview,
                CreatedByName = row.Note.CreatedByName ?? string.Empty,
                CreatedAt = row.Note.CreatedAt
            });
        }
        return result;
    }

    // ====================================================================
    // MARK SEEN: when assigned provider opens the patient's Care Notes tab,
    // every unseen note for that provider on that patient is stamped seen.
    // ====================================================================
    public async Task<int> MarkPatientNotesSeenAsync(int patientId, int providerId)
    {
        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return 0;

        var unseen = await _context.CareNotes
            .Where(n => n.TenantId == tenantId.Value
                     && n.PatientId == patientId
                     && n.ForProviderId == providerId
                     && n.SeenByProviderId == null
                     && !n.IsDeleted)
            .ToListAsync();

        if (unseen.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var n in unseen)
        {
            n.SeenByProviderId = providerId;
            n.SeenAt = now;
            // Same encryption-safety rule as soft-delete: re-encrypt before save
            // so the round-trip doesn't write Content back as plaintext.
            _encryption.EncryptEntity(n);
        }
        await _context.SaveChangesAsync();

        // Provider's bell count just dropped -- push the fresh value.
        var fresh = await GetUnseenCountForProviderAsync(providerId);
        await _notifier.NotifyUnseenChangedAsync(providerId, fresh);

        return unseen.Count;
    }

    // ====================================================================
    // helpers
    // ====================================================================
    private async Task<Dictionary<int, string>> BuildProviderNameMapAsync(List<int> providerIds)
    {
        var map = new Dictionary<int, string>();
        if (providerIds == null || providerIds.Count == 0) return map;

        var tenantId = _tenantProvider.TenantId;
        if (!tenantId.HasValue) return map;

        var providers = await _context.Providers
            .Where(p => p.TenantId == tenantId.Value && providerIds.Contains(p.ProviderId))
            .Select(p => new { p.ProviderId, p.FirstName, p.LastName })
            .ToListAsync();

        foreach (var p in providers)
        {
            map[p.ProviderId] = $"Dr. {p.FirstName} {p.LastName}".Trim();
        }
        return map;
    }

    private static CareNoteDto ToDto(CareNote n, Dictionary<int, string> providerNames)
    {
        string? forName = null;
        if (n.ForProviderId.HasValue && providerNames.TryGetValue(n.ForProviderId.Value, out var nm))
            forName = nm;

        return new CareNoteDto
        {
            CareNoteId = n.CareNoteId,
            PatientId = n.PatientId,
            Content = n.Content ?? string.Empty,

            ForProviderId = n.ForProviderId,
            ForProviderName = forName,

            CreatedByUserId = n.CreatedByUserId,
            CreatedByName = n.CreatedByName ?? string.Empty,
            CreatedAt = n.CreatedAt,

            IsEdited = n.IsEdited,
            EditedByUserId = n.EditedByUserId,
            EditedByName = n.EditedByName,
            EditedAt = n.EditedAt,

            SeenByProviderId = n.SeenByProviderId,
            SeenAt = n.SeenAt
        };
    }
}
