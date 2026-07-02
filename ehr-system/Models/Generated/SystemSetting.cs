using System;

namespace EHR.Models.Generated;

/// <summary>
/// System-wide configurable settings for clinic operations
/// Stores key-value pairs for threshold settings and other configuration
/// </summary>
public partial class SystemSetting
{
    public int SystemSettingId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Setting key identifier (e.g., "NoShowAlertThreshold", "LowVisitsThreshold")
    /// </summary>
    public string SettingKey { get; set; } = string.Empty;

    /// <summary>
    /// Setting value (stored as string, converted to appropriate type in code)
    /// </summary>
    public string SettingValue { get; set; } = string.Empty;

    /// <summary>
    /// Data type for proper parsing (int, decimal, bool, string, json)
    /// </summary>
    public string DataType { get; set; } = "string";

    /// <summary>
    /// Human-readable description of the setting
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Category for grouping settings in UI
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Default value if not set
    /// </summary>
    public string DefaultValue { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Tenant Tenant { get; set; }
}

/// <summary>
/// Predefined setting keys for consistency
/// </summary>
public static class SettingKeys
{
    // Care Episode Settings
    public const string NoShowAlertThresholdMinutes = "NoShowAlertThresholdMinutes";
    public const string LowVisitsRemainingThreshold = "LowVisitsRemainingThreshold";

    // Appointment Settings
    public const string DefaultAppointmentDuration = "DefaultAppointmentDuration";
    public const string MissedVisitGracePeriodHours = "MissedVisitGracePeriodHours";

    // Clinical Notes Settings
    public const string AutoCheckInOnNoteCreation = "AutoCheckInOnNoteCreation";

    // Appointment Reminder Settings
    public const string AppointmentRemindersEnabled = "AppointmentRemindersEnabled";
    public const string Reminder24hEnabled = "Reminder24hEnabled";
    public const string Reminder1hEnabled = "Reminder1hEnabled";
    public const string ReminderCheckIntervalMinutes = "ReminderCheckIntervalMinutes";
}
