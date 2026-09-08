using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Markup faults the compiler cannot see.
///
/// Razor compiles a view without ever checking that its HTML is balanced, so a
/// missing closing tag is a clean build, a 200 response, and a broken page. The
/// build output is identical either way, which is exactly the class of fault
/// that survives review.
///
/// THE ONE THAT PROMPTED THIS, and it was self inflicted. Removing a dead demo
/// script block from NewCustomer.cshtml took the closing &lt;/script&gt; with
/// it. The browser then treated the entire rest of the document as JavaScript:
/// two page errors, and every typeahead on the customer creation form silently
/// dead. The page still returned 200 and the screenshot still looked right.
/// It was found by reading the browser console, not by any test.
/// </summary>
public class DmeViewMarkupTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Views"))) d = d.Parent;
        d.Should().NotBeNull("the tests must be able to find the ehr-system folder");
        return d!.FullName;
    }

    private static string[] AllViews()
        => Directory.GetFiles(Path.Combine(RepoRoot(), "Views"), "*.cshtml", SearchOption.AllDirectories);

    /// <summary>
    /// Markup with comments removed.
    ///
    /// _Layout.cshtml carries an HTML comment that mentions &lt;script&gt; while
    /// explaining what DOMPurify strips, and counting that as a real tag makes
    /// this test fail on a file that is perfectly balanced. A test that cries
    /// wolf gets an exemption added to it, and the exemption is what fails to
    /// catch the real one later.
    /// </summary>
    private static string WithoutComments(string text)
    {
        text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"@\*.*?\*@", "", RegexOptions.Singleline);
    }

    /// <summary>
    /// Every &lt;script&gt; is closed.
    ///
    /// Counted rather than parsed, deliberately: a real HTML parser here would
    /// be a dependency and a source of its own arguments, and the fault this
    /// catches is always a missing tag rather than a misplaced one.
    ///
    /// Self closing script tags are not legal HTML and are not used here, so an
    /// open count that differs from a close count is always wrong.
    /// </summary>
    [Fact]
    public void EveryScriptTagInEveryViewIsClosed()
    {
        foreach (var file in AllViews())
        {
            var text = WithoutComments(File.ReadAllText(file));

            // <script> and <script src=...>, but not the word inside a comment
            // or a string, which is why the match is anchored on the bracket.
            var opens = Regex.Matches(text, @"<script\b", RegexOptions.IgnoreCase).Count;
            var closes = Regex.Matches(text, @"</script\s*>", RegexOptions.IgnoreCase).Count;

            closes.Should().Be(opens,
                $"{Path.GetFileName(file)} has {opens} script tag(s) and {closes} closing tag(s). "
                + "An unclosed script makes the browser read the rest of the page as JavaScript, "
                + "which still compiles, still returns 200, and still screenshots correctly.");
        }
    }

    /// <summary>
    /// The same for style blocks, for the same reason. An unclosed style hides
    /// the remainder of the page instead of executing it, which is at least
    /// visible, but it is the identical mistake.
    /// </summary>
    [Fact]
    public void EveryStyleBlockInEveryViewIsClosed()
    {
        foreach (var file in AllViews())
        {
            var text = WithoutComments(File.ReadAllText(file));
            var opens = Regex.Matches(text, @"<style\b", RegexOptions.IgnoreCase).Count;
            var closes = Regex.Matches(text, @"</style\s*>", RegexOptions.IgnoreCase).Count;

            closes.Should().Be(opens, $"{Path.GetFileName(file)} has an unbalanced style block");
        }
    }

    /// <summary>
    /// No view references a script file that is not in wwwroot.
    ///
    /// A missing script is a 404 whose body is an HTML error page, which the
    /// browser then parses as JavaScript and reports as "Unexpected token '&lt;'".
    /// The message names neither the file nor the page.
    ///
    /// Three clinical SignalR clients were deleted in this pass and their tags
    /// removed with them; this is what stops the reverse, a tag left behind
    /// pointing at a file that has gone.
    /// </summary>
    [Fact]
    public void EveryLocalScriptAndStylesheetAViewAsksForExists()
    {
        var wwwroot = Path.Combine(RepoRoot(), "wwwroot");

        foreach (var file in AllViews())
        {
            var text = WithoutComments(File.ReadAllText(file));

            foreach (Match m in Regex.Matches(text,
                @"(?:src|href)\s*=\s*""~(/[^""?]+)(?:\?[^""]*)?""", RegexOptions.IgnoreCase))
            {
                var relative = m.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var full = Path.Combine(wwwroot, relative);

                File.Exists(full).Should().BeTrue(
                    $"{Path.GetFileName(file)} asks for ~/{m.Groups[1].Value.TrimStart('/')}, "
                    + "and a missing one returns an HTML error page that the browser parses as script");
            }
        }
    }

    /// <summary>
    /// The clinical SignalR clients stay gone.
    ///
    /// No hub was ever mapped in this product: Program.cs has no AddSignalR and
    /// no MapHub. All three clients self started on DOMContentLoaded, failed
    /// negotiation with a 404, and retried with backoff, on every page load, for
    /// every user. They were consent kiosk notifications, appointment schedule
    /// updates and patient messaging, none of which exist here.
    /// </summary>
    [Theory]
    [InlineData("ScheduleSignalRService.js")]
    [InlineData("ConsentSignalRService.js")]
    [InlineData("MessagingSignalRService.js")]
    public void TheClinicalSignalRClientsStayRemoved(string fileName)
    {
        File.Exists(Path.Combine(RepoRoot(), "wwwroot", "js", "core", fileName))
            .Should().BeFalse($"{fileName} connected to a hub this product never mapped");

        File.ReadAllText(Path.Combine(RepoRoot(), "Views", "Shared", "_Layout.cshtml"))
            .Should().NotContain(fileName);
    }

    /// <summary>
    /// And the library they needed is no longer pulled from a CDN on every page
    /// load. Nothing left uses it: RecordingService checks for it and returns
    /// quietly when it is absent.
    /// </summary>
    [Fact]
    public void TheSignalRLibraryIsNoLongerLoaded()
    {
        File.ReadAllText(Path.Combine(RepoRoot(), "Views", "Shared", "_Layout.cshtml"))
            .Should().NotContain("microsoft-signalr",
                "a client side library loaded for a server feature that does not exist");
    }
}
