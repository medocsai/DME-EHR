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
/// Drop shipping: items that go from the distributor to the customer and never
/// enter this supplier's warehouse.
///
/// THE GUARD THAT MATTERS
/// A drop-shipped line must write NO stock movement and NO serialised unit. The
/// client says MOST of what they deliver ships this way, so getting it wrong
/// does not shade the inventory figures, it inverts them: on-hand marches
/// negative for the majority of the catalog. That is the stock problem at full
/// scale, and it is the reason the fix is mostly an absence.
/// </summary>
public class DmeDropShipTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Controllers"))) d = d.Parent;
        d.Should().NotBeNull("the tests must be able to find the ehr-system folder");
        return d!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    // ------------------------------------------------ the absence is the feature

    /// <summary>
    /// Reads the body of the per-line loop inside Deliver and proves the stock
    /// writes are unreachable for a drop-shipped line.
    /// </summary>
    [Fact]
    public void ADropShippedLineWritesNoStockMovementAndNoUnit()
    {
        var controller = Read("Controllers", "DmeController.cs");

        var deliver = Regex.Match(controller,
            @"public async Task<IActionResult> Deliver\(.*?DME_ORDER_DELIVERED",
            RegexOptions.Singleline).Value;

        deliver.Should().NotBeEmpty("the Deliver action should still be there");

        var guardAt = deliver.IndexOf("if (dropShipped) continue;", StringComparison.Ordinal);
        guardAt.Should().BeGreaterThan(-1,
            "a drop-shipped line has to leave the loop before it touches stock");

        var movementAt = deliver.IndexOf("INSERT INTO dbo.DmeStockMovements", StringComparison.Ordinal);
        var unitAt = deliver.IndexOf("INSERT INTO dbo.DmeSerializedUnits", StringComparison.Ordinal);

        movementAt.Should().BeGreaterThan(guardAt,
            "the stock ledger write must sit AFTER the guard, or on-hand goes negative " +
            "for the majority of what this supplier delivers");
        unitAt.Should().BeGreaterThan(guardAt,
            "this supplier never held the unit, so there is none of theirs to register");
    }

    /// <summary>
    /// The other half: billing does NOT change. A drop-shipped item is still
    /// delivered, still rented, still claimed. If the guard drifted upwards it
    /// would silently stop billing the majority of their business.
    /// </summary>
    [Fact]
    public void ADropShippedLineIsStillBilled()
    {
        var controller = Read("Controllers", "DmeController.cs");

        var deliver = Regex.Match(controller,
            @"public async Task<IActionResult> Deliver\(.*?DME_ORDER_DELIVERED",
            RegexOptions.Singleline).Value;

        var guardAt = deliver.IndexOf("if (dropShipped) continue;", StringComparison.Ordinal);
        var claimLineAt = deliver.IndexOf("INSERT INTO dbo.DmeClaimLines", StringComparison.Ordinal);
        var rentalAt = deliver.IndexOf("INSERT INTO dbo.DmeRentals", StringComparison.Ordinal);

        claimLineAt.Should().BeGreaterThan(-1).And.BeLessThan(guardAt,
            "the claim line is raised before the guard, so a drop-shipped item is still billed");
        rentalAt.Should().BeGreaterThan(-1).And.BeLessThan(guardAt,
            "and a drop-shipped rental still starts");
    }

    /// <summary>
    /// "Is this drop-shipped" is DERIVED from the distributor being present.
    /// A stored flag would be a second copy of the same fact, and the two would
    /// disagree the first time one was set without the other.
    /// </summary>
    [Fact]
    public void ThereIsNoStoredIsDropShippedFlag()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Drop_Ship.sql");

        foreach (var banned in new[] { "IsDropShip", "IsDropShipped", "DropShip BIT", "IsDirectShip" })
            migration.Should().NotContain(banned,
                "drop-shipped is derived from DistributorId, not stored beside it");

        migration.Should().Contain("DistributorId",
            "which is the one fact that has no other home");
    }

    /// <summary>
    /// Distributors are TENANT data, unlike the payer, ICD and HCPCS catalogs.
    /// Those are national lists; each supplier negotiates its own distributors.
    /// </summary>
    [Fact]
    public void DistributorsAreTenantScopedAndInTheSecurityPolicy()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Drop_Ship.sql");

        migration.Should().MatchRegex(@"CREATE TABLE dbo\.DmeDistributors[\s\S]*?TenantId\s+INT\s+NOT NULL",
            "each supplier buys from their own distributors");
        migration.Should().Contain("ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeDistributors");
        migration.Should().Contain("ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeDistributors AFTER INSERT");
    }

    /// <summary>
    /// A posted distributor is request-supplied data that decides what a record
    /// says, so it is checked against the caller's tenant. Row level security
    /// covers TenantId on the insert and would accept a foreign DistributorId
    /// sitting beside it, exactly as it would a foreign LocationId.
    /// </summary>
    [Fact]
    public void ADistributorPostedOnAnOrderIsCheckedAgainstTheTenant()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().MatchRegex(
            @"FROM dbo\.DmeDistributors WHERE DistributorId=@postedDist AND TenantId=@TenantId",
            "CreateOrder must confirm the posted distributor belongs to this tenant");
    }

    // -------------------------------------------------- the service's own rules

    private static (DmeDistributors service, Mock<IDmeDb> db, List<(string Sql, object? Prms)> calls) Build(
        object? scalarResult = null, int executeResult = 1)
    {
        var calls = new List<(string, object?)>();
        var db = new Mock<IDmeDb>();

        db.Setup(d => d.Scalar(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(scalarResult!);

        db.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(executeResult);

        db.Setup(d => d.Query(It.IsAny<string>(), It.IsAny<object>()))
          .Callback<string, object?>((sql, p) => calls.Add((sql, p)))
          .Returns(new List<Dictionary<string, object?>>());

        return (new DmeDistributors(db.Object), db, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADistributorNeedsAName(string? name)
    {
        var (service, db, _) = Build();

        service.Add(name, null, null, null).Should().BeNull();
        db.Verify(d => d.Scalar(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void TheSameDistributorCannotBeAddedTwice()
    {
        // The existence check returns a row, so the insert must never happen.
        var (service, db, calls) = Build(scalarResult: 7);

        service.Add("Invacare", null, null, null).Should().BeNull();

        calls.Should().NotContain(c => c.Sql.Contains("INSERT INTO dbo.DmeDistributors"),
            "two distributors with one name is exactly the ambiguity the unique index prevents");
    }

    [Fact]
    public void RetiringIsGuardedSoTwoClicksProduceOneRetirement()
    {
        var (service, _, calls) = Build();

        service.Retire(3);

        var update = calls.Last(c => c.Sql.Contains("UPDATE dbo.DmeDistributors")).Sql;
        update.Should().Contain("RetiredAt IS NULL",
            "without the guard the second click rewrites the retirement date");
        update.Should().Contain("TenantId=@TenantId",
            "and one supplier must not retire another's distributor");
    }

    [Fact]
    public void ADistributorIsRetiredNeverDeleted()
    {
        var source = Read("Services", "DmeDistributors.cs");

        source.Should().NotContain("DELETE FROM dbo.DmeDistributors",
            "past orders are the record of who shipped them, and that has to survive " +
            "the relationship ending");
    }

    [Fact]
    public void RetirementIsDerivedFromTheDate()
    {
        new Distributor(1, "Invacare", null, null, null, IsRetired: true).IsRetired.Should().BeTrue();
        new Distributor(1, "Invacare", null, null, null, IsRetired: false).IsRetired.Should().BeFalse();

        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Drop_Ship.sql");
        migration.Should().Contain("RetiredAt")
                 .And.NotContain("IsRetired BIT",
            "the date is the fact; the boolean is computed from it");
    }

    /// <summary>
    /// A retired distributor still has to RESOLVE, or an order placed two years
    /// ago stops saying who shipped it. Only the picker hides them.
    /// </summary>
    [Fact]
    public void ARetiredDistributorStillResolvesOnAnOldOrder()
    {
        var source = Read("Services", "DmeDistributors.cs");

        var find = Regex.Match(source, @"public Distributor\? Find\(.*?\n    \}", RegexOptions.Singleline).Value;

        find.Should().NotBeEmpty();
        find.Should().NotContain("RetiredAt IS NULL",
            "an order placed before the relationship ended still has to say who shipped it");
    }

    [Fact]
    public void TheOrderFormOffersOnlyLiveDistributors()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain("ViewBag.Distributors = _distributors.All();",
            "the NEW order picker must not offer a supplier this business no longer buys from");
    }

    // ------------------------------------------------------ what the screens show

    /// <summary>
    /// The client's actual ask. Drop-shipped items are shown, and shown
    /// SEPARATELY: an item in somebody else's warehouse is not stock this
    /// supplier holds, so it must not roll into either stock number.
    /// </summary>
    [Fact]
    public void InventoryShowsDropShipmentsWithoutFoldingThemIntoStock()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var view = Read("Views", "Dme", "Inventory.cshtml");

        controller.Should().Contain("vDmeDropShipments",
            "the third panel reads the view, which derives everything");
        view.Should().Contain("Direct from distributor");

        // vHcpcsCatalog computes OnHand from DmeStockMovements, and a
        // drop-shipped line writes none, so it is excluded by construction.
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Drop_Ship.sql");
        migration.Should().NotContain("vHcpcsCatalog",
            "the stock views are deliberately untouched: the fix is that nothing is written");
    }

    /// <summary>
    /// Everything the drop-ship view reports is derived. Arrival comes from the
    /// order's own status rather than a second flag somebody has to remember to
    /// set.
    /// </summary>
    [Fact]
    public void ArrivalIsDerivedFromTheOrderStatus()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Drop_Ship.sql");

        migration.Should().MatchRegex(@"CASE WHEN o\.Status = 'delivered' THEN 1 ELSE 0 END.*AS HasArrived");
        migration.Should().NotContain("ArrivedAt",
            "a second date to keep in step with the order status is the stock problem again");
    }

    /// <summary>
    /// On an order where EVERY line ships direct, nobody from this supplier is
    /// at the door, so the signature pad is not shown and the script that
    /// requires one is not emitted. A MIXED order still needs a signature,
    /// because somebody is there with part of it.
    /// </summary>
    [Fact]
    public void AnAllDropShippedOrderAsksForNoSignature()
    {
        var view = Read("Views", "Dme", "Order.cshtml");

        view.Should().Contain("lines.All(l => l[\"DistributorId\"] is not (null or DBNull))",
            "only when EVERY line ships direct");
        view.Should().Contain("@if (!allDropShipped)",
            "the signature script must not be emitted without a canvas to wire, or it " +
            "throws and takes the submit handler down with it");
    }
}
