using System;

namespace EHR.Models.Generated;

/// <summary>
/// Reference drug database - shared across all tenants.
/// Contains common medications for drug search during prescribing.
/// </summary>
public partial class DrugDatabase
{
    public int DrugId { get; set; }

    /// <summary>
    /// National Drug Code
    /// </summary>
    public string NDCCode { get; set; }

    public string BrandName { get; set; }

    public string GenericName { get; set; }

    /// <summary>
    /// e.g., "10mg", "500mg", "250mg/5ml"
    /// </summary>
    public string Strength { get; set; }

    /// <summary>
    /// 0=Tablet, 1=Capsule, 2=Liquid, etc. See DosageForm enum
    /// </summary>
    public int DosageForm { get; set; }

    /// <summary>
    /// 0=Oral, 1=Topical, etc. See MedicationRoute enum
    /// </summary>
    public int Route { get; set; }

    /// <summary>
    /// DEA Schedule (2-5) for controlled substances, null if not controlled
    /// </summary>
    public int? DEASchedule { get; set; }

    /// <summary>
    /// Default SIG directions, e.g., "Take 1 tablet by mouth once daily"
    /// </summary>
    public string CommonDirections { get; set; }

    /// <summary>
    /// Important warnings and precautions
    /// </summary>
    public string Warnings { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
