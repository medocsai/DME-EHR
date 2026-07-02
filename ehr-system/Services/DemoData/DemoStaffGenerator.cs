using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using static EHR.Services.DemoData.DemoDataConstants;

namespace EHR.Services.DemoData;

/// <summary>
/// Generates demo Users, Providers, and Locations.
/// Reuses existing locations/providers if they already exist for the tenant.
/// </summary>
public class DemoStaffGenerator
{
    private readonly EhrDbContext _db;
    private readonly EncryptionHelper _enc;

    public DemoStaffGenerator(EhrDbContext db, EncryptionHelper enc)
    {
        _db = db;
        _enc = enc;
    }

    public record StaffResult(List<Provider> Providers, List<User> Users, List<Location> Locations);

    public async Task<StaffResult> GenerateAsync(int tenantId)
    {
        var providers = new List<Provider>();
        var users = new List<User>();

        // ── Locations: reuse existing, only create if none exist ───
        var locations = await _db.Locations
            .Where(l => l.TenantId == tenantId && l.IsActive == true)
            .ToListAsync();

        if (locations.Count == 0)
        {
            foreach (var loc in DemoDataConstants.Locations)
            {
                var location = new Location
                {
                    TenantId = tenantId,
                    Name = loc.Name,
                    Address = loc.Addr,
                    City = loc.City,
                    State = loc.St,
                    ZipCode = loc.Zip,
                    Phone = "(512) 555-0100",
                    IsActive = true,
                    IsPrimary = locations.Count == 0,
                    TimeZoneId = loc.Tz,
                    PlaceOfServiceCode = loc.Pos,
                    FacilityNpi = loc.Npi,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Locations.Add(location);
                locations.Add(location);
            }
            await _db.SaveChangesAsync();
        }

        // ── Providers: reuse existing active ones + add demo providers ─
        var existingProviders = await _db.Providers
            .Where(p => p.TenantId == tenantId && p.IsActive == true)
            .ToListAsync();
        providers.AddRange(existingProviders);

        // Check existing emails to avoid duplicates
        var existingEmails = await _db.Users
            .Where(u => u.TenantId == tenantId)
            .Select(u => u.Email.ToLower())
            .ToListAsync();

        foreach (var p in DemoDataConstants.Providers)
        {
            var email = $"{p.First.ToLower()}.{p.Last.ToLower()}@test.com";
            if (existingEmails.Contains(email)) continue;

            var provider = new Provider
            {
                TenantId = tenantId,
                Npi = p.Npi,
                FirstName = p.First,
                LastName = p.Last,
                Credentials = p.Cred,
                Specialty = p.Specialty,
                Taxonomy = p.Taxonomy,
                Email = email,
                Phone = "(512) 555-01" + Rng.Next(10, 99),
                Color = p.Color,
                IsActive = true,
                DefaultAppointmentDuration = 30,
                CreatedAt = DateTime.UtcNow
            };
            _enc.EncryptEntity(provider);
            _db.Providers.Add(provider);
            providers.Add(provider);
        }
        await _db.SaveChangesAsync();

        // Create clinician users — match by NPI (plaintext) to avoid encrypted-email lookup issues
        foreach (var pc in DemoDataConstants.Providers)
        {
            var email = $"{pc.First.ToLower()}.{pc.Last.ToLower()}@test.com";
            if (existingEmails.Contains(email)) continue;

            var matchedProvider = providers.FirstOrDefault(p => p.Npi == pc.Npi);
            if (matchedProvider == null) continue;

            var user = new User
            {
                TenantId = tenantId,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                FirstName = pc.First,
                LastName = pc.Last,
                Role = 2, // Clinician
                ProviderId = matchedProvider.ProviderId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            users.Add(user);
        }

        // ── Support Staff (Nurses, MAs, Front Desk, Biller) ────────
        foreach (var s in SupportStaff)
        {
            if (existingEmails.Contains(s.Email.ToLower())) continue;

            var user = new User
            {
                TenantId = tenantId,
                Email = s.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                FirstName = s.First,
                LastName = s.Last,
                Role = s.Role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            users.Add(user);
        }

        await _db.SaveChangesAsync();

        // ── Provider Schedules (Mon-Fri, 9am-5pm) — only for new providers ──
        foreach (var prov in providers)
        {
            var hasSchedule = await _db.ProviderSchedules
                .AnyAsync(ps => ps.ProviderId == prov.ProviderId && ps.TenantId == tenantId);
            if (hasSchedule) continue;

            for (int day = 0; day <= 6; day++)
            {
                _db.ProviderSchedules.Add(new ProviderSchedule
                {
                    TenantId = tenantId,
                    ProviderId = prov.ProviderId,
                    LocationId = locations[0].LocationId,
                    DayOfWeek = day,
                    StartTime = new TimeOnly(8, 0),
                    EndTime = new TimeOnly(23, 0),
                    IsAvailable = true
                });
            }
        }
        await _db.SaveChangesAsync();

        return new StaffResult(providers, users, locations);
    }
}
