using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;

namespace EHR.Controllers;

/// <summary>
/// DME — HCPCS Level II catalog page.
///
/// The catalog is tenant data, not a shared national reference table: each DME
/// supplier maintains its own item master and its own contract pricing. It
/// therefore reads through IDmeDb like every other DME screen, so the row level
/// security policy scopes it. It previously opened its own SqlConnection with
/// no tenant context, which meant it returned every tenant's catalog.
/// </summary>
[Authorize]
[PhiAccessAudit(EntityType = "HcpcsCatalog")]
public class HcpcsController : Controller
{
    private readonly IDmeDb _db;
    public HcpcsController(IDmeDb db) => _db = db;

    public IActionResult Index()
    {
        ViewData["Title"] = "HCPCS Catalog";
        ViewData["ActivePage"] = "hcpcs";

        // OnHand comes from the stock ledger via the view, never from a stored
        // counter. See Migrations/Manual/2026-08-25_DME_Single_Source_Of_Truth.sql.
        var items = _db.Query(@"
            SELECT Hcpcs, Name, Category, IsSerialized, Rentable, Purchasable,
                   PurchasePrice, MonthlyRate, CappedRentalMonths, Modifiers, OnHand
            FROM dbo.vHcpcsCatalog
            ORDER BY Category, Hcpcs")
            .Select(r => new HcpcsRow
            {
                Hcpcs = F.S(r["Hcpcs"]),
                Name = F.S(r["Name"]),
                Category = F.S(r["Category"]),
                IsSerialized = F.B(r["IsSerialized"]),
                Rentable = F.B(r["Rentable"]),
                Purchasable = F.B(r["Purchasable"]),
                PurchasePrice = F.Dec(r["PurchasePrice"]),
                MonthlyRate = F.Dec(r["MonthlyRate"]),
                CappedRentalMonths = F.I(r["CappedRentalMonths"]),
                Modifiers = F.S(r["Modifiers"]),
                OnHand = F.I(r["OnHand"])
            })
            .ToList();

        return View("~/Views/Hcpcs/Index.cshtml", items);
    }
}

public class HcpcsRow
{
    public string Hcpcs { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsSerialized { get; set; }
    public bool Rentable { get; set; }
    public bool Purchasable { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal MonthlyRate { get; set; }
    public int CappedRentalMonths { get; set; }
    public string Modifiers { get; set; } = "";
    public int OnHand { get; set; }
}
