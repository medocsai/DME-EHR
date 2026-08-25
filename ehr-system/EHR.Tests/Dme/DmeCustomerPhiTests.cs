using System.Collections.Generic;
using System.Linq;
using EHR.Helpers;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// DME customer PHI is encrypted at rest and stays findable.
///
/// WHY THIS EXISTS
/// Clinical patient names in this database were already ciphertext while DME
/// customer names sat in the clear, for the same people. These tests pin the
/// three things that make the fix real rather than nominal: the right columns
/// are covered, a roll-out over half-plaintext data does not corrupt anything,
/// and search still returns the customer once the name is unreadable.
/// </summary>
public class DmeCustomerPhiTests
{
    private static DmeCustomerPhi Phi() => new(TestEncryptionHelper.Create());

    [Theory]
    [InlineData("FirstName")]
    [InlineData("LastName")]
    [InlineData("Phone")]
    [InlineData("Email")]
    [InlineData("AddressLine1")]
    [InlineData("City")]
    [InlineData("Zip")]
    [InlineData("SsnLast4")]
    [InlineData("EmergencyName")]
    [InlineData("EmergencyPhone")]
    public void DirectIdentifiers_AreCovered(string column)
    {
        DmeCustomerPhi.EncryptedColumns.Should().Contain(column,
            $"{column} identifies a person and must not be readable from a stolen backup");
    }

    [Theory]
    [InlineData("AccountNo", "it is an internal handle we generate, not derived from the person, and staff search on it")]
    [InlineData("Dob", "it is a DATE column; encrypting it needs a type change and a migration of its own, and half-doing it would be worse than not doing it")]
    public void DeliberateExclusions_StayExcluded(string column, string why)
    {
        DmeCustomerPhi.EncryptedColumns.Should().NotContain(column,
            $"{column} is deliberately left in the clear: {why}. " +
            "If that changes it must be a decision, not a drive-by edit.");
    }

    [Fact]
    public void EncryptedValue_RoundTripsBackToTheOriginal()
    {
        var phi = Phi();
        var row = new Dictionary<string, object?> { ["FirstName"] = phi.Encrypt("Margaret") };

        row["FirstName"].Should().NotBe("Margaret", "the stored value must not be readable");

        phi.DecryptRow(row);
        row["FirstName"].Should().Be("Margaret");
    }

    /// <summary>
    /// The roll-out safety property. Encryption cannot land as a flag day: for a
    /// while the table holds a mix of encrypted and plaintext rows, and a read
    /// has to cope with both. If decryption mangled plaintext, every row not yet
    /// backfilled would render as garbage.
    /// </summary>
    [Fact]
    public void DecryptRow_LeavesStillPlaintextValuesAlone()
    {
        var row = new Dictionary<string, object?>
        {
            ["FirstName"] = "Margaret",
            ["LastName"] = "Ellis",
            ["Phone"] = null,
        };

        Phi().DecryptRow(row);

        row["FirstName"].Should().Be("Margaret");
        row["LastName"].Should().Be("Ellis");
        row["Phone"].Should().BeNull("a null column must stay null, not become an empty string");
    }

    [Fact]
    public void IsEncrypted_DistinguishesCiphertextFromPlaintext()
    {
        var phi = Phi();

        phi.IsEncrypted(phi.Encrypt("Margaret")).Should().BeTrue();
        phi.IsEncrypted("Margaret").Should().BeFalse(
            "the backfill relies on this to skip rows it has already done; getting it wrong " +
            "double-encrypts silently");
    }

    [Fact]
    public void SearchHash_IsDeterministic_OrNothingEverMatches()
    {
        var phi = Phi();

        phi.SearchHash("margaret").Should().Be(phi.SearchHash("margaret"));
    }

    [Theory]
    [InlineData("Margaret", "margaret")]
    [InlineData("  MARGARET  ", "margaret")]
    [InlineData("O'Brien", "obrien")]
    public void SearchHash_NormalisesTheSameWayTheIndexDid(string typed, string canonical)
    {
        var phi = Phi();

        phi.SearchHash(typed).Should().Be(phi.SearchHash(canonical),
            "the query is normalised at search time and at index time; if the two ever " +
            "diverge, search silently returns nothing");
    }

    [Fact]
    public void SearchHash_ReturnsNull_ForAnEmptyTerm()
    {
        Phi().SearchHash("   ").Should().BeNull(
            "an empty term must not produce a hash that happens to match a stored token");
    }

