using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EHR.Helpers;
using EHR.Services;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The clearinghouse SFTP credential is the highest value secret this product
/// stores. It is the key to filing claims as this supplier.
///
/// WHY THIS EXISTS
/// A leaked credential does not announce itself. The four ways it escapes are
/// all quiet, and all of them are one careless line: stored in the clear,
/// selected onto a screen by a `SELECT *`, serialised into a response DTO, or
/// copied into an audit row that then keeps it for six years.
///
/// These tests pin each of those shut. They are cheap and they run with no
/// database, because the alternative is trusting everyone who touches the
/// settings screen to remember all four.
/// </summary>
public class DmeClearinghouseCredentialTests
{
    private const string PlainUser = "PTPINC1";
    private const string PlainPass = "n0t-a-real-password";

    /// <summary>
    /// Records every statement AND every parameter value the service sends, so a
    /// test can assert on what would actually have reached SQL Server.
    /// </summary>
    private sealed class FakeDb
    {
        public readonly Mock<IDmeDb> Mock = new();
        public readonly List<(string Sql, object? Prms)> Statements = new();

        public FakeDb(Dictionary<string, object?>? existing = null)
        {
            Mock.SetupGet(d => d.TenantId).Returns(1);

            Mock.Setup(d => d.QueryOne(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(existing);

            Mock.Setup(d => d.Query(It.IsAny<string>(), It.IsAny<object>()))
                .Returns(new List<Dictionary<string, object?>>());

            Mock.Setup(d => d.Scalar(It.IsAny<string>(), It.IsAny<object>()))
                .Callback<string, object?>((sql, p) => Statements.Add((sql, p)))
                .Returns(7);

            Mock.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>()))
                .Callback<string, object?>((sql, p) => Statements.Add((sql, p)))
                .Returns(1);
        }

        /// <summary>Every parameter value sent, flattened to strings.</summary>
        public IEnumerable<string> AllParameterValues =>
            Statements.SelectMany(s => Values(s.Prms));

        private static IEnumerable<string> Values(object? prms)
        {
            if (prms is IDictionary<string, object?> map)
                return map.Values.Select(v => v?.ToString() ?? "");
            if (prms == null) return Array.Empty<string>();
            return prms.GetType().GetProperties().Select(p => p.GetValue(prms)?.ToString() ?? "");
        }
    }

    private static (DmeSftpAccountService service, FakeDb db, Mock<IDmeAudit> audit, EncryptionHelper crypto)
        Build(Dictionary<string, object?>? existing = null)
    {
        var db = new FakeDb(existing);
        var audit = new Mock<IDmeAudit>();
        var crypto = TestEncryptionHelper.Create();
        return (new DmeSftpAccountService(db.Mock.Object, audit.Object, crypto), db, audit, crypto);
    }

    private static SftpAccountInput NewAccount(string? user = PlainUser, string? pass = PlainPass, int id = 0)
        => new(id, "Office Ally", "ftp10.officeally.com", 22, user, pass, IsTestMode: true, IsActive: true);

