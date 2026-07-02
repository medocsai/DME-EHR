using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class PatientLedger
{
    public int LedgerId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int EntryType { get; set; }

    public int? ChargeId { get; set; }

    public int? PaymentId { get; set; }

    public int? ClaimId { get; set; }

    public DateOnly TransactionDate { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Charge Charge { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Payment Payment { get; set; }

    public virtual Tenant Tenant { get; set; }
}
