using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The one picker, shared by all three reference-data lists: 4,017 payers,
/// 74,719 ICD-10-CM codes, 8,623 HCPCS codes.
///
/// WHY THESE EXIST
/// A dropdown that ignores the operator is invisible to every other kind of
/// test. The suite was green, the page rendered, the request went out and the
/// results came back, and picking one still did nothing on a laptop trackpad.
/// </summary>
public class DmeTypeaheadTests
{
    /// <summary>
    /// The bug, and it is a race that a mouse happens to win.
    ///
    /// The list used to close 150ms after the box lost focus, which was meant to
    /// give a click on a result time to land first. A mouse does mousedown,
    /// mouseup and click inside about 20ms and gets there. A trackpad has to
    /// RECOGNISE a tap before the click is synthesised, and any finger movement
    /// during it pushes that further out; past 150ms the list has been emptied
    /// and the button the click was aimed at no longer exists.
    ///
    /// The operator sees a dropdown that does nothing, works the moment they
    /// plug a mouse in, and explains itself never.
    ///
    /// Preventing the default on mousedown stops the focus from moving, so blur
    /// does not fire while somebody is picking and there is no race left.
    /// </summary>
    [Fact]
    public void TheListDoesNotCloseOnATimerTheClickHasToBeat()
    {
        var js = Typeahead();

        js.Should().Contain("list.addEventListener('mousedown', function (e) { e.preventDefault(); });",
            "stopping the focus from moving is what removes the race");

        js.Should().Contain("input.addEventListener('blur', hide);",
            "the list closes when focus actually leaves, not on a delay");

        js.Should().NotContain("setTimeout(hide",
            "a delay long enough for a trackpad is a delay the operator can see, " +
            "and any delay at all is still a race");
    }

    /// <summary>
    /// Typing again abandons the previous choice. Without it the visible box can
    /// read one payer while the hidden field still posts another, which is how
    /// the wrong payer reaches a claim.
    /// </summary>
    [Fact]
    public void TypingAgainAbandonsTheChoiceAlreadyMade()
    {
        var js = Typeahead();
        var onInput = js[js.IndexOf("input.addEventListener('input'", StringComparison.Ordinal)..];

        onInput[..onInput.IndexOf("});", StringComparison.Ordinal)]
            .Should().Contain("clearChoice();");
    }

    /// <summary>
    /// A slow reply to an older keystroke must not overwrite a newer one, or the
    /// list shows matches for something the operator has already finished typing
    /// past.
    /// </summary>
    [Fact]
    public void ALateReplyToAnOlderKeystrokeIsDropped()
    {
        var js = Typeahead();

        js.Should().Contain("var mine = ++seq;");
        js.Should().Contain("if (mine !== seq) return;");
    }

    /// <summary>
    /// The rows are type=button. This picker sits inside the customer form, and
    /// the default for a button in a form is submit: picking a payer would save
    /// the record.
    /// </summary>
    [Fact]
    public void PickingFromTheListDoesNotSubmitTheFormAroundIt()
    {
        Typeahead().Should().Contain("row.type = 'button';");
    }

    /// <summary>
    /// Only the hidden field is posted, and the server re-reads the name from the
    /// catalog, so nothing typed into the visible box can become what is stored.
    /// </summary>
    [Fact]
    public void TheVisibleBoxIsOnlyASearchField()
    {
        var js = Typeahead();
        var choose = js[js.IndexOf("function choose(item)", StringComparison.Ordinal)..];
        choose = choose[..choose.IndexOf("\n        }", StringComparison.Ordinal)];

        choose.Should().Contain("hidden.value = opts.value(item);");
        choose.Should().Contain("input.value = opts.label(item);");
    }

    /// <summary>
    /// Editing a .js file without bumping its ?v leaves every returning browser
    /// on the old copy, which for this fix means the trackpad stays broken for
    /// exactly the people who already use the product.
    /// </summary>
    [Fact]
    public void TheScriptIsCacheBustedPastTheVersionThatHadTheRace()
    {
        var layout = File.ReadAllText(Path.Combine(Root(), "Views", "Shared", "_Layout.cshtml"));

        layout.Should().Contain("Typeahead.js?v=");
        layout.Should().NotContain("Typeahead.js?v=2",
            "v=2 is the copy with the 150ms timer in it");
    }

    private static string Typeahead()
        => File.ReadAllText(Path.Combine(Root(), "wwwroot", "js", "dme", "Typeahead.js"));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
