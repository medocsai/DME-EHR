using Microsoft.Data.SqlClient;
using System.Globalization;
using EHR.Services;

namespace EHR.Helpers;

/// <summary>
/// Data access for the DME-native tables in DMEEHR.
///
/// WHY THIS EXISTS
/// Deliberately separate from the generated EHR DbContext so the DME product
/// stays isolated from the clinical EF model during the conversion. Rows come
/// back as case-insensitive dictionaries, which keeps the Razor views compact.
///
/// WHY IT IS SCOPED AND NOT STATIC
/// It used to be a static helper holding only a connection string, which meant
/// it had no way to know which tenant was asking. Every DME query therefore ran
/// unscoped. It is now request-scoped so it can resolve the caller's tenant and
/// stamp it onto the connection.
///
/// HOW TENANT ISOLATION WORKS
/// Every connection opened here immediately sets SESSION_CONTEXT
/// ('CurrentTenantId'), which is what the SQL Server TenantIsolationPolicy
/// reads. Filtering therefore happens inside the database, on every table, for
/// every statement, whether or not the caller remembered a WHERE clause. That
/// is the point: a forgotten filter is the normal failure, so the guard cannot
/// live in the queries.
///
/// The policy treats "no context set" as "show everything" (background jobs and
/// migrations rely on that). So this class REFUSES to open a connection when
/// the tenant is unknown rather than quietly running unfiltered.
///
/// @TenantId is added to every command automatically, so INSERT statements can
/// reference it without each caller having to remember to pass it.
///
/// WHO CALLS IT
/// DmeController and HcpcsController, by constructor injection.
/// </summary>
public interface IDmeDb
{
    /// <summary>Tenant this instance is bound to for the current request.</summary>
    int TenantId { get; }

    /// <summary>
    /// Branch the caller is currently looking at, or NULL for all of them.
    ///
    /// UNLIKE TenantId, THIS IS NOT A SECURITY BOUNDARY and is deliberately not
    /// enforced by row level security. The tenant is the wall. A location is a
    /// working filter inside it, and the owner of a multi-branch supplier needs
    /// to switch it off to see the whole business. Enforcing it in the database
    /// would mean punching a hole through the isolation to allow that.
    ///
    /// So a missing location shows everything, which is why it can be null and
    /// why TenantId cannot.
    /// </summary>
    int? LocationId { get; }

    /// <summary>
    /// The complete branch predicate for this request, ready to drop into a
    /// WHERE clause. Combines two separate things so no caller has to remember
    /// both:
    ///
    ///   what the caller CHOSE   the branch switcher, or all of them
    ///   what the caller MAY see dbo.UserLocations, for restricted roles
    ///
    /// The second is the one that must never be forgotten, which is exactly why
    /// it is not left to each query to remember. For a restricted caller with no
    /// grants it renders `1=0`: no rows, deliberately, rather than the whole
    /// tenant.
    ///
    /// Use it as: "SELECT * FROM dbo.vDmeOrders WHERE " + _db.LocationScope()
    ///
    /// <paramref name="column"/> qualifies the column when the query joins and
    /// the name would otherwise be ambiguous, for example "c.LocationId". It is
    /// a parameter rather than a string replacement on the result, because the
    /// predicate also contains @LocationId and a blind replace would rewrite the
    /// PARAMETER name too and produce nonsense that still compiles as SQL.
    /// </summary>
    string LocationScope(string column = "LocationId");

    /// <summary>
    /// Only the "may see" half, without the branch the caller happens to be
    /// viewing.
    ///
    /// This is for lists of branches themselves: the switcher and the New
    /// Customer picker. Applying the chosen half there would reduce the list to
    /// the one branch already selected, which makes switching impossible.
    /// </summary>
    string LocationGrants(string column = "LocationId");

    List<Dictionary<string, object?>> Query(string sql, object? prms = null);
    Dictionary<string, object?>? QueryOne(string sql, object? prms = null);
    int Execute(string sql, object? prms = null);
    object? Scalar(string sql, object? prms = null);
    string NextNumber(string prefix, int pad = 5);
}

/// <inheritdoc cref="IDmeDb"/>
public sealed class DmeDb : IDmeDb
{
    private readonly string _connectionString;
    private readonly int _tenantId;
    private readonly int? _locationId;

