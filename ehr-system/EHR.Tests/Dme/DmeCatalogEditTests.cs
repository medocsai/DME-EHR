using System.Text.RegularExpressions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// An item could be added to the catalog and never corrected. A price typed
/// wrong, or a rental cap entered as 3 instead of 13, was permanent, and every
/// order raised from it carried the mistake onto a claim.
/// </summary>
public class DmeCatalogEditTests
{
    /// <summary>
    /// The HCPCS code is CMS's identifier and past orders and claim lines point
    /// at it. Editing it would silently rewrite what an old order says it sold,
    /// so the action does not accept one: changing a code means retiring this
    /// item and adding the right one.
    /// </summary>
    [Fact]
    public void TheCodeItselfIsNotEditable()
    {
        var signature = Regex.Match(
            Controller(),
            @"public async Task<IActionResult> EditItem\((.*?)\)",
            RegexOptions.Singleline).Groups[1].Value;

        Assert.DoesNotContain("hcpcs", signature);
        Assert.Contains("int id", signature);
    }

    /// <summary>
    /// The posted id is not trusted. The row is read back inside the tenant
    /// scope first, so another supplier's id finds nothing rather than being
    /// edited, and the UPDATE carries the tenant as well.
    /// </summary>
    [Fact]
    public void AnotherSuppliersItemCannotBeEdited()
    {
        var body = EditItemBody();

        Assert.Contains("WHERE HcpcsCodeId=@id AND TenantId=@TenantId", body);
        Assert.Contains("That item is not in your catalog", body);
    }

    /// <summary>
    /// An item that is neither sold nor rented cannot be put on an order at all:
    /// the New Order picker would list it and then have no mode to offer.
    /// </summary>
    [Fact]
    public void AnItemMustStaySellableOrRentable()
    {
        Assert.Contains("!rentable && !purchasable", EditItemBody());
    }

    /// <summary>
    /// A price change is exactly the kind of edit somebody has to explain later,
    /// so both sides of it are recorded.
    /// </summary>
    [Fact]
    public void TheEditIsAuditedWithBeforeAndAfter()
    {
        var body = EditItemBody();

        Assert.Contains("DME_CATALOG_ITEM_EDITED", body);
        Assert.Contains("before: new", body);
        Assert.Contains("after: new", body);
    }

    [Fact]
    public void EditingIsAdminOnlyLikeAdding()
    {
        var c = Controller();
        var edit = c.IndexOf("public async Task<IActionResult> EditItem", StringComparison.Ordinal);
        var guard = c.LastIndexOf("[Authorize(Roles = \"0,1\")]", edit, StringComparison.Ordinal);

        Assert.True(guard > 0 && edit - guard < 200,
            "EditItem must carry the same [Authorize(Roles = \"0,1\")] as AddItem.");
    }

    /// <summary>
    /// The reorder chip compared on-hand against a hardcoded 5 rather than the
    /// item's own reorder point, so an item set to 20 stayed silent until it
    /// fell to 5.
    /// </summary>
    [Fact]
    public void TheReorderChipUsesTheItemsOwnReorderPoint()
    {
        var view = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Views", "Hcpcs", "Index.cshtml"));

        Assert.Contains("i.OnHand <= i.ReorderPoint", view);
        Assert.DoesNotContain("i.OnHand <= 5", view);
    }

    private static string EditItemBody()
    {
        var c = Controller();
        var start = c.IndexOf("public async Task<IActionResult> EditItem", StringComparison.Ordinal);
        Assert.True(start > 0, "EditItem not found on HcpcsController.");
        return c[start..];
    }

    private static string Controller() => File.ReadAllText(Path.Combine(
        ProductionRoot(), "Controllers", "HcpcsController.cs"));

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Add, correct, retire, and bring back. Before this the list could only grow:
/// a distributor entered wrong stayed wrong, and retiring one was a single
/// click with no way back, which quietly emptied the drop-ship picker.
/// </summary>
public class DmeDistributorEditTests
{
    [Fact]
    public void RenamingOntoAnotherDistributorsNameIsRefused()
    {
        var body = Method("public bool Update");

        Assert.Contains("DistributorId<>@distributorId", body);
        Assert.Contains("if (clash != null) return false;", body);
    }

