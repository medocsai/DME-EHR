using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EHR.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// New Order and Edit order are ONE form.
///
/// WHY THIS EXISTS
/// An order could be raised and never corrected. A quantity typed wrong, the
/// wrong HCPCS picked off a similar name, a delivery date moved: the only way
/// out was to cancel the order and raise another, which spends an order number
/// and leaves a cancelled row whose real reason was a typo. Every cancellation
/// then looks like a business event when most of them were corrections.
///
/// WHAT THESE GUARD
/// That the two screens stay one form, and that the window closes at delivery.
/// Delivery writes a claim, a rental, a stock movement and a serialised unit,
/// and none of those can be un-written by editing the order they came from.
/// </summary>
public class DmeOrderEditTests
{
    /// <summary>
    /// The pages render the partial and hold no fields of their own. A field
    /// added to one and not the other is the entire failure mode, and it is
    /// exactly what happened to the customer screens.
    /// </summary>
    [Theory]
    [InlineData("NewOrder.cshtml")]
    [InlineData("EditOrder.cshtml")]
    public void BothPagesRenderTheOneForm(string page)
    {
        var view = Read("Views", "Dme", page);

        view.Should().Contain("Html.PartialAsync(\"_OrderForm\")");
        view.Should().NotContain("<select name=",
            $"{page} must carry the heading and the banners only.");
    }

    /// <summary>
    /// One partial, two actions, and the mode is decided by the order being
    /// there rather than by a flag the caller has to remember to pass.
    /// </summary>
    [Fact]
    public void TheFormPostsToCreateOrUpdateDependingOnlyOnWhetherThereIsAnOrder()
    {
        var form = Form();

        form.Should().Contain("var editing = order != null;");
        form.Should().Contain("editing ? \"/Dme/UpdateOrder\" : \"/Dme/CreateOrder\"");
    }

    [Theory]
    [InlineData("customerId")]
    [InlineData("doctorId")]
    [InlineData("deliveryDate")]
    [InlineData("deposit")]
    [InlineData("hcpcs")]
    [InlineData("mode")]
    [InlineData("qty")]
    [InlineData("distributorId")]
    [InlineData("distributorRef")]
    public void CreateAndUpdateAcceptTheSameFields(string parameter)
    {
        foreach (var action in new[] { "CreateOrder", "UpdateOrder" })
        {
            Action(action).GetParameters().Select(p => p.Name)
                .Should().Contain(parameter, $"{action} is fed by the same form as the other one.");
        }
    }

    /// <summary>
    /// One writer, called by both. Everything a claim is built from is read out
    /// of the item master there: the name, the category, the price, the
    /// modifier. Two copies of that is how a posted price eventually becomes
    /// what a claim is worth.
    /// </summary>
    [Fact]
    public void OneWriterFilesTheLinesForBothActions()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain("private int SaveOrderLines(");
        controller.Should().Contain("SaveOrderLines(orderId, hcpcs, mode, qty, distributorId, distributorRef)");
        controller.Should().Contain("SaveOrderLines(id, hcpcs, mode, qty, distributorId, distributorRef)");

