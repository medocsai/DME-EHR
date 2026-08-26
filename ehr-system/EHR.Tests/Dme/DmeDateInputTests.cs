using System.Globalization;
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
}
