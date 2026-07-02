using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface ITherapistUnavailabilityService
{
    Task<List<TherapistUnavailabilityListDto>> GetUnavailabilitiesAsync(
        int? providerId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool includeUnapproved = false);
    
    Task<TherapistUnavailabilityListDto?> GetByIdAsync(int unavailabilityId);
    
    Task<TherapistUnavailability> CreateAsync(TherapistUnavailabilityCreateDto dto, int createdByUserId);
    
    Task<TherapistUnavailability?> UpdateAsync(int unavailabilityId, TherapistUnavailabilityUpdateDto dto, int? userRole = null);
    
    Task<bool> DeleteAsync(int unavailabilityId);
    
    Task<bool> ApproveAsync(int unavailabilityId, int approvedByUserId);
    
    Task<UnavailabilityCheckResult> CheckAvailabilityAsync(UnavailabilityCheckRequest request);
    
    Task<List<TherapistUnavailabilityListDto>> GetProviderUnavailabilitiesForDateRangeAsync(
        int providerId,
        DateTime startDate,
        DateTime endDate);
    
    Task<bool> IsProviderAvailableAsync(int providerId, DateTime startTime, DateTime endTime);
    
    Task<List<CalendarEventDto>> GetCalendarEventsAsync(int? providerId, DateOnly startDate, DateOnly endDate);
}

