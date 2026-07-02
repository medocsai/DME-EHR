using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Configuration;
using EHR.Services;
using EHR.Services.DemoData;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// Admin controller for HIPAA compliance operations.
/// Requires Admin role (Role = 1).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "1")]
public class AdminController : ControllerBase
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IAuditService _auditService;

    public AdminController(
        EhrDbContext context,
        EncryptionHelper encryptionHelper,
        IAuditService auditService)
    {
        _context = context;
        _encryptionHelper = encryptionHelper;
        _auditService = auditService;
    }

    /// <summary>
    /// Seed demo data for the current tenant. Idempotent — won't run if 50+ patients exist unless force=true.
    /// URL: POST /api/admin/seed-demo-data?force=true
    /// </summary>
    [HttpPost("seed-demo-data")]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> SeedDemoData([FromQuery] bool force = false)
    {
        var tenantId = int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
        var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;

        if (tenantId == 0)
            return BadRequest(new { success = false, message = "Invalid tenant" });

        try
        {
            // If force, clean up previous demo data first (keeps patients with MRN not starting with IM-D)
            if (force)
            {
                await CleanDemoDataAsync(tenantId);
            }

            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<DemoDataSeeder>>();
            var blindIndex = HttpContext.RequestServices.GetRequiredService<IBlindIndexService>();
            var seeder = new DemoDataSeeder(_context, _encryptionHelper, blindIndex, logger);
            var result = await seeder.SeedAsync(tenantId, userId, force);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = $"Seeding failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Removes demo-seeded data (patients with MRN IM-0xxxxx where ID > original patients).
    /// Cascades through all related entities.
    /// </summary>
    private async Task CleanDemoDataAsync(int tenantId)
    {
        // Find demo patients: MRN pattern IM-000012 and above (original patients are IM-000001 to IM-000011)
        var demoPatients = await _context.Patients
            .Where(p => p.TenantId == tenantId && p.PatientId > 11)
            .Select(p => p.PatientId)
            .ToListAsync();

        if (!demoPatients.Any()) return;

        // Delete in reverse dependency order
        _context.PatientLedgers.RemoveRange(_context.PatientLedgers.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        // Delete installment details and plans
        var planIds = await _context.InstallmentPlans.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)).Select(p => p.PlanId).ToListAsync();
        _context.InstallmentDetails.RemoveRange(_context.InstallmentDetails.Where(x => x.TenantId == tenantId && planIds.Contains(x.PlanId)));
        _context.InstallmentPlans.RemoveRange(_context.InstallmentPlans.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        // Delete copay payment tokens
        _context.CopayPaymentTokens.RemoveRange(_context.CopayPaymentTokens.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        var claimIds = await _context.BillingClaims.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)).Select(c => c.ClaimId).ToListAsync();
        _context.ClaimStatusHistories.RemoveRange(_context.ClaimStatusHistories.Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId)));
        _context.Payments.RemoveRange(_context.Payments.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        _context.Charges.RemoveRange(_context.Charges.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.BillingClaims.RemoveRange(_context.BillingClaims.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        var orderIds = await _context.Orders.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)).Select(o => o.OrderId).ToListAsync();
        _context.OrderResults.RemoveRange(_context.OrderResults.Where(x => orderIds.Contains(x.OrderId)));
        _context.Orders.RemoveRange(_context.Orders.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.Prescriptions.RemoveRange(_context.Prescriptions.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        _context.PatientVitals.RemoveRange(_context.PatientVitals.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.ClinicalNotes.RemoveRange(_context.ClinicalNotes.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.Encounters.RemoveRange(_context.Encounters.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.Appointments.RemoveRange(_context.Appointments.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        _context.PatientProblems.RemoveRange(_context.PatientProblems.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.PatientAllergies.RemoveRange(_context.PatientAllergies.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.PatientMedications.RemoveRange(_context.PatientMedications.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.PatientFamilyHistories.RemoveRange(_context.PatientFamilyHistories.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.PatientSocialHistories.RemoveRange(_context.PatientSocialHistories.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.PatientImmunizations.RemoveRange(_context.PatientImmunizations.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.Insurances.RemoveRange(_context.Insurances.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        _context.PatientSearchTokens.RemoveRange(_context.PatientSearchTokens.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        _context.Patients.RemoveRange(_context.Patients.Where(x => x.TenantId == tenantId && demoPatients.Contains(x.PatientId)));
        await _context.SaveChangesAsync();

        // Remove demo providers (NPI starting with 123456789) and their users/schedules
        var demoProviders = await _context.Providers
            .Where(p => p.TenantId == tenantId && p.Npi != null && p.Npi.StartsWith("123456789"))
            .Select(p => p.ProviderId)
            .ToListAsync();

        if (demoProviders.Any())
        {
            _context.ProviderSchedules.RemoveRange(_context.ProviderSchedules.Where(x => x.TenantId == tenantId && demoProviders.Contains(x.ProviderId)));
            _context.Users.RemoveRange(_context.Users.Where(x => x.TenantId == tenantId && x.ProviderId.HasValue && demoProviders.Contains(x.ProviderId.Value)));
            // Also remove demo support staff by email pattern
            _context.Users.RemoveRange(_context.Users.Where(x => x.TenantId == tenantId && x.Email != null && x.Email.EndsWith("@demo.clinic")));
            await _context.SaveChangesAsync();
            _context.Providers.RemoveRange(_context.Providers.Where(x => x.TenantId == tenantId && demoProviders.Contains(x.ProviderId)));
            await _context.SaveChangesAsync();
        }

        // Remove demo locations
        _context.Locations.RemoveRange(_context.Locations.Where(x => x.TenantId == tenantId && x.FacilityNpi != null && (x.FacilityNpi == "1122334455" || x.FacilityNpi == "1122334456")));
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Backfill encryption for existing unencrypted PHI data.
    /// This endpoint encrypts all PHI fields that are currently stored in plaintext.
    ///
    /// IMPORTANT: Run this once after deploying encryption. Safe to run multiple times.
    /// Only encrypts data that is not already encrypted.
    ///
    /// URL: GET /api/admin/backfill-encryption
    /// </summary>
    [HttpGet("backfill-encryption")]
    public async Task<ActionResult<BackfillResult>> BackfillEncryption()
    {
        var result = new BackfillResult();
        var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (int?)null;
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

        try
        {
            // Ensure encryption configuration is initialized
            EncryptionConfiguration.Initialize();

            // Log the backfill operation start
            await _auditService.LogAccessAsync(
                userId, userEmail,
                "BACKFILL_ENCRYPTION_START",
                "System", null,
                null, null,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // 1. PATIENTS
            var patients = await _context.Patients.ToListAsync();
            foreach (var patient in patients)
            {
                if (NeedsEncryption(patient))
                {
                    _encryptionHelper.EncryptEntity(patient);
                    result.PatientsEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            // 2. CLINICAL NOTES
            var clinicalNotes = await _context.ClinicalNotes.ToListAsync();
            foreach (var note in clinicalNotes)
            {
                if (NeedsEncryption(note))
                {
                    _encryptionHelper.EncryptEntity(note);
                    result.ClinicalNotesEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            // Note: Legacy Notes table removed - ClinicalNotes handles all clinical documentation

            // 3. INSURANCE
            var insurances = await _context.Insurances.ToListAsync();
            foreach (var insurance in insurances)
            {
                if (NeedsEncryption(insurance))
                {
                    _encryptionHelper.EncryptEntity(insurance);
                    result.InsurancesEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            // 5. PROVIDERS
            var providers = await _context.Providers.ToListAsync();
            foreach (var provider in providers)
            {
                if (NeedsEncryption(provider))
                {
                    _encryptionHelper.EncryptEntity(provider);
                    result.ProvidersEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            // 6. APPOINTMENTS
            var appointments = await _context.Appointments.ToListAsync();
            foreach (var appointment in appointments)
            {
                if (NeedsEncryption(appointment))
                {
                    _encryptionHelper.EncryptEntity(appointment);
                    result.AppointmentsEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            // 7. CARE EPISODES
            var careEpisodes = await _context.CareEpisodes.ToListAsync();
            foreach (var episode in careEpisodes)
            {
                if (NeedsEncryption(episode))
                {
                    _encryptionHelper.EncryptEntity(episode);
                    result.CareEpisodesEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            // Note: Users are no longer encrypted - user data (FirstName, LastName, Email)
            // is not considered PHI under HIPAA and is stored as plain text.

            // 8. CONSENTS
            var consents = await _context.Consents.ToListAsync();
            foreach (var consent in consents)
            {
                if (NeedsEncryption(consent))
                {
                    _encryptionHelper.EncryptEntity(consent);
                    result.ConsentsEncrypted++;
                }
            }
            await _context.SaveChangesAsync();

            result.Success = true;
            result.Message = "PHI encryption backfill completed successfully.";

            // Log the backfill operation completion
            await _auditService.LogAccessAsync(
                userId, userEmail,
                "BACKFILL_ENCRYPTION_COMPLETE",
                "System", null,
                null, System.Text.Json.JsonSerializer.Serialize(result),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Backfill failed: {ex.Message}";

            // Log the failure
            await _auditService.LogAccessAsync(
                userId, userEmail,
                "BACKFILL_ENCRYPTION_FAILED",
                "System", null,
                null, ex.Message,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(500, result);
        }
    }

    /// <summary>
    /// Check encryption status - shows how many records need encryption.
    /// Safe to run anytime to check status.
    ///
    /// URL: GET /api/admin/encryption-status
    /// </summary>
    [HttpGet("encryption-status")]
    public async Task<ActionResult<EncryptionStatusResult>> GetEncryptionStatus()
    {
        EncryptionConfiguration.Initialize();

        var result = new EncryptionStatusResult();

        // Count patients needing encryption
        var patients = await _context.Patients.ToListAsync();
        result.TotalPatients = patients.Count;
        result.PatientsNeedingEncryption = patients.Count(p => NeedsEncryption(p));

        // Count clinical notes needing encryption
        var clinicalNotes = await _context.ClinicalNotes.ToListAsync();
        result.TotalClinicalNotes = clinicalNotes.Count;
        result.ClinicalNotesNeedingEncryption = clinicalNotes.Count(n => NeedsEncryption(n));

        // Note: Legacy Notes table removed - ClinicalNotes handles all clinical documentation

        // Count insurances needing encryption
        var insurances = await _context.Insurances.ToListAsync();
        result.TotalInsurances = insurances.Count;
        result.InsurancesNeedingEncryption = insurances.Count(i => NeedsEncryption(i));

        // Count providers needing encryption
        var providers = await _context.Providers.ToListAsync();
        result.TotalProviders = providers.Count;
        result.ProvidersNeedingEncryption = providers.Count(p => NeedsEncryption(p));

        // Count appointments needing encryption
        var appointments = await _context.Appointments.ToListAsync();
        result.TotalAppointments = appointments.Count;
        result.AppointmentsNeedingEncryption = appointments.Count(a => NeedsEncryption(a));

        // Count care episodes needing encryption
        var careEpisodes = await _context.CareEpisodes.ToListAsync();
        result.TotalCareEpisodes = careEpisodes.Count;
        result.CareEpisodesNeedingEncryption = careEpisodes.Count(ce => NeedsEncryption(ce));

        // Note: Users are no longer encrypted - user data is not PHI
        result.TotalUsers = await _context.Users.CountAsync();
        result.UsersNeedingEncryption = 0; // Users are not encrypted

        // Count consents needing encryption
        var consents = await _context.Consents.ToListAsync();
        result.TotalConsents = consents.Count;
        result.ConsentsNeedingEncryption = consents.Count(c => NeedsEncryption(c));

        // Note: UsersNeedingEncryption is excluded - user data is not PHI
        result.AllEncrypted = result.PatientsNeedingEncryption == 0 &&
                              result.ClinicalNotesNeedingEncryption == 0 &&
                              result.InsurancesNeedingEncryption == 0 &&
                              result.ProvidersNeedingEncryption == 0 &&
                              result.AppointmentsNeedingEncryption == 0 &&
                              result.CareEpisodesNeedingEncryption == 0 &&
                              result.ConsentsNeedingEncryption == 0;

        return Ok(result);
    }

    /// <summary>
    /// Check if an entity has any unencrypted PHI fields
    /// </summary>
    private bool NeedsEncryption<T>(T entity) where T : class
    {
        if (entity == null) return false;

        var entityType = typeof(T);
        if (!EncryptionConfiguration.HasEncryptedFields(entityType)) return false;

        foreach (var fieldName in EncryptionConfiguration.GetEncryptedFields(entityType))
        {
            var property = entityType.GetProperty(fieldName);
            if (property == null || property.PropertyType != typeof(string)) continue;

            var value = property.GetValue(entity) as string;
            if (!string.IsNullOrEmpty(value) && !_encryptionHelper.IsEncrypted(value))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Result of the backfill encryption operation
/// </summary>
public class BackfillResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int PatientsEncrypted { get; set; }
    public int ClinicalNotesEncrypted { get; set; }
    public int NotesEncrypted { get; set; }
    public int InsurancesEncrypted { get; set; }
    public int ProvidersEncrypted { get; set; }
    public int AppointmentsEncrypted { get; set; }
    public int CareEpisodesEncrypted { get; set; }
    public int UsersEncrypted { get; set; }
    public int ConsentsEncrypted { get; set; }

    public int TotalEncrypted => PatientsEncrypted + ClinicalNotesEncrypted + NotesEncrypted +
                                  InsurancesEncrypted + ProvidersEncrypted + AppointmentsEncrypted +
                                  CareEpisodesEncrypted + UsersEncrypted + ConsentsEncrypted;
}

/// <summary>
/// Status of PHI encryption across all tables
/// </summary>
public class EncryptionStatusResult
{
    public bool AllEncrypted { get; set; }

    public int TotalPatients { get; set; }
    public int PatientsNeedingEncryption { get; set; }

    public int TotalClinicalNotes { get; set; }
    public int ClinicalNotesNeedingEncryption { get; set; }

    public int TotalNotes { get; set; }
    public int NotesNeedingEncryption { get; set; }

    public int TotalInsurances { get; set; }
    public int InsurancesNeedingEncryption { get; set; }

    public int TotalProviders { get; set; }
    public int ProvidersNeedingEncryption { get; set; }

    public int TotalAppointments { get; set; }
    public int AppointmentsNeedingEncryption { get; set; }

    public int TotalCareEpisodes { get; set; }
    public int CareEpisodesNeedingEncryption { get; set; }

    public int TotalUsers { get; set; }
    public int UsersNeedingEncryption { get; set; }

    public int TotalConsents { get; set; }
    public int ConsentsNeedingEncryption { get; set; }

    public int TotalRecordsNeedingEncryption => PatientsNeedingEncryption + ClinicalNotesNeedingEncryption +
                                                  NotesNeedingEncryption + InsurancesNeedingEncryption +
                                                  ProvidersNeedingEncryption + AppointmentsNeedingEncryption +
                                                  CareEpisodesNeedingEncryption + UsersNeedingEncryption +
                                                  ConsentsNeedingEncryption;
}
