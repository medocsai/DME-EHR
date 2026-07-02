using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface IDrugDatabaseService
{
    Task<List<DrugSearchResultDto>> SearchAsync(string query, int limit = 20);
    Task<DrugSearchResultDto?> GetByIdAsync(int id);
}

public class DrugDatabaseService : IDrugDatabaseService
{
    private readonly EhrDbContext _context;

    public DrugDatabaseService(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<List<DrugSearchResultDto>> SearchAsync(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<DrugSearchResultDto>();

        var q = query.ToLower();
        return await _context.DrugDatabases
            .Where(d => d.IsActive &&
                (d.BrandName.ToLower().Contains(q) ||
                 d.GenericName.ToLower().Contains(q)))
            .OrderBy(d => d.BrandName)
            .Take(limit)
            .Select(d => MapToDto(d))
            .ToListAsync();
    }

    public async Task<DrugSearchResultDto?> GetByIdAsync(int id)
    {
        return await _context.DrugDatabases
            .Where(d => d.DrugId == id)
            .Select(d => MapToDto(d))
            .FirstOrDefaultAsync();
    }

    private static DrugSearchResultDto MapToDto(DrugDatabase d) => new()
    {
        DrugId = d.DrugId,
        NDCCode = d.NDCCode,
        BrandName = d.BrandName,
        GenericName = d.GenericName,
        Strength = d.Strength,
        DosageForm = d.DosageForm,
        DosageFormName = EnumHelper.GetDosageFormName(d.DosageForm),
        Route = d.Route,
        RouteName = EnumHelper.GetMedicationRouteName(d.Route),
        DEASchedule = d.DEASchedule,
        CommonDirections = d.CommonDirections,
        Warnings = d.Warnings,
        DisplayName = $"{d.BrandName} ({d.GenericName}) {d.Strength}"
    };
}
