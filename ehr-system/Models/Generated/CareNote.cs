using System;

namespace EHR.Models.Generated;

/// <summary>
/// Care Note — structured staff-to-provider communication on a patient.
///
/// Why it exists:
///   Quick yellow PatientStickyNotes are too informal for clinical relays
///   ("patient called about BP", "pharmacy needs PA"), and ClinicalNote is
///   too heavy. Care Notes sit in between: documented, dated, signed by
///   author, optionally addressed to a specific provider for notification.
///
/// Who calls it:
///   - CareNoteService (read/write/edit/delete/mark-seen)
///   - CareNotesController (HTTP surface)
///   - CareNotesModule.js (top-nav bell + patient profile tab)
///
/// PHI:
///   Content carries clinical text. Encrypted at rest via
///   EncryptionConfiguration -> EncryptionHelper.EncryptEntity / DecryptEntity.
///   Never expose Content without calling DecryptEntity first.
/// </summary>
public partial class CareNote
{
    public int CareNoteId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    /// <summary>The note body. PHI -- encrypted at rest.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, the named provider sees this in the top-nav bell
    /// until they open the patient's Care Notes tab. NULL = general note,
    /// no notification.
    /// </summary>
    public int? ForProviderId { get; set; }

    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public bool IsEdited { get; set; }
    public int? EditedByUserId { get; set; }
    public string? EditedByName { get; set; }
    public DateTime? EditedAt { get; set; }

    public int? SeenByProviderId { get; set; }
    public DateTime? SeenAt { get; set; }

    public bool IsDeleted { get; set; }
    public int? DeletedByUserId { get; set; }
    public string? DeletedByName { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual Patient? Patient { get; set; }
    public virtual Tenant? Tenant { get; set; }
}
