using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Appointment
{
    public int AppointmentId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int ProviderId { get; set; }

    public int? LocationId { get; set; }

    public int? CareEpisodeId { get; set; }

    public int Type { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public int? Status { get; set; }

    public string Reason { get; set; }

    public string Notes { get; set; }

    public bool? IsRecurring { get; set; }

    public int? RecurrenceParentId { get; set; }

    public string RecurrencePattern { get; set; }

    public DateTime? CheckInTime { get; set; }

    public DateTime? CheckOutTime { get; set; }

    public bool? IsTelehealth { get; set; }

    public string TelehealthUrl { get; set; }

    /// <summary>
    /// Secure token for patient telehealth join link (GUID-based)
    /// </summary>
    public string TelehealthToken { get; set; }

    public decimal? CopayDue { get; set; }

    public decimal? CopayCollected { get; set; }

    public bool? InsuranceVerified { get; set; }

    public DateTime? ReminderSentAt { get; set; }

    /// <summary>
    /// Whether to send a 24-hour reminder before the appointment
    /// </summary>
    public bool Reminder24hEnabled { get; set; } = true;

    /// <summary>
    /// Whether to send a 1-hour reminder before the appointment
    /// </summary>
    public bool Reminder1hEnabled { get; set; } = true;

    /// <summary>
    /// When the 24-hour reminder was sent (null if not sent)
    /// </summary>
    public DateTime? Reminder24hSentAt { get; set; }

    /// <summary>
    /// When the 1-hour reminder was sent (null if not sent)
    /// </summary>
    public DateTime? Reminder1hSentAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// User ID of the staff member who created this appointment (null for patient-booked)
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>
    /// Patient ID when appointment was booked via patient portal (null for staff-created)
    /// </summary>
    public int? CreatedByPatientId { get; set; }

    // Cancellation tracking fields
    public string CancellationReason { get; set; }

    public int? CancelledByUserId { get; set; }

    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// For missed appointments: points to the new rescheduled appointment
    /// </summary>
    public int? RescheduledToAppointmentId { get; set; }

    /// <summary>
    /// For rescheduled appointments: points to the original missed appointment
    /// </summary>
    public int? RescheduledFromAppointmentId { get; set; }

    public virtual CareEpisode CareEpisode { get; set; }

    public virtual ICollection<Charge> Charges { get; set; } = new List<Charge>();

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual Location Location { get; set; }

    // Note: Legacy NotesNavigation removed - use ClinicalNotes instead

    public virtual Patient Patient { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual ICollection<CareEpisodeConsent> CareEpisodeConsents { get; set; } = new List<CareEpisodeConsent>();
}
