using Microsoft.EntityFrameworkCore;
using EHR.Data;
using EHR.Models;
using EHR.Helpers;

namespace EHR.Services;

public interface ITenantService
{
    Task<List<TenantListDto>> GetAllTenantsAsync();
    Task<Tenant?> GetTenantByIdAsync(int tenantId);
    Task<Tenant?> GetTenantBySubdomainAsync(string subdomain);
    Task<Tenant> CreateTenantAsync(TenantCreateDto dto);
    Task<Tenant?> UpdateTenantAsync(int tenantId, TenantUpdateDto dto);
    Task<bool> DeleteTenantAsync(int tenantId);
    Task<bool> UpdateTenantStatusAsync(int tenantId, TenantStatus status);
    Task<DashboardStatsDto> GetTenantStatsAsync(int tenantId, int? providerId = null, int? locationId = null);
}

public class TenantService : ITenantService
{
    private readonly EhrDbContext _context;
    private readonly ILocationProvider _locationProvider;
    private readonly ILocationService _locationService;

    public TenantService(EhrDbContext context, ILocationProvider locationProvider, ILocationService locationService)
    {
        _context = context;
        _locationProvider = locationProvider;
        _locationService = locationService;
    }
    
    public async Task<List<TenantListDto>> GetAllTenantsAsync()
    {
        return await _context.Tenants
            .Where(t => t.IsDeleted != true)
            .Select(t => new TenantListDto
            {
                TenantId = t.TenantId,
                Name = t.Name,
                Subdomain = t.Subdomain,
                Email = t.Email,
                Plan = t.Plan ?? 0,
                Status = t.Status ?? 0,
                UserCount = t.Users.Count(u => u.IsActive == true),
                PatientCount = t.Patients.Count(p => p.IsDeleted != true),
                CreatedAt = t.CreatedAt ?? DateTime.UtcNow
            })
            .OrderBy(t => t.Name)
            .ToListAsync();
    }
    
