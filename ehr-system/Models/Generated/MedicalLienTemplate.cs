using System;

namespace EHR.Models.Generated;

/// <summary>
/// Represents a Medical Lien Form template that can be assigned to specific tenants and locations.
/// Templates contain HTML content with placeholders that are replaced with actual data during PDF generation.
/// </summary>
public partial class MedicalLienTemplate
{
    public int TemplateId { get; set; }

    /// <summary>
    /// The tenant (clinic) this template belongs to. Required for tenant-specific templates.
    /// </summary>
    public int TenantId { get; set; }

    /// <summary>
    /// The location within the tenant this template is assigned to. Required for location-specific templates.
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// Display name of the template.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Brief description of the template.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// HTML content of the template with placeholders like {{PatientName}}, {{DateOfInjury}}, etc.
    /// </summary>
    public string HtmlContent { get; set; }

    /// <summary>
    /// Whether this template is currently active and available for use.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// User ID who created this template.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>
    /// When the template was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the template was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }
    public virtual Location Location { get; set; }
}
