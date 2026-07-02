using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Patient
{
    public int PatientId { get; set; }

    public int TenantId { get; set; }

    public string Mrn { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }

    public DateOnly DateOfBirth { get; set; }

    public string Gender { get; set; }

    public string Phone { get; set; }

    public string Email { get; set; }

    public string Address { get; set; }

    public string City { get; set; }

    public string State { get; set; }

    public string ZipCode { get; set; }

    public string EmergencyContactName { get; set; }

    public string EmergencyContactPhone { get; set; }

    public string EmergencyContactAltPhone { get; set; }

    public string EmergencyContactRelation { get; set; }

    public int? PreferredProviderId { get; set; }

    public int? PreferredLocationId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool? IsDeleted { get; set; }

    /// <summary>
    /// Indicates if patient is archived. Archived patients cannot be edited,
    /// only viewed, deleted, or unarchived.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Encrypted full SSN (AES-256-GCM)
    /// </summary>
    public string SsnEncrypted { get; set; }

    /// <summary>
    /// Hashed last 4 digits of SSN for quick lookup during verification
    /// Uses HMAC-SHA256 for deterministic hashing
    /// </summary>
    public string SsnLast4Hash { get; set; }

    /// <summary>
    /// Date of injury for medical lien and workers' compensation cases.
    /// Used in Medical Lien Form generation.
    /// </summary>
    public DateOnly? DateOfInjury { get; set; }

    /// <summary>
    /// Cloud storage path for the patient's profile picture (GCS).
    /// </summary>
    public string ProfilePicturePath { get; set; }

    /// <summary>
    /// Stripe Customer ID for saved payment methods and installment auto-charging.
    /// </summary>
    public string StripeCustomerId { get; set; }

    /// <summary>
    /// Opaque GUID used for the tablet intake URL (/intake/p/{token}).
    /// Lazy-generated on first clinic-side request, rotated on admin click.
    /// Never contains PatientId; lookup is via this column.
    /// </summary>
    public Guid? IntakePortalToken { get; set; }

    /// <summary>
    /// Intake Section 6: Drug reaction or adverse medication history (free text).
    /// Patient-level because it applies across all intakes.
    /// </summary>
    public string DrugReactionHistory { get; set; }

    /// <summary>
    /// Intake Section 6: Primary pharmacy and phone (single line).
    /// </summary>
    public string PrimaryPharmacyInfo { get; set; }

    /// <summary>
    /// Intake Section 6: Compounding / specialty pharmacy and phone (single line).
    /// </summary>
    public string CompoundingPharmacyInfo { get; set; }

    /// <summary>
    /// Intake Section 6: Current healthcare team (PCP, specialists, naturopath,
    /// chiropractor, therapist, etc.) — free text.
    /// </summary>
    public string HealthcareTeamNotes { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<BillingClaim> BillingClaims { get; set; } = new List<BillingClaim>();

    public virtual ICollection<CareEpisode> CareEpisodes { get; set; } = new List<CareEpisode>();

    public virtual ICollection<Charge> Charges { get; set; } = new List<Charge>();

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();

    public virtual ICollection<Consent> Consents { get; set; } = new List<Consent>();

    public virtual ICollection<CareEpisodeConsent> CareEpisodeConsents { get; set; } = new List<CareEpisodeConsent>();

    public virtual ICollection<Insurance> Insurances { get; set; } = new List<Insurance>();

    // Note: Legacy Notes navigation removed - use ClinicalNotes instead

    public virtual ICollection<PatientLedger> PatientLedgers { get; set; } = new List<PatientLedger>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<InstallmentPlan> InstallmentPlans { get; set; } = new List<InstallmentPlan>();

    public virtual ICollection<PatientDocument> PatientDocuments { get; set; } = new List<PatientDocument>();

    public virtual Location PreferredLocation { get; set; }

    public virtual Provider PreferredProvider { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual PatientValidation PatientValidation { get; set; }

    // Patient Intake (Phase 1)
    public virtual ICollection<PatientHealthConcern> HealthConcerns { get; set; } = new List<PatientHealthConcern>();

    public virtual ICollection<PatientSupplement> PatientSupplements { get; set; } = new List<PatientSupplement>();

    /// <summary>
    /// At most one longevity profile per patient (unique index on PatientId).
    /// </summary>
    public virtual PatientLongevityProfile PatientLongevityProfile { get; set; }

    public virtual ICollection<PatientIntakeSubmission> PatientIntakeSubmissions { get; set; } = new List<PatientIntakeSubmission>();
}
