using System;

namespace EHR.Models.Generated;

public partial class Pharmacy
{
    public int PharmacyId { get; set; }

    /// <summary>
    /// Nullable - shared pharmacies have no tenant
    /// </summary>
    public int? TenantId { get; set; }

    public string Name { get; set; }

    /// <summary>
    /// National Council for Prescription Drug Programs ID
    /// </summary>
    public string NCPDP { get; set; }

    /// <summary>
    /// National Provider Identifier
    /// </summary>
    public string NPI { get; set; }

    public string Address { get; set; }

    public string City { get; set; }

    public string State { get; set; }

    public string Zip { get; set; }

    public string Phone { get; set; }

    public string Fax { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; }
}
