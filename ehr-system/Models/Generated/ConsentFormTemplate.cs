using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Consent form template with HTML content and placeholders.
/// Templates are created by admin and rendered with patient data when signing.
/// </summary>
public partial class ConsentFormTemplate
{
    public int ConsentFormTemplateId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Optional: Location-specific template (null means tenant-wide)
    /// </summary>
    public int? LocationId { get; set; }

    /// <summary>
    /// Template name (e.g., "HIPAA Privacy Notice", "Treatment Consent")
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Form type: 0 = NewCareEpisode, 1 = ReturningVisit
    /// </summary>
    public int FormType { get; set; }

    /// <summary>
    /// Optional description for admin reference
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// HTML template content with placeholders
    /// </summary>
    public string HtmlContent { get; set; }

    /// <summary>
    /// Display order (lower numbers first)
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this template is active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Version number (incremented on each edit)
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Soft delete flag
    /// </summary>
    public bool IsDeleted { get; set; }

    public int CreatedByUserId { get; set; }

    public int? UpdatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }

    public virtual Location Location { get; set; }

    public virtual User CreatedByUser { get; set; }

    public virtual User UpdatedByUser { get; set; }

    public virtual ICollection<CareEpisodeConsentForm> CareEpisodeConsentForms { get; set; } = new List<CareEpisodeConsentForm>();
}

/// <summary>
/// Consent form type enumeration
/// </summary>
public enum ConsentFormType
{
    NewCareEpisode = 0,
    ReturningVisit = 1
}
