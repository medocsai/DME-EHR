using EHR.Configuration;
using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EHR.Services.DemoData;

/// <summary>
/// Orchestrates demo data generation across all generators.
/// Idempotent: checks if demo data already exists before seeding.
/// </summary>
public class DemoDataSeeder
{
    private readonly EhrDbContext _db;
    private readonly EncryptionHelper _enc;
    private readonly IBlindIndexService _blindIndex;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(EhrDbContext db, EncryptionHelper enc, IBlindIndexService blindIndex, ILogger<DemoDataSeeder> logger)
    {
        _db = db;
        _enc = enc;
        _blindIndex = blindIndex;
        _logger = logger;
    }

    public async Task<object> SeedAsync(int tenantId, int userId, bool force = false)
    {
        // ── Idempotency check ───────────────────────────────────────
        var existingPatients = await _db.Patients.CountAsync(p => p.TenantId == tenantId);
        if (!force && existingPatients > 50)
        {
            return new
            {
                success = false,
                message = "Demo data already exists for this tenant.",
                existingPatientCount = existingPatients
            };
        }

        _logger.LogInformation("Starting demo data seed for tenant {TenantId}", tenantId);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Ensure encryption fields are registered before encrypting
        EncryptionConfiguration.Initialize();

        try
        {
            // ── Layer 1: Staff (Providers, Users, Locations) ────────
            var staffGen = new DemoStaffGenerator(_db, _enc);
            var staff = await staffGen.GenerateAsync(tenantId);
            _logger.LogInformation("Staff created: {Providers} providers, {Users} users, {Locations} locations",
                staff.Providers.Count, staff.Users.Count, staff.Locations.Count);

            // Use passed userId as createdByUserId (the admin running the seed)
            var createdBy = userId;

            // ── Layer 2: Patients + Clinical History ────────────────
            var patientGen = new DemoPatientGenerator(_db, _enc, _blindIndex);
            var patientResult = await patientGen.GenerateAsync(tenantId, staff.Providers, staff.Locations, createdBy);
            _logger.LogInformation("Patients created: {Patients} patients, {Insurances} insurance records",
                patientResult.Patients.Count, patientResult.Insurances.Count);

            // ── Layer 3: Clinical (Appointments, Encounters, Notes, Vitals, Orders, Rx) ──
            var clinicalGen = new DemoClinicalGenerator(_db);
            var clinical = await clinicalGen.GenerateAsync(tenantId, patientResult.Patients, staff.Providers, staff.Locations, staff.Users, createdBy);
            _logger.LogInformation("Clinical created: {Appts} appointments, {Enc} encounters, {Notes} notes, {Orders} orders, {Rx} prescriptions",
                clinical.Appointments.Count, clinical.Encounters.Count, clinical.Notes.Count, clinical.Orders.Count, clinical.Prescriptions.Count);

            // ── Layer 4: Billing (Charges, Claims, Payments, Ledger) ──
            var billingGen = new DemoBillingGenerator(_db);
            var billing = await billingGen.GenerateAsync(tenantId, clinical.Encounters, clinical.Notes, clinical.Appointments, patientResult.Insurances, createdBy);
            _logger.LogInformation("Billing created: {Charges} charges, {Claims} claims, {Payments} payments, {Ledger} ledger entries, {Plans} installment plans",
                billing.ChargeCount, billing.ClaimCount, billing.PaymentCount, billing.LedgerCount, billing.InstallmentPlanCount);

            sw.Stop();
            _logger.LogInformation("Demo data seed completed in {Elapsed}ms", sw.ElapsedMilliseconds);

            return new
            {
                success = true,
                message = "Demo data seeded successfully.",
                elapsed = $"{sw.ElapsedMilliseconds}ms",
                summary = new
                {
                    providers = staff.Providers.Count,
                    users = staff.Users.Count,
                    locations = staff.Locations.Count,
                    patients = patientResult.Patients.Count,
                    insurances = patientResult.Insurances.Count,
                    appointments = clinical.Appointments.Count,
                    encounters = clinical.Encounters.Count,
                    clinicalNotes = clinical.Notes.Count,
                    orders = clinical.Orders.Count,
                    prescriptions = clinical.Prescriptions.Count,
                    charges = billing.ChargeCount,
                    billingClaims = billing.ClaimCount,
                    payments = billing.PaymentCount,
                    ledgerEntries = billing.LedgerCount,
                    installmentPlans = billing.InstallmentPlanCount
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding demo data for tenant {TenantId}", tenantId);
            throw;
        }
    }
}