    /// <summary>
    /// Seed value for a tenant's first sequence number. Chosen so generated
    /// numbers look like real operational data (ORD-01001) rather than ORD-00001
    /// on a brand new tenant.
    /// </summary>
    private const int SequenceSeed = 1000;

    private readonly IDmeLocationScope? _scope;

    public DmeDb(IConfiguration config, ITenantProvider tenantProvider, ILocationProvider locationProvider,
                 IDmeLocationScope? scope = null)
    {
        // Optional so the existing constructor tests, which are about the tenant
        // guard, do not have to build a scope they never exercise. In the app it
        // is always supplied by DI; a null one means "unrestricted", which is
        // the same answer an owner gets and cannot widen anyone in production.
        _scope = scope;

        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");

        // No throw here, unlike the tenant below. A null location legitimately
        // means "all branches", and TenantResolutionMiddleware has already
        // checked that any location it did resolve belongs to this tenant and is
        // active, so a stale or foreign id arrives here as null rather than as
        // somebody else's branch.
        _locationId = locationProvider.LocationId;

        // No tenant means we cannot scope the query, and the RLS policy would
        // fall through to showing every tenant's rows. Fail loudly instead.
        // Requests reach DME actions only when [Authorize] has already passed,
        // so a missing tenant here is a bug in token issuance, not a user error.
        _tenantId = tenantProvider.TenantId
            ?? throw new InvalidOperationException(
                "DME data access attempted without a tenant context. " +
                "The caller's token carries no TenantId claim.");
    }

    public int TenantId => _tenantId;

    /// <inheritdoc />
    public int? LocationId => _locationId;

    /// <inheritdoc />
    public string LocationScope(string column = "LocationId")
    {
        // What the caller CHOSE. Null is the all-branches view, so the chosen
        // half imposes nothing.
        var chosen = _locationId.HasValue
            ? $"{column} = @LocationId"
            : "1=1";

        // What the caller MAY see. An unrestricted caller (Super Admin, Clinic
        // Admin) is not narrowed at all.
        if (_scope == null || _scope.IsUnrestricted)
            return $"({chosen})";

        var allowed = _scope.AllowedLocationIds;

        // THE IMPORTANT LINE. No grants means no rows, not every row. The
        // tempting shape here is to skip the filter when the set is empty,
        // which silently hands the whole tenant to precisely the caller the
        // grants were meant to contain.
        if (allowed.Count == 0)
            return "(1=0)";

        // Values are ints read out of the database, never strings from the
        // request, so there is nothing here to inject. A parameter list would
        // need one placeholder per grant and buys nothing.
        var ids = string.Join(",", allowed);
        return $"({chosen} AND {column} IN ({ids}))";
    }

    /// <inheritdoc />
    public string LocationGrants(string column = "LocationId")
    {
        if (_scope == null || _scope.IsUnrestricted) return "(1=1)";

        var allowed = _scope.AllowedLocationIds;
        if (allowed.Count == 0) return "(1=0)";

        return $"({column} IN ({string.Join(",", allowed)}))";
    }

    public List<Dictionary<string, object?>> Query(string sql, object? prms = null)
    {
        var list = new List<Dictionary<string, object?>>();
        using var conn = OpenScoped();
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

    public Dictionary<string, object?>? QueryOne(string sql, object? prms = null)
        => Query(sql, prms).FirstOrDefault();

    public int Execute(string sql, object? prms = null)
    {
        using var conn = OpenScoped();
        using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, prms);
        return cmd.ExecuteNonQuery();
    }

    public object? Scalar(string sql, object? prms = null)
    {
        using var conn = OpenScoped();
        using var cmd = new SqlCommand(sql, conn);
        AddParams(cmd, prms);
        var v = cmd.ExecuteScalar();
        return v == DBNull.Value ? null : v;
    }

    /// <summary>
    /// Atomic human-friendly sequence per tenant (ORD-01011 etc.).
    /// Creates the tenant's counter on first use, so onboarding a tenant does
    /// not require seeding rows by hand. UPDLOCK/HOLDLOCK inside the
    /// transaction makes the check-then-insert safe against a concurrent
    /// request for the same tenant and prefix.
    /// </summary>
    public string NextNumber(string prefix, int pad = 5)
    {
        using var conn = OpenScoped();
        using var tx = conn.BeginTransaction();
        using var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM dbo.DmeSeq WITH (UPDLOCK, HOLDLOCK)
                           WHERE Name = @n AND TenantId = @TenantId)
                INSERT INTO dbo.DmeSeq (Name, TenantId, Val) VALUES (@n, @TenantId, @seed);

