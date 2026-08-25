using System.Security.Claims;
using System.Text.Json;

namespace EHR.Services;

/// <summary>
/// Records DME state changes to the AuditLogs table.
///
/// WHY THIS EXISTS
/// The PhiAccessAudit filter already logs one row per successful request, which
/// answers "who looked at this". It cannot answer "what did they change it
/// from, and to what", because by the time a filter runs the old values are
/// gone. An audit trail that only proves someone opened a page is cosmetic; a
/// trail that reconstructs the change is the one a HIPAA investigation or a
/// billing dispute actually needs.
///
/// WHAT IT DOES
/// Pulls the caller identity and IP off the current request so no call site has
/// to remember to, serialises the before/after state, and hands it to the
/// existing IAuditService. It owns the DME action naming so the log stays
/// queryable ("DME_ORDER_DELIVERED", not eleven spellings of the same event).
///
/// WHAT IT DOES NOT DO
/// It never throws into the caller. A failed audit write must not roll back a
/// delivery that physically happened, and IAuditService already swallows and
/// isolates its own write. The tradeoff is deliberate and matches the clinical
/// side.
///
/// WHO CALLS IT
/// DmeController, from each mutating action.
/// </summary>
public interface IDmeAudit
{
    /// <summary>
    /// Record one state change. <paramref name="before"/> may be null for a
    /// create. <paramref name="after"/> may be null for a delete.
    /// </summary>
    Task RecordAsync(string action, string entityType, int? entityId, object? before, object? after);
}

/// <inheritdoc cref="IDmeAudit"/>
public sealed class DmeAudit : IDmeAudit
{
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _http;

    // Compact on purpose: these rows are read by humans scanning for a change,
    // and indented JSON triples the storage for no added meaning.
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public DmeAudit(IAuditService auditService, IHttpContextAccessor http)
    {
        _auditService = auditService;
        _http = http;
    }

    public async Task RecordAsync(string action, string entityType, int? entityId, object? before, object? after)
    {
        var ctx = _http.HttpContext;
        var user = ctx?.User;

        int? userId = int.TryParse(
            user?.FindFirst("UserId")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out var uid) ? uid : null;

        await _auditService.LogAccessAsync(
            userId,
            user?.FindFirst(ClaimTypes.Email)?.Value,
            action,
            entityType,
            entityId,
            oldValues: Serialise(before),
            newValues: Serialise(after),
            ipAddress: ctx?.Connection.RemoteIpAddress?.ToString());
    }

    /// <summary>
    /// Serialise a snapshot for storage. Returns null rather than "null" so an
    /// absent side of the change reads as absent in the log.
    /// </summary>
    private static string? Serialise(object? value)
    {
        if (value == null) return null;
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (Exception)
        {
            // A snapshot that will not serialise must not cost us the audit row
            // entirely. Record that the change happened and that the detail was
            // lost, which is still far more than no row at all.
            return "{\"error\":\"snapshot could not be serialised\"}";
        }
    }
}
