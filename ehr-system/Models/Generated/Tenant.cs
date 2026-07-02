using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Tenant
{
    public int TenantId { get; set; }

    public string Name { get; set; }

    public string Subdomain { get; set; }

    public string LogoUrl { get; set; }

    public string Phone { get; set; }

    public string Email { get; set; }

    public string Address { get; set; }

    public string City { get; set; }

    public string State { get; set; }

    public string ZipCode { get; set; }

    public string TaxId { get; set; }

    public string Npi { get; set; }

    public int? Plan { get; set; }

    public int? Status { get; set; }

    public DateTime? SubscriptionStartDate { get; set; }

    public DateTime? SubscriptionEndDate { get; set; }

    public int? MaxUsers { get; set; }

    public int? MaxPatients { get; set; }

    public string Settings { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool? IsDeleted { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<BillingClaim> BillingClaims { get; set; } = new List<BillingClaim>();

    public virtual ICollection<CareEpisode> CareEpisodes { get; set; } = new List<CareEpisode>();

    public virtual ICollection<Charge> Charges { get; set; } = new List<Charge>();

    public virtual ICollection<ClaimStatusHistory> ClaimStatusHistories { get; set; } = new List<ClaimStatusHistory>();

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<Consent> Consents { get; set; } = new List<Consent>();

    public virtual ICollection<CredentialingRecord> CredentialingRecords { get; set; } = new List<CredentialingRecord>();

    public virtual ICollection<Insurance> Insurances { get; set; } = new List<Insurance>();

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();

    // Note: Legacy Notes navigation removed - use ClinicalNotes instead

    public virtual ICollection<PatientLedger> PatientLedgers { get; set; } = new List<PatientLedger>();

    public virtual ICollection<Patient> Patients { get; set; } = new List<Patient>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<ProviderSchedule> ProviderSchedules { get; set; } = new List<ProviderSchedule>();

    public virtual ICollection<Provider> Providers { get; set; } = new List<Provider>();

    public virtual ICollection<TherapistUnavailability> TherapistUnavailabilities { get; set; } = new List<TherapistUnavailability>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();

    public virtual ICollection<StripeConnectAccount> StripeConnectAccounts { get; set; } = new List<StripeConnectAccount>();
}
