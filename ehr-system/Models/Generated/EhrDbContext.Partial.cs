using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EHR.Services;

namespace EHR.Models.Generated;

/// <summary>
/// Hand-written partial extending the auto-generated EhrDbContext.
/// Lives in this folder alongside the generated file so the namespace and
/// partial-class wiring stays trivially correct, but is kept regen-safe by
/// living in a separate file the scaffolder won't overwrite.
///
/// Spec: rules/technical/history-review-soft-delete.md (sections 5 and 16).
///
/// Adds EF Core global query filters so soft-deleted rows in History Review
/// tables are auto-excluded from every read. Code that genuinely needs to see
/// deleted rows (admin "show deleted" views, audit recovery) must call
/// .IgnoreQueryFilters() explicitly.
/// </summary>
public partial class EhrDbContext
{
    /// <summary>
    /// Tenant ID used by the global query filters below. Resolved lazily from
    /// the request-scoped ITenantProvider so the same DbContext instance can be
    /// used across the request lifetime; EF compiles this property reference
    /// into the filter expression and re-evaluates per query.
    ///
    /// When NULL (no tenant resolved — pre-login flows, Super Admin, kiosk
    /// pre-verify), the filter degrades to "allow all rows" so existing
    /// cross-tenant flows (login email lookup, patient portal verification,
    /// Super Admin reports) keep working. Code paths that should NEVER bleed
    /// across tenants and need a hard guarantee should still call
    /// .IgnoreQueryFilters() and apply explicit .Where(x => x.TenantId == ...).
    /// </summary>
    private int? CurrentTenantId
    {
        get
        {
            try { return this.GetService<ITenantProvider>()?.TenantId; }
            catch { return null; }
        }
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // History Review soft-delete filters
        modelBuilder.Entity<PatientAllergy>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<PatientMedication>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<PatientProblem>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<PatientFamilyHistory>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<PatientSocialHistory>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<PatientImmunization>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<PatientSupplement>().HasQueryFilter(x => !x.IsDeleted);

        // Tenant isolation — every query on these entities is auto-scoped to
        // the request tenant. Defense in depth against developer mistakes
        // (forgotten WHERE TenantId = X). Code paths that genuinely need
        // cross-tenant access must call .IgnoreQueryFilters() explicitly.
        //
        // Filters use "CurrentTenantId == null || row.TenantId == CurrentTenantId"
        // so unresolved-tenant requests (login lookup, Super Admin) fall back
        // to the previous unfiltered behavior — no breakage of existing flows.
        modelBuilder.Entity<ClinicalNote>().HasQueryFilter(
            x => CurrentTenantId == null || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Appointment>().HasQueryFilter(
            x => CurrentTenantId == null || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Encounter>().HasQueryFilter(
            x => CurrentTenantId == null || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Insurance>().HasQueryFilter(
            x => CurrentTenantId == null || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Prescription>().HasQueryFilter(
            x => CurrentTenantId == null || x.TenantId == CurrentTenantId);
    }
}
