using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The certificate of medical necessity.
///
/// WHAT WAS THERE
///
/// Two places held the same fact and neither knew about the other.
///
/// dbo.DmeCmns holds real certificates: customer, doctor, HCPCS, initial date,
/// RECERT date, status. Nothing in the product read it. Not one C# file, not
/// one view.
///
/// dbo.DmeOrderLines.CmnOnFile was a BIT the order screen rendered as a green
/// "on file" or an amber "missing" chip. Nothing in the product wrote it. It
/// was set when the demo data was seeded and never moved again.
///
/// So the screen telling a biller whether the paperwork exists read a flag no
/// code maintained, while the table recording the certificate was read by
/// nobody.
///
/// WHY IT IS WORSE THAN AN ORDINARY DUPLICATE
///
/// A CMN EXPIRES. A stored boolean cannot. It says "on file" the day it is set
/// and for ever after, including the month after the certificate lapsed, so the
/// screen is at its most confident exactly when it is wrong. A claim dated
/// after the recert date is denied for want of documentation the supplier
/// genuinely had, once.
/// </summary>
public class DmeCmnTests
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

    private static string Migration()
        => Read("Migrations", "Manual", "2026-09-08_DME_Cmn_Derived.sql");

    /// <summary>
    /// The stored flag is gone and stays gone. It is the copy, and the copy was
    /// the thing that could not expire.
    /// </summary>
    [Fact]
    public void TheStoredCmnFlagIsNotReadAnywhere()
    {
        foreach (var dir in new[] { "Controllers", "Services", "Views", "Helpers" })
        {
            var full = Path.Combine(RepoRoot(), dir);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.GetFiles(full, "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs") && !file.EndsWith(".cshtml")) continue;

                File.ReadAllText(file).Should().NotContain("CmnOnFile",
                    $"{Path.GetFileName(file)} would be reading a boolean nothing maintains, "
                    + "in place of a certificate that has an expiry date");
            }
        }
    }

    /// <summary>
    /// The screen reads the derived view, not the base table. Reading the table
    /// is how the stale flag was reached in the first place.
    /// </summary>
    [Fact]
    public void TheOrderScreenReadsTheDerivedView()
    {
        var order = Regex.Match(
            Read("Controllers", "DmeController.cs"),
            @"public IActionResult Order\(int id\).*?\n    \}",
            RegexOptions.Singleline).Value;

        order.Should().Contain("FROM dbo.vDmeOrderLines",
            "CmnStatus only exists on the view");
        order.Should().NotContain("FROM dbo.DmeOrderLines",
            "the base table has no certificate on it at all");
    }

    /// <summary>
    /// THREE states, not two.
    ///
    /// "Missing" needs the doctor to sign something. "Expired" needs them to
    /// sign it AGAIN. Collapsing them loses the distinction between work that
    /// has never been done and work that has to be redone, and the second is the
    /// one that surprises people.
    /// </summary>
    [Theory]
    [InlineData("'missing'")]
    [InlineData("'expired'")]
    [InlineData("'on-file'")]
    public void TheViewReportsAllThreeStates(string state)
    {
        Migration().Should().Contain(state);
    }

    /// <summary>
    /// Expiry is decided by comparing the recert date to the date, which is the
    /// entire reason a stored flag could not do this job.
    /// </summary>
    [Fact]
    public void ExpiryIsDecidedByTheRecertDate()
    {
        Migration().Should().Contain("cmn.RecertDate < CAST(GETDATE() AS DATE)",
            "a certificate with a recert date in the past is expired, whatever it once was");
    }

    /// <summary>
    /// And the recert date reaches the screen, so it can say WHEN rather than
    /// only that it has lapsed. "Expired" alone sends somebody to look it up.
    /// </summary>
    [Fact]
    public void TheScreenCanSayWhenTheCertificateLapses()
    {
        Migration().Should().Contain("cmn.RecertDate     AS CmnRecertDate");
        Read("Views", "Dme", "Order.cshtml").Should().Contain("CmnRecertDate");
    }

    /// <summary>
    /// The certificate has to belong to the same customer AND the same code. A
    /// CMN covers one item; matching on the customer alone would mark every line
    /// on the order as documented because one of them was.
    /// </summary>
    [Fact]
    public void ACertificateOnlyCoversTheCodeItWasIssuedFor()
    {
        var migration = Migration();

        migration.Should().Contain("c.CustomerId = o.CustomerId");
        migration.Should().Contain("c.Hcpcs      = l.Hcpcs",
            "a CMN for an oxygen concentrator does not document a wheelchair");
    }

    /// <summary>
    /// A DDL object name cannot be a parameter.
    ///
    /// ALTER TABLE ... DROP CONSTRAINT @c is a syntax error, and inside
    /// sp_executesql it fails at RUN time with a message about the constraint
    /// rather than about the parameter, so it reads like a dependency problem.
    /// The auto generated default constraint name differs per database, so it
    /// has to be looked up and then concatenated.
    ///
    /// Both migrations that drop a column are checked, because the first one was
    /// written the wrong way and only worked because that column happened to
    /// carry no default.
    /// </summary>
    [Theory]
    [InlineData("2026-09-08_DME_Cmn_Derived.sql")]
    [InlineData("2026-09-08_DME_Drop_Fake_Eligibility.sql")]
    public void ADroppedDefaultConstraintIsNamedByConcatenationNotByParameter(string fileName)
    {
        var sql = Read("Migrations", "Manual", fileName);

        sql.Should().Contain("QUOTENAME(@df)",
            "the constraint name is read from a catalog view and pasted into the statement");

        // Comments stripped first. Both files EXPLAIN the wrong form in a
        // comment, and a test that matched its own documentation would fail on
        // the file that documents the fix best.
        var executable = Regex.Replace(sql, @"--[^\r\n]*", "");

        executable.Should().NotContain("DROP CONSTRAINT @c",
            "an object name in DDL cannot be parameterised");
    }
}
