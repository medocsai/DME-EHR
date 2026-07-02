using System;

namespace EHR.Models.Generated;

/// <summary>
/// Represents a document/attachment associated with a patient.
/// HIPAA-compliant document storage with encryption and audit tracking.
/// </summary>
public partial class PatientDocument
{
    public int DocumentId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    /// <summary>
    /// Original filename (encrypted in database)
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Secure storage filename (GUID-based, no PHI)
    /// </summary>
    public string StorageFileName { get; set; }

    /// <summary>
    /// MIME type of the file
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Document category: 0=InsuranceCard, 1=ID, 2=Referral, 3=MedicalRecord, 4=Consent, 5=Other
    /// </summary>
    public int Category { get; set; }

    /// <summary>
    /// Optional description or notes about the document
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Whether the document is encrypted at rest
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Hash of the file content for integrity verification
    /// </summary>
    public string FileHash { get; set; }

    /// <summary>
    /// User ID who uploaded the document
    /// </summary>
    public int? UploadedByUserId { get; set; }

    /// <summary>
    /// Whether this document was uploaded by the patient via the portal
    /// </summary>
    public bool IsPatientUploaded { get; set; }

    /// <summary>
    /// Soft delete flag
    /// </summary>
    public bool? IsDeleted { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }

    public virtual User UploadedByUser { get; set; }
}