    /// <summary>
    /// A retired distributor is still named on every old order that used them,
    /// so a typo in that name has to stay correctable.
    /// </summary>
    [Fact]
    public void ARetiredDistributorCanStillBeCorrected()
    {
        var body = Method("public bool Update");

        Assert.DoesNotContain("RetiredAt IS NULL", body);
    }

    /// <summary>
    /// Retire and Restore are mirrors, each guarded so a double click does the
    /// work once. Same shape as voiding a payment.
    /// </summary>
    [Fact]
    public void RetireAndRestoreAreBothGuarded()
    {
        Assert.Contains("RetiredAt IS NULL", Method("public bool Retire"));
        Assert.Contains("RetiredAt IS NOT NULL", Method("public bool Restore"));
    }

    [Fact]
    public void EveryDistributorWriteIsTenantScopedAndAudited()
    {
        var svc = Service();
        foreach (var m in new[] { "public bool Update", "public bool Retire", "public bool Restore" })
            Assert.Contains("TenantId=@TenantId", Method(m));

        var controller = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains("DME_DISTRIBUTOR_EDITED", controller);
        Assert.Contains("DME_DISTRIBUTOR_RESTORED", controller);
    }

    private static string Method(string signature)
    {
        var s = Service();
        var start = s.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " not found on DmeDistributors.");
        var end = s.IndexOf("\n    }", start, StringComparison.Ordinal);
        return s[start..(end > 0 ? end : s.Length)];
    }

    private static string Service() => File.ReadAllText(Path.Combine(
        ProductionRoot(), "Services", "DmeDistributors.cs"));

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Orders could be created and never called off, so a wrong customer or a
/// withdrawn prescription stayed on the list for good and the Open orders tile
/// went on counting it as work outstanding.
/// </summary>
public class DmeOrderCancelTests
{
    /// <summary>
    /// Delivering writes a claim, a rental, a stock movement and a serialised
    /// unit. Cancelling the order afterwards would leave all of it pointing at
    /// something that officially never happened, so it is refused twice: in the
    /// action, and in the UPDATE.
    /// </summary>
    [Fact]
    public void ADeliveredOrderCannotBeCancelled()
    {
        var body = Method("public async Task<IActionResult> CancelOrder");

        Assert.Contains("A delivered order cannot be cancelled", body);
        Assert.Contains("Status <> 'delivered'", body);
    }

    /// <summary>
    /// Somebody will ask why this order stops at nothing months from now. Same
    /// rule as voiding a payment.
    /// </summary>
    [Fact]
    public void CancellingNeedsAReasonAndIsAudited()
    {
        var body = Method("public async Task<IActionResult> CancelOrder");

        Assert.Contains("Say why the order is being cancelled", body);
        Assert.Contains("DME_ORDER_CANCELLED", body);
        Assert.Contains("Reason = reason.Trim()", body);
    }

    /// <summary>
    /// Delivery happens once. Nothing it writes is guarded against being
    /// written twice, so without this a refreshed confirmation or a double
    /// click bills the customer again for equipment they were given once.
    /// </summary>
    [Fact]
    public void AnOrderCannotBeDeliveredTwice()
    {
        var body = Method("public async Task<IActionResult> Deliver");

        Assert.Contains("already is \"delivered\" or \"cancelled\"", body);
        Assert.Contains("has already been delivered", body);
    }

    /// <summary>
    /// Reopening returns it to draft rather than to whatever it was. Confirmed
    /// means somebody checked eligibility and stock, and that check is stale.
    /// </summary>
    [Fact]
    public void ReopeningReturnsTheOrderToDraft()
    {
        var body = Method("public async Task<IActionResult> RestoreOrder");

        Assert.Contains("Status='draft'", body);
        Assert.Contains("Status='cancelled'", body);
    }

    private static string Method(string signature)
    {
        var c = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Controllers", "DmeController.cs"));
        var start = c.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " not found on DmeController.");
        var end = c.IndexOf("\n    }", start, StringComparison.Ordinal);
        return c[start..(end > 0 ? end : c.Length)];
    }

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
