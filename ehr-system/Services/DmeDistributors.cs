using EHR.Helpers;

namespace EHR.Services;

/// <summary>One distributor this supplier buys from.</summary>
/// <param name="DistributorId">Row id. What an order line points at.</param>
/// <param name="Name">Their name.</param>
/// <param name="AccountNo">This supplier's account number WITH them, or null.</param>
/// <param name="IsRetired">DERIVED from RetiredAt. Not a stored flag.</param>
public record Distributor(int DistributorId, string Name, string? AccountNo, bool IsRetired);

/// <summary>
/// Who a supplier buys from, for the items they never hold themselves.
/// </summary>
public interface IDmeDistributors
{
    /// <summary>Every distributor this tenant has, retired ones last.</summary>
    IReadOnlyList<Distributor> All(bool includeRetired = false);

    /// <summary>One by id, or null when it is not this tenant's.</summary>
    Distributor? Find(int distributorId);

    /// <summary>
    /// Adds one. Returns the new id, or null when the name is blank or this
    /// supplier already has a distributor by that name.
    /// </summary>
    int? Add(string? name, string? accountNo, string? phone, string? email);

    /// <summary>
    /// Takes one out of service. NEVER deletes: past orders are the record of
    /// who shipped them, and that record has to survive the relationship
    /// ending. Same rule as a clearinghouse account.
    /// </summary>
    bool Retire(int distributorId);
}

/// <summary>
/// WHY THIS EXISTS
/// The client: "Most of the items we deliver are drop-shipped from
/// manufacturer/distributors." Inventory assumed the supplier holds the goods,
/// so most of what they deliver had nowhere to be represented.
///
/// WHAT IT KNOWS THAT NOBODY ELSE HAS TO
///
/// 1. This is TENANT data, unlike the payer, ICD and HCPCS catalogs. Those are
///    national lists. Each supplier negotiates with their own distributors, so
///    dbo.DmeDistributors carries a TenantId and sits in the row level security
///    policy like every other DME table.
///
/// 2. A distributor is RETIRED, never deleted. `IsRetired` is derived from
///    `RetiredAt`; there is no second boolean to disagree with the date.
///
/// 3. A retired distributor still RESOLVES by id, so an order placed two years
///    ago still says who shipped it. Only the picker hides them.
///
/// WHO CALLS IT
/// DmeController for the picker on the order form and the Distributors screen.
/// The check that a POSTED distributor belongs to this tenant is done in
/// CreateOrder against the same table, beside the identical check on branch.
/// </summary>
public sealed class DmeDistributors : IDmeDistributors
{
    private readonly IDmeDb _db;

    public DmeDistributors(IDmeDb db) => _db = db;

    /// <inheritdoc />
    public IReadOnlyList<Distributor> All(bool includeRetired = false)
    {
        var rows = _db.Query(
            "SELECT DistributorId, Name, AccountNo, RetiredAt FROM dbo.DmeDistributors " +
            (includeRetired ? "" : "WHERE RetiredAt IS NULL ") +
            "ORDER BY CASE WHEN RetiredAt IS NULL THEN 0 ELSE 1 END, Name");

        return rows.Select(Map).ToList();
    }

    /// <inheritdoc />
    public Distributor? Find(int distributorId)
    {
        if (distributorId <= 0) return null;

        // No RetiredAt filter: an order placed before the relationship ended
        // still has to say who shipped it.
        var r = _db.QueryOne(
            "SELECT DistributorId, Name, AccountNo, RetiredAt FROM dbo.DmeDistributors WHERE DistributorId=@distributorId",
            new { distributorId });

        return r == null ? null : Map(r);
    }

    /// <inheritdoc />
    public int? Add(string? name, string? accountNo, string? phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = name.Trim();

        // Checked here as well as by the unique index, so the operator gets a
        // sentence rather than a constraint violation. RETIRED ones count: two
        // rows with the same name, one live and one dead, is exactly the
        // ambiguity the index exists to prevent.
        var exists = _db.Scalar(
            "SELECT DistributorId FROM dbo.DmeDistributors WHERE TenantId=@TenantId AND Name=@clean",
            new { clean });

        if (exists != null) return null;

        return Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeDistributors (TenantId, Name, AccountNo, Phone, Email)
            OUTPUT inserted.DistributorId
            VALUES (@TenantId, @clean, @accountNo, @phone, @email)",
            new
            {
                clean,
                accountNo = Blank(accountNo),
                phone = Blank(phone),
                email = Blank(email)
            }));
    }

    /// <inheritdoc />
    public bool Retire(int distributorId)
    {
        if (distributorId <= 0) return false;

        // Guarded on RetiredAt IS NULL so two people clicking Retire produce one
        // retirement, and the date stays the first one. Same shape as voiding a
        // payment.
        return _db.Execute(
            "UPDATE dbo.DmeDistributors SET RetiredAt = SYSUTCDATETIME() " +
            "WHERE DistributorId=@distributorId AND TenantId=@TenantId AND RetiredAt IS NULL",
            new { distributorId }) == 1;
    }

    private static object Blank(string? v)
        => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v.Trim();

    private static Distributor Map(Dictionary<string, object?> r) => new(
        F.I(r["DistributorId"]),
        F.S(r["Name"]),
        r["AccountNo"] is null or DBNull ? null : F.S(r["AccountNo"]),
        r["RetiredAt"] is not (null or DBNull));
}
