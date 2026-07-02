using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using System.Text.Json;

namespace EHR.Services;

/// <summary>
/// HIPAA-compliant audit logging service.
/// Tracks all access to Protected Health Information (PHI).
/// Required for HIPAA Security Rule compliance (45 CFR 164.312(b)).
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Log PHI access event (simplified)
    /// </summary>
    Task LogAccessAsync(
        int? userId,
        string? userEmail,
        string action,
        string entityType,
        int? entityId,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null);

    /// <summary>
    /// Get audit logs for a specific entity (e.g., patient access history)
    /// </summary>
    Task<List<AuditLogDto>> GetEntityAuditLogsAsync(string entityType, int entityId, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Get audit logs for a specific user (for compliance reviews)
    /// </summary>
    Task<List<AuditLogDto>> GetUserAuditLogsAsync(int userId, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Get all audit logs (for compliance officers)
    /// </summary>
    Task<List<AuditLogDto>> GetAuditLogsAsync(AuditLogFilter filter);
}

public class AuditService : IAuditService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;

    public AuditService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }

    public async Task LogAccessAsync(
        int? userId,
        string? userEmail,
        string action,
        string entityType,
        int? entityId,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null)
    {
        // CRITICAL (PHI encryption safety):
        // Use raw SQL instead of _context.SaveChangesAsync() so this write does
        // NOT flush other tracked entities in the shared request-scoped DbContext.
        // Calling SaveChangesAsync here would persist any decrypted Patient/Provider/
        // Insurance entities that were mutated via EncryptionHelper.DecryptEntity
        // earlier in the same request, silently corrupting PHI from encrypted to
        // plaintext in the database. ExecuteSqlInterpolatedAsync bypasses the EF
        // Core change tracker entirely.
        var tenantId = _tenantProvider.TenantId;
        var timestamp = DateTime.UtcNow;
        var ip = ipAddress ?? "Unknown";

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO AuditLogs (TenantId, UserId, UserEmail, Action, EntityType, EntityId, OldValues, NewValues, IpAddress, Timestamp)
                VALUES ({tenantId}, {userId}, {userEmail}, {action}, {entityType}, {entityId}, {oldValues}, {newValues}, {ip}, {timestamp})");
        }
        catch (Exception ex)
        {
            // Log to console/file if DB logging fails - HIPAA requires we never lose audit data
            var snapshot = new
            {
                TenantId = tenantId,
                UserId = userId,
                UserEmail = userEmail,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValues = oldValues,
                NewValues = newValues,
                IpAddress = ip,
                Timestamp = timestamp
            };
            Console.Error.WriteLine($"AUDIT LOG FAILURE: {JsonSerializer.Serialize(snapshot)} - Error: {ex.Message}");
        }
    }

    public async Task<List<AuditLogDto>> GetEntityAuditLogsAsync(string entityType, int entityId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.AuditLogs
            .Where(l => l.EntityType == entityType && l.EntityId == entityId)
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(l => l.TenantId == _tenantProvider.TenantId.Value);

        if (startDate.HasValue)
            query = query.Where(l => l.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(l => l.Timestamp <= endDate.Value);

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new AuditLogDto
            {
                AuditId = l.AuditId,
                Timestamp = l.Timestamp ?? DateTime.MinValue,
                UserId = l.UserId,
                UserEmail = l.UserEmail,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                OldValues = l.OldValues,
                NewValues = l.NewValues,
                Changes = l.Changes,
                IpAddress = l.IpAddress
            })
            .ToListAsync();
    }

    public async Task<List<AuditLogDto>> GetUserAuditLogsAsync(int userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.AuditLogs
            .Where(l => l.UserId == userId)
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(l => l.TenantId == _tenantProvider.TenantId.Value);

        if (startDate.HasValue)
            query = query.Where(l => l.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(l => l.Timestamp <= endDate.Value);

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new AuditLogDto
            {
                AuditId = l.AuditId,
                Timestamp = l.Timestamp ?? DateTime.MinValue,
                UserId = l.UserId,
                UserEmail = l.UserEmail,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                OldValues = l.OldValues,
                NewValues = l.NewValues,
                Changes = l.Changes,
                IpAddress = l.IpAddress
            })
            .ToListAsync();
    }

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(AuditLogFilter filter)
    {
        var query = _context.AuditLogs.AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(l => l.TenantId == _tenantProvider.TenantId.Value);

        if (filter.UserId.HasValue)
            query = query.Where(l => l.UserId == filter.UserId.Value);

        if (!string.IsNullOrEmpty(filter.EntityType))
            query = query.Where(l => l.EntityType == filter.EntityType);

        if (filter.EntityId.HasValue)
            query = query.Where(l => l.EntityId == filter.EntityId.Value);

        if (!string.IsNullOrEmpty(filter.Action))
            query = query.Where(l => l.Action.Contains(filter.Action));

        if (filter.StartDate.HasValue)
            query = query.Where(l => l.Timestamp >= filter.StartDate.Value);

        if (filter.EndDate.HasValue)
            query = query.Where(l => l.Timestamp <= filter.EndDate.Value);

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Take(filter.Limit ?? 1000)
            .Select(l => new AuditLogDto
            {
                AuditId = l.AuditId,
                Timestamp = l.Timestamp ?? DateTime.MinValue,
                UserId = l.UserId,
                UserEmail = l.UserEmail,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                OldValues = l.OldValues,
                NewValues = l.NewValues,
                Changes = l.Changes,
                IpAddress = l.IpAddress
            })
            .ToListAsync();
    }
}

/// <summary>
/// Audit log DTO for API responses
/// </summary>
public class AuditLogDto
{
    public long AuditId { get; set; }
    public DateTime Timestamp { get; set; }
    public int? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// Filter for querying audit logs
/// </summary>
public class AuditLogFilter
{
    public int? UserId { get; set; }
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? Action { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? Limit { get; set; } = 1000;
}

/// <summary>
/// Common audit actions for HIPAA compliance
/// </summary>
public static class AuditActions
{
    // Read operations
    public const string ViewPatient = "VIEW_PATIENT";
    public const string ViewPatientList = "VIEW_PATIENT_LIST";
    public const string ViewClinicalNote = "VIEW_CLINICAL_NOTE";
    public const string ViewInsurance = "VIEW_INSURANCE";
    public const string SearchPatients = "SEARCH_PATIENTS";
    public const string ExportPatientData = "EXPORT_PATIENT_DATA";

    // Write operations
    public const string CreatePatient = "CREATE_PATIENT";
    public const string UpdatePatient = "UPDATE_PATIENT";
    public const string DeletePatient = "DELETE_PATIENT";
    public const string CreateClinicalNote = "CREATE_CLINICAL_NOTE";
    public const string UpdateClinicalNote = "UPDATE_CLINICAL_NOTE";
    public const string SignClinicalNote = "SIGN_CLINICAL_NOTE";

    // Authentication
    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string Logout = "LOGOUT";
    public const string PasswordChange = "PASSWORD_CHANGE";

    // Administrative
    public const string UserCreated = "USER_CREATED";
    public const string UserUpdated = "USER_UPDATED";
    public const string RoleChanged = "ROLE_CHANGED";
    public const string PermissionChanged = "PERMISSION_CHANGED";

    // PHI Specific
    public const string PhiAccess = "PHI_ACCESS";
    public const string PhiExport = "PHI_EXPORT";
    public const string BreachAttempt = "BREACH_ATTEMPT";
}
