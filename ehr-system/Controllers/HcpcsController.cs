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
            SELECT HcpcsCodeId, Hcpcs, Name, Category, IsSerialized, Rentable, Purchasable,
                   PurchasePrice, MonthlyRate, CappedRentalMonths, Modifiers, ReorderPoint, OnHand
            FROM dbo.vHcpcsCatalog
            ORDER BY Category, Hcpcs")
            .Select(r => new HcpcsRow
            {
                // Needed by the edit form: the row has to say which item it is.
                HcpcsCodeId = F.I(r["HcpcsCodeId"]),
                ReorderPoint = F.I(r["ReorderPoint"]),
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

    /// <summary>
    /// Correct an item already in the catalog.
    ///
    /// Until now an item could be added and never touched again, so a price
    /// typed wrong, a rental cap entered as 3 instead of 13, or a name nobody
    /// recognises was permanent. Prices move every year in this business; a
    /// catalog that cannot be corrected stops matching reality within months,
    /// and every order raised from it carries the mistake onto a claim.
    ///
    /// The HCPCS code itself is NOT editable. It is CMS's identifier, orders and
    /// claim lines already point at it, and "changing the code" is really two
    /// acts: retire this item and add the right one. Letting it be edited would
    /// silently rewrite what past orders say they sold.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> EditItem(
        int id, string? name, string? category,
        bool isSerialized, bool rentable, bool purchasable,
        decimal purchasePrice, decimal monthlyRate, int cappedRentalMonths,
        string? modifiers, int reorderPoint)
    {
        // Read it back inside the tenant scope rather than trusting the posted
        // id. DmeDb pins the tenant, so another supplier's id simply finds
        // nothing instead of being edited.
        var existing = _db.QueryOne(@"
            SELECT HcpcsCodeId, Hcpcs, Name, Category, IsSerialized, Rentable, Purchasable,
                   PurchasePrice, MonthlyRate, CappedRentalMonths, Modifiers, ReorderPoint
            FROM dbo.HcpcsCodes WHERE HcpcsCodeId=@id AND TenantId=@TenantId", new { id });

        if (existing == null)
        {
            TempData["ItemError"] = "That item is not in your catalog.";
            return RedirectToAction("Index");
        }

        // An item that can be neither sold nor rented cannot be put on an order
        // at all, so the New Order picker would offer it and then have no mode
        // to choose. Refused here, where the operator can see why.
        if (!rentable && !purchasable)
        {
            TempData["ItemError"] = "An item has to be sellable, rentable, or both.";
            return RedirectToAction("Index");
        }

        var itemName = string.IsNullOrWhiteSpace(name) ? F.S(existing["Name"]) : name.Trim();

        _db.Execute(@"
            UPDATE dbo.HcpcsCodes SET
                Name=@name, Category=@category, IsSerialized=@ser, Rentable=@rent,
                Purchasable=@buy, PurchasePrice=@price, MonthlyRate=@rate,
                CappedRentalMonths=@cap, Modifiers=@mods, ReorderPoint=@reorder
            WHERE HcpcsCodeId=@id AND TenantId=@TenantId",
            new
            {
                id,
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
            });

        // Before AND after: a price change is exactly the kind of edit somebody
        // needs to be able to explain later.
        await _audit.RecordAsync("DME_CATALOG_ITEM_EDITED", "HcpcsCode", id,
            before: new
            {
                Code = F.S(existing["Hcpcs"]),
                Name = F.S(existing["Name"]),
                PurchasePrice = F.Dec(existing["PurchasePrice"]),
                MonthlyRate = F.Dec(existing["MonthlyRate"]),
                CappedRentalMonths = F.I(existing["CappedRentalMonths"])
            },
            after: new
            {
                Code = F.S(existing["Hcpcs"]),
                Name = itemName,
                PurchasePrice = purchasePrice,
                MonthlyRate = monthlyRate,
                CappedRentalMonths = cappedRentalMonths
            });

        TempData["ItemAdded"] = $"{F.S(existing["Hcpcs"])} updated.";
        return RedirectToAction("Index");
    }
}

public class HcpcsRow
{
    public int HcpcsCodeId { get; set; }
    public int ReorderPoint { get; set; }
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