        // The prices come from the catalog in the writer, and only there.
        controller.Split("F.Dec(item[\"PurchasePrice\"])").Length.Should().Be(2,
            "one place reads the purchase price onto a line");
    }

    /// <summary>
    /// The pickers are loaded once, for both screens. A list loaded on one and
    /// forgotten on the other renders an empty dropdown with no clue why.
    /// </summary>
    [Fact]
    public void BothScreensLoadTheSamePickers()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain("private void LoadOrderPickers()");
        controller.Split("LoadOrderPickers();").Length.Should().Be(3,
            "NewOrder and EditOrder each call it once");
    }

    // ------------------------------------------------------- the window closes

    /// <summary>
    /// Delivery is the line. Before it nothing irreversible has happened; after
    /// it the order has produced a claim, a rental, a stock movement and a
    /// serialised unit, and rewriting the lines would leave all four pointing at
    /// something that never happened.
    ///
    /// A cancelled order is reopened first, deliberately: reopening resets it to
    /// draft and says out loud that the eligibility and stock check behind
    /// "confirmed" has gone stale.
    /// </summary>
    [Theory]
    [InlineData("delivered")]
    [InlineData("cancelled")]
    public void AnOrderPastThePointOfNoReturnIsNotEditable(string status)
    {
        typeof(DmeController)
            .GetMethod("IsEditable", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { status })
            .Should().Be(false);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("confirmed")]
    public void AnOrderThatHasNotShippedIsEditable(string status)
    {
        typeof(DmeController)
            .GetMethod("IsEditable", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { status })
            .Should().Be(true);
    }

    /// <summary>
    /// The status is re-read on POST, not trusted from the screen the operator
    /// opened. Somebody can deliver an order while the edit form is sitting
    /// open, and the delivery has to win.
    /// </summary>
    [Fact]
    public void TheStatusIsCheckedAgainWhenTheFormIsSaved()
    {
        var update = Method("UpdateOrder");

        update.Should().Contain("if (!IsEditable(F.S(existing[\"Status\"])))");
        update.Should().Contain("This order was delivered while you were editing it.");

        // And the UPDATE itself carries the guard, so even a race past the check
        // above writes nothing.
        update.Should().Contain("AND Status NOT IN ('delivered','cancelled')");
    }

    /// <summary>
    /// An order with no lines is not an order, it is a ticket that later
    /// produces a $0.00 claim. The client hit exactly that, and the lines are
    /// added by JavaScript, so posting with none is a normal thing for a real
    /// person to do. Saving an edit must not be the way back to that state.
    /// </summary>
    [Fact]
    public void SavingAnEditCannotEmptyAnOrder()
    {
        Method("UpdateOrder")
            .Should().Contain("An order needs at least one item. Nothing was changed.");
    }

    /// <summary>
    /// The rows are rendered by the SERVER.
    ///
    /// The POST replaces the order's lines with whatever it carries, so a page
    /// whose script failed to run would post nothing and empty the order. Hidden
    /// inputs that exist in the markup before any script runs mean the worst a
    /// dead script can do is save the order exactly as it was. Same reasoning as
    /// the diagnosis chips on the customer form.
    /// </summary>
    [Fact]
    public void TheLinesExistBeforeAnyScriptRuns()
    {
        var form = Form();
        var table = form[form.IndexOf("id=\"lineTable\"", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("</table>", StringComparison.Ordinal)];

        table.Should().Contain("@foreach (var l in lines)");
        table.Should().Contain("<input type=\"hidden\" name=\"hcpcs\"");
        table.Should().Contain("<input type=\"hidden\" name=\"qty\"");
        table.Should().Contain("<input type=\"hidden\" name=\"distributorId\"");
    }

    /// <summary>
    /// Remove is delegated, not wired per row. A handler attached at add-time
    /// only would leave every line the order already had with a remove button
    /// that does nothing at all.
    /// </summary>
    [Fact]
    public void RemoveWorksOnLinesTheOrderAlreadyHad()
    {
        var form = Form();

        form.Should().Contain("body.addEventListener('click', function (e) {");
        form.Should().Contain("var btn = e.target.closest('.rm');");
        form.Should().NotContain("tr.querySelector('.rm').onclick",
            "a per-row handler skips the rows the server rendered");
    }

    /// <summary>
    /// An order that already has lines must not say "No items added yet"
    /// underneath them, which is what happens when the empty-state check only
    /// ever runs after somebody adds or removes one.
    /// </summary>
    [Fact]
    public void TheEmptyStateIsCorrectOnLoad()
    {
        var form = Form();
        var tail = form[form.LastIndexOf("function toggleEmpty()", StringComparison.Ordinal)..];

        tail.Should().Contain("toggleEmpty();", "it has to run once on load");
    }

    /// <summary>
    /// A retired distributor is hidden from the PICKER and still has to print on
    /// a line already filed. Those are two different questions: "who may I
    /// choose now" and "who did we use then".
    /// </summary>
    [Fact]
    public void ARetiredDistributorStillNamesItselfOnAnExistingLine()
    {
        Read("Controllers", "DmeController.cs")
            .Should().Contain("ViewBag.AllDistributors = _distributors.All(includeRetired: true);");

        Form().Should().Contain("allDistributors.FirstOrDefault(d => d.DistributorId == F.I(id))");
    }

    /// <summary>
    /// The link is offered on exactly the statuses the action accepts. A button
    /// that leads to a refusal is worse than no button.
    /// </summary>
    [Fact]
    public void TheOrderScreenOffersEditOnlyWhereItWorks()
    {
        var view = Read("Views", "Dme", "Order.cshtml");

        view.Should().Contain("href=\"/Dme/EditOrder/@F.I(o[\"OrderId\"])\"");
        view.Should().Contain("@if (status != \"delivered\" && status != \"billed\" && status != \"cancelled\")");
    }

    /// <summary>
    /// Editing an order changes what will be billed, so it is worth its own
    /// audit row rather than being invisible between "created" and "delivered".
    /// </summary>
    [Fact]
    public void AnEditIsAudited()
    {
        Read("Controllers", "DmeController.cs").Should().Contain("DME_ORDER_EDITED");
    }

    // ------------------------------------------------------------ draft orders

    /// <summary>
    /// A draft was unreachable.
    ///
    /// CreateOrder wrote 'confirmed' and nothing else in the product ever wrote
    /// 'draft' except reopening a cancelled order. So the status existed, the
    /// chip for it existed, and no operator could ever produce one. Every order
    /// was born saying "somebody has checked eligibility and stock" on the day
    /// it was typed, which on most orders is not true yet.
    /// </summary>
    [Fact]
    public void AnOrderCanBeSavedAsADraft()
    {
        var form = Form();

        form.Should().Contain("name=\"status\" value=\"draft\"");
        form.Should().Contain("name=\"status\" value=\"confirmed\"");

        foreach (var action in new[] { "CreateOrder", "UpdateOrder" })
            Action(action).GetParameters().Select(p => p.Name).Should().Contain("status");
    }

    /// <summary>
    /// Only the draft button produces a draft. A form posted with no status at
    /// all confirms, which is what every existing caller does and what the
    /// product did before the button existed.
    /// </summary>
    [Theory]
    [InlineData("draft", "draft", "receive-order")]
    [InlineData("confirmed", "confirmed", "order-ship")]
    [InlineData(null, "confirmed", "order-ship")]
    [InlineData("something-else", "confirmed", "order-ship")]
    public void OnlyTheDraftButtonProducesADraft(string? posted, string status, string stage)
    {
        var result = typeof(DmeController)
            .GetMethod("OrderState", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { posted })!;

        // Fields, not properties: a ValueTuple exposes Item1 and Item2 as public
        // fields, and GetProperty quietly returns null for them.
        var type = result.GetType();
        type.GetField("Item1")!.GetValue(result).Should().Be(status);
        type.GetField("Item2")!.GetValue(result).Should().Be(stage);
    }

    /// <summary>
    /// A confirmed order stays confirmed when it is corrected. Walking it
    /// backwards would take an order somebody is expecting to deliver off the
    /// delivery list without anybody deciding to.
    /// </summary>
    [Fact]
    public void CorrectingAConfirmedOrderDoesNotWalkItBackToDraft()
    {
        var update = Method("UpdateOrder");

        update.Should().Contain("F.S(existing[\"Status\"]) == \"draft\"");
        update.Should().Contain("? OrderState(status)");
        update.Should().Contain(": (\"confirmed\", \"order-ship\")");

        // And the form offers one button on a confirmed order, so the two
        // halves agree.
        Form().Should().Contain("@if (!editing || isDraft)");
    }

    /// <summary>
    /// The bug this closed, and it killed orders outright: reopening a cancelled
    /// order made it a draft, the delivery button only ever showed on a
    /// confirmed order, and nothing anywhere could confirm one. That order could
    /// never be delivered again.
    /// </summary>
    [Fact]
    public void ADraftCanBeConfirmedInOneClick()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain("public async Task<IActionResult> ConfirmOrder(int id)");
        controller.Should().Contain("SET Status='confirmed', Stage='order-ship' ");
        controller.Should().Contain("AND Status='draft'",
            "guarded so two clicks confirm once, and a delivered order cannot be walked back");
        controller.Should().Contain("DME_ORDER_CONFIRMED");

        Read("Views", "Dme", "Order.cshtml")
            .Should().Contain("action=\"/Dme/ConfirmOrder\"")
            .And.Contain("@if (status == \"draft\")");
    }

    /// <summary>
    /// Confirming says the order is ready to go out, so it carries the same
    /// rule as saving one: an order with no lines is a ticket that later
    /// produces a $0.00 claim.
    /// </summary>
    [Fact]
    public void AnEmptyOrderCannotBeConfirmed()
    {
        Method("ConfirmOrder")
            .Should().Contain("Add at least one item before confirming this order.");
    }

    /// <summary>
    /// Deliver refuses a draft, on the SERVER.
    ///
    /// The order screen hides the delivery panel on a draft and always did, but
    /// hiding a button is not a guard. A reopened order could be delivered by a
    /// POST carrying exactly the stale eligibility and stock check that
    /// reopening it as a draft was meant to flag.
    /// </summary>
    [Fact]
    public void ADraftCannotBeDelivered()
    {
        var deliver = Method("Deliver");

        deliver.Should().Contain("already is \"delivered\" or \"cancelled\" or \"draft\"");
        deliver.Should().Contain("This order is still a draft. Confirm it before delivering.");
    }

    private static string Form() => Read("Views", "Dme", "_OrderForm.cshtml");

    private static string Method(string name)
    {
        var controller = Read("Controllers", "DmeController.cs");
        var at = controller.IndexOf($"public async Task<IActionResult> {name}(", StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1, $"{name} must exist on DmeController");

        var body = controller[at..];
        return body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
    }

    private static MethodInfo Action(string name) => typeof(DmeController)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Single(m => m.Name == name);

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);

        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
