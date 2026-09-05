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
        // The five a DME supplier actually has. Clinician, Front Desk, Read
        // Only, Medical Assistant, Nurse and Patient came across with the
        // clinical fork and describe a clinic, not an equipment supplier.
        //
        // The NUMBERS are deliberately unchanged. Twenty eight [Authorize]
        // attributes name them and twenty eight live accounts carry them, so
        // renumbering to tidy the list would silently change what people can do.
        // Only the words change; 2 and 3 keep their meaning in the code.
        SuperAdmin = 0,   // Medocs, across every supplier
        Admin = 1,        // the supplier's own owner
        Intake = 2,       // customers, orders, insurance. Not money.
        Delivery = 3,     // deliveries, proof of delivery, stock. Not billing.
        Biller = 4        // claims, payments, denials. Not stock.
    }
}
