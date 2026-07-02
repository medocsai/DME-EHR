using System;

namespace EHR.Models.Generated;

public partial class PatientStickyNote
{
    public int PatientStickyNoteId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public string Content { get; set; }

    public int CreatedByUserId { get; set; }

    public string CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }
}
