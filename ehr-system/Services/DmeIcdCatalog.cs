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

        // Each word searches ONE column, chosen by what the word looks like.
        //
        // It used to search both and OR them together. That was the whole
        // performance problem: an OR between an indexed predicate and an
        // unindexed one throws away the index, so every keystroke scanned all
        // 74,719 rows and then sorted them. One lookup of E86.0 measured 23
        // SECONDS through the app, and a timed-out lookup renders as
        // "No diagnosis matches that" — the search telling the operator that a
        // real code does not exist.
        //
        // Splitting it costs nothing, because the OR was never useful: no
        // description contains "E86.0", and no code contains "dehydration".
        var clauses = new List<string>();
        var anyCodeWord = false;

        for (var i = 0; i < words.Count; i++)
        {
            var name = $"w{i}";

            if (LooksLikeACode(words[i]))
            {
                anyCodeWord = true;

                // Prefix, not contains: "%E86%" cannot seek whatever it is
                // indexed on, and prefix is what is meant anyway — E86 should
                // find E86.0, E86.1 and E86.9. Nobody searches for the middle of
                // a diagnosis code.
                prms[name] = SearchTerm.EscapeLike(Undotted(words[i])) + "%";
                clauses.Add($"CodeBare LIKE @{name} ESCAPE '\\'");
            }
            else
            {
                // Wording matches anywhere, because it has to: "dehydration"
                // must find "Dehydration of newborn".
                prms[name] = "%" + SearchTerm.EscapeLike(words[i]) + "%";
                clauses.Add($"Description LIKE @{name} ESCAPE '\\'");
            }
        }

        // An exact code first: somebody who typed a whole code knows what they
        // want and should not hunt for it under a wordier description. Only
        // worth ranking when a code was actually typed; on a wording search the
        // CASE matches nothing and merely forces a sort of everything found.
        var order = anyCodeWord
            ? @"ORDER BY CASE
                           WHEN CodeBare = @exact THEN 0
                           WHEN CodeBare LIKE @starts ESCAPE '\' THEN 1
                           ELSE 2
                        END,
                        Code"
            : "ORDER BY Code";

        var rows = _db.Query(@"
            SELECT TOP (@take) Code, Description
            FROM dbo.IcdCodes
            WHERE " + string.Join(" AND ", clauses) + @"
            " + order, prms);

        return rows.Select(r => new IcdMatch(F.S(r["Code"]), F.S(r["Description"]))).ToList();
    }

    /// <summary>
    /// Whether a word is somebody reaching for a CODE rather than a description.
    ///
    /// ICD-10-CM codes are a letter, then two alphanumerics, then optionally a
    /// dot and up to four more: E66.9, J44.9, M17.0, S72.001A. Nothing in a
    /// description looks like that, and no code contains an English word, which
    /// is why searching both columns for every word was pure waste.
    ///
    /// Deliberately loose at the short end: "E8" is not a whole code, but
    /// somebody typing it is clearly working towards one, and a typeahead has to
    /// answer while they are still typing.
    /// </summary>
    private static bool LooksLikeACode(string word)
    {
        var bare = Undotted(word);

        if (bare.Length is < 2 or > 8) return false;
        if (!char.IsLetter(bare[0])) return false;

        return bare.Skip(1).All(char.IsLetterOrDigit) && bare.Any(char.IsDigit);
    }

    /// <inheritdoc />
    public IcdMatch? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Matched without dots so a code typed either way resolves, and the
        // STORED spelling is what comes back and gets filed.
        var r = _db.QueryOne(
            // Find is an exact lookup, so it seeks the indexed column too.
            "SELECT Code, Description FROM dbo.IcdCodes WHERE CodeBare = @code",
            new { code = Undotted(code) });

        return r == null ? null : new IcdMatch(F.S(r["Code"]), F.S(r["Description"]));
    }

    private static string Undotted(string s) => s.Replace(".", "").Trim();
}
