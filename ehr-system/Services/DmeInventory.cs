using EHR.Helpers;

namespace EHR.Services;

/// <summary>Outcome of an inventory action, with a sentence a person can act on.</summary>
public record InventoryResult(bool Success, string? Error = null)
{
    public static readonly InventoryResult Ok = new(true);
    public static InventoryResult Refused(string why) => new(false, why);
}

/// <summary>One delivery line's stock, resolved before anything is written.</summary>
/// <param name="LineId">The order line.</param>
/// <param name="UnitIds">The physical units leaving, for a serialised line. Empty otherwise.</param>
/// <param name="Serials">Their serial numbers, in the same order.</param>
public record DeliveryStock(int LineId, IReadOnlyList<int> UnitIds, IReadOnlyList<string> Serials);

/// <summary>Stock in, stock moved, stock corrected, and equipment back from a rental.</summary>
public interface IDmeInventory
{
    /// <summary>Goods arrived at a branch. A serialised item needs one serial per unit.</summary>
    InventoryResult Receive(string hcpcs, int locationId, int qty, IReadOnlyList<string> serials, string? reference, int? userId);

    /// <summary>A counted correction to a consumable, with the reason. Never on a serialised item.</summary>
    InventoryResult Adjust(string hcpcs, int locationId, int delta, string? reason, int? userId);

    /// <summary>Stock moved between two branches of the same supplier.</summary>
    InventoryResult Transfer(string hcpcs, int fromLocationId, int toLocationId, int qty, IReadOnlyList<int> unitIds, int? userId);

    /// <summary>A unit on the shelf goes for repair, comes back, is recalled or is written off.</summary>
    InventoryResult SetUnitStatus(int unitId, string status, string? note, int? userId);

    /// <summary>Rented equipment came back. The rental stops and the unit returns to a shelf.</summary>
    InventoryResult ReturnRental(int rentalId, int locationId, string condition, int? userId);

    /// <summary>
    /// Checks that every stock line of an order can actually leave the branch,
    /// BEFORE the delivery writes anything. Returns the resolved units per line,
    /// or the sentence to show.
    /// </summary>
    (IReadOnlyList<DeliveryStock>? Plan, string? Error) PlanDelivery(
        IReadOnlyList<Dictionary<string, object?>> lines, int locationId,
        IReadOnlyDictionary<int, IReadOnlyList<int>> chosenUnits);
}

/// <summary>
/// WHY THIS EXISTS
/// The stock ledger only ever went down. Delivery wrote a negative movement and
/// nothing wrote a positive one, so a new supplier started at zero and every
/// delivery from their own shelf drove on-hand negative. Delivery also invented
/// a serial number for each serialised unit rather than using the unit on the
/// shelf, and a rental could never be returned. This is the half that was
/// missing.
///
/// THE RULES IT OWNS
/// 1. On-hand is SUM(DmeStockMovements.Qty). Nothing here stores a balance.
///    Every action writes a movement, and that row is also the history.
/// 2. A serialised item moves as UNITS: received with their serials, picked by
///    serial at delivery, moved by unit between branches, returned by unit.
///    Its count in the ledger always equals its units on the shelf, which is
///    why Adjust refuses a serialised item: the way to take one out of stock is
///    to change that unit's status, so the count and the unit cannot disagree.
/// 3. Every write is ONE SQL batch inside a transaction. DmeDb opens a fresh
///    connection per statement, so two calls could never share a transaction;
///    a receipt that registered the units and failed before the movement would
///    leave units the ledger does not count.
/// 4. The branch is checked against the tenant AND the caller's grants before
///    anything is filed into it. Row level security covers TenantId and would
///    accept a foreign LocationId beside it.
/// 5. Parameters are named @branchId, @fromId, @toId, never @locationId: DmeDb
///    injects @LocationId (the branch being VIEWED) into every command and SQL
///    parameter names are case insensitive. See the trap in CLAUDE.md.
/// </summary>
public sealed class DmeInventory : IDmeInventory
{
    private readonly IDmeDb _db;

    public DmeInventory(IDmeDb db) => _db = db;

