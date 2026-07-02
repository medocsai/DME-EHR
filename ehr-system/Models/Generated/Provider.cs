using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Provider
{
    public int ProviderId { get; set; }

    public int TenantId { get; set; }

    public string Npi { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }

    public string Credentials { get; set; }

    public string Specialty { get; set; }

    public string Taxonomy { get; set; }

    public string Email { get; set; }

    public string Phone { get; set; }

    public string Color { get; set; }

    public int? CredentialStatus { get; set; }

    public DateTime? CredentialExpiry { get; set; }

    public DateTime? LicenseExpiry { get; set; }

    public string LicenseNumber { get; set; }

    public string LicenseState { get; set; }

    public bool? IsActive { get; set; }

    public int? DefaultAppointmentDuration { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string SignatureImagePath { get; set; }

    /// <summary>
    /// Cloud storage path for the provider's profile picture (GCS).
    /// </summary>
    public string ProfilePicturePath { get; set; }

    public bool ShowResumePopup { get; set; } = false;

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<CareEpisode> CareEpisodes { get; set; } = new List<CareEpisode>();

    public virtual ICollection<Charge> Charges { get; set; } = new List<Charge>();

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<CredentialingRecord> CredentialingRecords { get; set; } = new List<CredentialingRecord>();

    // Note: Legacy Notes navigation removed - use ClinicalNotes instead

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();

    public virtual ICollection<ProviderSchedule> ProviderSchedules { get; set; } = new List<ProviderSchedule>();

    public virtual Tenant Tenant { get; set; }

    public virtual ICollection<TherapistUnavailability> TherapistUnavailabilities { get; set; } = new List<TherapistUnavailability>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
