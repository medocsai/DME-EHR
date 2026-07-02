namespace EHR.Models.Generated;

/// <summary>
/// Stores blind index tokens for HIPAA-compliant patient search.
/// Tokens are HMAC hashes of prefix substrings of searchable PHI fields.
/// This allows efficient database-level filtering without exposing PHI.
/// </summary>
public class PatientSearchToken
{
    public int PatientSearchTokenId { get; set; }

    /// <summary>
    /// The patient this token belongs to.
    /// </summary>
    public int PatientId { get; set; }

    /// <summary>
    /// The tenant for multi-tenant isolation.
    /// </summary>
    public int TenantId { get; set; }

    /// <summary>
    /// The patient's preferred location for location-based filtering.
    /// Nullable for patients without a preferred location.
    /// </summary>
    public int? LocationId { get; set; }

    /// <summary>
    /// The HMAC-SHA256 hash of the search prefix.
    /// Example: HMAC("joh") for searching "John"
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The type of field this token represents.
    /// Values: FirstName, LastName, MRN, Phone, Email, FullName
    /// </summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>
    /// The length of the original prefix (for debugging/analytics).
    /// </summary>
    public int PrefixLength { get; set; }

    // Navigation properties
    public virtual Patient Patient { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual Location? Location { get; set; }
}
