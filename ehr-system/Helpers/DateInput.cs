using System.Globalization;

namespace EHR.Helpers;

/// <summary>
/// Turns whatever a human typed or pasted into a date into a real date.
///
/// Why this exists: the customer form used &lt;input type="date"&gt;, and Chrome
/// refuses a paste into that control, so a date of birth copied out of a
/// referral or an insurance card could only be retyped segment by segment. The
/// field is now plain text, which means the parsing is ours to do.
///
/// Why not lean on model binding: ASP.NET parses a DateTime with
/// CultureInfo.CurrentCulture, which is whatever locale the server happens to
/// run under. "03/04/1950" would then be March on one machine and April on
/// another, silently, with no error either way. Formats are listed here
/// instead, US order first, and nothing else is accepted.
///
/// Who calls it: DmeController.CreateCustomer. Any other pasted date field
/// should call it too rather than repeating the format list.
/// </summary>
public static class DateInput
{
    /// <summary>
    /// Accepted spellings, in priority order. US month/day order comes first
    /// because that is what the staff using this product read off a chart; the
    /// ISO form is next because that is what a copy out of another system or a
    /// browser date picker produces.
    /// </summary>
    private static readonly string[] Formats =
    {
        "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy",
        "yyyy-MM-dd", "yyyy/MM/dd",
        "MMM d yyyy", "MMM d, yyyy", "d MMM yyyy",
        "MMMM d yyyy", "MMMM d, yyyy"
    };

    /// <summary>
    /// Parses a typed or pasted date. Returns null when the field was left
    /// empty AND when it cannot be read, because a date of birth is optional
    /// on this form: an unreadable value must not be guessed at.
    /// </summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // A paste out of a PDF or a spreadsheet routinely carries non-breaking
        // spaces and stray whitespace either side of the date.
        var text = value.Replace(' ', ' ').Trim();

        return DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
