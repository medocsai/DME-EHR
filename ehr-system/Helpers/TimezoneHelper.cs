using System;
using System.Collections.Generic;
using System.Linq;
using TimeZoneConverter;

namespace EHR.Helpers;

/// <summary>
/// Helper class for timezone conversions and formatting.
/// Uses IANA timezone identifiers (e.g., "America/New_York", "America/Los_Angeles")
/// All internal storage is in UTC; this helper converts to/from location timezones for display.
/// </summary>
public static class TimezoneHelper
{
    // Common US timezone mappings for abbreviation display
    private static readonly Dictionary<string, string> TimezoneAbbreviations = new()
    {
        // US Eastern
        { "America/New_York", "ET" },
        { "America/Detroit", "ET" },
        { "America/Indiana/Indianapolis", "ET" },
        // US Central
        { "America/Chicago", "CT" },
        { "America/Indiana/Knox", "CT" },
        // US Mountain
        { "America/Denver", "MT" },
        { "America/Phoenix", "MST" }, // Arizona doesn't observe DST
        { "America/Boise", "MT" },
        // US Pacific
        { "America/Los_Angeles", "PT" },
        // Alaska
        { "America/Anchorage", "AKT" },
        // Hawaii
        { "Pacific/Honolulu", "HST" },
        // Atlantic
        { "America/Puerto_Rico", "AST" },
        // Pakistan
        { "Asia/Karachi", "PKT" },
        // UK
        { "Europe/London", "GMT" },
        // India
        { "Asia/Kolkata", "IST" },
        // Japan
        { "Asia/Tokyo", "JST" },
        // Australia
        { "Australia/Sydney", "AEST" },
        { "Australia/Melbourne", "AEST" },
    };

    /// <summary>
    /// Default timezone if none is specified (Central Time for Naperville, IL)
    /// </summary>
    public const string DefaultTimeZoneId = "America/Chicago";

