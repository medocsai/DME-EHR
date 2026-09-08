using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Editing a customer.
///
/// WHY THIS DID NOT EXIST AND HAD TO
///
/// A customer could only be CREATED. There was no edit action anywhere in the
/// product, no way to add an insurance, and no way to correct a member ID or a
/// date of birth. One mistyped digit rejected every claim for that person for
/// as long as the record lived, and the only remedy staff had was a second
/// customer record: two files for one person, orders split across both, and the
/// rental history on whichever one they happened to open.
///
/// Insurance is the half that matters most. People change plan every January. A
/// customer whose payer is stale is not a cosmetic problem, it is a claim
/// billed to an insurer who has never heard of them, and there was no secondary
/// payer at all, so a Medicare supplement could never be billed and the 20%
/// went to the patient instead.
///
/// PROVED BY EXECUTION before any of this was written down: in a real signed-in
/// browser, the demographics saved, a rename made the customer findable under
/// the new name and not the old one, a secondary insurance was added through
/// the payer typeahead, and a diagnosis was added through the ICD typeahead and
/// came back marked primary.
/// </summary>
public class DmeCustomerEditingTests
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

    private static string Action(string name)
    {
        var body = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"IActionResult> " + name + @"\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        body.Should().NotBeEmpty($"{name} should still be there");
        return body;
    }

    // ------------------------------------------------------ the blind index

    /// <summary>
    /// THE ONE THAT BREAKS SILENTLY.
    ///
    /// Customer names are ciphertext at rest, so search runs against a blind
    /// index of hashed name prefixes. Saving a new name without rebuilding it
    /// leaves the customer findable under their OLD name and not findable under
    /// the new one, which is worse than either alone: the operator concludes
    /// the record was deleted and creates it again.
    ///
    /// Nothing about the screen would show this. The save succeeds, the page
    /// renders the new name, and only a later search reveals it.
    /// </summary>
    [Fact]
    public void SavingACustomerRebuildsTheSearchIndex()
    {
        Action("UpdateCustomer").Should().Contain("IndexCustomerForSearch(id, firstName, lastName, phone)",
            "the index is hashed from the plaintext, so a rename must rewrite it");
    }

    /// <summary>
    /// And the helper has to be safe to call again. It deletes the old tokens
    /// before inserting the new ones; without the delete a renamed customer
    /// accumulates every name they have ever had and stays findable under all
    /// of them.
    /// </summary>
    [Fact]
    public void TheIndexHelperClearsTheOldTokensFirst()
    {
        var helper = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"private void IndexCustomerForSearch\(.*?\n    \}",
            RegexOptions.Singleline).Value;

        helper.Should().Contain("DELETE FROM dbo.DmeCustomerSearchTokens",
            "re-indexing has to replace, not append");
    }

    /// <summary>
    /// PHI is encrypted on the way back in. An UPDATE that wrote plaintext would
    /// leave one readable row in a table of ciphertext, and the row that gave it
    /// away would be the one somebody had just corrected.
    /// </summary>
    [Fact]
    public void EditedCustomerFieldsAreEncryptedOnTheWayBackIn()
    {
        var update = Action("UpdateCustomer");

        update.Should().Contain("object Enc(string? v) => (object?)_phi.Encrypt(v)",
            "the same gateway the create form uses");

        update.Should().MatchRegex(@"fn = Enc\(firstName\), ln = Enc\(lastName\)",
            "names are the columns search depends on and must not be stored raw");
    }

    // ------------------------------------------------------------ insurance

    /// <summary>
    /// The payer is looked up in the catalog and its NAME read back out. A
    /// posted name would let a typo, or a forged field, become the payer a claim
    /// is addressed to. Same rule the create form already followed.
    /// </summary>
    [Fact]
    public void TheInsurancePayerComesFromTheCatalogNotTheBrowser()
    {
        var save = Action("SaveInsurance");

        save.Should().Contain("_payers.Find(payerId)");
        save.Should().Contain("pn = payer.Name").And.Contain("pid = payer.PayerCode",
            "both are read out of the catalog after the id is resolved");
    }

    /// <summary>
    /// A customer from another supplier must not be reachable by posting their
    /// id. DmeDb scopes the read, so the check is that the check exists at all.
    /// </summary>
    [Theory]
    [InlineData("SaveInsurance")]
    [InlineData("AddDiagnosis")]
    public void APostedCustomerIdIsCheckedAgainstThisTenant(string action)
    {
        Action(action).Should().Contain(
            "SELECT CustomerId FROM dbo.DmeCustomers WHERE CustomerId=@customerId",
            "the row level security scoped read is what makes a foreign id simply not exist");
    }

    /// <summary>
    /// CMS-1500 box 6. It was the literal 'Self' on every insert because the
    /// form never asked, so a customer covered by a spouse's policy was recorded
    /// as the subscriber themselves and their claim said so.
    /// </summary>
    [Fact]
    public void TheRelationshipToTheSubscriberIsAskedForAndValidated()
    {
        var save = Action("SaveInsurance");

        save.Should().Contain("SubscriberRelationships.Contains(subscriberRel",
            "a posted relationship has to be one the CHECK constraint will accept");

        Read("Controllers", "DmeController.cs").Should().Contain(
            "SubscriberRelationships = { \"Self\", \"Spouse\", \"Child\", \"Other\" }");
    }

    /// <summary>
    /// One primary and one secondary, so saving a primary over an existing one
    /// REPLACES it. That is what happens when somebody changes plan, and it is
    /// the commonest edit there is. Appending instead would leave two primaries
    /// and no way to say which gets billed.
    /// </summary>
    [Fact]
    public void SavingAnInsuranceOfTheSameKindReplacesRatherThanAppends()
    {
        var save = Action("SaveInsurance");

        save.Should().Contain("UPDATE dbo.DmeCustomerInsurances SET",
            "an existing kind is updated in place");
        save.Should().Contain("WHERE CustomerId=@customerId AND Kind=@kind");
    }

    /// <summary>
    /// And the database enforces it, so the rule survives a second writer.
    /// A plain unique index rather than a filtered one, deliberately: a FILTERED
    /// index makes every write to the table require QUOTED_IDENTIFIER ON, and
    /// sqlcmd defaults it OFF, which is a documented trap in this codebase.
    /// </summary>
    [Fact]
    public void TheDatabaseEnforcesOneInsuranceOfEachKind()
    {
        var sql = Read("Migrations", "Manual", "2026-09-08_DME_Customer_Editing.sql");

        sql.Should().Contain("CREATE UNIQUE INDEX UX_DmeCustomerInsurances_OnePerKind");
        sql.Should().Contain("ON dbo.DmeCustomerInsurances (CustomerId, Kind)");
        sql.Should().NotContain("WHERE Kind = 'primary'",
            "a filtered index would drag this table into the QUOTED_IDENTIFIER trap");
    }

    // ------------------------------------------------ the ambiguity it unlocks

    /// <summary>
    /// EVERY read of the primary payer must be ordered.
    ///
    /// TOP 1 with no ORDER BY is not deterministic. While only one primary could
    /// exist it could not be wrong; the moment insurances can be edited it can,
    /// and a rental would bill to a different insurer month to month with
    /// nothing on any screen showing it.
    ///
    /// This is the class of bug a new feature creates in old code, which is why
    /// it is asserted across the whole controller rather than in one place.
    /// </summary>
    [Fact]
    public void EveryPrimaryPayerLookupIsOrdered()
    {
        var controller = Read("Controllers", "DmeController.cs");

        var unordered = Regex.Matches(controller, @"TOP 1[^""]*Kind='primary'(?![^""]*ORDER BY)")
            .Select(m => m.Value)
            .ToArray();

        unordered.Should().BeEmpty(
            "TOP 1 without ORDER BY picks a row the engine is free to change between runs. "
            + $"Unordered: {string.Join(" | ", unordered)}");
    }

    // ------------------------------------------------------------ diagnoses

    /// <summary>
    /// A customer with diagnoses but no primary puts nothing in box 21 and the
    /// claim is denied for it. Removing the primary has to promote another.
    /// </summary>
    [Fact]
    public void RemovingThePrimaryDiagnosisPromotesAnother()
    {
        Action("RemoveDiagnosis").Should().Contain("SET IsPrimary = 1",
            "box 21 needs a primary, and losing one silently is a denial");
    }

    /// <summary>
    /// The first diagnosis added becomes the primary, so nobody has to nominate
    /// one by hand on a form that does not ask.
    /// </summary>
    [Fact]
    public void TheFirstDiagnosisBecomesThePrimary()
    {
        Action("AddDiagnosis").Should().Contain("p = hasPrimary == null ? 1 : 0");
    }

    // ---------------------------------------------------------------- audit

    /// <summary>
    /// Every one of these changes PHI or the money a claim is addressed to, so
    /// every one is audited. An edit nobody can reconstruct is worse than no
    /// edit, because the record now disagrees with the claim that was sent and
    /// nothing says when it changed.
    /// </summary>
    [Theory]
    [InlineData("UpdateCustomer", "DME_CUSTOMER_UPDATED")]
    [InlineData("SaveInsurance", "DME_INSURANCE_SAVED")]
    [InlineData("RemoveInsurance", "DME_INSURANCE_REMOVED")]
    [InlineData("AddDiagnosis", "DME_DIAGNOSIS_ADDED")]
    [InlineData("RemoveDiagnosis", "DME_DIAGNOSIS_REMOVED")]
    public void EveryEditIsAudited(string action, string eventName)
    {
        Action(action).Should().Contain(eventName);
    }

    /// <summary>
    /// And every one is a POST with an antiforgery token. A GET that changed
    /// data could be triggered by an image tag on another site.
    /// </summary>
    [Theory]
    [InlineData("UpdateCustomer")]
    [InlineData("SaveInsurance")]
    [InlineData("RemoveInsurance")]
    [InlineData("AddDiagnosis")]
    [InlineData("RemoveDiagnosis")]
    public void EveryEditIsAGuardedPost(string action)
    {
        var controller = Read("Controllers", "DmeController.cs");

        var attributes = Regex.Match(controller,
            @"\[HttpPost\]\s*\r?\n\s*\[ValidateAntiForgeryToken\][^}]*?IActionResult> " + action + @"\(",
            RegexOptions.Singleline).Value;

        attributes.Should().NotBeEmpty(
            $"{action} writes data, so it needs [HttpPost] and [ValidateAntiForgeryToken]");
    }

    // ----------------------------------------------------- the branch is not here

    /// <summary>
    /// The branch a customer belongs to is deliberately NOT editable here.
    /// Moving somebody between branches moves their orders, rentals and claims
    /// with them by derivation, which is a different decision from correcting a
    /// phone number, and it is the one column every other screen filters on.
    /// </summary>
    [Fact]
    public void TheEditFormDoesNotQuietlyMoveACustomerBetweenBranches()
    {
        Read("Views", "Dme", "EditCustomer.cshtml").Should().NotContain("name=\"locationId\"",
            "changing a branch moves the whole history and is not a demographics edit");

        Action("UpdateCustomer").Should().NotContain("LocationId",
            "the update statement must not touch it even if a field appeared");
    }
}
