using System;

namespace EHR.Models.Generated;

public partial class LabTestCatalog
{
    public int LabTestId { get; set; }

    /// <summary>Panel group name (e.g., "CBC", "BMP"). Null for individual tests.</summary>
    public string PanelName { get; set; }

    public string TestName { get; set; }
    public string TestCode { get; set; }
    public string Unit { get; set; }
    public string ReferenceRange { get; set; }
    public string SpecimenType { get; set; }
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
