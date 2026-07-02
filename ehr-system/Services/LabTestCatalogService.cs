using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

public interface ILabTestCatalogService
{
    Task<List<string>> GetPanelsAsync();
    Task<List<LabTestCatalogDto>> GetTestsByPanelAsync(string panelName);
    Task<List<LabTestCatalogDto>> SearchAsync(string query);
}

public class LabTestCatalogService : ILabTestCatalogService
{
    private readonly EhrDbContext _context;

    public LabTestCatalogService(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GetPanelsAsync()
    {
        return await _context.LabTestCatalogs
            .Where(t => t.IsActive && t.PanelName != null)
            .Select(t => t.PanelName!)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync();
    }

    public async Task<List<LabTestCatalogDto>> GetTestsByPanelAsync(string panelName)
    {
        return await _context.LabTestCatalogs
            .Where(t => t.IsActive && t.PanelName == panelName)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => MapToDto(t))
            .ToListAsync();
    }

    public async Task<List<LabTestCatalogDto>> SearchAsync(string query)
    {
        return await _context.LabTestCatalogs
            .Where(t => t.IsActive &&
                (t.TestName.Contains(query) || t.TestCode.Contains(query) ||
                 (t.PanelName != null && t.PanelName.Contains(query))))
            .OrderBy(t => t.PanelName)
            .ThenBy(t => t.DisplayOrder)
            .Take(50)
            .Select(t => MapToDto(t))
            .ToListAsync();
    }

    private static LabTestCatalogDto MapToDto(LabTestCatalog t) => new()
    {
        LabTestId = t.LabTestId,
        PanelName = t.PanelName,
        TestName = t.TestName,
        TestCode = t.TestCode,
        Unit = t.Unit,
        ReferenceRange = t.ReferenceRange,
        SpecimenType = t.SpecimenType,
        DisplayOrder = t.DisplayOrder
    };
}
