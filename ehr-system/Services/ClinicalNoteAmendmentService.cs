using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using System.Text.Json;

namespace EHR.Services;

// ============================================================
// DTOs
// ============================================================

public class CreateAmendmentRequest
{
    public string HtmlContent { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? SignatureData { get; set; }

    /// <summary>Required in Mode 3 (encounter closed). Must be null in Modes 1 & 2.</summary>
    public List<CptSelectionItem>? CptSelections { get; set; }

    /// <summary>Required in Mode 3. Must be null in Modes 1 & 2.
    /// Sent as full objects (code + description + aiSuggested) to match the
    /// format the encounter's Dx & CPT step writes, so Encounter.IcdSelections
    /// stays round-trip-consistent regardless of which path updates it.</summary>
    public List<IcdSelectionItem>? IcdSelections { get; set; }
}

public class CptSelectionItem
{
    public string CptCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Units { get; set; } = 1;
}

public class IcdSelectionItem
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AiSuggested { get; set; }
}

public class CreateAddendumRequest
{
    public string Content { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? SignatureData { get; set; }
}

public class NoteAmendmentStatusDto
{
    public int NoteId { get; set; }
    public DateTime? OriginalSignedAt { get; set; }
    public string? OriginalSignedByName { get; set; }
    public int CurrentVersionNumber { get; set; } = 1;
    public bool IsEncounterClosed { get; set; }
    public DateTime? EncounterCheckOutTime { get; set; }
    public bool IsAmendmentWindowOpen { get; set; }
    public DateTime? AmendmentWindowEndsAt { get; set; }
    public int AmendmentWindowHours { get; set; }
    public bool CanAmend { get; set; }
    public bool CanAddendum { get; set; }

    /// <summary>0 = N/A (not signed / not authorized), 1 = pre-close no codes, 2 = pre-close with codes, 3 = post-close.</summary>
    public int AmendmentMode { get; set; }

    public bool HasEncounterCodes { get; set; }
}

public class NoteVersionDto
{
    public int VersionNumber { get; set; }
    public DateTime SignedAt { get; set; }
    public string SignedByName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsCurrent { get; set; }
}

public class NoteAddendumDto
{
    public int AddendumId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime SignedAt { get; set; }
    public string SignedByName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class NoteHistoryResponse
{
    public NoteAmendmentStatusDto Status { get; set; } = new();
    public List<NoteVersionDto> Versions { get; set; } = new();
    public List<NoteAddendumDto> Addendums { get; set; } = new();

    /// <summary>The HTML content of the current (latest) version, decrypted.</summary>
    public string CurrentHtmlContent { get; set; } = string.Empty;
}

public class VersionContentDto
{
    public int VersionNumber { get; set; }
    public string HtmlContent { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string SignedByName { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

// ============================================================
// Service interface
// ============================================================

public interface IClinicalNoteAmendmentService
{
    Task<NoteAmendmentStatusDto> GetStatusAsync(int noteId, int currentUserId, int currentUserRole);
    Task<NoteHistoryResponse> GetHistoryAsync(int noteId, int currentUserId, int currentUserRole);
    Task<VersionContentDto?> GetVersionContentAsync(int noteId, int versionNumber);
    Task<(bool Ok, string? Error, int? NewVersion)> CreateAmendmentAsync(int noteId, CreateAmendmentRequest req, int currentUserId, int currentUserRole);
    Task<(bool Ok, string? Error, int? AddendumId)> CreateAddendumAsync(int noteId, CreateAddendumRequest req, int currentUserId, int currentUserRole);
    Task<bool> IsClaimSubmitLockedAsync(int claimId);
}

// ============================================================
// Service implementation
// ============================================================

public class ClinicalNoteAmendmentService : IClinicalNoteAmendmentService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IConfiguration _config;
    private readonly EncryptionHelper? _encryptionHelper;
    private readonly ILogger<ClinicalNoteAmendmentService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    private const int CLINICIAN_ROLE = 2;
    private const int NOTE_STATUS_SIGNED = 2;

    public ClinicalNoteAmendmentService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        IConfiguration config,
        ILogger<ClinicalNoteAmendmentService> logger,
        IServiceScopeFactory serviceScopeFactory,
        EncryptionHelper? encryptionHelper = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _config = config;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _encryptionHelper = encryptionHelper;
    }

    /// <summary>
    /// Fire-and-forget regeneration of the encounter summary after an amendment
    /// or addendum is signed. No-op when the note has no encounter linked.
    /// Spec: rules/technical/encounter-summary.md
    /// </summary>
    private void TriggerEncounterSummary(int? encounterId)
    {
        if (!encounterId.HasValue || encounterId.Value <= 0) return;
        var encId = encounterId.Value;
        var tenantId = _tenantProvider.TenantId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                if (tenantId.HasValue)
                {
                    var tp = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
                    tp.TenantId = tenantId;
                }
                var manager = scope.ServiceProvider.GetRequiredService<IEncounterSummaryManager>();
                await manager.GenerateSummaryAsync(encId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Background encounter summary regeneration failed for encounter {EncounterId}",
                    encId);
            }
        });
    }