            UPDATE dbo.DmeSeq SET Val = Val + 1
            OUTPUT inserted.Val
            WHERE Name = @n AND TenantId = @TenantId;", conn, tx);

        cmd.Parameters.AddWithValue("@n", prefix);
        cmd.Parameters.AddWithValue("@TenantId", _tenantId);
        cmd.Parameters.AddWithValue("@seed", SequenceSeed);

        var v = cmd.ExecuteScalar();
        tx.Commit();
        return prefix + "-" + Convert.ToInt32(v).ToString().PadLeft(pad, '0');
    }

    /// <summary>
    /// Open a connection and bind it to this request's tenant before any
    /// caller statement runs. SESSION_CONTEXT is per-connection, so this has to
    /// happen on every single connection; the middleware setting it on the EF
    /// connection does nothing for these.
    /// </summary>
    private SqlConnection OpenScoped()
    {
        var conn = new SqlConnection(_connectionString);
        try
        {
            conn.Open();
            using var ctx = new SqlCommand(
                "EXEC sp_set_session_context N'CurrentTenantId', @TenantId;", conn);
            ctx.Parameters.AddWithValue("@TenantId", _tenantId);
            ctx.ExecuteNonQuery();
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Bind caller parameters, then always add @TenantId so INSERT statements
    /// can reference it without every call site repeating it. A caller that
    /// supplies its own TenantId wins, which keeps the parameter explicit where
    /// a query genuinely needs to name it.
    /// </summary>
    private void AddParams(SqlCommand cmd, object? prms)
    {
        // A dictionary is accepted as well as an anonymous object so callers
        // that build a column list at runtime (the PHI backfill walks the
        // encrypted-column list) do not have to fall back to string building.
        if (prms is IDictionary<string, object?> map)
        {
            foreach (var kvp in map)
                cmd.Parameters.AddWithValue("@" + kvp.Key, kvp.Value ?? DBNull.Value);
        }
        else if (prms != null)
        {
            foreach (var p in prms.GetType().GetProperties())
                cmd.Parameters.AddWithValue("@" + p.Name, p.GetValue(prms) ?? DBNull.Value);
        }

        if (!cmd.Parameters.Contains("@TenantId"))
            cmd.Parameters.AddWithValue("@TenantId", _tenantId);

        // @LocationId is always present so a query can write
        //     AND (@LocationId IS NULL OR LocationId = @LocationId)
        // and get "this branch" or "all branches" from the same statement, with
        // no string building and no second query.
        if (!cmd.Parameters.Contains("@LocationId"))
            cmd.Parameters.AddWithValue("@LocationId", (object?)_locationId ?? DBNull.Value);
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
        ["maintenance"] = ("amber", "Maintenance"), ["recalled"] = ("red", "Recalled"), ["denied"] = ("red", "Denied"),
        // Payment outcomes. Derived in vDmeClaims from the payments posted, so
        // these chips can never disagree with the money behind them.
        ["unpaid"] = ("gray", "Awaiting payment"), ["partial"] = ("amber", "Part paid"),
        ["part-denied"] = ("red", "Part denied"),
        ["patient-due"] = ("teal", "Patient owes"), ["voided"] = ("gray", "Voided")
    };

    /// <summary>
    /// How a payment arrived, written the way a person writes it. The database
    /// stores the X12-ish lowercase token; this is presentation only.
    /// </summary>
    public static string PayMethod(object? method) => S(method) switch
    {
        "check" => "Check",
        "eft" => "EFT",
        "card" => "Card",
        "cash" => "Cash",
        _ => "Other"
    };

    /// <summary>HTML status chip (use with @Html.Raw).</summary>
    public static string StatusChip(object? status)
    {
        var s = S(status);
        if (!_chips.TryGetValue(s, out var v)) v = ("gray", s);
        return $"<span class=\"dme-chip {v.cls}\">{System.Net.WebUtility.HtmlEncode(v.label)}</span>";
    }
}