    /// <summary>
    /// Gets a TimeZoneInfo object from an IANA timezone identifier.
    /// Handles cross-platform differences between Windows and Linux.
    /// </summary>
    /// <param name="ianaTimeZoneId">IANA timezone identifier (e.g., "America/New_York")</param>
    /// <returns>TimeZoneInfo object or null if not found</returns>
    public static TimeZoneInfo GetTimeZoneInfo(string ianaTimeZoneId)
    {
        if (string.IsNullOrEmpty(ianaTimeZoneId))
            ianaTimeZoneId = DefaultTimeZoneId;

        try
        {
            // Try direct lookup first (works on Linux and .NET 6+)
            return TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                // On Windows, convert IANA to Windows timezone ID
                var windowsTimeZoneId = TZConvert.IanaToWindows(ianaTimeZoneId);
                return TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);
            }
            catch
            {
                // Fallback to UTC
                return TimeZoneInfo.Utc;
            }
        }
    }

    /// <summary>
    /// Converts a UTC DateTime to the specified timezone
    /// </summary>
    /// <param name="utcDateTime">DateTime in UTC</param>
    /// <param name="ianaTimeZoneId">Target IANA timezone identifier</param>
    /// <returns>DateTime converted to the target timezone</returns>
    public static DateTime ConvertFromUtc(DateTime utcDateTime, string ianaTimeZoneId)
    {
        var timeZone = GetTimeZoneInfo(ianaTimeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            timeZone);
    }

    /// <summary>
    /// Converts a local DateTime to UTC for the specified timezone
    /// </summary>
    /// <param name="localDateTime">DateTime in local timezone</param>
    /// <param name="ianaTimeZoneId">Source IANA timezone identifier</param>
    /// <returns>DateTime converted to UTC</returns>
    public static DateTime ConvertToUtc(DateTime localDateTime, string ianaTimeZoneId)
    {
        var timeZone = GetTimeZoneInfo(ianaTimeZoneId);
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified),
            timeZone);
    }

    /// <summary>
    /// Gets the timezone abbreviation for display (e.g., "EST", "PST", "PKT")
    /// </summary>
    /// <param name="ianaTimeZoneId">IANA timezone identifier</param>
    /// <param name="dateTime">DateTime to check for DST (defaults to now)</param>
    /// <returns>Timezone abbreviation string</returns>
    public static string GetTimezoneAbbreviation(string ianaTimeZoneId, DateTime? dateTime = null)
    {
        if (string.IsNullOrEmpty(ianaTimeZoneId))
            ianaTimeZoneId = DefaultTimeZoneId;

        var dt = dateTime ?? DateTime.UtcNow;
        var timeZone = GetTimeZoneInfo(ianaTimeZoneId);

        // Check if we have a predefined abbreviation
        if (TimezoneAbbreviations.TryGetValue(ianaTimeZoneId, out var abbr))
        {
            // For US timezones that observe DST, add S/D suffix
            if (abbr.EndsWith("T") && abbr != "HST" && abbr != "MST")
            {
                var isDst = timeZone.IsDaylightSavingTime(ConvertFromUtc(dt, ianaTimeZoneId));
                return abbr.Replace("T", isDst ? "DT" : "ST");
            }
            return abbr;
        }

        // Fallback: generate from offset
        var offset = timeZone.GetUtcOffset(ConvertFromUtc(dt, ianaTimeZoneId));
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        return $"UTC{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";
    }

    /// <summary>
    /// Formats a UTC DateTime for display in the specified timezone with the timezone abbreviation
    /// </summary>
    /// <param name="utcDateTime">DateTime in UTC</param>
    /// <param name="ianaTimeZoneId">Target IANA timezone identifier</param>
    /// <param name="format">Optional format string (default: "h:mm tt")</param>
    /// <returns>Formatted time string with timezone abbreviation (e.g., "9:00 AM EST")</returns>
    public static string FormatTimeWithTimezone(DateTime utcDateTime, string ianaTimeZoneId, string format = "h:mm tt")
    {
        var localTime = ConvertFromUtc(utcDateTime, ianaTimeZoneId);
        var abbr = GetTimezoneAbbreviation(ianaTimeZoneId, utcDateTime);
        return $"{localTime.ToString(format)} {abbr}";
    }

    /// <summary>
    /// Formats a UTC DateTime for display in the specified timezone with the timezone abbreviation
    /// </summary>
    /// <param name="utcDateTime">DateTime in UTC</param>
    /// <param name="ianaTimeZoneId">Target IANA timezone identifier</param>
    /// <returns>Formatted date string (e.g., "Dec 18, 2025")</returns>
    public static string FormatDateWithTimezone(DateTime utcDateTime, string ianaTimeZoneId)
    {
        var localTime = ConvertFromUtc(utcDateTime, ianaTimeZoneId);
        return localTime.ToString("MMM d, yyyy");
    }

    /// <summary>
    /// Formats a UTC DateTime for full display in the specified timezone
    /// </summary>
    /// <param name="utcDateTime">DateTime in UTC</param>
    /// <param name="ianaTimeZoneId">Target IANA timezone identifier</param>
    /// <returns>Formatted datetime string with timezone (e.g., "Dec 18, 2025 9:00 AM EST")</returns>
    public static string FormatDateTimeWithTimezone(DateTime utcDateTime, string ianaTimeZoneId)
    {
        var localTime = ConvertFromUtc(utcDateTime, ianaTimeZoneId);
        var abbr = GetTimezoneAbbreviation(ianaTimeZoneId, utcDateTime);
        return $"{localTime:MMM d, yyyy h:mm tt} {abbr}";
    }

    /// <summary>
    /// Gets the current UTC offset for a timezone
    /// </summary>
    /// <param name="ianaTimeZoneId">IANA timezone identifier</param>
    /// <returns>Formatted offset string (e.g., "-05:00")</returns>
    public static string GetUtcOffset(string ianaTimeZoneId)
    {
        var timeZone = GetTimeZoneInfo(ianaTimeZoneId);
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        return $"{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";
    }

    /// <summary>
    /// Gets the display name for a timezone
    /// </summary>
    /// <param name="ianaTimeZoneId">IANA timezone identifier</param>
    /// <returns>Display name (e.g., "Eastern Standard Time")</returns>
    public static string GetTimezoneDisplayName(string ianaTimeZoneId)
    {
        var timeZone = GetTimeZoneInfo(ianaTimeZoneId);
        return timeZone.DisplayName;
    }

    /// <summary>
    /// Gets a list of common timezones for dropdown selection
    /// </summary>
    /// <returns>List of timezone options</returns>
    public static List<TimezoneOption> GetCommonTimezones()
    {
        var commonTimezones = new[]
        {
            // US Timezones
            ("America/New_York", "Eastern Time (ET)", "US"),
            ("America/Chicago", "Central Time (CT)", "US"),
            ("America/Denver", "Mountain Time (MT)", "US"),
            ("America/Phoenix", "Arizona Time (MST)", "US"),
            ("America/Los_Angeles", "Pacific Time (PT)", "US"),
            ("America/Anchorage", "Alaska Time (AKT)", "US"),
            ("Pacific/Honolulu", "Hawaii Time (HST)", "US"),
            ("America/Puerto_Rico", "Atlantic Time (AST)", "US"),
            // International
            ("Europe/London", "London (GMT/BST)", "Europe"),
            ("Europe/Paris", "Paris (CET/CEST)", "Europe"),
            ("Europe/Berlin", "Berlin (CET/CEST)", "Europe"),
            ("Asia/Dubai", "Dubai (GST)", "Asia"),
            ("Asia/Karachi", "Pakistan (PKT)", "Asia"),
            ("Asia/Kolkata", "India (IST)", "Asia"),
            ("Asia/Singapore", "Singapore (SGT)", "Asia"),
            ("Asia/Tokyo", "Japan (JST)", "Asia"),
            ("Australia/Sydney", "Sydney (AEST/AEDT)", "Pacific"),
            ("Pacific/Auckland", "New Zealand (NZST/NZDT)", "Pacific"),
        };

        return commonTimezones.Select(tz => new TimezoneOption
        {
            TimeZoneId = tz.Item1,
            DisplayName = tz.Item2,
            Region = tz.Item3,
            Abbreviation = GetTimezoneAbbreviation(tz.Item1),
            UtcOffset = GetUtcOffset(tz.Item1)
        }).ToList();
    }

    /// <summary>
    /// Validates if a timezone ID is valid
    /// </summary>
    /// <param name="ianaTimeZoneId">IANA timezone identifier to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidTimezone(string ianaTimeZoneId)
    {
        if (string.IsNullOrEmpty(ianaTimeZoneId))
            return false;

        try
        {
            var tz = GetTimeZoneInfo(ianaTimeZoneId);
            return tz != null && tz != TimeZoneInfo.Utc;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a TimeOnly (local time) to UTC DateTime for a specific date and timezone
    /// </summary>
    /// <param name="localTime">Local time (e.g., 9:00 AM)</param>
    /// <param name="localDate">Local date</param>
    /// <param name="ianaTimeZoneId">Source timezone</param>
    /// <returns>UTC DateTime</returns>
    public static DateTime TimeOnlyToUtc(TimeOnly localTime, DateOnly localDate, string ianaTimeZoneId)
    {
        var localDateTime = localDate.ToDateTime(localTime);
        return ConvertToUtc(localDateTime, ianaTimeZoneId);
    }

    /// <summary>
    /// Gets the start of day (midnight) in UTC for a given date in a timezone
    /// </summary>
    /// <param name="localDate">Date in local timezone</param>
    /// <param name="ianaTimeZoneId">Timezone</param>
    /// <returns>UTC DateTime representing midnight in the specified timezone</returns>
    public static DateTime GetStartOfDayUtc(DateOnly localDate, string ianaTimeZoneId)
    {
        var localMidnight = localDate.ToDateTime(TimeOnly.MinValue);
        return ConvertToUtc(localMidnight, ianaTimeZoneId);
    }

    /// <summary>
    /// Gets the end of day (23:59:59.999) in UTC for a given date in a timezone
    /// </summary>
    /// <param name="localDate">Date in local timezone</param>
    /// <param name="ianaTimeZoneId">Timezone</param>
    /// <returns>UTC DateTime representing end of day in the specified timezone</returns>
    public static DateTime GetEndOfDayUtc(DateOnly localDate, string ianaTimeZoneId)
    {
        var localEndOfDay = localDate.ToDateTime(new TimeOnly(23, 59, 59, 999));
        return ConvertToUtc(localEndOfDay, ianaTimeZoneId);
    }
}

/// <summary>
/// Timezone option for dropdown selection
/// </summary>
public class TimezoneOption
{
    public string TimeZoneId { get; set; }
    public string DisplayName { get; set; }
    public string Region { get; set; }
    public string Abbreviation { get; set; }
    public string UtcOffset { get; set; }
}
