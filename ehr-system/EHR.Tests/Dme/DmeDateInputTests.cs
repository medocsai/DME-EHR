using System.Globalization;
using System.Text.RegularExpressions;
using EHR.Helpers;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Date of birth is typed or pasted as free text, so the parsing is ours.
/// These pin the two things that would go wrong silently: a date read in the
/// wrong month/day order, and an unreadable value being guessed at.
/// </summary>
public class DmeDateInputTests
{
    [Theory]
    [InlineData("01/15/1950")]
    [InlineData("1/15/1950")]
    [InlineData("01-15-1950")]
    [InlineData("1950-01-15")]
    [InlineData("1950/01/15")]
    [InlineData("Jan 15 1950")]
    [InlineData("Jan 15, 1950")]
    [InlineData("15 Jan 1950")]
    [InlineData("January 15, 1950")]
    [InlineData("  01/15/1950  ")]
    public void ReadsEverySpellingAnOperatorMightPaste(string typed)
    {
        Assert.Equal(new DateTime(1950, 1, 15), DateInput.Parse(typed));
    }

    /// <summary>
    /// The one that costs money if it is wrong. 03/04 is March 4th, and it must
    /// stay March 4th on a server running under any locale, because a date of
    /// birth off by a month fails eligibility and the claim is denied.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    public void MonthComesFirstWhateverLocaleTheServerRunsUnder(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(new DateTime(1950, 3, 4), DateInput.Parse("03/04/1950"));
            // English month names too: under de-DE, "Jan" is only a month to a
            // parser that was told which language to read it in.
            Assert.Equal(new DateTime(1950, 1, 15), DateInput.Parse("Jan 15 1950"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("13/45/1950")]
    [InlineData("1950")]
    public void RefusesToGuessAtWhatItCannotRead(string? typed)
    {
        Assert.Null(DateInput.Parse(typed));
    }

    /// <summary>
    /// Every date a human types in DME is a plain text box. The native control
    /// refuses a paste, which is the complaint that started all of this, so one
    /// reappearing anywhere is the same bug coming back.
    /// </summary>
    [Fact]
    public void NoDmeScreenUsesTheNativeDateControl()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(ProductionRoot(), "Views"), "*.cshtml",
                            SearchOption.AllDirectories)
            // An <input>, not the word in a comment explaining why we do not use one.
            .Where(f => Regex.IsMatch(File.ReadAllText(f),
                                      "<input[^>]*type=\"date\"", RegexOptions.IgnoreCase))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These still use <input type=\"date\">, which refuses a paste: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The dates arrive as strings on purpose. Typed as DateTime, ASP.NET binds
    /// them under the server's locale and 03/04/2026 becomes March or April
    /// depending on the machine, with no error either way. A wrong service date
    /// is a denied claim, so this pins the signatures rather than the symptom.
    /// </summary>
    [Theory]
    [InlineData("string? deliveryDate")]
    [InlineData("string postedDate")]
    public void PastedDatesReachTheControllerAsTextNotDateTime(string expected)
    {
        var source = File.ReadAllText(
            Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains(expected, source);
    }

    /// <summary>
    /// A posting date that cannot be read must be refused, never quietly
    /// defaulted: that would post real money into the wrong month.
    /// </summary>
    [Fact]
    public void AnUnreadablePostingDateIsRefusedRatherThanDefaulted()
    {
        var source = File.ReadAllText(
            Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains("DateInput.Parse(postedDate)", source);
        Assert.Contains("The posting date could not be read", source);
        Assert.DoesNotContain("postedDate ?? DateTime.Today", source);
    }

    /// <summary>
    /// A date of birth that was typed but cannot be read must be refused, not
    /// dropped. DateInput.Parse returns null for "blank" and for "gibberish"
    /// alike, so without an explicit check the customer saves with no DOB and
    /// nobody is told until eligibility fails weeks later.
    /// </summary>
    [Fact]
    public void AnUnreadableDateOfBirthIsRefusedRatherThanSilentlyDropped()
    {
        var source = File.ReadAllText(
            Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains("That date of birth could not be read", source);
        Assert.Contains("DateInput.Parse(dob) == null", source);

        // The rule lives in one place and both the create and the correct paths
        // call it, so a customer cannot be saved through one of them with a date
        // the other would have refused.
        Assert.Contains("private static string? CustomerFieldProblem(", source);
        Assert.Equal(2, Regex.Matches(source, @"CustomerFieldProblem\(dob, email").Count);
    }

    /// <summary>
    /// Every telephone box refuses letters somehow. One left plain is the one a
    /// name gets pasted into.
    ///
    /// Two mechanisms are accepted because they do genuinely different jobs.
    /// data-mask="phone" (InputMaskUtils.js, inherited with the fork) strips
    /// everything but digits and reformats to (XXX) XXX-XXXX, capped at ten, so
    /// it cannot carry an extension or a country code. js-phone (FormFields.js)
    /// only removes letters and leaves the rest as written, which is what the
    /// DME forms want. What must never happen is a phone box with neither.
    /// </summary>
    [Fact]
    public void EveryPhoneBoxCarriesTheSharedRule()
    {
        var bare = Directory
            .EnumerateFiles(Path.Combine(ProductionRoot(), "Views"), "*.cshtml",
                            SearchOption.AllDirectories)
            .SelectMany(f => Regex
                .Matches(File.ReadAllText(f), "<input[^>]*name=\"[a-zA-Z]*[Pp]hone\"[^>]*>")
                .Select(m => new { File = Path.GetFileName(f), Tag = m.Value }))
            .Where(x => !x.Tag.Contains("js-phone")
                     && !x.Tag.Contains("data-mask=\"phone\""))
            .Select(x => x.File)
            .ToList();

        Assert.True(bare.Count == 0,
            "These phone fields still accept letters: " + string.Join(", ", bare));
    }

    /// <summary>
    /// The browser filters these fields as they are typed, which is a courtesy
    /// and nothing more: it is off, bypassed or simply not reached by a form
    /// posted from anywhere else. State and ZIP are printed onto the CMS-1500,
    /// where a wrong one is a denial, so the rule has to exist server side too.
    /// </summary>
    [Theory]
    [InlineData("That email address is not valid.")]
    [InlineData("The SSN box takes the last four digits only.")]
    [InlineData("State is the two letter code, for example TX.")]
    [InlineData("ZIP is five digits.")]
    public void TheCustomerFieldRulesAreEnforcedOnTheServerNotOnlyInTheBrowser(string message)
    {
        var source = File.ReadAllText(
            Path.Combine(ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains(message, source);
    }

    /// <summary>
    /// A check that cannot speak is a field that goes red with no explanation.
    /// Every .js-* check on the customer form needs its .js-field-error to
    /// write into.
    /// </summary>
    [Fact]
    public void EveryInlineCheckHasSomewhereToPutItsMessage()
    {
        var view = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Views", "Dme", "NewCustomer.cshtml"));

        // <input> tags, not the comment explaining what the classes do.
        var checks = Regex.Matches(view, "<input[^>]*js-(dateinput|email)").Count;
        var slots = Regex.Matches(view, "js-field-error").Count;

        Assert.True(slots >= checks,
            $"{checks} inline check(s) but only {slots} place(s) to report them.");
    }

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
