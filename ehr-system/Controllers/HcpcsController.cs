using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;
using EHR.Services;

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
    private readonly IDmeHcpcsCatalog _national;
    private readonly IDmeAudit _audit;

    public HcpcsController(IDmeDb db, IDmeHcpcsCatalog national, IDmeAudit audit)
    {
        _db = db;
        _national = national;
        _audit = audit;
    }

    public IActionResult Index()
    {
        ViewData["Title"] = "HCPCS Catalog";
        ViewData["ActivePage"] = "hcpcs";
        ViewBag.ItemError = TempData["ItemError"];
        ViewBag.ItemAdded = TempData["ItemAdded"];

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

    /// <summary>
    /// Add an item to THIS supplier's catalog, naming a code from the CMS
    /// national list.
    ///
    /// The client's complaint was that the item list was not exhaustive: there
    /// were 22 items and no way to add a 23rd, so a supplier could not sell
    /// anything the demo data did not contain.
    ///
    /// What is stored here is the SUPPLIER'S fact: their name for the item,
    /// their price, their rental terms. The code and its official wording stay
    /// in dbo.HcpcsNationalCodes and are not copied, except the item name, which
    /// defaults to CMS's short description and is then the supplier's to edit.
    ///
    /// Restricted to admin roles: pricing is a commercial decision, not
    /// something a delivery driver sets.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> AddItem(
        string hcpcs, string? name, string? category,
        bool isSerialized, bool rentable, bool purchasable,
        decimal purchasePrice, decimal monthlyRate, int cappedRentalMonths,
        string? modifiers, int reorderPoint)
    {
        // A real code, checked against the national list rather than trusted
        // from the form. An invented code is a claim that will be rejected weeks
        // later, by which time the equipment has been delivered.
        var code = _national.Find(hcpcs);
        if (code == null)
        {
            TempData["ItemError"] = $"'{hcpcs}' is not a HCPCS Level II code. Pick one from the search box.";
            return RedirectToAction("Index");
        }

        // A retired code cannot be billed. Refused here rather than at the
        // clearinghouse, because the catalog is where the mistake is cheap.
        if (code.IsRetired)
        {
            TempData["ItemError"] =
                $"{code.Code} was retired on {code.TerminatedOn:MMM d, yyyy} and cannot be billed.";
            return RedirectToAction("Index");
        }

        // Tenant scoped by DmeDb, so this only ever sees this supplier's master.
        var already = _db.Scalar(
            "SELECT HcpcsCodeId FROM dbo.HcpcsCodes WHERE Hcpcs=@code AND TenantId=@TenantId",
            new { code = code.Code });

        if (already != null)
        {
            TempData["ItemError"] = $"{code.Code} is already in your catalog.";
            return RedirectToAction("Index");
        }

        var itemName = string.IsNullOrWhiteSpace(name) ? code.ShortDescription : name.Trim();

        var id = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.HcpcsCodes
            (Hcpcs,Name,Category,IsSerialized,Rentable,Purchasable,PurchasePrice,MonthlyRate,CappedRentalMonths,Modifiers,ReorderPoint,TenantId)
            OUTPUT inserted.HcpcsCodeId
            VALUES (@code,@name,@category,@ser,@rent,@buy,@price,@rate,@cap,@mods,@reorder,@TenantId)",
            new
            {
                code = code.Code,
                name = itemName,
                category = string.IsNullOrWhiteSpace(category) ? "Uncategorised" : category.Trim(),
                ser = isSerialized,
                rent = rentable,
                buy = purchasable,
                price = purchasePrice,
                rate = monthlyRate,
                cap = cappedRentalMonths,
                mods = (object?)modifiers ?? DBNull.Value,
                reorder = reorderPoint
            }));

        await _audit.RecordAsync("DME_CATALOG_ITEM_ADDED", "HcpcsCode", id,
            before: null,
            after: new { code.Code, Name = itemName, PurchasePrice = purchasePrice, MonthlyRate = monthlyRate });

        TempData["ItemAdded"] = $"{code.Code} added to your catalog.";
        return RedirectToAction("Index");
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
