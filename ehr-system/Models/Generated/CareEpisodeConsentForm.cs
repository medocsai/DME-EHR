using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Individual consent form within a consent record.
/// Stores the rendered HTML and captured signatures.
/// </summary>
public partial class CareEpisodeConsentForm
{
    public int CareEpisodeConsentFormId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Reference to the parent consent record
    /// </summary>
    public int CareEpisodeConsentId { get; set; }

    /// <summary>
    /// Reference to the template used. NULL for manual uploads without a template.
    /// </summary>
    public int? ConsentFormTemplateId { get; set; }

    /// <summary>
    /// Template version at time of signing
    /// </summary>
    public int TemplateVersion { get; set; }

    /// <summary>
    /// Form name at time of signing (snapshot)
    /// </summary>
    public string FormName { get; set; }

    /// <summary>
    /// Rendered HTML content with patient data filled in (encrypted)
    /// </summary>
    public string RenderedHtmlContent { get; set; }

    /// <summary>
    /// JSON of all signatures captured
    /// Format: [{ "fieldId": "patient_sig", "imageData": "data:image/png;base64,...", "timestamp": "2024-01-01T10:00:00Z" }, ...]
    /// </summary>
    public string SignaturesJson { get; set; }

    /// <summary>
    /// When the form was first viewed
    /// </summary>
    public DateTime? ViewedAt { get; set; }

    /// <summary>
    /// How long the patient viewed the form (seconds)
    /// </summary>
    public int? ViewDurationSeconds { get; set; }

    /// <summary>
    /// Display order within the consent
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// When the form was signed
    /// </summary>
    public DateTime SignedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }

    public virtual CareEpisodeConsent CareEpisodeConsent { get; set; }

    public virtual ConsentFormTemplate ConsentFormTemplate { get; set; }
}
