using System;

namespace EHR.Models.Generated;

/// <summary>
/// Per-user favorite billing code (ICD-10 or CPT) starred from the Dx & CPT step.
/// Strict per-user isolation — no clinic-wide visibility. The owning UserId is
/// the source of truth; queries MUST always filter by current user's UserId.
///
/// Description is captured at favorite time so that edits to the central
/// Icdcodes / Cptcodes table do not silently change what the provider sees.
///
/// Units are intentionally NOT stored — when "Add" is clicked on a CPT favorite,
/// the code is added to Selected Codes with units=1 and the provider sets the
/// real units inline on the visit row.
/// </summary>
public partial class ProviderFavoriteCode
{
    public int Id { get; set; }

    /// <summary>FK to Users.UserId — the owning provider/clinician.</summary>
    public int UserId { get; set; }

    /// <summary>Code family. One of: "ICD10", "CPT".</summary>
    public string CodeType { get; set; } = null!;

    /// <summary>The billing code itself (e.g. "E11.65", "99214").</summary>
    public string Code { get; set; } = null!;

    /// <summary>Description captured at favorite time.</summary>
    public string Description { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
}
