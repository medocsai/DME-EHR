using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Consent record for a care episode or appointment.
/// CareEpisodeId can be null when consent is signed before care episode exists
/// (e.g., Initial Evaluation appointments).
/// </summary>
public partial class CareEpisodeConsent
{
    public int CareEpisodeConsentId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Care Episode ID - CAN BE NULL for initial consents before care episode exists.
    /// Updated when Care Episode is created and appointment is linked.
    /// </summary>
    public int? CareEpisodeId { get; set; }

    /// <summary>
    /// Patient ID - Required for all queries
    /// </summary>
    public int PatientId { get; set; }

    /// <summary>
    /// Appointment ID - CAN BE NULL for manual uploads without an appointment.
    /// For kiosk consents, this links to the appointment being consented.
    /// </summary>
    public int? AppointmentId { get; set; }

    /// <summary>
    /// Location where consent was signed
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// Consent type: 0 = NewCareEpisode, 1 = ReturningVisit
    /// </summary>
    public int ConsentType { get; set; }

    /// <summary>
    /// When consent was signed
    /// </summary>
    public DateTime SignedAt { get; set; }

    /// <summary>
    /// Encrypted PDF of completed consent (Base64)
    /// </summary>
    public string EncryptedPdfData { get; set; }

    /// <summary>
    /// File hash for integrity verification
    /// </summary>
    public string PdfHash { get; set; }

    /// <summary>
    /// Encrypted last 4 SSN used for verification
    /// </summary>
    public string VerificationSsnLast4Encrypted { get; set; }

    /// <summary>
    /// DOB used for verification
    /// </summary>
    public DateOnly VerificationDob { get; set; }

    /// <summary>
    /// ZIP code used for verification
    /// </summary>
    public string VerificationZipCode { get; set; }

    /// <summary>
    /// IP address for audit
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    /// User agent for audit
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// Number of forms signed
    /// </summary>
    public int FormCount { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }

    public virtual CareEpisode CareEpisode { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual Location Location { get; set; }

    public virtual ICollection<CareEpisodeConsentForm> CareEpisodeConsentForms { get; set; } = new List<CareEpisodeConsentForm>();
}
