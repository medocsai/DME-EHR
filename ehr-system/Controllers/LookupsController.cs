using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Models.Generated;
using System.Text.Json;

namespace EHR.Controllers;

/// <summary>
/// Controller for reference data lookups.
/// Provides access to CPT codes, ICD codes, and payers for form dropdowns.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LookupsController : ControllerBase
{
    private readonly EhrDbContext _context;

    public LookupsController(EhrDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get CPT codes with optional search and category filtering.
    /// </summary>
    [HttpGet("cpt-codes")]
    public ActionResult<List<Cptcode>> GetCPTCodes([FromQuery] string? search, [FromQuery] string? category)
    {
        var query = _context.Cptcodes.Where(c => c.IsActive == true).AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Code.Contains(search) || c.Description.Contains(search));

        if (!string.IsNullOrEmpty(category))
            query = query.Where(c => c.Category == category);

        return Ok(query.OrderBy(c => c.Code).Take(50).ToList());
    }

    /// <summary>
    /// Get ICD codes with optional search and category filtering.
    /// </summary>
    [HttpGet("icd-codes")]
    public ActionResult<List<Icdcode>> GetICDCodes([FromQuery] string? search, [FromQuery] string? category)
    {
        var query = _context.Icdcodes.Where(c => c.IsActive == true).AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Code.Contains(search) || c.Description.Contains(search));

        if (!string.IsNullOrEmpty(category))
            query = query.Where(c => c.Category == category);

        return Ok(query.OrderBy(c => c.Code).Take(50).ToList());
    }

    /// <summary>
    /// Get all active payers.
    /// </summary>
    [HttpGet("payers")]
    public ActionResult<List<Payer>> GetPayers()
    {
        return Ok(_context.Payers.Where(p => p.IsActive == true).OrderBy(p => p.Name).ToList());
    }

    /// <summary>
    /// Search payers by name or payer ID code.
    /// Returns up to 20 matches for autocomplete usage.
    /// </summary>
    [HttpGet("payers/search")]
    public ActionResult<List<object>> SearchPayers([FromQuery] string q, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<object>());

        var query = _context.Payers
            .Where(p => p.IsActive == true && (p.Name.Contains(q) || p.PayerIdCode.Contains(q)))
            .OrderBy(p => p.Name)
            .Take(take)
            .Select(p => new { p.Name, p.PayerIdCode })
            .ToList();

        return Ok(query);
    }

    /// <summary>
    /// Import payers from App_Data/payers_import.json (Office Ally payer list).
    /// Clears existing payers and replaces with the import file.
    /// Admin only (roles 0, 1).
    /// </summary>
    [HttpPost("payers/import")]
    [Authorize(Roles = "0,1")]
    public ActionResult ImportPayers()
    {
        var payersFile = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "payers_import.json");
        if (!System.IO.File.Exists(payersFile))
            return NotFound(new { message = "payers_import.json not found in App_Data" });

        var json = System.IO.File.ReadAllText(payersFile);
        var items = JsonSerializer.Deserialize<List<PayerImportItem>>(json);
        if (items == null || items.Count == 0)
            return BadRequest(new { message = "No payer data found in file" });

        // Remove old payers
        _context.Payers.RemoveRange(_context.Payers);
        _context.SaveChanges();

        // Insert new payers
        var payers = items.Select(p => new Payer
        {
            Name = (p.name?.Length > 100 ? p.name[..100] : p.name) ?? "",
            PayerIdCode = (p.payerIdCode?.Length > 50 ? p.payerIdCode[..50] : p.payerIdCode) ?? "",
            IsActive = true
        }).ToList();

        _context.Payers.AddRange(payers);
        _context.SaveChanges();

        return Ok(new { message = $"Imported {payers.Count} payers", count = payers.Count });
    }

    private class PayerImportItem
    {
        public string? name { get; set; }
        public string? payerIdCode { get; set; }
    }
}
