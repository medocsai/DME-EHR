using System;

namespace EHR.Models.Generated;

public partial class PatientVital
{
    public int PatientVitalId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int? EncounterId { get; set; }

    public DateTime RecordedAt { get; set; }

    public int RecordedByUserId { get; set; }

    /// <summary>
    /// Systolic blood pressure (mmHg)
    /// </summary>
    public int? SystolicBp { get; set; }

    /// <summary>
    /// Diastolic blood pressure (mmHg)
    /// </summary>
    public int? DiastolicBp { get; set; }

    /// <summary>
    /// Heart rate (beats per minute)
    /// </summary>
    public int? HeartRate { get; set; }

    /// <summary>
    /// Respiratory rate (breaths per minute)
    /// </summary>
    public int? RespiratoryRate { get; set; }

    /// <summary>
    /// Temperature in Fahrenheit
    /// </summary>
    public decimal? Temperature { get; set; }

    /// <summary>
    /// Oxygen saturation (%)
    /// </summary>
    public decimal? SpO2 { get; set; }

    /// <summary>
    /// Weight in pounds
    /// </summary>
    public decimal? Weight { get; set; }

    /// <summary>
    /// Height in inches
    /// </summary>
    public decimal? Height { get; set; }

    /// <summary>
    /// Body Mass Index (calculated)
    /// </summary>
    public decimal? Bmi { get; set; }

    public string Notes { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Encounter Encounter { get; set; }

    public virtual Tenant Tenant { get; set; }
}