    [Fact]
    public void BuildSearchTokens_MakesAnEncryptedCustomerFindableByPrefix()
    {
        var phi = Phi();
        var tokens = phi.BuildSearchTokens("Margaret", "Ellis", "(214) 555-0144");
        var hashes = tokens.Select(t => t.hash).ToHashSet();

        hashes.Should().Contain(phi.SearchHash("mar")!, "typing a few letters is how people search");
        hashes.Should().Contain(phi.SearchHash("margaret")!);
        hashes.Should().Contain(phi.SearchHash("ellis")!, "surname search must work too");
    }

    [Fact]
    public void BuildSearchTokens_FindsAPhoneByItsDigits()
    {
        var phi = Phi();
        var hashes = phi.BuildSearchTokens("Margaret", "Ellis", "(214) 555-0144")
                        .Select(t => t.hash).ToHashSet();

        hashes.Should().Contain(phi.SearchHash("2145550144")!,
            "people type a phone number without its punctuation");
        hashes.Should().Contain(phi.SearchHash("214")!,
            "and often only the first few digits");
    }

    [Fact]
    public void BuildSearchTokens_MatchesEitherNameOrder()
    {
        var phi = Phi();
        var hashes = phi.BuildSearchTokens("Margaret", "Ellis", null)
                        .Select(t => t.hash).ToHashSet();

        hashes.Should().Contain(phi.SearchHash("margaret ellis")!);
        hashes.Should().Contain(phi.SearchHash("ellis margaret")!,
            "staff type surname first at least as often as given name first");
    }

    [Fact]
    public void BuildSearchTokens_DoesNotLeakThePlaintext()
    {
        var tokens = Phi().BuildSearchTokens("Margaret", "Ellis", "(214) 555-0144");

        tokens.Should().NotContain(t => t.hash.Contains("argaret") || t.hash.Contains("llis"),
            "a blind index that stores anything recognisable defeats the encryption it sits beside");
    }

    [Fact]
    public void BuildSearchTokens_ReturnsNothing_WhenThereIsNothingToIndex()
    {
        Phi().BuildSearchTokens(null, null, null).Should().BeEmpty(
            "indexing empty values would give every blank-named customer a shared token");
    }

    // ---------------------------------------------------------------------
    // Regression: composing a customer name from encrypted parts.
    //
    // This class of bug shipped once already and was caught by loading the
    // pages, not by a unit test. vDmeOrders and vDmeRentals originally returned
    // cu.FirstName + ' ' + cu.LastName. Once those columns were encrypted, the
    // view concatenated two ciphertexts, which cannot be decrypted as one value
    // no matter what key you hold, and the Orders, Rentals, Dashboard and
    // Schedule screens rendered base64 where the customer name should be.
    //
    // Nothing threw. The screens returned 200. That is what makes it worth a
    // test: the failure is invisible to any check that only looks at status
    // codes.
    // ---------------------------------------------------------------------

    [Fact]
    public void ComposeCustomerName_JoinsTheDecryptedPartsFromAView()
    {
        var phi = Phi();
        var row = new Dictionary<string, object?>
        {
            ["CustomerFirstName"] = phi.Encrypt("Margaret"),
            ["CustomerLastName"] = phi.Encrypt("Ellis"),
        };

        phi.ComposeCustomerName(row);

        row["CustomerName"].Should().Be("Margaret Ellis",
            "the parts must be decrypted first and joined second; joining ciphertext in SQL " +
            "produces a value that can never be decrypted");
    }

    [Fact]
    public void ComposeCustomerName_DecryptsAStoredNameInPlace()
    {
        var phi = Phi();
        var row = new Dictionary<string, object?> { ["CustomerName"] = phi.Encrypt("Margaret Ellis") };

        phi.ComposeCustomerName(row);

        row["CustomerName"].Should().Be("Margaret Ellis",
            "claims store the name as filed rather than joining it, so that path must " +
            "decrypt rather than compose");
    }

    [Fact]
    public void ComposeCustomerName_HandlesAMissingCustomer()
    {
        var phi = Phi();
        var row = new Dictionary<string, object?>
        {
            ["CustomerFirstName"] = null,
            ["CustomerLastName"] = null,
        };

        phi.ComposeCustomerName(row);

        row["CustomerName"].Should().Be("",
            "an order whose customer row is missing must render blank, not \" \" or a crash");
    }

    [Fact]
    public void ComposeCustomerName_ProducesNoCiphertext()
    {
        var phi = Phi();
        var row = new Dictionary<string, object?>
        {
            ["CustomerFirstName"] = phi.Encrypt("Margaret"),
            ["CustomerLastName"] = phi.Encrypt("Ellis"),
        };

        phi.ComposeCustomerName(row);

        var name = row["CustomerName"] as string;
        phi.IsEncrypted(name).Should().BeFalse(
            "this is the exact regression: the screens rendered base64 where a name belonged");
        name.Should().NotContain("=", "base64 padding leaking into the UI is the tell");
    }
}
