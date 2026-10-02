using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Inventory: stock in, moved, corrected, and back from a rental.
///
/// WHY THESE EXIST
/// The stock ledger only ever went down. Nothing wrote a receipt, so a new
/// supplier started at zero and every delivery from their shelf drove on-hand
/// negative. Delivery invented a serial number instead of using the unit on the
/// shelf, and a rental could never be returned. These pin the half that was
/// missing, and the ordering that makes delivery safe.
/// </summary>
public class DmeInventoryTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Controllers"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    private static Dictionary<string, object?> Row(params (string k, object? v)[] cells)
    {
        var r = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in cells) r[k] = v;
        return r;
    }

    /// <summary>A database with one serialised and one consumable item, and one branch.</summary>
    private static (DmeInventory inv, Mock<IDmeDb> db) Build(int onHand = 0)
    {
        var db = new Mock<IDmeDb>();
        db.SetupGet(d => d.TenantId).Returns(1);
        db.Setup(d => d.LocationGrants(It.IsAny<string>())).Returns("1=1");

        db.Setup(d => d.QueryOne(It.Is<string>(s => s.Contains("FROM dbo.HcpcsCodes")), It.IsAny<object>()))
          .Returns((string _, object p) =>
          {
              var h = p.GetType().GetProperty("h")!.GetValue(p) as string;
              return h switch
              {
                  "E1390" => Row(("Hcpcs", "E1390"), ("Name", "Oxygen concentrator"), ("IsSerialized", true)),
                  "A4253" => Row(("Hcpcs", "A4253"), ("Name", "Test strips"), ("IsSerialized", false)),
                  _ => null
              };
          });

        db.Setup(d => d.QueryOne(It.Is<string>(s => s.Contains("FROM dbo.Locations")), It.IsAny<object>()))
          .Returns(Row(("LocationId", 1), ("Name", "Main Office")));

        db.Setup(d => d.Scalar(It.Is<string>(s => s.Contains("SUM(Qty)")), It.IsAny<object>())).Returns(onHand);

        return (new DmeInventory(db.Object), db);
    }

    // ---------------------------------------------------------------- serials

    [Fact]
    public void SerialsPastedOffAPackingSlipAreSplitAndTrimmed()
        => DmeInventory.ParseSerials(" OXC-1,\r\nOXC-2 ;\tOXC-3\n\n").Should().Equal("OXC-1", "OXC-2", "OXC-3");

    [Fact]
    public void ASerialisedReceiptWithNoSerialsIsRefused()
    {
        var (inv, db) = Build();

        var r = inv.Receive("E1390", 1, 3, Array.Empty<string>(), null, 1);

        r.Success.Should().BeFalse("the count of a serialised item IS its serials");
        db.Verify(d => d.Execute(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void TheSameSerialTwiceInOneReceiptIsRefused()
    {
        var (inv, _) = Build();

        inv.Receive("E1390", 1, 2, new[] { "OXC-1", "oxc-1" }, null, 1)
           .Error.Should().Contain("listed twice");
    }

    [Fact]
    public void AnItemOffTheCatalogCannotBeReceived()
        => Build().inv.Receive("Z9999", 1, 1, Array.Empty<string>(), null, 1).Success.Should().BeFalse();

    // ---------------------------------------------------------------- adjust

    /// <summary>
    /// A serialised item's count must always equal its units. Adjusting the
    /// number directly would let the two disagree, which is the bug the old
    /// OnHand counter had.
    /// </summary>
    [Fact]
    public void ASerialisedItemCannotBeAdjustedByNumber()
    {
        var (inv, db) = Build(onHand: 5);

        inv.Adjust("E1390", 1, -1, "lost", 1).Error.Should().Contain("Change the unit's status");
        db.Verify(d => d.Execute(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void ACorrectionNeedsAReason()
        => Build(onHand: 5).inv.Adjust("A4253", 1, -1, "  ", 1).Success.Should().BeFalse();

    [Fact]
    public void StockCannotBeCorrectedBelowZero()
        => Build(onHand: 2).inv.Adjust("A4253", 1, -3, "count", 1).Error.Should().Contain("below zero");

    [Fact]
    public void ACorrectionIsALedgerRowNotAStoredBalance()
    {
        var (inv, db) = Build(onHand: 5);

        inv.Adjust("A4253", 1, -2, "count", 1).Success.Should().BeTrue();

        db.Verify(d => d.Execute(It.Is<string>(s => s.Contains("INSERT INTO dbo.DmeStockMovements") && s.Contains("'adjustment'")),
                                 It.IsAny<object>()), Times.Once);
    }

    // ---------------------------------------------------------------- transfer

    [Fact]
    public void ATransferNeedsTwoDifferentLocations()
        => Build(onHand: 5).inv.Transfer("A4253", 1, 1, 1, Array.Empty<int>(), 1).Error.Should().Contain("two different");

    [Fact]
    public void MoreThanTheBranchHoldsCannotBeMoved()
        => Build(onHand: 2).inv.Transfer("A4253", 1, 2, 3, Array.Empty<int>(), 1).Success.Should().BeFalse();

    // ---------------------------------------------------------------- delivery

    [Fact]
    public void ADeliveryShortOfStockIsRefusedWithAWayForward()
    {
        var (inv, _) = Build(onHand: 1);
        var lines = new List<Dictionary<string, object?>>
        {
            Row(("LineId", 7), ("Hcpcs", "A4253"), ("Qty", 3), ("IsSerialized", false), ("DistributorId", null)),
        };

        var (plan, error) = inv.PlanDelivery(lines, 1, new Dictionary<int, IReadOnlyList<int>>());

        plan.Should().BeNull();
        error.Should().Contain("Receive the stock").And.Contain("drop-shipped");
    }

    [Fact]
    public void ADropShippedLineNeedsNoStock()
    {
        var (inv, _) = Build(onHand: 0);
        var lines = new List<Dictionary<string, object?>>
        {
            Row(("LineId", 7), ("Hcpcs", "E1390"), ("Qty", 1), ("IsSerialized", true), ("DistributorId", 4)),
        };

        inv.PlanDelivery(lines, 1, new Dictionary<int, IReadOnlyList<int>>()).Error.Should().BeNull(
            "an item that never touches this supplier's shelf cannot be short on it");
    }

    [Fact]
    public void ASerialisedLineMustNameItsUnits()
    {
        var (inv, _) = Build(onHand: 5);
        var lines = new List<Dictionary<string, object?>>
        {
            Row(("LineId", 7), ("Hcpcs", "E1390"), ("Qty", 1), ("IsSerialized", true), ("DistributorId", null)),
        };

        inv.PlanDelivery(lines, 1, new Dictionary<int, IReadOnlyList<int>>()).Error.Should().Contain("by serial number");
    }

    /// <summary>
    /// The serial on a delivered unit is the serial of a unit that was on the
    /// shelf. A recall notice names a real serial; an invented one answers it
    /// with nothing.
    /// </summary>
    [Fact]
    public void DeliveryNeverInventsASerialNumber()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().NotContain("\"SN-\" + Guid", "the serial comes from the unit picked off the shelf");
        controller.Should().NotContain("INSERT INTO dbo.DmeSerializedUnits",
            "delivery hands out a received unit; only Receive registers one");
    }

    /// <summary>
    /// Stock is checked before the order is marked delivered. A delivery that
    /// cannot come out of the shelf must not leave a claim and a rental behind.
    /// </summary>
    [Fact]
    public void StockIsCheckedBeforeDeliveryWritesAnything()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var deliver = Regex.Match(controller, @"public async Task<IActionResult> Deliver\(.*?DME_ORDER_DELIVERED",
            RegexOptions.Singleline).Value;

        var planAt = deliver.IndexOf("_inventory.PlanDelivery(", StringComparison.Ordinal);
        var firstWrite = deliver.IndexOf("UPDATE dbo.DmeOrders SET Status='delivered'", StringComparison.Ordinal);

        planAt.Should().BeGreaterThan(-1);
        planAt.Should().BeLessThan(firstWrite, "the check has to come before the first write");
    }

    /// <summary>A unit is handed out only while it is still on the shelf, so two deliveries cannot take one unit.</summary>
    [Fact]
    public void AUnitIsHandedOutOnlyFromTheShelf()
        => Read("Controllers", "DmeController.cs").Should().Contain("WHERE Status='in-stock' AND UnitId IN (");

    // ---------------------------------------------------------------- the service itself

    /// <summary>
    /// DmeDb opens a connection per statement, so a multi-row write is only
    /// atomic if it is one batch with its own transaction. A receipt that
    /// registered the units and died before the movement would leave units the
    /// ledger does not count.
    /// </summary>
    [Fact]
    public void EveryMultiRowWriteIsOneTransaction()
    {
        var service = Read("Services", "DmeInventory.cs");

        foreach (var method in new[] { "public InventoryResult Receive(", "public InventoryResult Transfer(",
                                       "public InventoryResult SetUnitStatus(", "public InventoryResult ReturnRental(" })
        {
            var at = service.IndexOf(method, StringComparison.Ordinal);
            at.Should().BeGreaterThan(-1, method);
            var next = service.IndexOf("public ", at + method.Length, StringComparison.Ordinal);
            var body = service[at..(next > 0 ? next : service.Length)];
            body.Should().Contain("BEGIN TRANSACTION").And.Contain("COMMIT").And.Contain("SET XACT_ABORT ON", method);
        }
    }

    /// <summary>
    /// DmeDb injects @LocationId (the branch being VIEWED) into every command and
    /// parameter names are case insensitive. A write naming @locationId would
    /// file stock into whichever branch the person happened to be looking at.
    /// </summary>
    [Fact]
    public void TheServiceNeverNamesTheInjectedLocationParameter()
    {
        var code = Regex.Replace(Read("Services", "DmeInventory.cs"), @"//.*$", "", RegexOptions.Multiline);
        code.Should().NotContainEquivalentOf("@locationId");
    }

    [Fact]
    public void ABranchIsCheckedAgainstTheTenantAndTheCallersGrants()
    {
        var service = Read("Services", "DmeInventory.cs");
        var at = service.IndexOf("private Dictionary<string, object?>? Branch(", StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1);
        var body = service[at..(at + 400)];
        body.Should().Contain("TenantId=@TenantId").And.Contain("IsActive=1").And.Contain("_db.LocationGrants()");
    }

    /// <summary>On-hand is a sum, never a stored number. The fix for OnHand must not be undone.</summary>
    [Fact]
    public void NoBalanceIsStored()
    {
        var migration = Read("Migrations", "Manual", "2026-10-02_DME_Inventory.sql");
        migration.Should().NotMatchRegex(@"ADD\s+(OnHand|QtyOnHand|Balance|ReturnedAt)\b");
    }

    // ---------------------------------------------------------------- permissions

    [Fact]
    public void ACountCorrectionIsAdminOnly()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var at = controller.IndexOf("public Task<IActionResult> AdjustStock(", StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1);
        controller[Math.Max(0, at - 300)..at].Should().Contain("[Authorize(Roles = \"0,1\")]");
    }

    [Fact]
    public void TakingAUnitOutOfStockIsAdminOnly()
    {
        var controller = Read("Controllers", "DmeController.cs");
        controller.Should().Contain("Only an administrator can take a unit out of stock.");
    }

    // ---------------------------------------------------------------- the migration

    /// <summary>
    /// The serial filter carries the em dash older deliveries wrote. Without a
    /// byte order mark sqlcmd reads the file in the ANSI code page, turns the dash
    /// into something else, and the filter silently stops excluding it.
    /// </summary>
    [Fact]
    public void TheInventoryMigrationCarriesAByteOrderMark()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "Migrations", "Manual", "2026-10-02_DME_Inventory.sql"));
        bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public void TheReasonAndStatusListsMatchTheService()
    {
        var migration = Read("Migrations", "Manual", "2026-10-02_DME_Inventory.sql");

        foreach (var status in DmeInventory.ManualStatuses.Keys)
            migration.Should().Contain($"''{status}''", $"the database must accept {status}");

        foreach (var reason in new[] { "receipt", "return", "adjustment", "transfer", "delivery" })
            migration.Should().Contain($"''{reason}''");
    }
}