    private int GetWindowHours()
    {
        // Read every time so ops can change appsettings and recycle pool without rebuild.
        return _config.GetValue<int?>("ClinicalNotes:AmendmentWindowHours") ?? 24;
    }

    private int TenantId => _tenantProvider.TenantId ?? throw new InvalidOperationException("Tenant context missing.");

    // Stamp Kind=Utc on DateTimes we return so System.Text.Json emits the 'Z'
    // suffix and the browser parses them as UTC instead of local.
    // See GetStatusAsync for the rationale.
    private static DateTime AsUtc(DateTime dt)
    {
        return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
    private static DateTime? AsUtc(DateTime? dt) => dt.HasValue ? AsUtc(dt.Value) : (DateTime?)null;

    private string DecryptContent(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        if (_encryptionHelper == null) return encrypted;
        try { return _encryptionHelper.Decrypt(encrypted) ?? encrypted; }
        catch { return encrypted; }
    }

    private string? EncryptContent(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        if (_encryptionHelper == null) return plain;
        return _encryptionHelper.Encrypt(plain) ?? plain;
    }

    // ------------------------------------------------------------
    // Status (drives button enable/disable + mode detection)
    // ------------------------------------------------------------
    public async Task<NoteAmendmentStatusDto> GetStatusAsync(int noteId, int currentUserId, int currentUserRole)
    {
        var tenantId = TenantId;
        var note = await _context.ClinicalNotes
            .Where(n => n.ClinicalNoteId == noteId && n.TenantId == tenantId)
            .FirstOrDefaultAsync();

        var status = new NoteAmendmentStatusDto
        {
            NoteId = noteId,
            AmendmentWindowHours = GetWindowHours()
        };

        if (note == null) return status;
        if (note.Status != NOTE_STATUS_SIGNED) return status;

        // All DateTimes we surface are stored as UTC in the DB (the app writes them
        // with DateTime.UtcNow / GETUTCDATE()), but SQL Server strips the Kind,
        // so EF hydrates them as DateTimeKind.Unspecified. Without an explicit
        // Kind the .NET JSON serializer emits ISO strings without a trailing 'Z',
        // and JS `new Date(...)` parses those as LOCAL time — off by the client's
        // UTC offset. Stamping Kind=Utc on every DateTime we return keeps the
        // wire format consistent and the countdown math correct.
        status.OriginalSignedAt = AsUtc(note.SignedAt);

        // Resolve signer name
        if (note.SignedByUserId.HasValue)
        {
            var user = await _context.Users
                .Where(u => u.UserId == note.SignedByUserId.Value)
                .Select(u => new { u.FirstName, u.LastName })
                .FirstOrDefaultAsync();
            status.OriginalSignedByName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null;
        }

        // Current version number = v1 + count of amendments
        var amendCount = await _context.ClinicalNoteAmendments
            .CountAsync(a => a.ClinicalNoteId == noteId);
        status.CurrentVersionNumber = 1 + amendCount;

        // Look up appointment checkout time (window anchor)
        DateTime? checkOutTime = null;
        if (note.AppointmentId.HasValue)
        {
            checkOutTime = await _context.Appointments
                .Where(a => a.AppointmentId == note.AppointmentId.Value)
                .Select(a => a.CheckOutTime)
                .FirstOrDefaultAsync();
        }

        status.IsEncounterClosed = checkOutTime.HasValue;
        status.EncounterCheckOutTime = checkOutTime.HasValue ? AsUtc(checkOutTime.Value) : (DateTime?)null;

        var windowHours = status.AmendmentWindowHours;

        // Window is open iff encounter is closed AND within the window horizon
        if (checkOutTime.HasValue && windowHours > 0)
        {
            var windowEnd = checkOutTime.Value.AddHours(windowHours);
            status.AmendmentWindowEndsAt = AsUtc(windowEnd);
            status.IsAmendmentWindowOpen = DateTime.UtcNow < windowEnd;
        }

        // Do encounter codes exist? (for Mode 2 vs Mode 1 detection)
        bool hasCodes = false;
        if (note.EncounterId.HasValue)
        {
            var enc = await _context.Encounters
                .Where(e => e.EncounterId == note.EncounterId.Value)
                .Select(e => new { e.CptSelections, e.IcdSelections })
                .FirstOrDefaultAsync();
            if (enc != null)
            {
                hasCodes = !IsEmptyJsonArray(enc.CptSelections) || !IsEmptyJsonArray(enc.IcdSelections);
            }
        }
        status.HasEncounterCodes = hasCodes;

        // Authorization: only original signing clinician
        bool isAuthor = currentUserRole == CLINICIAN_ROLE
                        && note.SignedByUserId.HasValue
                        && note.SignedByUserId.Value == currentUserId;

        // Addendum: allowed for author any time
        status.CanAddendum = isAuthor;

        // Amendment button:
        //   - Window hours == 0 → amendment disabled after checkout
        //   - Encounter open → Mode 1 or 2 (always allowed for author)
        //   - Encounter closed → allowed only while window open
        if (isAuthor)
        {
            if (!status.IsEncounterClosed)
            {
                status.CanAmend = true;
                status.AmendmentMode = hasCodes ? 2 : 1;
            }
            else if (status.IsAmendmentWindowOpen)
            {
                status.CanAmend = true;
                status.AmendmentMode = 3;
            }
            else
            {
                status.CanAmend = false;
                status.AmendmentMode = 0;
            }
        }

        return status;
    }

    private static bool IsEmptyJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        var trimmed = json.Trim();
        if (trimmed == "[]" || trimmed == "null") return true;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return true;
            return doc.RootElement.GetArrayLength() == 0;
        }
        catch { return true; }
    }

    // ------------------------------------------------------------
    // History (versions + addendums + current content)
    // ------------------------------------------------------------
    public async Task<NoteHistoryResponse> GetHistoryAsync(int noteId, int currentUserId, int currentUserRole)
    {
        var status = await GetStatusAsync(noteId, currentUserId, currentUserRole);
        var result = new NoteHistoryResponse { Status = status };

        var tenantId = TenantId;
        var note = await _context.ClinicalNotes
            .Where(n => n.ClinicalNoteId == noteId && n.TenantId == tenantId)
            .FirstOrDefaultAsync();
        if (note == null || note.Status != NOTE_STATUS_SIGNED) return result;

        // v1 — the original
        string? origSignerName = status.OriginalSignedByName;
        result.Versions.Add(new NoteVersionDto
        {
            VersionNumber = 1,
            SignedAt = AsUtc(note.SignedAt ?? note.CreatedAt),
            SignedByName = origSignerName ?? "",
            Reason = null,
            IsOriginal = true,
            IsCurrent = status.CurrentVersionNumber == 1
        });

        // v2+ — amendments ordered by VersionNumber
        var amendments = await _context.ClinicalNoteAmendments
            .Where(a => a.ClinicalNoteId == noteId)
            .OrderBy(a => a.VersionNumber)
            .Select(a => new { a.VersionNumber, a.SignedAt, a.SignedByUserId, a.Reason })
            .ToListAsync();

        if (amendments.Any())
        {
            var userIds = amendments.Select(a => a.SignedByUserId).Distinct().ToList();
            var users = await _context.Users
                .Where(u => userIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.FirstName, u.LastName })
                .ToListAsync();
            var nameMap = users.ToDictionary(u => u.UserId, u => $"{u.FirstName} {u.LastName}".Trim());

            foreach (var a in amendments)
            {
                result.Versions.Add(new NoteVersionDto
                {
                    VersionNumber = a.VersionNumber,
                    SignedAt = AsUtc(a.SignedAt),
                    SignedByName = nameMap.TryGetValue(a.SignedByUserId, out var n) ? n : "",
                    Reason = a.Reason,
                    IsOriginal = false,
                    IsCurrent = a.VersionNumber == status.CurrentVersionNumber
                });
            }
        }

        // Addendums (chronological)
        var addendums = await _context.ClinicalNoteAddendums
            .Where(a => a.ClinicalNoteId == noteId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.AddendumId, a.CreatedAt, a.SignedAt, a.SignedByUserId, a.Reason, a.Content })
            .ToListAsync();

        if (addendums.Any())
        {
            var userIds = addendums.Select(a => a.SignedByUserId).Distinct().ToList();
            var users = await _context.Users
                .Where(u => userIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.FirstName, u.LastName })
                .ToListAsync();
            var nameMap = users.ToDictionary(u => u.UserId, u => $"{u.FirstName} {u.LastName}".Trim());

            foreach (var a in addendums)
            {
                result.Addendums.Add(new NoteAddendumDto
                {
                    AddendumId = a.AddendumId,
                    CreatedAt = AsUtc(a.CreatedAt),
                    SignedAt = AsUtc(a.SignedAt),
                    SignedByName = nameMap.TryGetValue(a.SignedByUserId, out var n) ? n : "",
                    Reason = a.Reason,
                    Content = DecryptContent(a.Content)
                });
            }
        }

        // Current HTML content
        if (status.CurrentVersionNumber == 1)
        {
            result.CurrentHtmlContent = DecryptContent(note.HtmlContent);
        }
        else
        {
            var latest = await _context.ClinicalNoteAmendments
                .Where(a => a.ClinicalNoteId == noteId)
                .OrderByDescending(a => a.VersionNumber)
                .Select(a => a.HtmlContent)
                .FirstOrDefaultAsync();
            result.CurrentHtmlContent = DecryptContent(latest);
        }

        return result;
    }

    // ------------------------------------------------------------
    // Version content (for "View" button on earlier version)
    // ------------------------------------------------------------
    public async Task<VersionContentDto?> GetVersionContentAsync(int noteId, int versionNumber)
    {
        var tenantId = TenantId;
        var note = await _context.ClinicalNotes
            .Where(n => n.ClinicalNoteId == noteId && n.TenantId == tenantId)
            .FirstOrDefaultAsync();
        if (note == null) return null;

        if (versionNumber == 1)
        {
            string? signerName = null;
            if (note.SignedByUserId.HasValue)
            {
                var u = await _context.Users
                    .Where(x => x.UserId == note.SignedByUserId.Value)
                    .Select(x => new { x.FirstName, x.LastName })
                    .FirstOrDefaultAsync();
                signerName = u != null ? $"{u.FirstName} {u.LastName}".Trim() : null;
            }
            return new VersionContentDto
            {
                VersionNumber = 1,
                HtmlContent = DecryptContent(note.HtmlContent),
                SignedAt = AsUtc(note.SignedAt ?? note.CreatedAt),
                SignedByName = signerName ?? "",
                Reason = null
            };
        }

        var a = await _context.ClinicalNoteAmendments
            .Where(x => x.ClinicalNoteId == noteId && x.VersionNumber == versionNumber)
            .FirstOrDefaultAsync();
        if (a == null) return null;

        string? name = null;
        var user = await _context.Users
            .Where(x => x.UserId == a.SignedByUserId)
            .Select(x => new { x.FirstName, x.LastName })
            .FirstOrDefaultAsync();
        if (user != null) name = $"{user.FirstName} {user.LastName}".Trim();

        return new VersionContentDto
        {
            VersionNumber = a.VersionNumber,
            HtmlContent = DecryptContent(a.HtmlContent),
            SignedAt = AsUtc(a.SignedAt),
            SignedByName = name ?? "",
            Reason = a.Reason
        };
    }

    // ------------------------------------------------------------
    // Create amendment (atomic: note v2 + encounter codes + charges + claim snapshot)
    // ------------------------------------------------------------
    public async Task<(bool Ok, string? Error, int? NewVersion)> CreateAmendmentAsync(int noteId, CreateAmendmentRequest req, int currentUserId, int currentUserRole)
    {
        if (currentUserRole != CLINICIAN_ROLE)
            return (false, "Only clinicians can amend a clinical note.", null);

        if (string.IsNullOrWhiteSpace(req.HtmlContent))
            return (false, "Amendment content is required.", null);

        if (string.IsNullOrWhiteSpace(req.Reason))
            return (false, "Reason for amendment is required.", null);

        if (req.Reason.Length > 500)
            return (false, "Reason must be 500 characters or fewer.", null);

        if (string.IsNullOrWhiteSpace(req.SignatureData))
            return (false, "Signature is required.", null);

        var tenantId = TenantId;
        var note = await _context.ClinicalNotes
            .Where(n => n.ClinicalNoteId == noteId && n.TenantId == tenantId)
            .FirstOrDefaultAsync();

        if (note == null) return (false, "Note not found.", null);
        if (note.Status != NOTE_STATUS_SIGNED) return (false, "Note must be signed to amend.", null);
        if (!note.SignedByUserId.HasValue || note.SignedByUserId.Value != currentUserId)
            return (false, "Only the original signing clinician can amend this note.", null);

        // Mode detection (server-authoritative)
        DateTime? checkOutTime = null;
        if (note.AppointmentId.HasValue)
        {
            checkOutTime = await _context.Appointments
                .Where(a => a.AppointmentId == note.AppointmentId.Value)
                .Select(a => a.CheckOutTime)
                .FirstOrDefaultAsync();
        }
        var windowHours = GetWindowHours();
        bool encounterClosed = checkOutTime.HasValue;
        bool windowOpen = encounterClosed
                          && windowHours > 0
                          && DateTime.UtcNow < checkOutTime!.Value.AddHours(windowHours);

        int mode;
        if (!encounterClosed)
        {
            // Determine 1 or 2 based on codes presence
            bool hasCodes = false;
            if (note.EncounterId.HasValue)
            {
                var enc = await _context.Encounters
                    .Where(e => e.EncounterId == note.EncounterId.Value)
                    .Select(e => new { e.CptSelections, e.IcdSelections })
                    .FirstOrDefaultAsync();
                if (enc != null)
                    hasCodes = !IsEmptyJsonArray(enc.CptSelections) || !IsEmptyJsonArray(enc.IcdSelections);
            }
            mode = hasCodes ? 2 : 1;
        }
        else if (windowOpen)
        {
            mode = 3;
        }
        else
        {
            return (false, "Amendment window has closed. Use Addendum to add information.", null);
        }

        // In Modes 1/2 the client must NOT send codes
        if (mode != 3 && (req.CptSelections != null || req.IcdSelections != null))
            return (false, "Codes cannot be changed in this amendment mode (encounter is still open).", null);

        // In Mode 3, codes must be supplied and at least one CPT required (same rule as checkout)
        if (mode == 3)
        {
            if (req.CptSelections == null || req.CptSelections.Count == 0)
                return (false, "At least one CPT code is required.", null);
        }

        // Open a transaction for atomic save
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Next version number
            int nextVersion = 2;
            var existingMax = await _context.ClinicalNoteAmendments
                .Where(a => a.ClinicalNoteId == noteId)
                .Select(a => (int?)a.VersionNumber)
                .MaxAsync();
            if (existingMax.HasValue) nextVersion = existingMax.Value + 1;

            var now = DateTime.UtcNow;

            var amendment = new ClinicalNoteAmendment
            {
                ClinicalNoteId = noteId,
                TenantId = tenantId,
                VersionNumber = nextVersion,
                HtmlContent = EncryptContent(req.HtmlContent) ?? req.HtmlContent,
                Reason = req.Reason.Trim(),
                SignedAt = now,
                SignedByUserId = currentUserId,
                SignatureData = EncryptContent(req.SignatureData),
                CreatedAt = now
            };
            _context.ClinicalNoteAmendments.Add(amendment);

            // Touch the parent note's UpdatedAt
            note.UpdatedAt = now;
            note.UpdatedByUserId = currentUserId;

            // Mode 3 — sync encounter codes, charges, and claim snapshot.
            //
            // Some historical notes have a null EncounterId even though an encounter
            // does exist for their appointment. In that case the direct lookup fails;
            // we fall back to resolving the encounter via AppointmentId. Without this
            // fallback, amendments on those notes silently skipped the sync block —
            // Encounter.CptSelections / IcdSelections stayed unchanged and the next
            // amendment opened with an empty "Selected Codes" cart.
            if (mode == 3)
            {
                Encounter? encounter = null;
                if (note.EncounterId.HasValue)
                {
                    encounter = await _context.Encounters
                        .Where(e => e.EncounterId == note.EncounterId.Value)
                        .FirstOrDefaultAsync();
                }
                if (encounter == null && note.AppointmentId.HasValue)
                {
                    encounter = await _context.Encounters
                        .Where(e => e.AppointmentId == note.AppointmentId.Value && e.TenantId == tenantId)
                        .OrderByDescending(e => e.CreatedAt)
                        .FirstOrDefaultAsync();

                    // Repair: back-link the note to the encounter so future reads
                    // (amendment modal pre-fill, etc.) take the fast path.
                    if (encounter != null)
                    {
                        note.EncounterId = encounter.EncounterId;
                    }
                }

                if (encounter != null)
                {
                    // Store as JSON in the same format the encounter's Dx & CPT step writes:
                    //   CPT → full objects (cptCode, description, units, rationale, aiSuggested)
                    //   ICD → full objects (code, description, aiSuggested)
                    // This keeps Encounter.CptSelections / IcdSelections round-trip-consistent
                    // regardless of which path (encounter step OR amendment) did the last write.
                    var cptForEncounter = req.CptSelections.Select(c => new {
                        cptCode = c.CptCode,
                        description = c.Description ?? "",
                        units = c.Units,
                        rationale = "",
                        aiSuggested = false
                    });
                    encounter.CptSelections = JsonSerializer.Serialize(cptForEncounter);

                    var icdForEncounter = (req.IcdSelections ?? new List<IcdSelectionItem>()).Select(i => new {
                        code = i.Code,
                        description = i.Description ?? "",
                        aiSuggested = i.AiSuggested
                    });
                    encounter.IcdSelections = JsonSerializer.Serialize(icdForEncounter);
                    encounter.UpdatedAt = now;

                    // Sync Charges: replace existing unposted charges with the new CPT list.
                    //
                    // IMPORTANT: the claim for this encounter was auto-created at checkout
                    // (CoreServices.AutoCreateDraftClaimAsync), so charges here need to be
                    // linked to that claim via ClaimId. Without ClaimId, new charges are
                    // orphaned and the claim renders "No Service Line Added" on screen.
                    var appointmentId = note.AppointmentId;
                    if (appointmentId.HasValue)
                    {
                        // Find the draft claim for this note/encounter so we can link
                        // any newly-added charges to it. If there's no claim (e.g. self-pay),
                        // newCharges just get no ClaimId and remain Pending, same as today.
                        var existingClaim = await _context.BillingClaims
                            .Where(b => b.ClinicalNoteId == noteId && b.TenantId == tenantId)
                            .OrderByDescending(b => b.CreatedAt)
                            .FirstOrDefaultAsync();

                        var existingCharges = await _context.Charges
                            .Where(c => c.AppointmentId == appointmentId.Value && c.TenantId == tenantId)
                            .ToListAsync();

                        var newCodes = req.CptSelections!.Select(c => c.CptCode).Where(s => !string.IsNullOrEmpty(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                        // Remove charges no longer present
                        foreach (var c in existingCharges)
                        {
                            if (!newCodes.Contains(c.Cptcode ?? ""))
                            {
                                _context.Charges.Remove(c);
                            }
                        }

                        // Add charges for new codes + update units/description on existing
                        var existingByCode = existingCharges
                            .Where(c => !string.IsNullOrEmpty(c.Cptcode))
                            .GroupBy(c => c.Cptcode!, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                        foreach (var item in req.CptSelections!)
                        {
                            if (string.IsNullOrEmpty(item.CptCode)) continue;
                            if (existingByCode.TryGetValue(item.CptCode, out var existing))
                            {
                                existing.Units = item.Units;
                                if (!string.IsNullOrEmpty(item.Description))
                                    existing.Cptdescription = item.Description;
                                // Ensure stale existing rows still point at the claim
                                // (they normally already do, but repair if they don't).
                                if (existingClaim != null && !existing.ClaimId.HasValue)
                                {
                                    existing.ClaimId = existingClaim.ClaimId;
                                    existing.Status = (int)ChargeStatus.Billed;
                                }
                            }
                            else
                            {
                                // BUGFIX (2026-04-20): previously this branch created the Charge
                                // without ClaimId → the claim's detail view showed "No Service
                                // Line Added" because the Include(c.Charges) filter couldn't
                                // find charges with claim.ClaimId. Now we link on creation.
                                _context.Charges.Add(new Charge
                                {
                                    TenantId = tenantId,
                                    PatientId = encounter.PatientId,
                                    AppointmentId = appointmentId.Value,
                                    ClinicalNoteId = noteId,
                                    ProviderId = encounter.ProviderId,
                                    ServiceDate = encounter.EncounterDate,
                                    Cptcode = item.CptCode,
                                    Cptdescription = item.Description,
                                    Units = item.Units,
                                    ChargeAmount = 0,
                                    // Link to the claim so it appears as a service line
                                    ClaimId = existingClaim?.ClaimId,
                                    // Match the status used when the claim was created (Billed
                                    // if we have a claim, Pending otherwise).
                                    Status = existingClaim != null
                                        ? (int)ChargeStatus.Billed
                                        : (int)ChargeStatus.Pending,
                                    CreatedAt = now
                                });
                            }
                        }
                    }

                    // Update BillingClaim snapshot(s) for this encounter
                    var claims = await _context.BillingClaims
                        .Where(b => b.ClinicalNoteId == noteId && b.TenantId == tenantId)
                        .ToListAsync();

                    if (claims.Any() && req.IcdSelections != null)
                    {
                        // BillingClaim.DiagnosisCodes is a snapshot of code STRINGS only
                        // (matches CMS-1500 Box 21: 12 codes, letters A-L). Don't store
                        // full objects here — we'd break billers' existing list views
                        // and claim form rendering which expect a flat string array.
                        var diagCodesOnly = req.IcdSelections
                            .Select(i => i.Code)
                            .Where(c => !string.IsNullOrEmpty(c))
                            .ToList();
                        var diagJson = JsonSerializer.Serialize(diagCodesOnly);
                        foreach (var claim in claims)
                        {
                            claim.DiagnosisCodes = diagJson;
                            claim.UpdatedAt = now;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // Re-generate encounter summary with the latest amended content (fire-and-forget).
            TriggerEncounterSummary(note.EncounterId);

            return (true, null, nextVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save amendment for note {NoteId}", noteId);
            await tx.RollbackAsync();
            return (false, "Failed to save amendment. Please try again.", null);
        }
    }

    // ------------------------------------------------------------
    // Create addendum (always allowed for the original signer)
    // ------------------------------------------------------------
    public async Task<(bool Ok, string? Error, int? AddendumId)> CreateAddendumAsync(int noteId, CreateAddendumRequest req, int currentUserId, int currentUserRole)
    {
        if (currentUserRole != CLINICIAN_ROLE)
            return (false, "Only clinicians can add an addendum.", null);

        if (string.IsNullOrWhiteSpace(req.Content))
            return (false, "Addendum content is required.", null);

        if (string.IsNullOrWhiteSpace(req.Reason))
            return (false, "Reason for addendum is required.", null);

        if (req.Reason.Length > 500)
            return (false, "Reason must be 500 characters or fewer.", null);

        if (string.IsNullOrWhiteSpace(req.SignatureData))
            return (false, "Signature is required.", null);

        var tenantId = TenantId;
        var note = await _context.ClinicalNotes
            .Where(n => n.ClinicalNoteId == noteId && n.TenantId == tenantId)
            .FirstOrDefaultAsync();

        if (note == null) return (false, "Note not found.", null);
        if (note.Status != NOTE_STATUS_SIGNED) return (false, "Note must be signed to add an addendum.", null);
        if (!note.SignedByUserId.HasValue || note.SignedByUserId.Value != currentUserId)
            return (false, "Only the original signing clinician can add an addendum.", null);

        var now = DateTime.UtcNow;
        var addendum = new ClinicalNoteAddendum
        {
            ClinicalNoteId = noteId,
            TenantId = tenantId,
            Content = EncryptContent(req.Content) ?? req.Content,
            Reason = req.Reason.Trim(),
            SignedAt = now,
            SignedByUserId = currentUserId,
            SignatureData = EncryptContent(req.SignatureData),
            CreatedAt = now
        };
        _context.ClinicalNoteAddendums.Add(addendum);

        note.UpdatedAt = now;
        note.UpdatedByUserId = currentUserId;

        await _context.SaveChangesAsync();

        // Re-generate encounter summary so addendum content is reflected (fire-and-forget).
        TriggerEncounterSummary(note.EncounterId);

        return (true, null, addendum.AddendumId);
    }

    // ------------------------------------------------------------
    // Claim submit lock check (used by billing submit endpoint)
    // ------------------------------------------------------------
    public async Task<bool> IsClaimSubmitLockedAsync(int claimId)
    {
        var windowHours = GetWindowHours();
        if (windowHours <= 0) return false;

        var claim = await _context.BillingClaims
            .Where(c => c.ClaimId == claimId)
            .Select(c => new { c.ClinicalNoteId, c.AppointmentId })
            .FirstOrDefaultAsync();
        if (claim == null) return false;

        // Prefer note-based linkage, fall back to appointment
        DateTime? checkOutTime = null;
        if (claim.ClinicalNoteId.HasValue)
        {
            checkOutTime = await _context.ClinicalNotes
                .Where(n => n.ClinicalNoteId == claim.ClinicalNoteId.Value)
                .Join(_context.Appointments, n => n.AppointmentId, a => a.AppointmentId, (n, a) => a.CheckOutTime)
                .FirstOrDefaultAsync();
        }
        if (!checkOutTime.HasValue && claim.AppointmentId.HasValue)
        {
            checkOutTime = await _context.Appointments
                .Where(a => a.AppointmentId == claim.AppointmentId.Value)
                .Select(a => a.CheckOutTime)
                .FirstOrDefaultAsync();
        }

        if (!checkOutTime.HasValue) return false;

        return DateTime.UtcNow < checkOutTime.Value.AddHours(windowHours);
    }
}
