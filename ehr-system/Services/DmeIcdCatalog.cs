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
    private readonly IcdCodeCache _cache;

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

    public DmeIcdCatalog(IDmeDb db, IcdCodeCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <inheritdoc />
    /// <summary>
    /// Clinical abbreviations a biller types that appear nowhere in the CMS
    /// wording.
    ///
    /// This is the same problem the payer catalog has with Blue Cross and BCBS,
    /// and it fails the same way: silently, with an empty list, so the operator
    /// concludes the code is missing rather than that they spelled it the way
    /// their profession does.
    ///
    /// MEASURED. Before this, "copd" matched ZERO of 74,719 codes, because CMS
    /// writes "Chronic obstructive pulmonary disease". J44.9 is one of the
    /// commonest diagnoses on a DMEPOS claim there is: it is what an oxygen
    /// concentrator and a nebuliser are billed under, and it appears on this
    /// product's own sample claim.
    ///
    /// Deliberately small, and deliberately DME shaped. It covers the conditions
    /// this equipment is prescribed for, not medicine in general. It is not a
    /// thesaurus and must not become one.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["copd"] = new[] { "obstructive pulmonary" },
            ["osa"]  = new[] { "sleep apnea" },
            ["chf"]  = new[] { "heart failure" },
            ["dm"]   = new[] { "diabetes" },
            ["htn"]  = new[] { "hypertension" },
            ["cva"]  = new[] { "cerebral infarction" },
            ["dvt"]  = new[] { "thrombophlebitis" },
            ["pvd"]  = new[] { "peripheral vascular" },
            ["cad"]  = new[] { "atherosclerotic heart" },
            ["mi"]   = new[] { "myocardial infarction" },
            ["ckd"]  = new[] { "chronic kidney" },
            ["als"]  = new[] { "motor neuron" },
            ["ms"]   = new[] { "multiple sclerosis" },
        };

    /// <summary>
    /// Every code, loaded once. See IcdCodeCache for why this is not a SQL
    /// query: searching in the database took about 1.5 seconds per keystroke
    /// against 54ms for the payer picker on the same form, and no index can fix
    /// a LIKE '%word%' over 74,719 rows.
    /// </summary>
    private IReadOnlyList<IcdCodeCache.Entry> Codes() => _cache.All(() =>
    {
        var rows = _db.Query("SELECT Code, Description FROM dbo.IcdCodes");
        var list = new List<IcdCodeCache.Entry>(rows.Count);
        foreach (var r in rows)
        {
            var code = F.S(r["Code"]);
            list.Add(new IcdCodeCache.Entry(code, F.S(r["Description"]), Undotted(code)));
        }
        return list;
    });

    private static bool Has(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <inheritdoc />
    public IReadOnlyList<IcdMatch> Search(string? term, int limit = 20)
    {
        var q = (term ?? "").Trim();
        var words = SearchTerm.Words(q, MaxTerms);
        if (words.Count == 0) return Array.Empty<IcdMatch>();

        var take = Math.Clamp(limit, 1, MaxLimit);

        // Matching on the code is done WITHOUT dots on both sides, so E669,
        // E66.9 and a pasted "E66.9 " all find the same row.
        var exact = Undotted(q);

        // Each word, plus anything the profession calls it. Every word has to
        // match SOMETHING, and an alias is an OR within that word: typing "copd"
        // finds the code, and typing "obstructive" still finds it too.
        var terms = new List<string[]>(words.Count);
        var bare = new List<string>(words.Count);
        foreach (var w in words)
        {
            var alternatives = new List<string> { w };
            if (Aliases.TryGetValue(w, out var also)) alternatives.AddRange(also);
            terms.Add(alternatives.ToArray());
            bare.Add(Undotted(w));
        }

        var hits = new List<IcdCodeCache.Entry>();
        foreach (var e in Codes())
        {
            var all = true;
            for (var i = 0; i < terms.Count && all; i++)
            {
                var any = Has(e.CodeNoDot, bare[i]);
                for (var a = 0; a < terms[i].Length && !any; a++)
                    any = Has(e.Description, terms[i][a]);
                all = any;
            }
            if (all) hits.Add(e);
        }

        // An exact code first: somebody who typed a whole code knows what they
        // want and should not have to hunt for it under a wordier description.
        hits.Sort((a, b) =>
        {
            var ra = Rank(a, exact);
            var rb = Rank(b, exact);
            return ra != rb ? ra - rb : string.CompareOrdinal(a.Code, b.Code);
        });

        var result = new List<IcdMatch>(Math.Min(take, hits.Count));
        for (var i = 0; i < hits.Count && i < take; i++)
            result.Add(new IcdMatch(hits[i].Code, hits[i].Description));

        return result;
    }

    private static int Rank(IcdCodeCache.Entry e, string exact)
    {
        if (string.Equals(e.CodeNoDot, exact, StringComparison.OrdinalIgnoreCase)) return 0;
        if (e.CodeNoDot.StartsWith(exact, StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    /// <inheritdoc />
    public IcdMatch? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Matched without dots so a code typed either way resolves, and the
        // STORED spelling is what comes back and gets filed. Served from the
        // same in-memory list as the search, so filing a diagnosis does not
        // cost a query against 74,719 rows either.
        var wanted = Undotted(code);
        foreach (var e in Codes())
            if (string.Equals(e.CodeNoDot, wanted, StringComparison.OrdinalIgnoreCase))
                return new IcdMatch(e.Code, e.Description);

        return null;
    }

    private static string Undotted(string s) => s.Replace(".", "").Trim();
}
