using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Order
{
    public int OrderId { get; set; }
    public int TenantId { get; set; }
    public int PatientId { get; set; }
    public int ProviderId { get; set; }
    public int? EncounterId { get; set; }

    /// <summary>0=Lab, 1=Imaging, 2=Referral</summary>
    public int OrderType { get; set; }

    /// <summary>0=Draft, 1=Pending, 2=Sent, 3=InProgress, 4=ResultsReceived, 5=Completed, 6=Cancelled</summary>
    public int Status { get; set; }

    /// <summary>0=Routine, 1=Urgent, 2=STAT</summary>
    public int Priority { get; set; }

    public DateOnly OrderDate { get; set; }

    public string DiagnosisCode { get; set; }
    public string ClinicalIndication { get; set; }
    public string Notes { get; set; }

    // --- Lab-specific fields ---
    public string LabPanelName { get; set; }
    public bool? FastingRequired { get; set; }
    public string SpecimenType { get; set; }

    // --- Imaging-specific fields ---
    public int? Modality { get; set; }
    public string BodyPart { get; set; }
    public bool? ContrastRequired { get; set; }
    public string ImagingFacility { get; set; }

    // --- Referral-specific fields ---
    public string ReferralSpecialty { get; set; }
    public string ReferredToProvider { get; set; }
    public string ReferredToFacility { get; set; }
    public string ReferredToPhone { get; set; }
    public string ReferredToFax { get; set; }
    public string ReferralReason { get; set; }
    public int? ReferralUrgency { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public virtual Patient Patient { get; set; }
    public virtual Provider Provider { get; set; }
    public virtual Encounter Encounter { get; set; }
    public virtual Tenant Tenant { get; set; }
    public virtual ICollection<OrderResult> Results { get; set; } = new List<OrderResult>();
}
