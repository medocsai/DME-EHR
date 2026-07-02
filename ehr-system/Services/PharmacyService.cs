using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IPharmacyService
{
    Task<List<PharmacyDto>> GetAllAsync();
    Task<List<PharmacyDto>> SearchAsync(string query);
    Task<PharmacyDto?> GetByIdAsync(int id);
}

public class PharmacyService : IPharmacyService
{
    private readonly EhrDbContext _context;

    public PharmacyService(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<List<PharmacyDto>> GetAllAsync()
    {
        return await _context.Pharmacies
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => MapToDto(p))
            .ToListAsync();
    }

    public async Task<List<PharmacyDto>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return await GetAllAsync();

        var q = query.ToLower();
        return await _context.Pharmacies
            .Where(p => p.IsActive &&
                (p.Name.ToLower().Contains(q) ||
                 p.City.ToLower().Contains(q) ||
                 p.State.ToLower().Contains(q)))
            .OrderBy(p => p.Name)
            .Take(20)
            .Select(p => MapToDto(p))
            .ToListAsync();
    }

    public async Task<PharmacyDto?> GetByIdAsync(int id)
    {
        return await _context.Pharmacies
            .Where(p => p.PharmacyId == id)
            .Select(p => MapToDto(p))
            .FirstOrDefaultAsync();
    }

    private static PharmacyDto MapToDto(Pharmacy p) => new()
    {
        PharmacyId = p.PharmacyId,
        Name = p.Name,
        NCPDP = p.NCPDP,
        NPI = p.NPI,
        Address = p.Address,
        City = p.City,
        State = p.State,
        Zip = p.Zip,
        Phone = p.Phone,
        Fax = p.Fax,
        FullAddress = $"{p.Address}, {p.City}, {p.State} {p.Zip}",
        IsActive = p.IsActive
    };
}
