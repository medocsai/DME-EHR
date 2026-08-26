using EHR.Helpers;

namespace EHR.Services;

/// <summary>One HCPCS Level II code as CMS publishes it.</summary>
/// <param name="Code">The five character code billed on a claim.</param>
/// <param name="Description">The long description.</param>
/// <param name="ShortDescription">CMS's own 28 character abbreviation, a sane default item name.</param>
/// <param name="CoverageCode">Medicare coverage: C, D, I, M or S. Null when CMS gives none.</param>
/// <param name="TerminatedOn">The date CMS retired the code, or null.</param>
public record HcpcsMatch(
    string Code,
    string Description,
    string ShortDescription,
    string? CoverageCode,
    DateTime? TerminatedOn)
{
    /// <summary>
    /// DERIVED, never stored. A retired code cannot be billed, so it must not be
    /// added to an item master, but it stays in the catalog so a supplier who
    /// already stocks one sees why it stopped working.
    /// </summary>
    public bool IsRetired => TerminatedOn.HasValue && TerminatedOn.Value.Date <= DateTime.Today;

    /// <summary>
    /// DERIVED. I, M and S all mean Medicare will not pay. Worth saying before
    /// a supplier prices an item and bills it, not after the denial.
    /// </summary>
    public bool MedicareNeverPays => CoverageCode is "I" or "M" or "S";
}

/// <summary>The national HCPCS Level II code list.</summary>
public interface IDmeHcpcsCatalog
{
    /// <summary>Codes matching what has been typed, best match first.</summary>
    IReadOnlyList<HcpcsMatch> Search(string? term, int limit = 20);

    /// <summary>One code, or null when it is not a real HCPCS Level II code.</summary>
    HcpcsMatch? Find(string? code);
}

/// <summary>
/// WHY THIS EXISTS
/// The client said the HCPCS list was not exhaustive, on two screens. It held
/// 22 items and there was no way to add a 23rd, so a supplier could not sell
/// anything the demo data did not already contain.
///
/// THE DISTINCTION THAT MATTERS
/// dbo.HcpcsCodes is the SUPPLIER'S ITEM MASTER: what they stock, their price,
/// their rental terms. dbo.HcpcsNationalCodes, which this reads, is what CMS
/// publishes: codes and the wording that goes on a claim. They are different
/// facts with different owners, which is why the national list did not simply
/// get poured into the existing table.
///
/// Adding an item is therefore "find the code, set your price", and the price
/// stays the supplier's own.
///
/// WHO CALLS IT
/// LookupsController for the typeahead on the HCPCS Catalog screen, and
/// HcpcsController.AddItem to check that a new item names a real, current code
/// before it is filed.
/// </summary>
public sealed class DmeHcpcsCatalog : IDmeHcpcsCatalog
{
    private readonly IDmeDb _db;

    private const int MaxLimit = 50;
    private const int MaxTerms = 5;

    private const string Columns =
        "Code, LongDescription, ShortDescription, CoverageCode, TerminatedOn";

    public DmeHcpcsCatalog(IDmeDb db) => _db = db;

    /// <inheritdoc />
    public IReadOnlyList<HcpcsMatch> Search(string? term, int limit = 20)
    {
        var q = (term ?? "").Trim();
        var words = SearchTerm.Words(q, MaxTerms);
        if (words.Count == 0) return Array.Empty<HcpcsMatch>();

        var prms = new Dictionary<string, object?>
        {
            ["take"] = Math.Clamp(limit, 1, MaxLimit),
            ["exact"] = q,
            ["starts"] = SearchTerm.EscapeLike(q) + "%"
        };

        var clauses = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            var name = $"w{i}";
            prms[name] = "%" + SearchTerm.EscapeLike(words[i]) + "%";
            clauses.Add($"(LongDescription LIKE @{name} ESCAPE '\\' " +
                        $"OR ShortDescription LIKE @{name} ESCAPE '\\' " +
                        $"OR Code LIKE @{name} ESCAPE '\\')");
        }

        // Current codes before retired ones: a supplier searching for something
        // to stock wants what they can still bill, and a retired code offered
        // first is an invitation to a denial.
        var rows = _db.Query($@"
            SELECT TOP (@take) {Columns}
            FROM dbo.HcpcsNationalCodes
            WHERE " + string.Join(" AND ", clauses) + @"
            ORDER BY CASE WHEN TerminatedOn IS NULL OR TerminatedOn > GETDATE() THEN 0 ELSE 1 END,
                     CASE
                        WHEN Code = @exact THEN 0
                        WHEN Code LIKE @starts ESCAPE '\' THEN 1
                        ELSE 2
                     END,
                     Code", prms);

        return rows.Select(Map).ToList();
    }

    /// <inheritdoc />
    public HcpcsMatch? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var r = _db.QueryOne(
            $"SELECT {Columns} FROM dbo.HcpcsNationalCodes WHERE Code = @code",
            new { code = code.Trim().ToUpperInvariant() });

        return r == null ? null : Map(r);
    }

    private static HcpcsMatch Map(Dictionary<string, object?> r) => new(
        F.S(r["Code"]),
        F.S(r["LongDescription"]),
        F.S(r["ShortDescription"]),
        r["CoverageCode"] is null or DBNull ? null : F.S(r["CoverageCode"]).Trim(),
        r["TerminatedOn"] is null or DBNull ? null : Convert.ToDateTime(r["TerminatedOn"]));
}
