// ============================================================================
// Enums for MEDOCS DME. These match database INT columns.
//
// Trimmed 2026-08-25 from 48 enums to 3, when the clinical EHR this product was
// copied from was removed. The other 45 described appointments, encounters,
// prescriptions, claims, care episodes and intake: concepts that no longer exist
// here. Every enum below is referenced by live code.
//
// DME's own state values (order status, rental status, claim status) are
// deliberately NOT here. They live as strings in the DME tables and are rendered
// through F.StatusChip, because they are read and written by raw SQL in DmeDb
// rather than by EF, and a C# enum that nothing maps to would be a second
// definition of the same fact.
// ============================================================================

namespace EHR.Models
{
    /// <summary>
    /// Tenants.Status. Used by TenantService when listing and activating clinics.
    /// </summary>
    public enum TenantStatus
    {
        Pending = 0,
        Active = 1,
        Suspended = 2,
        Cancelled = 3
    }

    /// <summary>
    /// Tenants.Plan. Drives the MaxUsers limit set at tenant creation.
    /// </summary>
    public enum SubscriptionPlan
    {
        Trial = 0,
        Basic = 1,
        Professional = 2,
        Enterprise = 3
    }

    /// <summary>
    /// Users.Role, and the value behind every [Authorize(Roles = "...")] in the
    /// product. SuperAdmin(0) manages tenants; ClinicAdmin(1) manages one
    /// tenant's users. The remaining values are inherited from the clinical
    /// product and are kept because existing user rows still carry them: a user
    /// created as Biller(4) must keep working, and renumbering live accounts to
    /// tidy the list would silently change what people can do.
    /// </summary>
    public enum UserRole
    {
        SuperAdmin = 0,
        ClinicAdmin = 1,
        Clinician = 2,
        FrontDesk = 3,
        Biller = 4,
        ReadOnly = 5,
        MedicalAssistant = 6,
        Nurse = 7,
        Patient = 8
    }
}
