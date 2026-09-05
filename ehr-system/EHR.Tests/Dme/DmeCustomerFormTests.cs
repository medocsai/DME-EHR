using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EHR.Controllers;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// New Customer and Edit customer are ONE form.
///
/// WHY THIS EXISTS
/// They were two files. New had Basic Info, Emergency Contact, Insurance,
/// Diagnosis and Attachments; Edit had the first two. A note on Edit said
/// insurance and diagnosis "are changed from the customer's own page", and they
/// were not: no action anywhere in the product changed either, so a payer
/// entered wrong on the day a customer was created stayed wrong for the life of
/// the record, and every claim went to it.
///
/// Two files describing one thing drift, and these two already had. These tests
/// are the alarm that says they have been split apart again.
/// </summary>
public class DmeCustomerFormTests
{
    /// <summary>
    /// The pages render the partial and hold no fields of their own. A field
    /// added to one page and not the other is the entire failure mode.
    /// </summary>
    [Theory]
    [InlineData("NewCustomer.cshtml")]
    [InlineData("EditCustomer.cshtml")]
    public void BothPagesRenderTheOneForm(string page)
    {
        var view = Read("Views", "Dme", page);

        view.Should().Contain("Html.PartialAsync(\"_CustomerForm\")");
        view.Should().NotContain("<input name=",
            $"{page} must carry the heading and the error banner only. Any field " +
            "declared here is a field the other page does not have.");
    }

    /// <summary>
    /// Every card the operator expects, in the one file, so both pages get all
    /// five. Insurance and Diagnosis are the two Edit was missing.
    /// </summary>
    [Theory]
    [InlineData("Basic Info")]
    [InlineData("Emergency Contact")]
    [InlineData("Insurance")]
    [InlineData("Diagnosis (ICD-10)")]
    [InlineData("Attachments")]
    public void TheFormCarriesEveryCard(string card)
    {
        Form().Should().Contain($"<h2>{card}</h2>");
    }

    /// <summary>
    /// One partial, two actions. The mode is decided by the customer being
    /// there, not by a flag the caller has to remember to pass.
    /// </summary>
    [Fact]
    public void TheFormPostsToCreateOrUpdateDependingOnlyOnWhetherThereIsACustomer()
    {
        var form = Form();

        form.Should().Contain("var editing = c != null;");
        form.Should().Contain("editing ? \"/Dme/UpdateCustomer\" : \"/Dme/CreateCustomer\"");
    }

    /// <summary>
    /// Both actions take the same arguments, because they are the same form.
    /// UpdateCustomer used to take the basic fields only, which is why saving
    /// the edit screen could not have changed insurance even if the screen had
    /// shown it.
    /// </summary>
    [Theory]
    [InlineData("insPayerId")]
    [InlineData("insMemberId")]
    [InlineData("insGroup")]
    [InlineData("insCopay")]
    [InlineData("insCoins")]
    [InlineData("insDeductible")]
    [InlineData("secPayerId")]
    [InlineData("secMemberId")]
    [InlineData("secGroup")]
    [InlineData("secCopay")]
    [InlineData("secCoins")]
    [InlineData("secDeductible")]
    [InlineData("dxCodes")]
    public void CreateAndUpdateAcceptTheSameFields(string parameter)
    {
        foreach (var action in new[] { "CreateCustomer", "UpdateCustomer" })
        {
            Action(action).GetParameters().Select(p => p.Name)
                .Should().Contain(parameter,
                    $"{action} is fed by the same form as the other one.");
        }
    }

    /// <summary>
    /// One writer each, called by both. Two copies of these rules is how the
    /// screens came apart in the first place.
    /// </summary>
    [Theory]
    [InlineData("SaveCustomerInsurance")]
    [InlineData("SaveCustomerDiagnoses")]
    public void OneWriterServesBothActions(string writer)
    {
        var controller = Read("Controllers", "DmeController.cs");

        // The declaration plus one call from each action.
        controller.Split(writer).Length.Should().BeGreaterOrEqualTo(4,
            $"{writer} must be declared once and called from both CreateCustomer " +
            "and UpdateCustomer.");

        var declarations = new[] { $"private string? {writer}", $"private IReadOnlyList<string> {writer}" };
        declarations.Any(controller.Contains).Should().BeTrue(
            $"{writer} must be declared once, on the controller, as one method.");
    }