    private static Dictionary<string, object?> Existing() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["SftpAccountId"] = 7,
        ["Label"] = "Office Ally",
        ["Host"] = "ftp10.officeally.com",
        ["Port"] = 22,
        ["IsTestMode"] = true,
        ["IsActive"] = true,
    };

    // ------------------------------------------------------- ciphertext at rest

    [Fact]
    public async Task NeitherHalfOfTheCredentialReachesTheDatabaseInTheClear()
    {
        var (service, db, _, _) = Build();

        var result = await service.SaveAsync(NewAccount());

        result.Ok.Should().BeTrue();
        db.AllParameterValues.Should().NotContain(PlainPass,
            "a password stored in the clear is readable by anyone with a database backup, " +
            "and a backup travels further than the database does");
        db.AllParameterValues.Should().NotContain(PlainUser,
            "a username is half a credential and is protected like the other half");
    }

    [Fact]
    public async Task WhatIsStoredDecryptsBackToWhatWasEntered()
    {
        var (service, db, _, crypto) = Build();

        await service.SaveAsync(NewAccount());

        // Encrypting is worthless if it cannot be read back by the sender that
        // will need it. Prove the round trip rather than assuming it.
        var stored = db.AllParameterValues.ToList();
        stored.Select(v => crypto.Decrypt(v)).Should().Contain(PlainUser);
        stored.Select(v => crypto.Decrypt(v)).Should().Contain(PlainPass);
    }

    // ------------------------------------------------------------ never leaked

    [Fact]
    public async Task TheAuditRowRecordsThatACredentialWasSetAndNotWhatItIs()
    {
        var (service, _, audit, _) = Build();
        object? captured = null;
        audit.Setup(a => a.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<object>(), It.IsAny<object>()))
             .Callback<string, string, int?, object?, object?>((_, _, _, _, after) => captured = after)
             .Returns(Task.CompletedTask);

        await service.SaveAsync(NewAccount());

        var json = JsonSerializer.Serialize(captured);
        json.Should().NotContain(PlainPass,
            "AuditLogs is retained for six years, so a secret copied into it outlives every " +
            "rotation policy that was supposed to protect it");
        json.Should().NotContain(PlainUser);
        json.Should().Contain("CredentialsSet");
    }

    [Fact]
    public void TheListingReadsTheViewThatHasNoCredentialColumns()
    {
        var (service, db, _, _) = Build();

        service.List();

        db.Mock.Verify(d => d.Query(It.Is<string>(s => s.Contains("vDmeSftpAccounts")), It.IsAny<object>()), Times.Once);
        db.Mock.Verify(d => d.Query(It.Is<string>(s => s.Contains("FROM dbo.DmeSftpAccounts")), It.IsAny<object>()), Times.Never,
            "selecting from the base table is how the password ends up in a ViewBag");
    }

    /// <summary>
    /// The view exists so no screen has to remember to omit the two columns.
    /// A screen that goes back to the base table loses that protection silently,
    /// because the page still renders.
    /// </summary>
    [Fact]
    public void NoScreenReadsTheCredentialTableDirectly()
    {
        var offenders = ScreenFiles()
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"FROM\s+dbo\.DmeSftpAccounts\b", RegexOptions.IgnoreCase))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty(
            "only DmeSftpAccountService may touch dbo.DmeSftpAccounts. Everything else reads " +
            $"vDmeSftpAccounts, which does not expose Username or Password. Offending files: {string.Join(", ", offenders)}");
    }

    // ------------------------------------------------------------------- rules

    [Fact]
    public async Task ANewAccountMustCarryBothHalvesOfTheCredential()
    {
        var (service, db, _, _) = Build();

        var result = await service.SaveAsync(NewAccount(pass: ""));

        result.Ok.Should().BeFalse();
        db.Statements.Should().BeEmpty("a half-entered credential is a login that will fail at the worst moment");
    }

    /// <summary>
    /// Changing the port must not require re-typing the password. That sounds
    /// like a convenience and is a security property: a form that demands the
    /// password for every unrelated edit is a form that gets a sticky note.
    /// </summary>
    [Fact]
    public async Task EditingAnAccountWithoutRetypingThePasswordLeavesTheStoredOneAlone()
    {
        var (service, db, _, _) = Build(Existing());

        var result = await service.SaveAsync(new SftpAccountInput(
            7, "Office Ally", "ftp12.officeally.com", 22, null, null, IsTestMode: true, IsActive: true));

        result.Ok.Should().BeTrue();
        var update = db.Statements.Single().Sql;
        update.Should().Contain("Host=@host");
        update.Should().NotContain("Password=@password",
            "a blank password means unchanged, and writing it anyway would blank the stored credential");
        update.Should().NotContain("Username=@username");
    }

    [Fact]
    public async Task RetypingThePasswordDoesReplaceIt()
    {
        var (service, db, _, _) = Build(Existing());

        await service.SaveAsync(new SftpAccountInput(
            7, "Office Ally", "ftp10.officeally.com", 22, null, "a-new-password", IsTestMode: true, IsActive: true));

        db.Statements.Single().Sql.Should().Contain("Password=@password");
    }

    [Fact]
    public async Task AnAccountIsTakenOutOfServiceAndNeverDeleted()
    {
        var (service, db, _, _) = Build(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Label"] = "Office Ally",
            ["IsActive"] = true,
        });

        var result = await service.SetActiveAsync(7, false);

        result.Ok.Should().BeTrue();
        db.Statements.Single().Sql.Should().NotContain("DELETE",
            "the row is the record of what past claims were submitted under; deleting it " +
            "would destroy submission history to save one row");
        db.Statements.Single().Sql.Should().Contain("IsActive<>@isActive",
            "a repeated click must not write an audit row describing a change that did not happen");
    }

    [Fact]
    public async Task DeactivatingAnAlreadyInactiveAccountIsNotAudited()
    {
        var (service, db, audit, _) = Build(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Label"] = "Office Ally",
            ["IsActive"] = false,
        });
        db.Mock.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>())).Returns(0);

        var result = await service.SetActiveAsync(7, false);

        result.Ok.Should().BeFalse();
        audit.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "ftp10.officeally.com", 22)]
    [InlineData("Office Ally", "", 22)]
    [InlineData("Office Ally", "ftp10.officeally.com", 0)]
    [InlineData("Office Ally", "ftp10.officeally.com", 70000)]
    public async Task AnIncompleteAccountIsRefusedBeforeAnythingIsWritten(string label, string host, int port)
    {
        var (service, db, _, _) = Build();

        var result = await service.SaveAsync(new SftpAccountInput(0, label, host, port, PlainUser, PlainPass, true, true));

        result.Ok.Should().BeFalse();
        db.Statements.Should().BeEmpty();
    }

    // --------------------------------------------------- the identity it bills as

    /// <summary>
    /// The billing provider was a hardcoded string in the CMS-1500 view, so every
    /// claim carried an NPI belonging to nobody. This is the ratchet against it
    /// coming back the next time somebody wants the form to "look right" before
    /// the data exists.
    /// </summary>
    [Fact]
    public void TheCms1500HasNoHardcodedBillingProvider()
    {
        var cms = File.ReadAllText(Path.Combine(ProductionRoot(), "Views", "Dme", "Cms.cshtml"));

        // A run of ten digits in the markup is an NPI somebody typed in.
        Regex.IsMatch(cms, @"(?<![\d@""(])\d{10}(?![\d])").Should().BeFalse(
            "an NPI in the markup is an NPI that belongs to nobody, printed on every claim. " +
            "Box 33 reads vDmeBillingProvider.");

        cms.Should().Contain("ViewBag.Provider",
            "the form must read the supplier record rather than describing a supplier of its own");
    }

    /// <summary>
    /// dbo.Tenants is the platform registry and is NOT covered by the row level
    /// security policy that protects every DME table. An UPDATE against it
    /// without an explicit tenant filter renames every tenant on the server, and
    /// nothing would report an error.
    /// </summary>
    [Fact]
    public void EveryWriteToTheTenantRegistryIsScopedToTheCallersTenant()
    {
        var controller = File.ReadAllText(Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        var updates = Regex.Matches(controller, @"UPDATE\s+dbo\.Tenants\b.*?""", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        updates.Should().NotBeEmpty("the settings screen writes the supplier identity to dbo.Tenants");
        foreach (System.Text.RegularExpressions.Match update in updates)
        {
            update.Value.Should().Contain("WHERE TenantId=@TenantId",
                "dbo.Tenants has no row level security behind it, so this WHERE clause is the " +
                "only thing standing between saving your own name and renaming every tenant");
        }
    }

    private static string[] ScreenFiles()
    {
        var root = ProductionRoot();
        var views = Directory.Exists(Path.Combine(root, "Views", "Dme"))
            ? Directory.GetFiles(Path.Combine(root, "Views", "Dme"), "*.cshtml")
            : Array.Empty<string>();

        return views
            .Concat(new[]
            {
                Path.Combine(root, "Controllers", "DmeController.cs"),
                Path.Combine(root, "Controllers", "HcpcsController.cs"),
            })
            .Where(File.Exists)
            .ToArray();
    }

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
