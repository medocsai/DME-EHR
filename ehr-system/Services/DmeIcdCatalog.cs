using EHR.Helpers;

namespace EHR.Services;

/// <summary>One ICD-10-CM diagnosis as the catalog knows it.</summary>
/// <param name="Code">Dotted, as it is written on a claim and stored on the customer.</param>
/// <param name="Description">The CMS wording.</param>
public record IcdMatch(string Code, string Description);

/// <summary>The ICD-10-CM diagnosis catalog: every code valid for submission.</summary>
public interface IDmeIcdCatalog
{
    /// <summary>
    /// Diagnoses matching what has been typed so far, best match first. Nothing
    /// comes back for an empty term: a picker over 74,719 rows must not answer
    /// before it is asked.
    /// </summary>
    IReadOnlyList<IcdMatch> Search(string? term, int limit = 20);

    /// <summary>
    /// One diagnosis by code, or null when the code is not a valid ICD-10-CM
    /// code. This is what turns a posted code into the description filed
    /// against the customer, and what refuses one that was never in the list.
    /// </summary>
    IcdMatch? Find(string? code);
}

/// <summary>
/// WHY THIS EXISTS
/// The diagnosis picker offered TWELVE codes, hardcoded as a C# array in
/// DmeController. The client asked for E66.9, obesity, which was not one of
/// them, and a DMEPOS claim without a diagnosis does not get paid. It is now
/// the full CMS FY2026 code set, loaded by
/// Migrations/Manual/2026-08-27_DME_Icd10_Catalog.sql.
///
/// WHAT IT KNOWS THAT NOBODY ELSE HAS TO
///
/// 1. The catalog is GLOBAL. dbo.IcdCodes has no TenantId and is not in
///    TenantIsolationPolicy. ICD-10-CM is a national code set; no supplier owns
///    a private copy. The tenant fact is which diagnosis a CUSTOMER carries,
///    and that lives on DmeCustomerDiagnoses with its own copy of the wording,
///    so a catalog revision cannot rewrite what was billed.
///
/// 2. Only BILLABLE codes are in it. CMS ships headers like E66 alongside leaf
///    codes like E66.9, and putting a header on a claim is a denial. The
///    migration loads the codes file, which is the valid-for-submission set.
///
/// 3. A code is searched with or without its dot. The catalog stores E66.9;
///    a biller reading a referral may type either, and a system that only
///    accepts one spelling of the same code is the same complaint again.
///
/// 4. Searching is by WORD, in any order, across the code AND the description,
///    because nobody remembers M17.0 but everybody can type "knee".
///
/// WHO CALLS IT
/// LookupsController for the typeahead, and DmeController.CreateCustomer to
/// turn the posted code into the diagnosis filed against the customer.
/// </summary>
public sealed class DmeIcdCatalog : IDmeIcdCatalog
{
    private readonly IDmeDb _db;

    /// <summary>
    /// Hard ceiling on one page of matches, whatever a caller asks for. The
    /// control is a picker, not an export of 74,719 rows.
    /// </summary>
    private const int MaxLimit = 50;

    /// <summary>
    /// Most words honoured before the rest are ignored. Diagnosis wording is
    /// long ("Type 2 diabetes mellitus with diabetic neuropathy, unspecified"),
    /// so this is higher than the payer list needs.
    /// </summary>
    private const int MaxTerms = 6;

    public DmeIcdCatalog(IDmeDb db) => _db = db;

    /// <inheritdoc />
    public IReadOnlyList<IcdMatch> Search(string? term, int limit = 20)
    {
        var q = (term ?? "").Trim();
        var words = SearchTerm.Words(q, MaxTerms);
        if (words.Count == 0) return Array.Empty<IcdMatch>();

        var prms = new Dictionary<string, object?>
        {
            ["take"] = Math.Clamp(limit, 1, MaxLimit),
            // Matching on the code is done WITHOUT dots on both sides, so E669,
            // E66.9 and a pasted "E66.9 " all find the same row.
            ["exact"] = Undotted(q),
            ["starts"] = SearchTerm.EscapeLike(Undotted(q)) + "%"
        };

        var clauses = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            var word = $"w{i}";
            var bare = $"b{i}";
            prms[word] = "%" + SearchTerm.EscapeLike(words[i]) + "%";
            prms[bare] = "%" + SearchTerm.EscapeLike(Undotted(words[i])) + "%";

            clauses.Add($"(Description LIKE @{word} ESCAPE '\\' OR REPLACE(Code,'.','') LIKE @{bare} ESCAPE '\\')");
        }

        // An exact code first: somebody who typed a whole code knows what they
        // want and should not have to hunt for it under a wordier description.
        var rows = _db.Query(@"
            SELECT TOP (@take) Code, Description
            FROM dbo.IcdCodes
            WHERE " + string.Join(" AND ", clauses) + @"
            ORDER BY CASE
                        WHEN REPLACE(Code,'.','') = @exact THEN 0
                        WHEN REPLACE(Code,'.','') LIKE @starts ESCAPE '\' THEN 1
                        ELSE 2
                     END,
                     Code", prms);

        return rows.Select(r => new IcdMatch(F.S(r["Code"]), F.S(r["Description"]))).ToList();
    }

    /// <inheritdoc />
    public IcdMatch? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Matched without dots so a code typed either way resolves, and the
        // STORED spelling is what comes back and gets filed.
        var r = _db.QueryOne(
            "SELECT Code, Description FROM dbo.IcdCodes WHERE REPLACE(Code,'.','') = @code",
            new { code = Undotted(code) });

        return r == null ? null : new IcdMatch(F.S(r["Code"]), F.S(r["Description"]));
    }

    private static string Undotted(string s) => s.Replace(".", "").Trim();
}
