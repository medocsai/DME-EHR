using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EHR.Helpers;
using EHR.Services;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Customer documents: the CMN, the prescription, the insurance card, the ID.
///
/// WHY THESE EXIST
/// The Attachments panel on the New Customer screen was demo UI from the day it
/// was written. It let an operator pick a file, listed it, and stored nothing at
/// all. A list of file names reads as "saved", which made it the worst kind of
/// broken: the intake clerk attaches the referral, sees it on the screen, and
/// the referral is gone the moment the page navigates.
///
/// WHAT THEY GUARD
/// The same four things the proof-of-delivery tests guard, because it is the
/// same machinery, plus the one thing that is new: a customer document has a
/// KIND, and a kind outside the list must never reach the table.
/// </summary>
public class DmeCustomerDocumentTests
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

    private static EncryptionHelper Encryption()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = "a-test-key-long-enough-for-derivation-2026"
            })
            .Build();

        return new EncryptionHelper(config);
    }

    /// <summary>A storage that remembers what it was handed, so the bytes can be inspected.</summary>
    private sealed class RecordingStorage : IFileStorageService
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Deleted = new();

        public Task<FileUploadResult> UploadBytesAsync(byte[] b, string name, string folder, string ct,
            Dictionary<string, string>? m = null)
        {
            var key = folder.TrimEnd('/') + "/" + name;
            Files[key] = b;
            return Task.FromResult(new FileUploadResult { Success = true, CloudPath = key, FileSize = b.Length });
        }

        public async Task<FileUploadResult> UploadFileAsync(Stream s, string name, string folder, string ct,
            Dictionary<string, string>? m = null)
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            return await UploadBytesAsync(ms.ToArray(), name, folder, ct, m);
        }

        public Task<byte[]?> DownloadBytesAsync(string p) => Task.FromResult(Files.TryGetValue(p, out var b) ? b : null);
        public Task<Stream?> DownloadFileAsync(string p) => Task.FromResult<Stream?>(Files.TryGetValue(p, out var b) ? new MemoryStream(b) : null);
        public Task<bool> DeleteFileAsync(string p) { Deleted.Add(p); Files.Remove(p); return Task.FromResult(true); }
        public Task<bool> FileExistsAsync(string p) => Task.FromResult(Files.ContainsKey(p));
        public Task<string> GetSignedUrlAsync(string p, int? m = null) => throw new NotSupportedException();
        public Task<List<string>> ListFilesAsync(string f) => Task.FromResult(Files.Keys.Where(k => k.StartsWith(f)).ToList());
        public Task<bool> CopyFileAsync(string a, string b) => Task.FromResult(true);
    }

    private static (DmeCustomerDocuments docs, RecordingStorage storage, EncryptionHelper enc)
        Build(bool customerExists = true)
    {
        var db = new Mock<IDmeDb>();
        db.SetupGet(d => d.TenantId).Returns(1);

        db.Setup(d => d.QueryOne(It.Is<string>(s => s.Contains("FROM dbo.DmeCustomers")), It.IsAny<object>()))
          .Returns(customerExists
              ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["CustomerId"] = 2 }
              : null);

        db.Setup(d => d.Scalar(It.IsAny<string>(), It.IsAny<object>())).Returns(7);

        var storage = new RecordingStorage();
        var enc = Encryption();
        var files = new DmeDocumentStore(storage, new FilePathBuilder(), enc);

        return (new DmeCustomerDocuments(db.Object, files), storage, enc);
    }

    private static byte[] RealPdf()
        => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    // ------------------------------------------------------------ encrypted at rest

    /// <summary>
    /// The guard the whole feature turns on. A CMN carries the customer's name,
    /// address, diagnosis and prescriber. What reaches storage must not be it.
    /// </summary>
    [Fact]
    public async Task TheBytesThatReachStorageAreNotThePlainDocument()
    {
        var (docs, storage, _) = Build();
        var plain = RealPdf();

        var result = await docs.AttachAsync(2, "cmn", "margaret-ellis-cmn.pdf", "application/pdf", plain, 1);

        result.Success.Should().BeTrue(result.Error);
        var stored = storage.Files.Values.Single();

        stored.Should().NotEqual(plain, "the document must be encrypted before it leaves the app");
        Encoding.ASCII.GetString(stored).Should().NotContain("%PDF");
    }

    /// <summary>
    /// A bucket listing must not read "margaret-ellis-cmn.pdf", and the row must
    /// not either: the original name routinely carries the customer's name.
    /// </summary>
    [Fact]
    public async Task NeitherTheObjectKeyNorTheRowCarriesThePatientsName()
    {
        var (docs, storage, enc) = Build();

        await docs.AttachAsync(2, "cmn", "margaret-ellis-cmn.pdf", "application/pdf", RealPdf(), 1);

        var key = storage.Files.Keys.Single();
        key.Should().NotContain("margaret").And.NotContain("ellis");
        key.Should().StartWith("customer/1/2/", "scoped by tenant and customer");

        // And the name in the row is ciphertext that still decrypts to the original.
        var sealedName = enc.Encrypt("margaret-ellis-cmn.pdf");
        sealedName.Should().NotContain("margaret");
        enc.Decrypt(sealedName).Should().Be("margaret-ellis-cmn.pdf");
    }

    // ------------------------------------------------------------------ the kind

    /// <summary>
    /// A biller answering a CO-50 denial is looking for the CMN that establishes
    /// medical necessity, not for "a file". The kind is what makes that
    /// findable, so a value outside the list must not reach the table: it would
    /// be refused by the check constraint as a 500 rather than a sentence.
    /// </summary>
    [Fact]
    public async Task AnInventedKindIsRefusedBeforeItReachesTheDatabase()
    {
        var (docs, storage, _) = Build();

        var result = await docs.AttachAsync(2, "smuggled", "cmn.pdf", "application/pdf", RealPdf(), 1);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Choose what kind of document that is.");
        storage.Files.Should().BeEmpty("a refused document must leave nothing behind");
    }

    /// <summary>
    /// The list the picker offers and the list the constraint allows are the
    /// same list. A kind offered by the form and rejected by the database is a
    /// 500 the operator caused by using the screen as intended.
    /// </summary>
    [Fact]
    public void EveryKindTheFormOffersIsOneTheDatabaseAccepts()
    {
        var (docs, _, _) = Build();
        var migration = Read("Migrations", "Manual", "2026-09-05_DME_Customer_Documents.sql");

        var allowed = migration[migration.IndexOf("CHECK (Kind IN", StringComparison.Ordinal)..];
        allowed = allowed[..allowed.IndexOf(')', StringComparison.Ordinal)];

        foreach (var kind in docs.Kinds)
            allowed.Should().Contain($"'{kind.Value}'", $"the picker offers {kind.Value}");

        docs.Kinds.Should().NotBeEmpty();
    }

    // ------------------------------------------------------- a file is what it says

    [Theory]
    [InlineData("prescription.exe")]
    [InlineData("notes.docx")]
    [InlineData("card.zip")]
    public async Task OnlyADocumentWeCanFileIsAccepted(string fileName)
    {
        var (docs, storage, _) = Build();

        var result = await docs.AttachAsync(2, "rx", fileName, "application/octet-stream", RealPdf(), 1);

        result.Success.Should().BeFalse();
        storage.Files.Should().BeEmpty();
    }

    /// <summary>
    /// The check that matters. Extension and MIME both come from the browser and
    /// are whatever the uploader says they are.
    /// </summary>
    [Fact]
    public async Task AnExecutableRenamedToPdfIsRefused()
    {
        var (docs, storage, _) = Build();
        var executable = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 };   // MZ

        var result = await docs.AttachAsync(2, "cmn", "cmn.pdf", "application/pdf", executable, 1);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not really a PDF");
        storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyOrOversizedFileIsRefused()
    {
        var (docs, storage, _) = Build();

        (await docs.AttachAsync(2, "cmn", "cmn.pdf", "application/pdf", Array.Empty<byte>(), 1))
            .Success.Should().BeFalse();

        var huge = new byte[DmeDocumentStore.MaxFileBytes + 1];
        (await docs.AttachAsync(2, "cmn", "cmn.pdf", "application/pdf", huge, 1))
            .Success.Should().BeFalse();

        storage.Files.Should().BeEmpty();
    }

    /// <summary>
    /// Row level security filters the customer read, so somebody else's customer
    /// is simply not found. Checked here as well, so a refusal is a sentence
    /// rather than a foreign key violation.
    /// </summary>
    [Fact]
    public async Task ACustomerFromAnotherSupplierAcceptsNothing()
    {
        var (docs, storage, _) = Build(customerExists: false);

        var result = await docs.AttachAsync(2, "cmn", "cmn.pdf", "application/pdf", RealPdf(), 1);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("That customer does not exist.");
        storage.Files.Should().BeEmpty();
    }

    // --------------------------------------------------------------- the schema

    /// <summary>
    /// No IsDeleted, no IsEncrypted, no LocationId. The first is derived from
    /// DeletedAt by the view, the second is constant, the third comes from the
    /// customer. Every one of them would be a second copy of a fact that already
    /// has a home.
    /// </summary>
    [Theory]
    [InlineData("IsDeleted")]
    [InlineData("IsEncrypted")]
    [InlineData("LocationId")]
    public void TheTableStoresNoDerivedState(string column)
    {
        var migration = Read("Migrations", "Manual", "2026-09-05_DME_Customer_Documents.sql");
        var table = migration[migration.IndexOf("CREATE TABLE dbo.DmeCustomerDocuments", StringComparison.Ordinal)..];
        table = table[..table.IndexOf(");", StringComparison.Ordinal)];

        // The COLUMNS, not the words. The comments in that block name IsDeleted
        // and LocationId to explain where each one is derived from instead, and
        // that explanation is the part worth keeping.
        var columns = string.Join("\n", table
            .Split('\n')
            .Select(line => line.Contains("--") ? line[..line.IndexOf("--", StringComparison.Ordinal)] : line));

        columns.Should().NotContain(column);
    }

    /// <summary>
    /// A document is removed, never erased. The ROW is the record that one was
    /// attached and withdrawn; the BYTES are deleted, because PHI nobody can
    /// reach through the product is storage risk with no purpose.
    /// </summary>
    [Fact]
    public void ADocumentIsRetiredNeverErased()
    {
        var service = Read("Services", "DmeCustomerDocuments.cs");

        service.Should().Contain("SET DeletedAt = SYSUTCDATETIME()");
        service.Should().Contain("AND DeletedAt IS NULL",
            "guarded so two people clicking Remove produce one removal");
        service.Should().NotContain("DELETE FROM dbo.DmeCustomerDocuments");
    }

    /// <summary>
    /// Listing and opening both go through the view, which excludes removed
    /// rows. That exclusion IS the removal mechanism, so no caller has to
    /// remember a DeletedAt filter and a removed document cannot be downloaded.
    /// </summary>
    [Fact]
    public void ListingAndOpeningGoThroughTheViewSoRemovedFilesAreGone()
    {
        var service = Read("Services", "DmeCustomerDocuments.cs");
        var migration = Read("Migrations", "Manual", "2026-09-05_DME_Customer_Documents.sql");

        service.Should().NotContain("FROM dbo.DmeCustomerDocuments WHERE",
            "reads go through vDmeCustomerDocuments");
        service.Split("vDmeCustomerDocuments").Length.Should().BeGreaterOrEqualTo(4,
            "listing, opening and removing all read the view");

        migration.Should().Contain("WHERE       d.DeletedAt IS NULL;");
    }

    /// <summary>
    /// Tenant isolation is not optional on a new table. Without both predicates
    /// the row level security policy treats it as open, and a document belonging
    /// to another supplier would be readable.
    /// </summary>
    [Fact]
    public void TheNewTableIsInTheTenantIsolationPolicy()
    {
        var migration = Read("Migrations", "Manual", "2026-09-05_DME_Customer_Documents.sql");

        migration.Should().Contain("ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerDocuments");
        migration.Should().Contain("ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerDocuments AFTER INSERT");
        migration.Should().Contain("ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerDocuments AFTER UPDATE");

        // ALTER SECURITY POLICY is validated at COMPILE time, so an IF NOT EXISTS
        // guard around a bare statement does not protect it.
        migration.Should().Contain("EXEC sp_executesql N'");
    }

    // ------------------------------------------------------------- serving them

    /// <summary>
    /// A signed URL is valid for anyone holding it and leaves both the tenant
    /// check and the audit trail behind. Same decision as DownloadPod.
    /// </summary>
    [Fact]
    public void FilesAreServedByAnAuditedActionNotASignedUrl()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var service = Read("Services", "DmeCustomerDocuments.cs");

        controller.Should().Contain("public async Task<IActionResult> DownloadCustomerDoc(int id)");
        service.Should().NotContain("GetSignedUrl");
    }

    /// <summary>
    /// Removing a CMN removes the document that defends a claim, so it is not a
    /// correction an intake clerk makes in passing. Admin only, matching
    /// RemovePod.
    /// </summary>
    [Fact]
    public void RemovingADocumentIsAdminOnly()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var at = controller.IndexOf("public async Task<IActionResult> RemoveCustomerDoc", StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1);

        var attributes = controller[Math.Max(0, at - 400)..at];
        attributes.Should().Contain("[Authorize(Roles = DmeRoles.Admin)]");
        attributes.Should().Contain("[ValidateAntiForgeryToken]");
    }

    // ------------------------------------------------------------------ the panel

    /// <summary>
    /// The uploader is a form of its own and CANNOT end up inside the customer
    /// form. HTML forbids nested forms and a browser handed one silently drops
    /// the inner one, so the file would go nowhere and nothing would say why.
    ///
    /// An earlier draft passed a weaker version of this test and was still
    /// broken: it put the attachments card in the two column grid, INSIDE the
    /// customer form, and moved the uploader there with a script. The source
    /// order looked right and the browser reported a nested form anyway, because
    /// appendChild produces the same nesting in the DOM that the parser refuses
    /// in markup. So this counts brackets rather than comparing positions.
    /// </summary>
    [Fact]
    public void TheUploaderIsNotInsideTheCustomerForm()
    {
        var view = Read("Views", "Dme", "_CustomerForm.cshtml");

        // Everything from the customer form's opening tag to its matching close.
        var opens = view.IndexOf("<form method=\"post\" action=\"@(editing", StringComparison.Ordinal);
        opens.Should().BeGreaterThan(-1, "the customer form is still the first form in the file");

        var closes = view.IndexOf("\n</form>", opens, StringComparison.Ordinal);
        closes.Should().BeGreaterThan(opens);

        // Past the opening tag, or the slice starts with the very "<form" it is
        // about to complain about.
        var customerForm = view[(view.IndexOf('>', opens) + 1)..closes];

        customerForm.Should().NotContain("<form",
            "no form of any kind may sit inside the customer form");
        customerForm.Should().NotContain("AttachCustomerDoc");
        customerForm.Should().NotContain("RemoveCustomerDoc");

        // And nothing may move it back in afterwards.
        view.Should().NotContain("appendChild(content)",
            "relocating the uploader by script recreates the nesting it avoids");
    }

    /// <summary>
    /// A document attaches to a record, and on New Customer there is not one
    /// yet. Said plainly, rather than shown as a picker that quietly discards
    /// what it is given, which is exactly what the old panel did.
    /// </summary>
    [Fact]
    public void TheNewCustomerScreenSaysWhyItCannotAttachYet()
    {
        var form = Read("Views", "Dme", "_CustomerForm.cshtml");

        form.Should().Contain("Save the customer first.");
        form.Should().NotContain("Customer documents are not stored yet.",
            "they are stored now");
    }

    /// <summary>
    /// One set of file rules. Copying them for this feature would have put the
    /// magic-bytes check in two places, and the version that gets forgotten is
    /// always the one that mattered.
    /// </summary>
    [Fact]
    public void BothDocumentServicesShareOneSetOfFileRules()
    {
        foreach (var service in new[] { "DmeOrderDocuments.cs", "DmeCustomerDocuments.cs" })
        {
            var text = Read("Services", service);

            text.Should().Contain("DmeDocumentStore _files");
            text.Should().NotContain("SHA256.HashData", $"{service} must not hash on its own");
            text.Should().NotContain("EncryptBytes", $"{service} must not encrypt on its own");
            text.Should().NotContain("ValidateFileContent", $"{service} must not validate on its own");
        }
    }
}