    /// <summary>
    /// A posted payer id of 0 means KEEP the payer on file.
    ///
    /// The stored row holds the payer's name and Payer ID, never the catalog
    /// id, and the id is recoverable from neither: Office Ally issues ALLCA to
    /// two different payers. So the picker starts empty and an untouched form
    /// posts 0. If 0 meant "no payer", opening the edit screen to correct a
    /// phone number would drop the customer's insurance.
    /// </summary>
    [Fact]
    public void AnUntouchedPayerPickerKeepsThePayerOnFile()
    {
        var writer = Method("SaveCustomerInsurance");

        writer.Should().Contain("chosen?.Name ?? F.S(existing[\"PayerName\"])");
        writer.Should().Contain("chosen?.PayerCode ?? F.S(existing[\"PayerId\"])");

        // A row is only ever removed by the explicit "this policy has ended"
        // path. Correcting a payer is an update, never a delete, and the delete
        // sits above the return that ends that branch so the two cannot meet.
        var removal = writer[..writer.IndexOf("// No payer chosen", StringComparison.Ordinal)];
        removal.Should().Contain("if (remove)")
            .And.Contain("DELETE FROM dbo.DmeCustomerInsurances");

        var correction = writer[writer.IndexOf("// No payer chosen", StringComparison.Ordinal)..];
        correction.Should().NotContain("DELETE FROM dbo.DmeCustomerInsurances");
    }

    /// <summary>
    /// Nothing to keep and nothing chosen writes nothing at all, rather than an
    /// insurance row with a blank payer and real copay figures on it.
    /// </summary>
    [Fact]
    public void NoPayerAnywhereFilesNoInsurance()
    {
        Method("SaveCustomerInsurance")
            .Should().Contain("if (chosen == null && existing == null) return null;");
    }

    /// <summary>
    /// The diagnosis chips are rendered by the SERVER.
    ///
    /// The posted list replaces what is on file, so a page whose script failed
    /// to run would otherwise post nothing and silently empty a customer's
    /// diagnoses. Hidden inputs that exist in the markup before any script runs
    /// mean the worst a dead script can do is leave the record as it was.
    /// </summary>
    [Fact]
    public void TheDiagnosisChipsExistBeforeAnyScriptRuns()
    {
        var form = Form();
        var chips = form[form.IndexOf("id=\"dxChips\"", StringComparison.Ordinal)..];
        chips = chips[..chips.IndexOf("id=\"dxHint\"", StringComparison.Ordinal)];

        chips.Should().Contain("@for (var i = 0; i < diagnoses.Count; i++)");
        chips.Should().Contain("<input type=\"hidden\" name=\"dxCodes\" value=\"@code\" />");

        // And the script reads those chips rather than carrying its own copy of
        // the list, so the two cannot start out disagreeing.
        form.Should().Contain("chips.querySelectorAll('[data-code]')");
    }

    /// <summary>
    /// An unchanged list is not rewritten. Nothing points at DiagnosisId so the
    /// churn would be harmless, and still wrong: it would make every corrected
    /// phone number look like a clinical change in the audit log.
    /// </summary>
    [Fact]
    public void SavingWithoutTouchingTheDiagnosesChangesNothing()
    {
        Method("SaveCustomerDiagnoses")
            .Should().Contain("if (current.SequenceEqual(codes)) return codes;");
    }

    /// <summary>
    /// The description is read from the CMS catalog, never from the browser. A
    /// code that is not valid ICD-10-CM files nothing rather than a diagnosis
    /// with blank wording, and the first code posted is the primary.
    /// </summary>
    [Fact]
    public void ADiagnosisIsFiledFromTheCatalogAndTheFirstIsPrimary()
    {
        var writer = Method("SaveCustomerDiagnoses");

        writer.Should().Contain("var diagnosis = _icd.Find(code);");
        writer.Should().Contain("if (diagnosis == null || filed.Any(f => f.Code == diagnosis.Code)) continue;");
        writer.Should().Contain("primary = diagnosis.Code == codes[0]");
    }

    /// <summary>
    /// The edit screen has to LOAD what it now shows, or it renders empty boxes
    /// over stored values and saving them wipes the record.
    /// </summary>
    [Fact]
    public void TheEditActionLoadsTheInsuranceAndDiagnosesTheFormShows()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var action = controller[controller.IndexOf("public IActionResult EditCustomer(", StringComparison.Ordinal)..];
        action = action[..action.IndexOf("\n    }", StringComparison.Ordinal)];

