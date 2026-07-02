using System;

namespace EHR.Models.Generated;

/// <summary>
/// Represents an authorization record for insurance coverage.
/// Each insurance can have multiple authorization records over time,
/// preserving historical authorization data.
/// </summary>
public partial class Authorization
{
    public int AuthorizationId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// Foreign key to the Insurance record this authorization belongs to
    /// </summary>
    public int InsuranceId { get; set; }

    /// <summary>
    /// Unique authorization number provided by the insurance company
    /// </summary>
    public string AuthorizationNumber { get; set; }

    /// <summary>
    /// Date when this authorization expires
    /// </summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>
    /// Total number of visits authorized by the insurance company
    /// </summary>
    public int AuthorizedVisits { get; set; }

    /// <summary>
    /// Timestamp when this authorization was validated/obtained from the insurance company
    /// </summary>
    public DateTime DateOfValidation { get; set; }

    /// <summary>
    /// Optional notes about this authorization
    /// </summary>
    public string Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Insurance Insurance { get; set; }

    public virtual Tenant Tenant { get; set; }
}
