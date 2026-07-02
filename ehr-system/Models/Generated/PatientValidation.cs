using System;

namespace EHR.Models.Generated;

/// <summary>
/// Stores validation status for patient profiles.
/// Tracks whether a patient's record is complete and what fields are missing.
/// </summary>
public partial class PatientValidation
{
    public int PatientValidationId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    /// <summary>
    /// True if all required fields are present
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Count of missing required fields
    /// </summary>
    public int MissingFieldsCount { get; set; }

    /// <summary>
    /// JSON array of missing field names for display
    /// e.g., ["Emergency Contact", "Insurance"]
    /// </summary>
    public string MissingSections { get; set; }

    /// <summary>
    /// JSON object with detailed missing fields by section
    /// e.g., {"BasicInfo": ["Email"], "EmergencyContact": ["Name", "Phone", "Relationship"]}
    /// </summary>
    public string MissingFieldsDetails { get; set; }

    /// <summary>
    /// The first section with missing information (for click-to-fix navigation)
    /// Values: BasicInfo, Address, EmergencyContact, Insurance
    /// </summary>
    public string FirstIncompleteSection { get; set; }

    /// <summary>
    /// When the validation was last run
    /// </summary>
    public DateTime? LastValidatedAt { get; set; }

    /// <summary>
    /// User ID who triggered the validation (null for scheduled runs)
    /// </summary>
    public int? ValidatedByUserId { get; set; }

    /// <summary>
    /// Source of the validation trigger
    /// Values: Create, Update, Scheduled, Manual
    /// </summary>
    public string ValidationSource { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }
}
