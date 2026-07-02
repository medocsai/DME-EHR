using System;

namespace EHR.Models.Generated;

/// <summary>
/// Longevity intake Section 7 data. One row per patient (unique on PatientId).
/// JSON columns store flexible rating/goal structures per the rules doc.
/// </summary>
public partial class PatientLongevityProfile
{
    public int PatientLongevityProfileId { get; set; }

    public int PatientId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// JSON object, e.g. { "brainFog": 7, "fatigue": 8 }. Scale 0-10 or null.
    /// </summary>
    public string SymptomRatings { get; set; }

    /// <summary>
    /// JSON array, e.g. ["extendLifespan","cancerPrevention"].
    /// </summary>
    public string Goals { get; set; }

    /// <summary>
    /// JSON array, e.g. ["23andMe","DexaScan"].
    /// </summary>
    public string PriorTesting { get; set; }

    /// <summary>
    /// JSON array, e.g. ["Metformin","NMN"].
    /// </summary>
    public string CurrentInterventions { get; set; }

    /// <summary>
    /// JSON object, e.g. { "mold": true, "heavyMetals": false }.
    /// </summary>
    public string ToxinExposure { get; set; }

    public string BiomarkerGoals { get; set; }

    public string OptimalHealthVision { get; set; }

    /// <summary>Date of last comprehensive blood panel (from PDF Section 4).</summary>
    public DateOnly? LastBloodPanelDate { get; set; }

    /// <summary>Date of last full physical exam (from PDF Section 4).</summary>
    public DateOnly? LastPhysicalDate { get; set; }

    /// <summary>
    /// Provenance. See IntakeSource enum.
    /// </summary>
    public int Source { get; set; }

    public int? IntakeSubmissionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual PatientIntakeSubmission IntakeSubmission { get; set; }
}
