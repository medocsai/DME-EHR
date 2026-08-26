using EHR.Helpers;

namespace EHR.Services;

/// <summary>One payer as the catalog knows it.</summary>
/// <param name="PayerId">Our row id. What a form posts, so a name is never trusted from the client.</param>
/// <param name="Name">The payer's name as Office Ally publishes it.</param>
/// <param name="PayerCode">The Payer ID an 837 is addressed to. NOT unique: see DmePayerCatalog.</param>
public record PayerMatch(int PayerId, string Name, string PayerCode);

/// <summary>
/// The payer catalog: Office Ally's national list of who a claim can be sent to.
/// </summary>
public interface IDmePayerCatalog
{
    /// <summary>
    /// Payers matching what the operator has typed so far, best match first.
    /// An empty or whitespace term returns nothing rather than the first page of
    /// 4,017 rows, because a typeahead that answers before it is asked anything
    /// just flashes noise under the cursor.
    /// </summary>
    IReadOnlyList<PayerMatch> Search(string? term, int limit = 20);

    /// <summary>
    /// One payer by id, or null. This is how a posted payer becomes a name and a
    /// Payer ID: the form sends only the id and the two facts are read here.
    /// </summary>
    PayerMatch? Find(int payerId);
}

/// <summary>
/// WHY THIS EXISTS
/// The customer form used to offer four payers from a tenant-scoped table, and
/// the client's complaint was simply that the list was not exhaustive. It is now
/// Office Ally's published list of 4,017, loaded by
/// Migrations/Manual/2026-08-27_DME_Payer_Catalog.sql.
///
/// WHAT IT KNOWS THAT NOBODY ELSE HAS TO
///
/// 1. The catalog is GLOBAL. dbo.DmePayers has no TenantId and is not in
///    TenantIsolationPolicy, exactly like dbo.DmeCarcCodes. A national list
///    copied per tenant is thousands of duplicates of a fact none of them owns,
///    and the copies drift the first time one is corrected. The payer a customer
///    is actually insured with is the tenant fact, and that lives in
///    DmeCustomerInsurances.
///
/// 2. PayerCode is NOT unique. Office Ally issues ALLCA to two different payers.
///    So the row id is the identity, and Find() takes an id rather than a code.
///
/// 3. Matching is by WORD, contained anywhere, in any order. Anchoring at the
///    start of the name finds almost nothing, and matching the typed phrase as
///    one substring only works when the operator happens to type the words in
///    the vendor's order. 4,017 rows is small enough that the scan costs
///    nothing worth optimising, and being found matters more than the plan.
///
/// 4. It knows that "Blue Cross" and "BCBS" are the same thing. 145 rows use
///    one spelling and 18 use the other, so without that the search misses the
///    most common insurer in the country.
///
/// WHO CALLS IT
/// PayersController for the typeahead, and DmeController.CreateCustomer to turn
/// the posted id into the name and Payer ID it files on the insurance record.
/// </summary>
public sealed class DmePayerCatalog : IDmePayerCatalog
{
    private readonly IDmeDb _db;

    /// <summary>
    /// Hard ceiling on a page of matches, whatever a caller asks for. The list
    /// is a picker, not an export, and an unbounded limit posted from a browser
    /// would hand back all 4,017 rows on one keystroke.
    /// </summary>
    private const int MaxLimit = 50;

    public DmePayerCatalog(IDmeDb db) => _db = db;

    /// <summary>
    /// How people say a payer's name against how Office Ally spells it.
    ///
    /// This is not decoration. The list holds 145 payers spelled "BCBS ..." and
    /// only 18 spelled "Blue Cross ...", so a biller typing what is printed on
    /// the card finds almost none of them. Without this the search is worse than
    /// the four-item dropdown it replaced, because at least that showed its
    /// contents.
    ///
    /// Deliberately tiny. It is for spellings the whole industry uses two names
    /// for, not a general thesaurus.
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"]   = new[] { "bcbs" },
            ["cross"]  = new[] { "bcbs" },
            ["shield"] = new[] { "bcbs" },
            ["bcbs"]   = new[] { "blue" },
        };

    /// <summary>
    /// Most words a caller may type before the rest are ignored. Four is more
    /// than any real payer name needs, and it bounds the generated SQL.
    /// </summary>
    private const int MaxTerms = 4;

    /// <inheritdoc />
    public IReadOnlyList<PayerMatch> Search(string? term, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(term)) return Array.Empty<PayerMatch>();

        var q = term.Trim();
        var words = SearchTerm.Words(q, MaxTerms);

        if (words.Count == 0) return Array.Empty<PayerMatch>();

        // EVERY word must appear somewhere in the row, in any order, so
        // "texas bcbs" and "bcbs texas" both find the same payer. Matching the
        // typed phrase as one substring only works when the operator happens to
        // type the words in the vendor's order.
        var prms = new Dictionary<string, object?>
        {
            ["take"] = Math.Clamp(limit, 1, MaxLimit),
            ["exact"] = q,
            ["starts"] = SearchTerm.EscapeLike(q) + "%"
        };

        var clauses = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            var alternatives = new List<string> { words[i] };
            if (Aliases.TryGetValue(words[i], out var also)) alternatives.AddRange(also);

            var ors = new List<string>();
            for (var a = 0; a < alternatives.Count; a++)
            {
                var name = $"w{i}_{a}";
                prms[name] = "%" + SearchTerm.EscapeLike(alternatives[a]) + "%";
                ors.Add($"Name LIKE @{name} ESCAPE '\\' OR PayerCode LIKE @{name} ESCAPE '\\'");
            }
            clauses.Add("(" + string.Join(" OR ", ors) + ")");
        }

        // Ordering is the whole usability of the control: an exact Payer ID
        // first (somebody pasted one off a remittance), then names that START
        // with what was typed, then everything else.
        var rows = _db.Query(@"
            SELECT TOP (@take) PayerId, Name, PayerCode
            FROM dbo.DmePayers
            WHERE " + string.Join(" AND ", clauses) + @"
            ORDER BY CASE
                        WHEN PayerCode = @exact  THEN 0
                        WHEN Name LIKE @starts ESCAPE '\' THEN 1
                        ELSE 2
                     END,
                     Name", prms);

        return rows.Select(r => new PayerMatch(
            F.I(r["PayerId"]), F.S(r["Name"]), F.S(r["PayerCode"]))).ToList();
    }

    /// <inheritdoc />
    public PayerMatch? Find(int payerId)
    {
        if (payerId <= 0) return null;

        var r = _db.QueryOne(
            "SELECT PayerId, Name, PayerCode FROM dbo.DmePayers WHERE PayerId=@payerId",
            new { payerId });

        return r == null ? null
            : new PayerMatch(F.I(r["PayerId"]), F.S(r["Name"]), F.S(r["PayerCode"]));
    }

}
