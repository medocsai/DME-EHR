using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EHR.Controllers;

/// <summary>
/// DME — HCPCS Level II catalog page (Phase 1 DME feature).
/// Reads the DME HcpcsCodes table directly (no EF entity yet) so it stays
/// isolated from the generated EHR DbContext during the conversion.
/// </summary>
public class HcpcsController : Controller
{
    private readonly IConfiguration _config;
    public HcpcsController(IConfiguration config) => _config = config;

    public IActionResult Index()
    {
        ViewData["Title"] = "HCPCS Catalog";
        ViewData["ActivePage"] = "hcpcs";

        var items = new List<HcpcsRow>();
        using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Hcpcs, Name, Category, IsSerialized, Rentable, Purchasable,
                                       PurchasePrice, MonthlyRate, CappedRentalMonths, Modifiers, OnHand
                                FROM dbo.HcpcsCodes ORDER BY Category, Hcpcs";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                items.Add(new HcpcsRow
                {
                    Hcpcs = r.GetString(0),
                    Name = r.GetString(1),
                    Category = r.GetString(2),
                    IsSerialized = r.GetBoolean(3),
                    Rentable = r.GetBoolean(4),
                    Purchasable = r.GetBoolean(5),
                    PurchasePrice = r.GetDecimal(6),
                    MonthlyRate = r.GetDecimal(7),
                    CappedRentalMonths = r.GetInt32(8),
                    Modifiers = r.IsDBNull(9) ? "" : r.GetString(9),
                    OnHand = r.GetInt32(10)
                });
            }
        }
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