    /// <summary>Upper bound on one receipt or transfer. A typo of 10000 for 100 should not land.</summary>
    public const int MaxQty = 5000;

    /// <summary>The unit statuses a person may set by hand, and what each means.</summary>
    public static readonly IReadOnlyDictionary<string, string> ManualStatuses = new Dictionary<string, string>
    {
        ["in-stock"]    = "On the shelf",
        ["maintenance"] = "In repair or cleaning",
        ["recalled"]    = "Recalled by the manufacturer",
        ["written-off"] = "Lost, damaged beyond repair or scrapped",
    };

    /// <summary>
    /// Statuses that count in the ledger. A unit in repair is still owned and
    /// still at the branch, so it stays counted; recalled and written-off units
    /// are gone and leave the count.
    /// </summary>
    private static readonly HashSet<string> Counted = new() { "in-stock", "maintenance" };

    /// <summary>
    /// One serial per line, or separated by commas. Blank entries are dropped
    /// and surrounding spaces trimmed, because a list pasted out of a packing
    /// slip always carries both.
    /// </summary>
    public static IReadOnlyList<string> ParseSerials(string? raw)
        => (raw ?? "")
            .Split(new[] { '\n', '\r', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    // ---------------------------------------------------------------- lookups

    private Dictionary<string, object?>? Item(string hcpcs)
        => _db.QueryOne("SELECT Hcpcs, Name, IsSerialized FROM dbo.HcpcsCodes WHERE Hcpcs=@h", new { h = hcpcs });

    /// <summary>The branch, when it is this tenant's, active, and one the caller may work in.</summary>
    private Dictionary<string, object?>? Branch(int branchId)
        => _db.QueryOne(
            "SELECT LocationId, Name FROM dbo.Locations WHERE LocationId=@branchId AND TenantId=@TenantId AND IsActive=1 AND "
            + _db.LocationGrants(), new { branchId });

    private int OnHandAt(string hcpcs, int branchId)
        => Convert.ToInt32(_db.Scalar(
            "SELECT ISNULL(SUM(Qty),0) FROM dbo.DmeStockMovements WHERE Hcpcs=@h AND LocationId=@branchId",
            new { h = hcpcs, branchId }));

    private static object Db(object? v) => v ?? DBNull.Value;

    // ---------------------------------------------------------------- receive

    /// <inheritdoc />
    public InventoryResult Receive(string hcpcs, int locationId, int qty, IReadOnlyList<string> serials, string? reference, int? userId)
    {
        var item = Item(hcpcs ?? "");
        if (item == null) return InventoryResult.Refused("Choose an item from your catalog.");

        var branch = Branch(locationId);
        if (branch == null) return InventoryResult.Refused("Choose the location the stock arrived at.");

        var serialised = F.B(item["IsSerialized"]);
        if (serialised)
        {
            // The count IS the serials. Asking for a separate quantity would
            // let the two disagree.
            qty = serials.Count;
            if (qty == 0)
                return InventoryResult.Refused("This item is tracked by serial number. Enter one serial per unit.");

            var bad = serials.FirstOrDefault(s => s.Length > 40);
            if (bad != null) return InventoryResult.Refused($"Serial '{bad[..20]}...' is longer than 40 characters.");

            var repeated = serials.GroupBy(s => s, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (repeated != null) return InventoryResult.Refused($"Serial {repeated.Key} is listed twice.");
        }

        if (qty < 1 || qty > MaxQty)
            return InventoryResult.Refused($"Quantity must be between 1 and {MaxQty}.");

        var p = new Dictionary<string, object?>
        {
            ["h"] = item["Hcpcs"], ["n"] = item["Name"], ["branchId"] = locationId, ["qty"] = qty,
            ["note"] = string.IsNullOrWhiteSpace(reference) ? "Received" : "Received: " + reference.Trim()[..Math.Min(reference.Trim().Length, 180)],
            ["userId"] = Db(userId),
        };

        var unitSql = "";
        if (serialised)
        {
            // Already registered is checked up front so the person is told
            // WHICH serial, rather than getting the unique index's error.
            var names = new List<string>();
            for (var i = 0; i < serials.Count; i++)
            {
                p["s" + i] = serials[i];
                names.Add("@s" + i);
            }
            var taken = _db.QueryOne(
                "SELECT TOP 1 SerialNumber FROM dbo.DmeSerializedUnits WHERE Hcpcs=@h AND SerialNumber IN (" + string.Join(",", names) + ")", p);
            if (taken != null)
                return InventoryResult.Refused($"Serial {F.S(taken["SerialNumber"])} is already registered for this item.");

            unitSql = "INSERT INTO dbo.DmeSerializedUnits (Hcpcs,ItemName,SerialNumber,Status,TenantId,LocationId) VALUES "
                      + string.Join(",", names.Select(n => $"(@h,@n,{n},'in-stock',@TenantId,@branchId)")) + ";";
        }

        _db.Execute($@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            {unitSql}
            INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,Note,CreatedBy)
            VALUES (@TenantId,@branchId,@h,@qty,'receipt',@note,@userId);
            COMMIT;", p);

        return InventoryResult.Ok;
    }

    // ---------------------------------------------------------------- adjust

    /// <inheritdoc />
    public InventoryResult Adjust(string hcpcs, int locationId, int delta, string? reason, int? userId)
    {
        var item = Item(hcpcs ?? "");
        if (item == null) return InventoryResult.Refused("Choose an item from your catalog.");

        if (F.B(item["IsSerialized"]))
            return InventoryResult.Refused(
                "This item is tracked by serial number. Change the unit's status instead, so the count and the units cannot disagree.");

        var branch = Branch(locationId);
        if (branch == null) return InventoryResult.Refused("Choose the location being corrected.");

        if (delta == 0 || Math.Abs(delta) > MaxQty)
            return InventoryResult.Refused($"Enter how many to add or remove, up to {MaxQty}.");

        if (string.IsNullOrWhiteSpace(reason))
            return InventoryResult.Refused("Say why. A correction with no reason cannot be checked later.");

        var onHand = OnHandAt(F.S(item["Hcpcs"]), locationId);
        if (onHand + delta < 0)
            return InventoryResult.Refused($"{F.S(branch["Name"])} holds {onHand}. It cannot go below zero.");

        _db.Execute(@"
            INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,Note,CreatedBy)
            VALUES (@TenantId,@branchId,@h,@delta,'adjustment',@note,@userId)",
            new { h = item["Hcpcs"], branchId = locationId, delta, note = Trim200(reason), userId = Db(userId) });

        return InventoryResult.Ok;
    }

    // ---------------------------------------------------------------- transfer

    /// <inheritdoc />
    public InventoryResult Transfer(string hcpcs, int fromLocationId, int toLocationId, int qty, IReadOnlyList<int> unitIds, int? userId)
    {
        var item = Item(hcpcs ?? "");
        if (item == null) return InventoryResult.Refused("Choose an item from your catalog.");

        if (fromLocationId == toLocationId)
            return InventoryResult.Refused("Choose two different locations.");

        var from = Branch(fromLocationId);
        var to = Branch(toLocationId);
        if (from == null || to == null) return InventoryResult.Refused("Choose the location it leaves and the one it goes to.");

        var p = new Dictionary<string, object?>
        {
            ["h"] = item["Hcpcs"], ["fromId"] = fromLocationId, ["toId"] = toLocationId, ["userId"] = Db(userId),
            ["noteOut"] = "Transfer to " + F.S(to["Name"]), ["noteIn"] = "Transfer from " + F.S(from["Name"]),
        };

        var unitSql = "";
        if (F.B(item["IsSerialized"]))
        {
            var ids = unitIds.Distinct().ToList();
            if (ids.Count == 0) return InventoryResult.Refused("Choose the units being moved.");

            var names = new List<string>();
            for (var i = 0; i < ids.Count; i++) { p["u" + i] = ids[i]; names.Add("@u" + i); }
            var inList = string.Join(",", names);

            // Every unit must be this item, at the branch it is leaving, and not
            // out with a customer.
            var movable = Convert.ToInt32(_db.Scalar(
                "SELECT COUNT(*) FROM dbo.DmeSerializedUnits WHERE UnitId IN (" + inList + ") AND Hcpcs=@h AND LocationId=@fromId AND Status IN ('in-stock','maintenance')", p));
            if (movable != ids.Count)
                return InventoryResult.Refused($"Only units on the shelf at {F.S(from["Name"])} can be moved.");

            qty = ids.Count;
            unitSql = "UPDATE dbo.DmeSerializedUnits SET LocationId=@toId WHERE UnitId IN (" + inList + ") AND LocationId=@fromId;";
        }
        else
        {
            if (qty < 1 || qty > MaxQty) return InventoryResult.Refused($"Quantity must be between 1 and {MaxQty}.");

            var onHand = OnHandAt(F.S(item["Hcpcs"]), fromLocationId);
            if (qty > onHand) return InventoryResult.Refused($"{F.S(from["Name"])} holds {onHand}.");
        }

        p["qty"] = qty;
        _db.Execute($@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            {unitSql}
            INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,Note,CreatedBy)
            VALUES (@TenantId,@fromId,@h,-@qty,'transfer',@noteOut,@userId),
                   (@TenantId,@toId,  @h, @qty,'transfer',@noteIn, @userId);
            COMMIT;", p);

        return InventoryResult.Ok;
    }

    // ---------------------------------------------------------------- unit status

    /// <inheritdoc />
    public InventoryResult SetUnitStatus(int unitId, string status, string? note, int? userId)
    {
        if (!ManualStatuses.ContainsKey(status ?? ""))
            return InventoryResult.Refused("Choose a status.");

        var unit = _db.QueryOne("SELECT UnitId, Hcpcs, SerialNumber, Status, LocationId FROM dbo.DmeSerializedUnits WHERE UnitId=@unitId", new { unitId });
        if (unit == null) return InventoryResult.Refused("That unit does not exist.");

        var current = F.S(unit["Status"]);
        if (current == "rented" || current == "sold")
            return InventoryResult.Refused("That unit is with a customer. A rental comes back through Return on the Rentals screen.");

        if (current == status) return InventoryResult.Ok;

        // Recalled and written-off are final. Bringing one back would need the
        // ledger to invent where it had been.
        if (!Counted.Contains(current))
            return InventoryResult.Refused($"That unit is {current}. It cannot be put back into stock.");

        if (!Counted.Contains(status!) && string.IsNullOrWhiteSpace(note))
            return InventoryResult.Refused("Say why the unit is leaving stock.");

        // Leaving the counted set is a -1 in the ledger; moving between
        // in-stock and maintenance is not, because the unit is still here.
        var leaves = Counted.Contains(current) && !Counted.Contains(status!);

        _db.Execute($@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.DmeSerializedUnits SET Status=@status WHERE UnitId=@unitId AND Status=@current;
            {(leaves ? @"INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,RefType,RefId,Note,CreatedBy)
                         VALUES (@TenantId,@branchId,@h,-1,'adjustment','DmeSerializedUnit',@unitId,@note,@userId);" : "")}
            COMMIT;",
            new
            {
                unitId, status, current, h = unit["Hcpcs"], branchId = unit["LocationId"],
                note = Trim200($"{F.S(unit["SerialNumber"])} {status}: {note}".Trim()), userId = Db(userId)
            });

        return InventoryResult.Ok;
    }

    // ---------------------------------------------------------------- return

    /// <inheritdoc />
    public InventoryResult ReturnRental(int rentalId, int locationId, string condition, int? userId)
    {
        if (condition != "in-stock" && condition != "maintenance")
            return InventoryResult.Refused("Say whether it goes back on the shelf or for cleaning and repair first.");

        var rental = _db.QueryOne("SELECT RentalId, Hcpcs, Serial, Status, UnitId FROM dbo.DmeRentals WHERE RentalId=@rentalId", new { rentalId });
        if (rental == null) return InventoryResult.Refused("That rental does not exist.");

        // Only an ACTIVE rental can come back. 'ended' means the cap was
        // reached and the customer owns the equipment now.
        if (F.S(rental["Status"]) != "active")
            return InventoryResult.Refused("Only an active rental can be returned. A rental that reached its cap belongs to the customer.");

        var branch = Branch(locationId);
        if (branch == null) return InventoryResult.Refused("Choose the location it came back to.");

        var unitId = rental["UnitId"] is null or DBNull ? (int?)null : Convert.ToInt32(rental["UnitId"]);

        _db.Execute($@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.DmeRentals SET Status='returned' WHERE RentalId=@rentalId AND Status='active';
            IF @@ROWCOUNT = 1
            BEGIN
                {(unitId != null ? "UPDATE dbo.DmeSerializedUnits SET Status=@condition, CustomerId=NULL, LocationId=@branchId WHERE UnitId=@unitId;" : "")}
                INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,RefType,RefId,Note,CreatedBy)
                VALUES (@TenantId,@branchId,@h,1,'return','DmeRental',@rentalId,@note,@userId);
            END
            COMMIT;",
            new
            {
                rentalId, branchId = locationId, condition, unitId = Db(unitId), h = rental["Hcpcs"],
                note = Trim200("Returned from rental " + F.S(rental["Serial"])), userId = Db(userId)
            });

        return InventoryResult.Ok;
    }

    // ---------------------------------------------------------------- delivery

    /// <inheritdoc />
    public (IReadOnlyList<DeliveryStock>? Plan, string? Error) PlanDelivery(
        IReadOnlyList<Dictionary<string, object?>> lines, int locationId,
        IReadOnlyDictionary<int, IReadOnlyList<int>> chosenUnits)
    {
        var plan = new List<DeliveryStock>();
        var used = new HashSet<int>();
        var need = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var l in lines)
        {
            // Drop-shipped lines never touch this supplier's stock.
            if (l["DistributorId"] is not (null or DBNull)) continue;

            var lineId = F.I(l["LineId"]);
            var hcpcs = F.S(l["Hcpcs"]);
            var qty = Math.Max(F.I(l["Qty"]), 1);

            if (!F.B(l["IsSerialized"]))
            {
                need[hcpcs] = need.GetValueOrDefault(hcpcs) + qty;
                plan.Add(new DeliveryStock(lineId, Array.Empty<int>(), Array.Empty<string>()));
                continue;
            }

            var ids = chosenUnits.TryGetValue(lineId, out var c) ? c.Distinct().ToList() : new List<int>();
            if (ids.Count != qty)
                return (null, $"Choose {qty} unit(s) of {hcpcs} by serial number.");
            if (ids.Any(id => !used.Add(id)))
                return (null, "The same unit is chosen twice.");

            var p = new Dictionary<string, object?> { ["h"] = hcpcs, ["branchId"] = locationId };
            var names = new List<string>();
            for (var i = 0; i < ids.Count; i++) { p["u" + i] = ids[i]; names.Add("@u" + i); }

            var units = _db.Query(
                "SELECT UnitId, SerialNumber FROM dbo.DmeSerializedUnits WHERE UnitId IN (" + string.Join(",", names) +
                ") AND Hcpcs=@h AND LocationId=@branchId AND Status='in-stock'", p);

            if (units.Count != ids.Count)
                return (null, $"A chosen unit of {hcpcs} is no longer on the shelf at this location. Choose again.");

            var serialById = units.ToDictionary(u => F.I(u["UnitId"]), u => F.S(u["SerialNumber"]));
            plan.Add(new DeliveryStock(lineId, ids, ids.Select(id => serialById[id]).ToList()));
            need[hcpcs] = need.GetValueOrDefault(hcpcs) + qty;
        }

        // Enough on the shelf for every item, counted across lines.
        foreach (var (hcpcs, qty) in need)
        {
            var onHand = OnHandAt(hcpcs, locationId);
            if (onHand < qty)
                return (null, $"Only {Math.Max(onHand, 0)} of {hcpcs} in stock at this location, and the order needs {qty}. " +
                              "Receive the stock on the Inventory screen, or mark the line drop-shipped.");
        }

        return (plan, null);
    }

    private static string Trim200(string? s)
    {
        var t = (s ?? "").Trim();
        return t.Length <= 200 ? t : t[..200];
    }
}
