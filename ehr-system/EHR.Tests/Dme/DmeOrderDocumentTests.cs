using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
/// Proof-of-delivery attachments: the first real file upload in the product.
///
/// WHAT THESE GUARD
///
/// 1. The bytes are ENCRYPTED before they leave the app. A proof of delivery
///    carries the customer's name, home address and signature.
/// 2. A file is what it claims to be. Extension and MIME come from the browser;
///    only the leading bytes are the file itself.
/// 3. A document is REMOVED, never erased: the row survives so the record that
///    one was attached and withdrawn survives with it.
/// 4. Files are served by an authenticated, audited action, never a signed URL.
/// </summary>
public class DmeOrderDocumentTests
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

    private static (DmeOrderDocuments docs, RecordingStorage storage, EncryptionHelper enc, Mock<IDmeDb> db)
        Build(bool orderExists = true)
    {
        var db = new Mock<IDmeDb>();
        db.SetupGet(d => d.TenantId).Returns(1);

        db.Setup(d => d.QueryOne(It.Is<string>(s => s.Contains("FROM dbo.DmeOrders")), It.IsAny<object>()))
          .Returns(orderExists
              ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["OrderId"] = 3 }
              : null);

        db.Setup(d => d.Scalar(It.IsAny<string>(), It.IsAny<object>())).Returns(11);

        var storage = new RecordingStorage();
        var enc = Encryption();

        return (new DmeOrderDocuments(db.Object, storage, new FilePathBuilder(), enc), storage, enc, db);
    }

    private static byte[] RealPdf()
        => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    // ---------------------------------------------------------- encrypted at rest

    /// <summary>
    /// The guard the whole feature turns on. What reaches storage must not be
    /// the document.
    /// </summary>
    [Fact]
    public async Task TheBytesThatReachStorageAreNotThePlainDocument()
    {
        var (docs, storage, _, _) = Build();
        var plain = RealPdf();

        var result = await docs.AttachAsync(3, "pod.pdf", "application/pdf", plain, 5);

        result.Success.Should().BeTrue();
        var stored = storage.Files.Values.Single();

        stored.Should().NotEqual(plain, "a proof of delivery must never sit in storage in the clear");
        Encoding.ASCII.GetString(stored.Take(4).ToArray())
            .Should().NotBe("%PDF", "the file header must not survive into storage either");
    }

    [Fact]
    public async Task TheStoredNameCarriesNoPatientDetail()
    {
        var (docs, storage, _, _) = Build();

        await docs.AttachAsync(3, "john-doe-oxygen-pod.pdf", "application/pdf", RealPdf(), 5);

        var key = storage.Files.Keys.Single();
        key.Should().NotContain("john", "a bucket listing must not read as a patient list");
        key.Should().NotContain("doe");
    }

    /// <summary>
    /// Round trip. Encryption that cannot be reversed exactly is data loss, and
    /// a PDF is unforgiving about a single wrong byte.
    /// </summary>
    [Fact]
    public void EncryptedBytesComeBackExactly()
    {
        var enc = Encryption();
        var plain = RealPdf();

        var cipher = enc.EncryptBytes(plain);
        cipher.Should().NotEqual(plain);

        enc.DecryptBytes(cipher).Should().Equal(plain);
    }

    /// <summary>
    /// Tampered data must come back NULL, not as itself. The string overload
    /// deliberately falls back to returning its input, which for a file would
    /// mean serving ciphertext to a browser as a PDF: a detected tamper turned
    /// into a silent corrupt download.
    /// </summary>
    [Fact]
    public void TamperedBytesDecryptToNullRatherThanToThemselves()
    {
        var enc = Encryption();
        var cipher = enc.EncryptBytes(RealPdf());

        cipher[^1] ^= 0xFF;

        enc.DecryptBytes(cipher).Should().BeNull();
        enc.DecryptBytes(null).Should().BeNull();
        enc.DecryptBytes(new byte[] { 1, 2, 3 }).Should().BeNull();
    }

    // ------------------------------------------------------------- what it accepts

    [Theory]
    [InlineData("notes.docx")]
    [InlineData("prices.xlsx")]
    [InlineData("script.exe")]
    [InlineData("archive.zip")]
    public async Task OnlyAProofOfDeliveryIsAccepted(string fileName)
    {
        var (docs, storage, _, _) = Build();

        var result = await docs.AttachAsync(3, fileName, "application/pdf", RealPdf(), 5);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace("a refusal has to say what to do instead");
        storage.Files.Should().BeEmpty();
    }

    /// <summary>
    /// The check that actually stops something. A renamed executable passes both
    /// the extension and the MIME type, because the browser reports whatever the
    /// uploader claims.
    /// </summary>
    [Fact]
    public async Task AnExecutableRenamedToPdfIsRefused()
    {
        var (docs, storage, _, _) = Build();
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00 };

        var result = await docs.AttachAsync(3, "pod.pdf", "application/pdf", exe, 5);

        result.Success.Should().BeFalse();
        storage.Files.Should().BeEmpty("nothing that failed validation may reach storage");
    }

    [Fact]
    public async Task AnEmptyOrOversizedFileIsRefused()
    {
        var (docs, storage, _, _) = Build();

        (await docs.AttachAsync(3, "pod.pdf", "application/pdf", Array.Empty<byte>(), 5)).Success.Should().BeFalse();

        var huge = new byte[DmeOrderDocuments.MaxFileBytes + 1];
        (await docs.AttachAsync(3, "pod.pdf", "application/pdf", huge, 5)).Success.Should().BeFalse();

        storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOrderFromAnotherSupplierAcceptsNothing()
    {
        // DmeDb scopes the read, so another tenant's order simply is not found.
        var (docs, storage, _, _) = Build(orderExists: false);

        var result = await docs.AttachAsync(999, "pod.pdf", "application/pdf", RealPdf(), 5);

        result.Success.Should().BeFalse();
        storage.Files.Should().BeEmpty();
    }

    // ------------------------------------------------------- derived, not stored

    [Fact]
    public void TheDocumentTableStoresNoDerivedState()
    {
        var migration = Read("Migrations", "Manual", "2026-08-27_DME_Order_Documents.sql");

        var block = Regex.Match(migration,
            @"CREATE TABLE dbo\.DmeOrderDocuments[\s\S]*?\);").Value;

        block.Should().NotBeEmpty();

        // The comments in that block explain the rule by naming the columns it
        // forbids. The assertion is about the schema, not the prose.
        var create = Regex.Replace(block, @"--.*$", "", RegexOptions.Multiline);
        create.Should().NotContain("IsDeleted", "removal is DeletedAt, and IsDeleted is derived");
        create.Should().NotContain("IsEncrypted", "every file is encrypted, so the flag is a constant");
        create.Should().NotContain("LocationId", "the branch derives from the order, per the locations rule");
        create.Should().Contain("DeletedAt", "which is the one deletion fact");
    }

    [Fact]
    public void ADocumentIsRetiredNeverErased()
    {
        var service = Read("Services", "DmeOrderDocuments.cs");

        service.Should().NotContain("DELETE FROM dbo.DmeOrderDocuments",
            "the record that a proof of delivery was attached and withdrawn is itself evidence");
        service.Should().Contain("DeletedAt IS NULL",
            "and the update is guarded so two clicks produce one removal");
    }

    [Fact]
    public void TheHashIsOfThePlaintext()
    {
        var service = Read("Services", "DmeOrderDocuments.cs");

        var hashLine = service.Split('\n').First(l => l.Contains("SHA256.HashData"));
        hashLine.Should().Contain("bytes",
            "hashing the ciphertext would make the document uncheckable after a key rotation");

        var hashAt = service.IndexOf("SHA256.HashData", StringComparison.Ordinal);
        var encryptAt = service.IndexOf("EncryptBytes", StringComparison.Ordinal);
        hashAt.Should().BeLessThan(encryptAt, "the hash is taken before encryption");
    }

    [Fact]
    public void ListingAndOpeningGoThroughTheViewSoRemovedFilesAreGone()
    {
        var service = Read("Services", "DmeOrderDocuments.cs");

        foreach (var _ in new[] { "ForOrder", "OpenAsync" })
            service.Should().Contain("vDmeOrderDocuments");

        service.Should().NotContain("FROM dbo.DmeOrderDocuments WHERE",
            "reading the base table would serve documents that were removed");
    }

    // ---------------------------------------------------- served, not signed away

    [Fact]
    public void FilesAreServedByAnAuditedActionNotASignedUrl()
    {
        var controller = Read("Controllers", "DmeController.cs");
        var service = Read("Services", "DmeOrderDocuments.cs");

        controller.Should().Contain("DownloadPod",
            "the file goes out through our own action");

        // Comments name the thing they warn against, so strip them first.
        var code = Regex.Replace(service, @"//.*$", "", RegexOptions.Multiline);
        code.Should().NotContain("GetSignedUrlAsync",
            "a signed URL is valid for anyone holding it and leaves the tenant check " +
            "and the audit trail behind");

        controller.Should().Contain("DME_POD_ATTACHED").And.Contain("DME_POD_REMOVED",
            "attaching and withdrawing the document that defends a claim are both audited");
    }

    [Fact]
    public void RemovingADocumentIsAdminOnly()
    {
        var controller = Read("Controllers", "DmeController.cs");

        var action = Regex.Match(controller,
            @"\[HttpPost\][\s\S]{0,200}?public async Task<IActionResult> RemovePod").Value;

        action.Should().Contain(@"[Authorize(Roles = ""0,1"")]");
        action.Should().Contain("[ValidateAntiForgeryToken]");
    }

    // ------------------------------------------------------------ which storage

    /// <summary>
    /// The bucket name was inherited from the product this was forked from. Left
    /// as it was, the first service account key would have written DME
    /// proof-of-delivery documents into another product's bucket.
    /// </summary>
    [Fact]
    public void NoBucketIsInheritedFromTheProductThisWasForkedFrom()
    {
        var settings = Read("appsettings.json");

        settings.Should().NotContain("imehr-files", "that is another product's bucket");
        settings.Should().NotContain("rehabdox-ptehr-files", "and that is a third product's");
    }

    [Fact]
    public void StorageIsChosenByConfigurationNotByBuildConfiguration()
    {
        var program = Read("Program.cs");

        program.Should().Contain("GoogleCloudStorage:BucketName",
            "a configured bucket is what selects cloud storage");
        program.Should().Contain("LocalFileStorageService",
            "and no bucket falls back to disk, so the feature works before one exists");
    }

    /// <summary>
    /// Anything under wwwroot is served by URL as a static file, with no
    /// authentication, no tenant check and no audit row.
    /// </summary>
    [Fact]
    public void LocalFilesAreNotStoredSomewhereTheWebServerWillHandOut()
    {
        var service = Read("Services", "Storage", "LocalFileStorageService.cs");

        service.Should().Contain("App_Data");
        service.Should().NotContain("WebRootPath", "wwwroot is public by design");
        service.Should().Contain("escapes the storage root",
            "an object key must not be able to climb out of the storage folder");
    }
}
