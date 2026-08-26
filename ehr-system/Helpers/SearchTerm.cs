namespace EHR.Helpers;

/// <summary>
/// Turns what somebody typed into a picker into terms a LIKE can be built from.
///
/// WHY THIS EXISTS
/// Three reference lists are searched the same way: payers (4,017), ICD-10-CM
/// (74,719) and the national HCPCS codes (8,623). The escaping is the part that
/// must not be reinvented per list: a search term legitimately contains the
/// characters LIKE treats as wildcards, and one list that forgets to escape
/// them is one list where typing "%" returns everything.
///
/// WHO CALLS IT
/// DmePayerCatalog, DmeIcdCatalog, DmeHcpcsCatalog. Each keeps its own
/// vocabulary (the payer list knows Blue Cross means BCBS); this holds only
/// what is identical across all three.
/// </summary>
public static class SearchTerm
{
    /// <summary>
    /// Words that carry no meaning in a code or payer description and would
    /// otherwise have to appear in the row for it to match. "Blue Cross OF
    /// Texas" fails against "BCBS Texas" on the word "of" alone.
    /// </summary>
    private static readonly HashSet<string> Noise =
        new(StringComparer.OrdinalIgnoreCase) { "of", "the", "and", "&", "with", "due", "to" };

    /// <summary>
    /// The meaningful words in a search term, at most <paramref name="max"/> of
    /// them. Empty when the caller typed nothing but noise, which the callers
    /// treat as "no search", never as "match everything".
    /// </summary>
    public static IReadOnlyList<string> Words(string? term, int max)
    {
        if (string.IsNullOrWhiteSpace(term)) return Array.Empty<string>();

        return term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                   .Where(w => !Noise.Contains(w))
                   .Take(max)
                   .ToList();
    }

    /// <summary>
    /// Neutralises the wildcards LIKE would otherwise honour. Pair it with
    /// ESCAPE '\' in the query: escaping the term does nothing on its own.
    /// </summary>
    public static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