    public async Task<Tenant?> GetTenantByIdAsync(int tenantId)
    {
        return await _context.Tenants
            .Include(t => t.Locations)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.IsDeleted != true);
    }
    
    public async Task<Tenant?> GetTenantBySubdomainAsync(string subdomain)
    {
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && t.IsDeleted != true);
    }
    
    public async Task<Tenant> CreateTenantAsync(TenantCreateDto dto)
    {
        // Auto-generate subdomain from clinic name if not provided
        var subdomain = !string.IsNullOrWhiteSpace(dto.Subdomain)
            ? dto.Subdomain.ToLower()
            : GenerateSubdomain(dto.Name);

        // Check if subdomain is unique, add number suffix if needed
        var baseSubdomain = subdomain;
        var counter = 1;
        while (await _context.Tenants.AnyAsync(t => t.Subdomain == subdomain))
        {
            subdomain = $"{baseSubdomain}-{counter}";
            counter++;
        }

        var tenant = new Tenant
        {
            Name = dto.Name,
            Subdomain = subdomain,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            City = dto.City,
            State = dto.State,
            ZipCode = dto.ZipCode,
            TaxId = dto.TaxId,
            Npi = dto.NPI,
            Plan = dto.Plan,
            Status = (int)TenantStatus.Active,
            SubscriptionStartDate = DateTime.UtcNow,
            SubscriptionEndDate = dto.Plan == (int)SubscriptionPlan.Trial 
                ? DateTime.UtcNow.AddDays(30) 
                : DateTime.UtcNow.AddYears(1),
            MaxUsers = dto.Plan switch
            {
                (int)SubscriptionPlan.Trial => 3,
                (int)SubscriptionPlan.Basic => 5,
                (int)SubscriptionPlan.Professional => 20,
                (int)SubscriptionPlan.Enterprise => 100,
                _ => 5
            },
            MaxPatients = dto.Plan switch
            {
                (int)SubscriptionPlan.Trial => 50,
                (int)SubscriptionPlan.Basic => 500,
                (int)SubscriptionPlan.Professional => 5000,
                (int)SubscriptionPlan.Enterprise => 50000,
                _ => 500
            },
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        
        // Create default location
        // Use InitialLocationName if provided, otherwise fall back to tenant name or "Main Office"
        var locationName = !string.IsNullOrWhiteSpace(dto.InitialLocationName)
            ? dto.InitialLocationName
            : !string.IsNullOrWhiteSpace(dto.Name)
                ? dto.Name
                : "Main Office";

        var location = new Location
        {
            TenantId = tenant.TenantId,
            Name = locationName,
            Address = dto.Address,
            City = dto.City,
            State = dto.State,
            ZipCode = dto.ZipCode,
            Phone = dto.Phone,
            TimeZoneId = dto.InitialLocationTimeZoneId ?? "America/Chicago",
            IsPrimary = true,
            IsActive = true,
            PortalCode = Guid.NewGuid().ToString("N")[..8]
        };
        _context.Locations.Add(location);
        
        // Create admin user for the tenant
        var adminUser = new User
        {
            TenantId = tenant.TenantId,
            Email = dto.AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.AdminPassword),
            FirstName = dto.AdminFirstName,
            LastName = dto.AdminLastName,
            Role = (int)UserRole.ClinicAdmin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(adminUser);
        
        await _context.SaveChangesAsync();
        
        return tenant;
    }
    
    public async Task<Tenant?> UpdateTenantAsync(int tenantId, TenantUpdateDto dto)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant == null || tenant.IsDeleted == true)
            return null;
        
        if (dto.Name != null) tenant.Name = dto.Name;
        if (dto.Phone != null) tenant.Phone = dto.Phone;
        if (dto.Email != null) tenant.Email = dto.Email;
        if (dto.Address != null) tenant.Address = dto.Address;
        if (dto.City != null) tenant.City = dto.City;
        if (dto.State != null) tenant.State = dto.State;
        if (dto.ZipCode != null) tenant.ZipCode = dto.ZipCode;
        if (dto.TaxId != null) tenant.TaxId = dto.TaxId;
        if (dto.NPI != null) tenant.Npi = dto.NPI;
        if (dto.LogoUrl != null) tenant.LogoUrl = dto.LogoUrl;
        
        tenant.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return tenant;
    }
    
    public async Task<bool> DeleteTenantAsync(int tenantId)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant == null)
            return false;
        
        tenant.IsDeleted = true;
        tenant.Status = (int)TenantStatus.Cancelled;
        tenant.UpdatedAt = DateTime.UtcNow;
        
        // Deactivate all users
        var users = await _context.Users
            .Where(u => u.TenantId == tenantId)
            .ToListAsync();
        foreach (var user in users)
        {
            user.IsActive = false;
        }
        
        await _context.SaveChangesAsync();
        return true;
    }
    
    public async Task<bool> UpdateTenantStatusAsync(int tenantId, TenantStatus status)
    {
        var tenant = await _context.Tenants.FindAsync(tenantId);
        if (tenant == null)
            return false;
        
        tenant.Status = (int)status;
        tenant.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return true;
    }
    
    public async Task<DashboardStatsDto> GetTenantStatsAsync(int tenantId, int? providerId = null, int? locationId = null)
    {
        // Use provided locationId or fall back to location provider
        var effectiveLocationId = locationId ?? _locationProvider.LocationId;

        // ISSUE #2 FIX: Get location's timezone for "today" calculation
        string? locationTimeZoneId = null;
        if (effectiveLocationId.HasValue)
        {
            locationTimeZoneId = await _locationService.GetLocationTimezoneAsync(effectiveLocationId.Value);
        }
        // Default to Central Time (America/Chicago) if no location specified
        locationTimeZoneId ??= "America/Chicago";

        // Get today's date in the location's timezone
        var nowUtc = DateTime.UtcNow;
        var locationNow = TimezoneHelper.ConvertFromUtc(nowUtc, locationTimeZoneId);
        var todayInLocationTz = locationNow.Date;

        // Convert location's today start/end to UTC for database queries
        var today = TimezoneHelper.GetStartOfDayUtc(DateOnly.FromDateTime(todayInLocationTz), locationTimeZoneId);
        var todayEnd = TimezoneHelper.GetStartOfDayUtc(DateOnly.FromDateTime(todayInLocationTz.AddDays(1)), locationTimeZoneId);
        var authExpiryCutoff = DateOnly.FromDateTime(todayInLocationTz.AddDays(14));
        var paymentDateStart = DateOnly.FromDateTime(todayInLocationTz);
        var paymentDateEnd = DateOnly.FromDateTime(todayInLocationTz.AddDays(1));

        // Base patient query with location filter
        var patientsQuery = _context.Patients
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsDeleted != true);

        if (effectiveLocationId.HasValue)
        {
            patientsQuery = patientsQuery.Where(p => p.PreferredLocationId == effectiveLocationId.Value);
        }

        // For TodayAppointments, filter by provider and location
        var todayAppointmentsQuery = _context.Appointments
            .IgnoreQueryFilters()
            .Include(a => a.Patient)
            .Where(a => a.TenantId == tenantId && a.StartTime >= today && a.StartTime < todayEnd);

        if (effectiveLocationId.HasValue)
        {
            todayAppointmentsQuery = todayAppointmentsQuery.Where(a => a.Patient.PreferredLocationId == effectiveLocationId.Value);
        }

        if (providerId.HasValue)
        {
            todayAppointmentsQuery = todayAppointmentsQuery.Where(a => a.ProviderId == providerId);
        }

        // Completed appointments query with location filter
        var completedQuery = _context.Appointments
            .IgnoreQueryFilters()
            .Include(a => a.Patient)
            .Where(a => a.TenantId == tenantId &&
                       a.StartTime >= today &&
                       a.StartTime < todayEnd &&
                       a.Status == (int)AppointmentStatus.Completed);

        if (effectiveLocationId.HasValue)
        {
            completedQuery = completedQuery.Where(a => a.Patient.PreferredLocationId == effectiveLocationId.Value);
        }

        // No-show appointments query with location filter
        var noShowQuery = _context.Appointments
            .IgnoreQueryFilters()
            .Include(a => a.Patient)
            .Where(a => a.TenantId == tenantId &&
                       a.StartTime >= today &&
                       a.StartTime < todayEnd &&
                       a.Status == (int)AppointmentStatus.NoShow);

        if (effectiveLocationId.HasValue)
        {
            noShowQuery = noShowQuery.Where(a => a.Patient.PreferredLocationId == effectiveLocationId.Value);
        }

        // Clinical notes query - filter by patient's location
        var notesQuery = _context.ClinicalNotes
            .IgnoreQueryFilters()
            .Include(n => n.Patient)
            .Where(n => n.TenantId == tenantId &&
                       (n.Status == (int)ClinicalNoteStatus.Draft ||
                        n.Status == (int)ClinicalNoteStatus.PendingSignature));

        if (effectiveLocationId.HasValue)
        {
            notesQuery = notesQuery.Where(n => n.Patient.PreferredLocationId == effectiveLocationId.Value);
        }

        // Authorizations query - filter by patient's location (via Insurance → Patient)
        var authQuery = _context.Authorizations
            .IgnoreQueryFilters()
            .Include(a => a.Insurance)
                .ThenInclude(i => i.Patient)
            .Where(a => a.TenantId == tenantId &&
                       a.ExpiryDate != null &&
                       a.ExpiryDate <= authExpiryCutoff);

        if (effectiveLocationId.HasValue)
        {
            authQuery = authQuery.Where(a => a.Insurance.Patient.PreferredLocationId == effectiveLocationId.Value);
        }

        var stats = new DashboardStatsDto
        {
            TotalPatients = await patientsQuery.CountAsync(),

            // Active patients: those with active/overdue care episodes OR no care episodes yet (and not archived)
            ActivePatients = await patientsQuery
                .Where(p => !p.IsArchived &&
                    (p.CareEpisodes.Any(ce => ce.Status == (int)CareEpisodeStatus.Active || ce.Status == (int)CareEpisodeStatus.Overdue) ||
                     !p.CareEpisodes.Any()))
                .CountAsync(),

            TodayAppointments = await todayAppointmentsQuery.CountAsync(),

            CompletedToday = await completedQuery.CountAsync(),

            NoShowsToday = await noShowQuery.CountAsync(),

            PendingNotes = await notesQuery.CountAsync(),

            // Payments and billing - these are tenant-wide (not location-specific for now)
            TodayCollections = await _context.Payments
                .IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId &&
                           p.PaymentDate >= paymentDateStart &&
                           p.PaymentDate < paymentDateEnd &&
                           p.Status == (int)PaymentStatus.Completed)
                .SumAsync(p => (decimal?)p.Amount) ?? 0,

            OutstandingAR = await _context.BillingClaims
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId &&
                           (c.Status == (int)ClaimStatus.Submitted ||
                            c.Status == (int)ClaimStatus.Pending))
                .SumAsync(c => (decimal?)c.TotalCharged - (c.TotalPaid ?? 0)) ?? 0,

            ClaimsPending = await _context.BillingClaims
                .IgnoreQueryFilters()
                .CountAsync(c => c.TenantId == tenantId &&
                                c.Status == (int)ClaimStatus.Pending),

            AuthorizationsExpiringSoon = await authQuery.CountAsync()
        };

        return stats;
    }

    /// <summary>
    /// Generates a URL-safe subdomain from a clinic name.
    /// Removes special characters, replaces spaces with hyphens, and converts to lowercase.
    /// </summary>
    private static string GenerateSubdomain(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "clinic";

        // Convert to lowercase and replace spaces with hyphens
        var subdomain = name.ToLower().Trim();

        // Remove apostrophes and similar characters
        subdomain = subdomain.Replace("'", "").Replace("\"", "");

        // Replace spaces and underscores with hyphens
        subdomain = subdomain.Replace(" ", "-").Replace("_", "-");

        // Remove any characters that aren't alphanumeric or hyphens
        subdomain = System.Text.RegularExpressions.Regex.Replace(subdomain, @"[^a-z0-9-]", "");

        // Remove consecutive hyphens
        subdomain = System.Text.RegularExpressions.Regex.Replace(subdomain, @"-+", "-");

        // Remove leading and trailing hyphens
        subdomain = subdomain.Trim('-');

        // Ensure minimum length
        if (string.IsNullOrEmpty(subdomain))
            subdomain = "clinic";

        // Limit length to 63 characters (DNS subdomain limit)
        if (subdomain.Length > 63)
            subdomain = subdomain.Substring(0, 63).TrimEnd('-');

        return subdomain;
    }
}
