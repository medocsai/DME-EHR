using System;

namespace EHR.Models.Generated;

/// <summary>
/// Vitamins and supplements (distinct from prescription medications, which live in PatientMedication).
/// Path A storage: name + free-text notes (brand, dose, frequency, duration).
/// </summary>
public partial class PatientSupplement
{
    public int PatientSupplementId { get; set; }

    public int PatientId { get; set; }

    public int TenantId { get; set; }

    public string SupplementName { get; set; }

    /// <summary>
    /// Free text: brand, dose, frequency, duration.
    /// </summary>
    public string Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Provenance. See IntakeSource enum.
    /// </summary>
    public int Source { get; set; }

    public int? IntakeSubmissionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public int? DeletedByUserId { get; set; }

    public string DeletedReason { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual PatientIntakeSubmission IntakeSubmission { get; set; }

    public virtual User DeletedByUser { get; set; }
}