public class TherapistUnavailabilityService : ITherapistUnavailabilityService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper? _encryptionHelper;

    public TherapistUnavailabilityService(EhrDbContext context, ITenantProvider tenantProvider, EncryptionHelper? encryptionHelper = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
    }
    
    public async Task<List<TherapistUnavailabilityListDto>> GetUnavailabilitiesAsync(
        int? providerId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool includeUnapproved = false)
    {
        // AsNoTracking: read-only list; Provider/User decrypted for DTO.
        var query = _context.TherapistUnavailabilities
            .AsNoTracking()
            .Include(u => u.Provider)
            .AsQueryable();
        
        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(u => u.TenantId == _tenantProvider.TenantId.Value);
        
        if (providerId.HasValue)
            query = query.Where(u => u.ProviderId == providerId.Value);
        
        if (startDate.HasValue)
        {
            var startDateOnly = DateOnly.FromDateTime(startDate.Value);
            query = query.Where(u => u.EndDate >= startDateOnly);
        }
        
        if (endDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(endDate.Value);
            query = query.Where(u => u.StartDate <= endDateOnly);
        }
        
        if (!includeUnapproved)
            query = query.Where(u => u.IsApproved);
        
        var results = await query
            .OrderBy(u => u.StartDate)
            .ToListAsync();

        // Decrypt PHI fields for Provider
        if (_encryptionHelper != null)
        {
            foreach (var u in results)
            {
                if (u.Provider != null)
                    _encryptionHelper.DecryptEntity(u.Provider);
            }
        }

        // Get approver names
        // AsNoTracking on Users lookup to avoid tracking decrypted User entities.
        var approverIds = results.Where(u => u.ApprovedByUserId.HasValue).Select(u => u.ApprovedByUserId!.Value).Distinct().ToList();
        var approvers = await _context.Users.AsNoTracking().Where(u => approverIds.Contains(u.UserId)).ToListAsync();
        if (_encryptionHelper != null)
        {
            foreach (var approver in approvers)
                _encryptionHelper.DecryptEntity(approver);
        }
        var approverNames = approvers.ToDictionary(u => u.UserId, u => $"{u.FirstName} {u.LastName}");
        
        return results.Select(u => new TherapistUnavailabilityListDto
        {
            UnavailabilityId = u.UnavailabilityId,
            ProviderId = u.ProviderId,
            ProviderName = u.Provider != null ? $"{u.Provider.FirstName} {u.Provider.LastName}" : "",
            ProviderColor = u.Provider?.Color,
            StartDate = u.StartDate,
            EndDate = u.EndDate,
            Type = u.Type,
            Reason = u.Reason,
            IsFullDay = u.IsFullDay,
            StartTime = u.StartTime,
            EndTime = u.EndTime,
            IsApproved = u.IsApproved,
            ApprovedByName = u.ApprovedByUserId.HasValue && approverNames.ContainsKey(u.ApprovedByUserId.Value)
                ? approverNames[u.ApprovedByUserId.Value]
                : null,
            ApprovedAt = u.ApprovedAt
        }).ToList();
    }
    
    public async Task<TherapistUnavailabilityListDto?> GetByIdAsync(int unavailabilityId)
    {
        // AsNoTracking: read-only detail; Provider decrypted for DTO.
        var query = _context.TherapistUnavailabilities
            .AsNoTracking()
            .Include(u => u.Provider)
            .Where(u => u.UnavailabilityId == unavailabilityId);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == _tenantProvider.TenantId.Value);
        }

        var u = await query.FirstOrDefaultAsync();

        if (u == null) return null;

        // Decrypt PHI fields for Provider
        if (_encryptionHelper != null && u.Provider != null)
            _encryptionHelper.DecryptEntity(u.Provider);

        string? approverName = null;
        if (u.ApprovedByUserId.HasValue)
        {
            // User.FindAsync is tracked — detach before decrypt for safety.
            var approver = await _context.Users.FindAsync(u.ApprovedByUserId.Value);
            if (approver != null)
            {
                _context.Entry(approver).State = EntityState.Detached;
                _encryptionHelper?.DecryptEntity(approver);
                approverName = $"{approver.FirstName} {approver.LastName}";
            }
        }
        
        return new TherapistUnavailabilityListDto
        {
            UnavailabilityId = u.UnavailabilityId,
            ProviderId = u.ProviderId,
            ProviderName = u.Provider != null ? $"{u.Provider.FirstName} {u.Provider.LastName}" : "",
            ProviderColor = u.Provider?.Color,
            StartDate = u.StartDate,
            EndDate = u.EndDate,
            Type = u.Type,
            Reason = u.Reason,
            IsFullDay = u.IsFullDay,
            StartTime = u.StartTime,
            EndTime = u.EndTime,
            IsApproved = u.IsApproved,
            ApprovedByName = approverName,
            ApprovedAt = u.ApprovedAt
        };
    }
    
    public async Task<TherapistUnavailability> CreateAsync(TherapistUnavailabilityCreateDto dto, int createdByUserId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            throw new InvalidOperationException("Tenant context required");
        
        // Validate dates
        if (dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("End date cannot be before start date");
        
        // Check for overlapping unavailability
        var existing = await _context.TherapistUnavailabilities
            .Where(u => u.TenantId == _tenantProvider.TenantId.Value
                       && u.ProviderId == dto.ProviderId
                       && u.StartDate <= dto.EndDate
                       && u.EndDate >= dto.StartDate)
            .AnyAsync();
        
        if (existing)
            throw new InvalidOperationException("Overlapping unavailability period exists for this provider");
        
        var unavailability = new TherapistUnavailability
        {
            TenantId = _tenantProvider.TenantId.Value,
            ProviderId = dto.ProviderId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Type = dto.Type,
            Reason = dto.Reason,
            IsFullDay = dto.IsFullDay,
            // Only set times for partial day (IsFullDay = false)
            StartTime = dto.IsFullDay ? null : dto.StartTime,
            EndTime = dto.IsFullDay ? null : dto.EndTime,
            RecurrencePattern = dto.RecurrencePattern,
            IsApproved = false, // Requires approval
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId
        };
        
        _context.TherapistUnavailabilities.Add(unavailability);
        await _context.SaveChangesAsync();
        
        return unavailability;
    }
    
    public async Task<TherapistUnavailability?> UpdateAsync(int unavailabilityId, TherapistUnavailabilityUpdateDto dto, int? userRole = null)
    {
        var unavailability = await _context.TherapistUnavailabilities
            .FirstOrDefaultAsync(u => u.UnavailabilityId == unavailabilityId);

        if (unavailability == null)
            return null;

        // CRITICAL: Tenant data isolation - verify unavailability belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && unavailability.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Prevent non-admins (Role > 1) from editing approved time off requests
        // Role 0 = SuperAdmin, Role 1 = ClinicAdmin, Role 2+ = Clinician/Staff
        if (unavailability.IsApproved && userRole.HasValue && userRole.Value > 1)
        {
            throw new InvalidOperationException("Cannot edit an approved time off request. Please contact your administrator.");
        }

        if (dto.StartDate.HasValue) unavailability.StartDate = dto.StartDate.Value;
        if (dto.EndDate.HasValue) unavailability.EndDate = dto.EndDate.Value;
        if (dto.Type.HasValue) unavailability.Type = dto.Type.Value;
        if (dto.Reason != null) unavailability.Reason = dto.Reason;
        if (dto.RecurrencePattern != null) unavailability.RecurrencePattern = dto.RecurrencePattern;

        // Handle IsFullDay and time fields together
        if (dto.IsFullDay.HasValue)
        {
            unavailability.IsFullDay = dto.IsFullDay.Value;
            if (dto.IsFullDay.Value)
            {
                // Full day - clear time fields
                unavailability.StartTime = null;
                unavailability.EndTime = null;
            }
            else
            {
                // Partial day - set time fields from dto
                unavailability.StartTime = dto.StartTime;
                unavailability.EndTime = dto.EndTime;
            }
        }
        else
        {
            // IsFullDay not being changed, but times might be updated
            if (dto.StartTime.HasValue) unavailability.StartTime = dto.StartTime;
            if (dto.EndTime.HasValue) unavailability.EndTime = dto.EndTime;
        }

        unavailability.UpdatedAt = DateTime.UtcNow;

        // Reset approval if dates changed (only applies to admins who can edit approved records)
        if (dto.StartDate.HasValue || dto.EndDate.HasValue)
        {
            unavailability.IsApproved = false;
            unavailability.ApprovedByUserId = null;
            unavailability.ApprovedAt = null;
        }

        await _context.SaveChangesAsync();
        return unavailability;
    }
    
    public async Task<bool> DeleteAsync(int unavailabilityId)
    {
        var unavailability = await _context.TherapistUnavailabilities
            .FirstOrDefaultAsync(u => u.UnavailabilityId == unavailabilityId);

        if (unavailability == null)
            return false;

        // CRITICAL: Tenant data isolation - verify unavailability belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && unavailability.TenantId != _tenantProvider.TenantId.Value)
            return false;

        _context.TherapistUnavailabilities.Remove(unavailability);
        await _context.SaveChangesAsync();
        return true;
    }
    
    public async Task<bool> ApproveAsync(int unavailabilityId, int approvedByUserId)
    {
        var unavailability = await _context.TherapistUnavailabilities
            .FirstOrDefaultAsync(u => u.UnavailabilityId == unavailabilityId);

        if (unavailability == null)
            return false;

        // CRITICAL: Tenant data isolation - verify unavailability belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && unavailability.TenantId != _tenantProvider.TenantId.Value)
            return false;

        unavailability.IsApproved = true;
        unavailability.ApprovedByUserId = approvedByUserId;
        unavailability.ApprovedAt = DateTime.UtcNow;
        unavailability.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return true;
    }
    
    public async Task<UnavailabilityCheckResult> CheckAvailabilityAsync(UnavailabilityCheckRequest request)
    {
        var conflict = await GetConflictingUnavailabilityAsync(
            request.ProviderId,
            request.StartTime,
            request.EndTime);
        
        if (conflict == null)
        {
            return new UnavailabilityCheckResult
            {
                IsAvailable = true
            };
        }
        
        return new UnavailabilityCheckResult
        {
            IsAvailable = false,
            ConflictReason = $"Provider is unavailable ({conflict.TypeName}: {conflict.Reason ?? "No reason specified"})",
            ConflictingUnavailability = conflict
        };
    }
    
    public async Task<List<TherapistUnavailabilityListDto>> GetProviderUnavailabilitiesForDateRangeAsync(
        int providerId,
        DateTime startDate,
        DateTime endDate)
    {
        return await GetUnavailabilitiesAsync(
            providerId: providerId,
            startDate: startDate,
            endDate: endDate,
            includeUnapproved: false);
    }
    
    public async Task<bool> IsProviderAvailableAsync(int providerId, DateTime startTime, DateTime endTime)
    {
        var conflict = await GetConflictingUnavailabilityAsync(providerId, startTime, endTime);
        return conflict == null;
    }
    
    public async Task<List<CalendarEventDto>> GetCalendarEventsAsync(int? providerId, DateOnly startDate, DateOnly endDate)
    {
        // AsNoTracking: read-only calendar event projection; Provider decrypted.
        var query = _context.TherapistUnavailabilities
            .AsNoTracking()
            .Include(u => u.Provider)
            .Where(u => u.IsApproved && u.StartDate <= endDate && u.EndDate >= startDate);
        
        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(u => u.TenantId == _tenantProvider.TenantId.Value);
        
        if (providerId.HasValue)
            query = query.Where(u => u.ProviderId == providerId.Value);

        var unavailabilities = await query.ToListAsync();

        // Decrypt PHI fields for Provider
        if (_encryptionHelper != null)
        {
            foreach (var u in unavailabilities)
            {
                if (u.Provider != null)
                    _encryptionHelper.DecryptEntity(u.Provider);
            }
        }

        return unavailabilities.Select(u => new CalendarEventDto
        {
            Id = $"unavail-{u.UnavailabilityId}",
            Title = $"{u.Provider?.FirstName} {u.Provider?.LastName} - {GetTypeName(u.Type)}",
            Start = u.StartDate.ToDateTime(u.StartTime ?? TimeOnly.MinValue),
            End = u.EndDate.ToDateTime(u.EndTime ?? TimeOnly.MaxValue),
            BackgroundColor = "#9e9e9e",
            BorderColor = "#616161",
            TextColor = "#ffffff",
            Display = "background",
            AllDay = u.IsFullDay,
            ExtendedProps = new Dictionary<string, object>
            {
                { "isUnavailability", true },
                { "providerId", u.ProviderId },
                { "providerName", $"{u.Provider?.FirstName} {u.Provider?.LastName}" },
                { "type", u.Type },
                { "typeName", GetTypeName(u.Type) },
                { "reason", u.Reason ?? "" }
            }
        }).ToList();
    }
    
    private async Task<TherapistUnavailabilityListDto?> GetConflictingUnavailabilityAsync(
        int providerId,
        DateTime startTime,
        DateTime endTime)
    {
        var date = DateOnly.FromDateTime(startTime);
        var requestStartTime = TimeOnly.FromDateTime(startTime);
        var requestEndTime = TimeOnly.FromDateTime(endTime);
        
        // AsNoTracking: read-only conflict check; Provider decrypted for DTO.
        var query = _context.TherapistUnavailabilities
            .AsNoTracking()
            .Include(u => u.Provider)
            .Where(u => u.IsApproved
                       && u.ProviderId == providerId
                       && u.StartDate <= date
                       && u.EndDate >= date);
        
        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(u => u.TenantId == _tenantProvider.TenantId.Value);
        
        var unavailabilities = await query.ToListAsync();

        foreach (var u in unavailabilities)
        {
            // Decrypt Provider if needed
            if (_encryptionHelper != null && u.Provider != null)
                _encryptionHelper.DecryptEntity(u.Provider);

            // Full day unavailability always conflicts
            if (u.IsFullDay)
            {
                return MapToDto(u);
            }

            // Partial day - check time overlap
            if (u.StartTime.HasValue && u.EndTime.HasValue)
            {
                if (requestStartTime < u.EndTime.Value && requestEndTime > u.StartTime.Value)
                {
                    return MapToDto(u);
                }
            }
        }

        return null;
    }

    private TherapistUnavailabilityListDto MapToDto(TherapistUnavailability u)
    {
        return new TherapistUnavailabilityListDto
        {
            UnavailabilityId = u.UnavailabilityId,
            ProviderId = u.ProviderId,
            ProviderName = u.Provider != null ? $"{u.Provider.FirstName} {u.Provider.LastName}" : "",
            ProviderColor = u.Provider?.Color,
            StartDate = u.StartDate,
            EndDate = u.EndDate,
            Type = u.Type,
            Reason = u.Reason,
            IsFullDay = u.IsFullDay,
            StartTime = u.StartTime,
            EndTime = u.EndTime,
            IsApproved = u.IsApproved
        };
    }
    
    private static string GetTypeName(int type) => type switch
    {
        0 => "Vacation",
        1 => "Sick Leave",
        2 => "Training",
        3 => "Personal Leave",
        4 => "Holiday",
        5 => "Conference",
        6 => "Admin Time",
        _ => "Other"
    };
}