        action.Should().Contain("ViewBag.Insurance = Insurance(id, \"primary\");");
        action.Should().Contain("ViewBag.SecondaryInsurance = Insurance(id, \"secondary\");");
        action.Should().Contain("ViewBag.Diagnoses = _db.Query(");
    }

    /// <summary>
    /// The attachments panel is on both pages, and since 2026-09-05 it actually
    /// stores what it is given. On New Customer there is no record to attach to
    /// yet, and it says so rather than taking a file and dropping it, which is
    /// what it did for the first two years of this product.
    /// </summary>
    [Fact]
    public void TheAttachmentsPanelIsOnBothPagesAndIsHonestOnEach()
    {
        var form = Form();

        form.Should().Contain("Save the customer first.");
        form.Should().Contain("action=\"/Dme/AttachCustomerDoc\"");
        form.Should().NotContain("Customer documents are not stored yet.");
    }

    /// <summary>
    /// Secondary insurance had a column and a customer-page panel since the
    /// schema was written, and no form ever wrote one. Medicare plus a
    /// supplement is the ordinary case in this trade, so a customer who had one
    /// could only have got it from the demo seed.
    /// </summary>
    [Fact]
    public void TheFormWritesBothKindsOfInsurance()
    {
        var controller = Read("Controllers", "DmeController.cs");

        controller.Should().Contain("SaveCustomerInsurance(\n            custId, \"primary\"")
            .And.Contain("custId, \"secondary\"");
        controller.Should().Contain("id, \"primary\"")
            .And.Contain("id, \"secondary\"");

        // And the edit screen loads what it now shows, or it renders empty boxes
        // over a stored policy and saving wipes it.
        controller.Should().Contain("ViewBag.SecondaryInsurance = Insurance(id, \"secondary\");");
    }

    /// <summary>
    /// A secondary can be ENDED and a primary cannot, and that asymmetry is on
    /// purpose. A secondary genuinely lapses: a spouse changes job, COBRA runs
    /// out. A primary does not lapse into nothing, it becomes a different payer,
    /// which the picker already does. A supplier with no primary cannot bill at
    /// all, so removing it is not a state worth a button.
    /// </summary>
    [Fact]
    public void OnlyTheSecondaryCanBeEnded()
    {
        var form = Form();
        var controller = Read("Controllers", "DmeController.cs");

        form.Should().Contain("name=\"secRemove\"");
        form.Should().NotContain("name=\"insRemove\"");

        // The checkbox reaches UpdateCustomer only. Creating a customer cannot
        // remove a policy that does not exist yet.
        controller.Should().Contain("remove: secRemove");
        Action("CreateCustomer").GetParameters().Select(p => p.Name)
            .Should().NotContain("secRemove");
    }

    /// <summary>
    /// The delete is guarded on the row existing and is the only DELETE against
    /// this table. It is safe because a claim carries its own PayerName, copied
    /// when the claim was raised, so ending a policy today cannot change what a
    /// past claim says it was billed under.
    /// </summary>
    [Fact]
    public void EndingAPolicyCannotRewriteWhatAClaimWasBilledUnder()
    {
        var writer = Method("SaveCustomerInsurance");
        var controller = Read("Controllers", "DmeController.cs");

        writer.Should().Contain("if (existing != null)");
        controller.Split("DELETE FROM dbo.DmeCustomerInsurances").Length.Should().Be(2,
            "one delete, in the one writer");

        // The claim's own copy is what makes it safe. If this ever became a
        // join, ending a policy would silently rewrite old claims.
        controller.Should().Contain("INSERT INTO dbo.DmeClaims (ClaimNumber,OrderId,CustomerId,CustomerName,PayerName");
    }

    /// <summary>
    /// The two pickers are independent. dmeTypeahead is keyed entirely off the
    /// selectors it is handed, so the only thing that could make them collide is
    /// a shared id.
    /// </summary>
    [Fact]
    public void TheTwoPayerPickersDoNotShareIds()
    {
        var form = Form();

        foreach (var id in new[] { "payerSearch", "insPayerId", "payerResults", "payerChosen" })
            form.Should().Contain($"\"#{id}\"".Replace("\"", ""), $"the primary picker still uses {id}");

        foreach (var id in new[] { "secPayerSearch", "secPayerId", "secPayerResults", "secPayerChosen" })
            form.Should().Contain(id, $"the secondary picker needs its own {id}");
    }

    /// <summary>
    /// Kind was NVARCHAR(12) with a comment next to it and nothing enforcing it,
    /// which was survivable while only one screen wrote the table and only ever
    /// wrote 'primary'. Six places read WHERE Kind='primary', so a row stored as
    /// anything else would be found by none of them, and a claim would be raised
    /// with no payer: not an error, just a claim addressed to nobody.
    /// </summary>
    [Fact]
    public void TheDatabaseEnforcesWhatKindMeans()
    {
        var migration = Read("Migrations", "Manual", "2026-09-05_DME_Secondary_Insurance.sql");

        migration.Should().Contain("CHECK (Kind IN ('primary', 'secondary'))");
        migration.Should().Contain("CREATE UNIQUE INDEX UX_DmeCustomerInsurances_Kind");
        migration.Should().Contain("ON dbo.DmeCustomerInsurances (CustomerId, Kind)");
    }

    private static string Form() => Read("Views", "Dme", "_CustomerForm.cshtml");

    /// <summary>
    /// The body of one private writer.
    ///
    /// Anchored on "private", not on the name: both writers are CALLED from
    /// CreateCustomer, which appears in the file long before either is
    /// declared, so a plain name search returns the call site and reads the
    /// wrong method entirely. That is what an earlier draft of these tests did.
    /// </summary>
    private static string Method(string name)
    {
        var controller = Read("Controllers", "DmeController.cs");
        var at = controller.IndexOf($"private string? {name}(", StringComparison.Ordinal);
        if (at < 0) at = controller.IndexOf($"private IReadOnlyList<string> {name}(", StringComparison.Ordinal);

        at.Should().BeGreaterThan(-1, $"{name} must be declared on DmeController");

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
