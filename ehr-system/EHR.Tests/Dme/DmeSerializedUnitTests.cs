using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The unit register holds SERIALISED equipment, and nothing else.
///
/// WHAT WAS THERE
///
/// Deliver wrote a row into dbo.DmeSerializedUnits for every line it touched,
/// serialised or not. DmeOrderLines.IsSerialized existed, matched the catalog,
/// and was never consulted.
///
/// So a box of 50 test strips got a place in the unit register with the serial
/// "-", was counted on the Inventory tile that reads "tracked by serial", and
/// recorded ONE row for a line whose quantity was three. Two of the nine units
/// in this database are that.
///
/// WHY IT MATTERS BEYOND A WRONG COUNT
///
/// The register answers "which physical unit is with which customer": for a
/// manufacturer's recall, for a service visit, and for the return at the end of
/// a capped rental when the equipment is still the supplier's. A consumable has
/// no answer to that question, and filling the register with rows that cannot
/// answer it makes the ones that can harder to find.
///
/// PROVED BY EXECUTION. A mixed order was created and delivered through the
/// app: one serialised rental (E1390) and one consumable purchase (A4253,
/// quantity 3). It produced exactly one unit row, for E1390, with a generated
/// serial, and TWO stock movements: E1390 -1 and A4253 -3.
/// </summary>
public class DmeSerializedUnitTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Controllers"))) d = d.Parent;
        d.Should().NotBeNull("the tests must be able to find the ehr-system folder");
        return d!.FullName;
    }

    /// <summary>The per-line loop inside Deliver, where all of this happens.</summary>
    private static string DeliverLoop()
    {
        var body = Regex.Match(
            File.ReadAllText(Path.Combine(RepoRoot(), "Controllers", "DmeController.cs")),
            @"public async Task<IActionResult> Deliver\(.*?DME_ORDER_DELIVERED",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty("the Deliver action should still be there");
        return body;
    }

    [Fact]
    public void OnlyASerialisedItemIsRegisteredAsAUnit()
    {
        DeliverLoop().Should().Contain("if (F.B(l[\"IsSerialized\"]))",
            "the flag is on the line and matches the catalog; it was simply never read");
    }

    /// <summary>
    /// THE ORDERING TRAP, AND IT IS THE WHOLE POINT.
    ///
    /// The serialised check must be an `if` around the unit insert, never an
    /// early exit from the loop. The stock movement below it is unconditional:
    /// three boxes left the warehouse whether or not anybody tracks their serial
    /// numbers.
    ///
    /// Written as `if (!serialised) continue;` this would compile, pass a smoke
    /// test, and stop recording stock for every consumable the supplier sells,
    /// so on-hand would drift upward for ever. That is the same trap the
    /// drop-ship guard documents, in reverse: there the `continue` is correct
    /// because nothing physically moved, here nothing moved only for the
    /// REGISTER, and the ledger still has to be written.
    /// </summary>
    [Fact]
    public void TheStockMovementIsWrittenForEveryLineThatLeftTheWarehouse()
    {
        var deliver = DeliverLoop();

        var guardAt = deliver.IndexOf("if (F.B(l[\"IsSerialized\"]))", StringComparison.Ordinal);
        // The unit is handed out (an UPDATE of a unit received onto the shelf),
        // not created at delivery any more. The ordering rule is unchanged.
        var unitAt = deliver.IndexOf("UPDATE dbo.DmeSerializedUnits", StringComparison.Ordinal);
        var movementAt = deliver.IndexOf("INSERT INTO dbo.DmeStockMovements", StringComparison.Ordinal);

        guardAt.Should().BeGreaterThan(-1);
        unitAt.Should().BeGreaterThan(guardAt, "the register write is what the guard covers");
        movementAt.Should().BeGreaterThan(unitAt, "the ledger write comes after it");

        // The guard's block must CLOSE before the stock movement. Between the
        // unit insert and the movement there has to be a closing brace, or the
        // movement is inside the conditional too.
        var between = deliver.Substring(unitAt, movementAt - unitAt);
        between.Should().Contain("}",
            "the serialised block has to close before the stock ledger is written, "
            + "or a consumable stops being deducted from on-hand");
    }

    /// <summary>
    /// And the serialised check must not have become a `continue`, whatever
    /// shape the code takes later. Asserted on the text because the failure is
    /// silent: on-hand simply climbs, and nothing on any screen says why.
    /// </summary>
    [Fact]
    public void TheSerialisedCheckIsNotAnEarlyExit()
    {
        DeliverLoop().Should().NotContain("IsSerialized\"])) continue",
            "an early exit would skip the stock movement as well as the register");
    }

    /// <summary>
    /// The drop-ship guard keeps its place and its meaning. It comes FIRST and
    /// it IS a continue, because a drop-shipped item was never on a shelf: there
    /// is no unit of this supplier's to register and no movement to record.
    /// </summary>
    [Fact]
    public void TheDropShipGuardStaysAnEarlyExitAndStaysFirst()
    {
        var deliver = DeliverLoop();

        var dropAt = deliver.IndexOf("if (dropShipped) continue;", StringComparison.Ordinal);
        var serialAt = deliver.IndexOf("if (F.B(l[\"IsSerialized\"]))", StringComparison.Ordinal);

        dropAt.Should().BeGreaterThan(-1, "drop shipping still has to skip both writes");
        serialAt.Should().BeGreaterThan(dropAt,
            "a drop-shipped serialised item is still not this supplier's unit to register");
    }

    /// <summary>
    /// And both guards still sit AFTER the rental and the claim line, so a
    /// consumable and a drop-shipped item are both still billed. Moving either
    /// guard up stops billing the majority of this supplier's business.
    /// </summary>
    [Fact]
    public void BothGuardsStillComeAfterTheRentalAndTheClaimLine()
    {
        var deliver = DeliverLoop();

        var rentalAt = deliver.IndexOf("INSERT INTO dbo.DmeRentals", StringComparison.Ordinal);
        var claimLineAt = deliver.IndexOf("INSERT INTO dbo.DmeClaimLines", StringComparison.Ordinal);
        var dropAt = deliver.IndexOf("if (dropShipped) continue;", StringComparison.Ordinal);
        var serialAt = deliver.IndexOf("if (F.B(l[\"IsSerialized\"]))", StringComparison.Ordinal);

        dropAt.Should().BeGreaterThan(rentalAt).And.BeGreaterThan(claimLineAt);
        serialAt.Should().BeGreaterThan(rentalAt).And.BeGreaterThan(claimLineAt);
    }
}
