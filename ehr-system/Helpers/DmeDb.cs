using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace EHR.Helpers;

/// <summary>
/// Thin raw-ADO data access for the DME-native tables in DMEEHR.
/// Deliberately separate from the generated EHR DbContext so the DME product
/// stays isolated from the clinical EF model during the conversion.
/// Rows are returned as case-insensitive dictionaries for compact views.
/// </summary>
public static class DmeDb
{
    private static string _cs = "";
    public static void Init(string cs) => _cs = cs;

    public static List<Dictionary<string, object?>> Query(string sql, object? prms = null)
    {
        var list = new List<Dictionary<string, object?>>();
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, prms);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.FieldCount; i++)
                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    public static Dictionary<string, object?>? QueryOne(string sql, object? prms = null)
        => Query(sql, prms).FirstOrDefault();

    public static int Execute(string sql, object? prms = null)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, prms);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(string sql, object? prms = null)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, prms);
        var v = cmd.ExecuteScalar();
        return v == DBNull.Value ? null : v;
    }

    /// <summary>Atomic human-friendly sequence (ORD-01011 etc.).</summary>
    public static string NextNumber(string prefix, int pad = 5)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(
            "UPDATE dbo.DmeSeq SET Val = Val + 1 OUTPUT inserted.Val WHERE Name = @n", conn);
        cmd.Parameters.AddWithValue("@n", prefix);
        var v = cmd.ExecuteScalar();
        return prefix + "-" + Convert.ToInt32(v).ToString().PadLeft(pad, '0');
    }

    private static void AddParams(SqlCommand cmd, object? prms)
    {
        if (prms == null) return;
        foreach (var p in prms.GetType().GetProperties())
            cmd.Parameters.AddWithValue("@" + p.Name, p.GetValue(prms) ?? DBNull.Value);
    }
}

/// <summary>Formatting helpers for DME Razor views (USD-locked, null-safe).</summary>
public static class F
{
    private static readonly CultureInfo US = CultureInfo.GetCultureInfo("en-US");

    public static string S(object? o) => o?.ToString() ?? "";
    public static int I(object? o) => o == null ? 0 : Convert.ToInt32(o);
    public static decimal Dec(object? o) => o == null ? 0m : Convert.ToDecimal(o);
    public static bool B(object? o) => o != null && Convert.ToBoolean(o);
    public static string Money(object? o) => "$" + Dec(o).ToString("N2", US);
    public static string Date(object? o)
    {
        if (o == null) return "—";
        var d = Convert.ToDateTime(o);
        return d.ToString("MMM d, yyyy", US);
    }
    public static int Age(object? o)
    {
        if (o == null) return 0;
        var d = Convert.ToDateTime(o);
        var n = DateTime.Today;
        var a = n.Year - d.Year;
        if (d.Date > n.AddYears(-a)) a--;
        return a;
    }
    public static int? DaysUntil(object? o)
    {
        if (o == null) return null;
        return (Convert.ToDateTime(o).Date - DateTime.Today).Days;
    }
    public static string Initials(object? first, object? last)
        => ((S(first) + " ")[0].ToString() + (S(last) + " ")[0]).ToUpper();

    private static readonly Dictionary<string, (string cls, string label)> _chips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["draft"] = ("gray", "Draft"), ["confirmed"] = ("blue", "Confirmed"), ["delivered"] = ("green", "Delivered"),
        ["billed"] = ("teal", "Billed"), ["active"] = ("green", "Active"), ["ready"] = ("amber", "Ready to bill"),
        ["submitted"] = ("blue", "Submitted"), ["paid"] = ("green", "Paid"), ["rented"] = ("blue", "On rent"),
        ["sold"] = ("gray", "Sold"), ["in-stock"] = ("gray", "In stock"), ["ended"] = ("gray", "Ended"),
        ["maintenance"] = ("amber", "Maintenance"), ["recalled"] = ("red", "Recalled"), ["denied"] = ("red", "Denied")
    };

    /// <summary>HTML status chip (use with @Html.Raw).</summary>
    public static string StatusChip(object? status)
    {
        var s = S(status);
        if (!_chips.TryGetValue(s, out var v)) v = ("gray", s);
        return $"<span class=\"dme-chip {v.cls}\">{System.Net.WebUtility.HtmlEncode(v.label)}</span>";
    }
}
