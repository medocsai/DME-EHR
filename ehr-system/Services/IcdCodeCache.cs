using System;
using System.Collections.Generic;

namespace EHR.Services;

/// <summary>
/// The whole ICD-10-CM code set, held in memory.
///
/// WHY THIS EXISTS, MEASURED RATHER THAN ASSUMED
///
/// Searching the 74,719 codes in SQL took roughly 1.5 seconds per query, every
/// query, against 54ms for the payer lookup sitting next to it on the same
/// form. Repeating a query gave the same time, so it was not a cold cache.
///
/// It cannot be indexed away. A biller searching "obesity" has to find "Morbid
/// (severe) obesity due to excess calories", so the predicate is
/// LIKE '%word%', and a leading wildcard cannot seek. A persisted computed
/// column for the undotted code was tried and made it WORSE, because the
/// description half still scanned and there was now more work per row. Only a
/// full-text catalog would fix it in SQL, and that is a deployment dependency
/// on the client's server for one picker.
///
/// So the database is taken out of the path. This list is:
///
///   - GLOBAL. No TenantId, outside the row level security policy, identical
///     for every supplier on the server. One copy is correct for everyone.
///   - IMMUTABLE between releases. CMS republishes once a year, on October 1.
///   - SMALL. About 6MB of characters, roughly 20MB as objects. A rounding
///     error next to what a web server already holds.
///
/// THE ANNUAL REFRESH NEEDS A RESTART, and that is the honest cost of this.
/// Reloading the table with the October release does not change what is in
/// memory. That is acceptable because the reload is a migration run by hand
/// during a deployment, and the deployment restarts the app anyway. It is
/// written down here so the first person to run the October script and see old
/// wording does not spend an afternoon on it.
///
/// A singleton, deliberately. IDmeDb is request scoped and pinned to a tenant,
/// so this cannot hold one; the first request that needs the list hands its own
/// database over as a loader, and every request after that is served from
/// memory.
/// </summary>
public sealed class IcdCodeCache
{
    /// <summary>One code, as it is searched.</summary>
    /// <param name="Code">The stored spelling, with the dot. E66.9.</param>
    /// <param name="Description">CMS's wording, as filed on a claim.</param>
    /// <param name="CodeNoDot">The same code with the dot removed. E669.</param>
    public sealed record Entry(string Code, string Description, string CodeNoDot);

    private readonly object _gate = new();
    private volatile IReadOnlyList<Entry>? _all;

    /// <summary>
    /// The whole list, loading it on first use.
    ///
    /// Double checked so that the thousandth request does not take a lock, and
    /// the loader runs exactly once even if several requests arrive together
    /// during startup. A failed load leaves the field null, so the next request
    /// retries rather than caching an empty list forever, which would look
    /// exactly like a code set that had gone missing.
    /// </summary>
    public IReadOnlyList<Entry> All(Func<IReadOnlyList<Entry>> load)
    {
        var cached = _all;
        if (cached != null) return cached;

        lock (_gate)
        {
            if (_all != null) return _all;
            var loaded = load();
            if (loaded.Count > 0) _all = loaded;
            return loaded;
        }
    }

    /// <summary>Whether the list has been loaded yet. For diagnostics only.</summary>
    public bool IsLoaded => _all != null;

    /// <summary>How many codes are held. Zero until the first search.</summary>
    public int Count => _all?.Count ?? 0;
}
