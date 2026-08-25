using Microsoft.EntityFrameworkCore;
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
