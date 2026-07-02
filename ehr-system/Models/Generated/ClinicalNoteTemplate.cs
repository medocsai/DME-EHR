using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class ClinicalNoteTemplate
{
    public int TemplateId { get; set; }

    public int? TenantId { get; set; }

    /// <summary>
    /// Optional location assignment. If null, template is available for all locations in the tenant.
    /// </summary>
    public int? LocationId { get; set; }

    public string Name { get; set; }

    public string HtmlContent { get; set; }

    public bool IsSystemTemplate { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Location Location { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();
}
