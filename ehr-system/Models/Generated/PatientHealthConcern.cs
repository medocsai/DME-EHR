using System;

namespace EHR.Models.Generated;

/// <summary>
/// Patient-reported chief complaint / priority concern, captured in intake Section 2.
/// Ranked 1..5 by the patient; max 5 per patient (enforced in service, not SQL).
/// </summary>
public partial class PatientHealthConcern
{
    public int PatientHealthConcernId { get; set; }

    public int PatientId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Patient's ranking: 1 = highest priority.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Short name of the concern (e.g. "Chronic fatigue").
    /// </summary>
    public string Concern { get; set; }

    /// <summary>
    /// Free text: onset, frequency, severity, better/worse factors.
    /// </summary>
    public string Details { get; set; }

    /// <summary>
    /// 0=mild, 1=moderate, 2=severe. Nullable (patient may skip).
    /// </summary>
    public int? Severity { get; set; }

    /// <summary>
    /// Provenance. See IntakeSource enum: 0=System, 1=Clinic, 2=Patient, 3=Kiosk, 4=Import.
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
