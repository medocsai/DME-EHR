using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Location
{
    public int LocationId { get; set; }

    public int TenantId { get; set; }

    public string Name { get; set; }

    public string Address { get; set; }

    public string City { get; set; }

    public string State { get; set; }

    public string ZipCode { get; set; }

    public string Phone { get; set; }

    public bool? IsActive { get; set; }

    public bool? IsPrimary { get; set; }

    /// <summary>
    /// IANA timezone identifier for this location (e.g., "America/New_York", "America/Los_Angeles", "Asia/Karachi")
    /// All appointment times are displayed in this timezone for users working at this location.
    /// </summary>
    public string TimeZoneId { get; set; }

    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Random 8-char code for patient portal URL (e.g., "f83dcb21").
    /// URL: /portal/{PortalCode} — non-guessable, unique per location.
    /// </summary>
    public string? PortalCode { get; set; }

    /// <summary>
    /// Per-location feature flag: gates the Longevity section of Patient Intake.
    /// AtomicMedX = true; other clinics = false until requested.
    /// </summary>
    public bool EnableLongevity { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();

    public virtual ICollection<ProviderSchedule> ProviderSchedules { get; set; } = new List<ProviderSchedule>();

    public string PlaceOfServiceCode { get; set; }

    public string FacilityNpi { get; set; }

    /// <summary>
    /// FK to the Stripe Connect account this location uses for payments.
    /// Null = payments are blocked at this location until ClinicAdmin connects an account.
    /// Multiple locations within the same tenant can share the same connected account.
    /// </summary>
    public int? StripeConnectAccountId { get; set; }

    /// <summary>
    /// Clinic-facing online payment fee percentage (e.g., 3.90 = 3.9%).
    /// Null = use global default from appsettings.json (PlatformFees:DefaultOnlineFeePercent).
    /// </summary>
    public decimal? OnlineFeePercent { get; set; }

    /// <summary>
    /// Clinic-facing online payment flat fee in cents (e.g., 50 = $0.50).
    /// Null = use global default from appsettings.json (PlatformFees:DefaultOnlineFeeFlatCents).
    /// </summary>
    public int? OnlineFeeFlatCents { get; set; }

    /// <summary>
    /// Clinic-facing card-present (Tap to Pay) fee percentage. For Phase 2.
    /// Null = use global default.
    /// </summary>
    public decimal? CardPresentFeePercent { get; set; }

    /// <summary>
    /// Clinic-facing card-present flat fee in cents. For Phase 2.
    /// Null = use global default.
    /// </summary>
    public int? CardPresentFeeFlatCents { get; set; }

    public virtual StripeConnectAccount StripeConnectAccount { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual LocationKioskSettings KioskSettings { get; set; }

    public virtual ICollection<ConsentFormTemplate> ConsentFormTemplates { get; set; } = new List<ConsentFormTemplate>();

    public virtual ICollection<CareEpisodeConsent> CareEpisodeConsents { get; set; } = new List<CareEpisodeConsent>();
}
