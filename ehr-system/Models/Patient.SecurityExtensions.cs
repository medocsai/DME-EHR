namespace EHR.Models.Generated;

/// <summary>
/// Security-specific columns added outside the auto-generated Patient.cs so a
/// future EF scaffolding rerun does not wipe them. Runs as a partial class
/// extension. See migration_security_phase_h_d.sql for the corresponding SQL
/// schema additions.
/// </summary>
public partial class Patient
{
    /// <summary>
    /// When the IntakePortalToken stops being accepted by IntakeAccessTokenService.
    /// Tablet intake links are typically sent via SMS/email; setting an expiry
    /// (default 30 days from issuance) limits the blast radius of a leaked URL.
    /// Null means "never expires" — only used for legacy rows pre-migration.
    /// </summary>
    public DateTime? IntakePortalTokenExpiresAt { get; set; }
}
