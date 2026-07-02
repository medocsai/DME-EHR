using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Models.Generated;

namespace EHR.Controllers;

/// <summary>
/// ONE-TIME admin-only controller that re-encrypts PHI fields on records that
/// were silently corrupted to plaintext by the EF change-tracking bug fixed in
/// this same change set. Safe to run multiple times — EncryptEntity is
/// idempotent thanks to the IsEncrypted() guard in EncryptionHelper, so
/// already-encrypted rows are skipped.
///
/// USAGE (run each endpoint once after deploying the fix to stop the leak):
///     POST /api/phi-encryption-backfill/patients
///     POST /api/phi-encryption-backfill/insurances
///     POST /api/phi-encryption-backfill/providers
///     POST /api/phi-encryption-backfill/all  (runs all three, sequentially)
///
/// Each returns JSON with { total, encrypted, alreadyEncrypted, nullValues }.
///
/// SECURITY: Only SuperAdmin (0) or ClinicAdmin (1) can invoke. Respects
/// tenant isolation if a tenant is resolved. Does NOT decrypt — only encrypts
/// fields that are currently plaintext.
/// </summary>
[ApiController]
[Route("api/phi-encryption-backfill")]
[Authorize(Roles = "0,1")]
public class PhiEncryptionBackfillController : ControllerBase
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly ILogger<PhiEncryptionBackfillController> _logger;

    public PhiEncryptionBackfillController(
        EhrDbContext context,
        EncryptionHelper encryptionHelper,
        ILogger<PhiEncryptionBackfillController> logger)
    {
        _context = context;
        _encryptionHelper = encryptionHelper;
        _logger = logger;
    }

    // SuperAdmin (role 0) operates across all tenants — system repair tool.
    // ClinicAdmin (role 1) is scoped to their own tenant only.
    private bool IsSuperAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "0";
    private int? CurrentTenantId =>
        int.TryParse(User.FindFirst("TenantId")?.Value, out var t) ? t : null;

    /// <summary>
    /// Re-encrypt all Patient rows whose registered PHI fields are currently
    /// stored as plaintext. Idempotent.
    /// </summary>
    [HttpPost("patients")]
    public async Task<IActionResult> BackfillPatients()
    {
        // Load all patients tracked (we need EF to detect the re-encryption changes
        // and flush them). This is intentionally the opposite of the read-path
        // AsNoTracking fix — here we WANT tracking because we intend to save.
        IQueryable<Patient> query = _context.Patients;
        if (!IsSuperAdmin)
        {
            var tid = CurrentTenantId;
            if (tid == null) return Forbid();
            query = query.Where(p => p.TenantId == tid.Value);
        }
        var patients = await query.ToListAsync();

        int encryptedCount = 0;
        int alreadyEncryptedCount = 0;

        foreach (var patient in patients)
        {
            // Snapshot IsEncrypted state of FirstName as a representative field
            // (all Patient encrypted fields are either all plaintext or all
            // encrypted for a given row, because they went through DecryptEntity
            // together in the bug path).
            var wasPlaintext = !string.IsNullOrEmpty(patient.FirstName)
                               && !_encryptionHelper.IsEncrypted(patient.FirstName);

            // Idempotent: EncryptEntity internally skips fields that are already
            // encrypted, so this is safe to run on a mixed plaintext/encrypted set.
            _encryptionHelper.EncryptEntity(patient);

            if (wasPlaintext)
                encryptedCount++;
            else
                alreadyEncryptedCount++;
        }

        var saved = await _context.SaveChangesAsync();

        _logger.LogWarning(
            "PHI BACKFILL (Patients): {Encrypted} re-encrypted, {Already} already encrypted, {Saved} DB rows written",
            encryptedCount, alreadyEncryptedCount, saved);

        return Ok(new
        {
            entity = "Patient",
            total = patients.Count,
            encrypted = encryptedCount,
            alreadyEncrypted = alreadyEncryptedCount,
            dbRowsWritten = saved
        });
    }

    /// <summary>
    /// Re-encrypt all Insurance rows. Idempotent.
    /// </summary>
    [HttpPost("insurances")]
    public async Task<IActionResult> BackfillInsurances()
    {
        IQueryable<Insurance> query = _context.Insurances;
        if (!IsSuperAdmin)
        {
            var tid = CurrentTenantId;
            if (tid == null) return Forbid();
            query = query.Where(i => i.TenantId == tid.Value);
        }
        var insurances = await query.ToListAsync();

        int encryptedCount = 0;
        int alreadyEncryptedCount = 0;

        foreach (var insurance in insurances)
        {
            // Use SubscriberName as the canary field since it's the only registered
            // encrypted identity field that's commonly populated. If null, fall
            // back to PolicyNumber.
            var canary = insurance.SubscriberName ?? insurance.PolicyNumber;
            var wasPlaintext = !string.IsNullOrEmpty(canary)
                               && !_encryptionHelper.IsEncrypted(canary);

            _encryptionHelper.EncryptEntity(insurance);

            if (wasPlaintext)
                encryptedCount++;
            else
                alreadyEncryptedCount++;
        }

        var saved = await _context.SaveChangesAsync();

        _logger.LogWarning(
            "PHI BACKFILL (Insurances): {Encrypted} re-encrypted, {Already} already encrypted, {Saved} DB rows written",
            encryptedCount, alreadyEncryptedCount, saved);

        return Ok(new
        {
            entity = "Insurance",
            total = insurances.Count,
            encrypted = encryptedCount,
            alreadyEncrypted = alreadyEncryptedCount,
            dbRowsWritten = saved
        });
    }

    /// <summary>
    /// Re-encrypt all Provider rows. Idempotent.
    /// </summary>
    [HttpPost("providers")]
    public async Task<IActionResult> BackfillProviders()
    {
        IQueryable<Provider> query = _context.Providers;
        if (!IsSuperAdmin)
        {
            var tid = CurrentTenantId;
            if (tid == null) return Forbid();
            query = query.Where(p => p.TenantId == tid.Value);
        }
        var providers = await query.ToListAsync();

        int encryptedCount = 0;
        int alreadyEncryptedCount = 0;

        foreach (var provider in providers)
        {
            // Email is the canary (it's an encrypted Provider field per
            // EncryptionConfiguration). Fall back to Phone if null.
            var canary = provider.Email ?? provider.Phone;
            var wasPlaintext = !string.IsNullOrEmpty(canary)
                               && !_encryptionHelper.IsEncrypted(canary);

            _encryptionHelper.EncryptEntity(provider);

            if (wasPlaintext)
                encryptedCount++;
            else
                alreadyEncryptedCount++;
        }

        var saved = await _context.SaveChangesAsync();

        _logger.LogWarning(
            "PHI BACKFILL (Providers): {Encrypted} re-encrypted, {Already} already encrypted, {Saved} DB rows written",
            encryptedCount, alreadyEncryptedCount, saved);

        return Ok(new
        {
            entity = "Provider",
            total = providers.Count,
            encrypted = encryptedCount,
            alreadyEncrypted = alreadyEncryptedCount,
            dbRowsWritten = saved
        });
    }

    /// <summary>
    /// Run all three backfills sequentially and return a combined report.
    /// </summary>
    [HttpPost("all")]
    public async Task<IActionResult> BackfillAll()
    {
        var patients = await BackfillPatients() as OkObjectResult;
        var insurances = await BackfillInsurances() as OkObjectResult;
        var providers = await BackfillProviders() as OkObjectResult;

        return Ok(new
        {
            patients = patients?.Value,
            insurances = insurances?.Value,
            providers = providers?.Value
        });
    }

    /// <summary>
    /// Read-only status check. Reports how many rows in each entity have
    /// plaintext vs. encrypted values in their key field. Useful before and
    /// after running the backfill to verify. Does NOT modify anything.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        // Short read-only projection. AsNoTracking because we do NOT want these
        // loads to accidentally trigger the very bug we're fixing.
        int? scopeTid = null;
        if (!IsSuperAdmin)
        {
            scopeTid = CurrentTenantId;
            if (scopeTid == null) return Forbid();
        }

        IQueryable<Patient> patientQuery = _context.Patients.AsNoTracking();
        if (scopeTid != null) patientQuery = patientQuery.Where(p => p.TenantId == scopeTid.Value);
        var patients = await patientQuery
            .Select(p => new { p.PatientId, p.FirstName })
            .ToListAsync();
        int patientPlain = 0, patientEnc = 0, patientNull = 0;
        foreach (var p in patients)
        {
            if (string.IsNullOrEmpty(p.FirstName)) patientNull++;
            else if (_encryptionHelper.IsEncrypted(p.FirstName)) patientEnc++;
            else patientPlain++;
        }

        IQueryable<Insurance> insuranceQuery = _context.Insurances.AsNoTracking();
        if (scopeTid != null) insuranceQuery = insuranceQuery.Where(i => i.TenantId == scopeTid.Value);
        var insurances = await insuranceQuery
            .Select(i => new { i.InsuranceId, i.SubscriberName, i.PolicyNumber })
            .ToListAsync();
        int insPlain = 0, insEnc = 0, insNull = 0;
        foreach (var i in insurances)
        {
            var canary = i.SubscriberName ?? i.PolicyNumber;
            if (string.IsNullOrEmpty(canary)) insNull++;
            else if (_encryptionHelper.IsEncrypted(canary)) insEnc++;
            else insPlain++;
        }

        IQueryable<Provider> providerQuery = _context.Providers.AsNoTracking();
        if (scopeTid != null) providerQuery = providerQuery.Where(p => p.TenantId == scopeTid.Value);
        var providers = await providerQuery
            .Select(p => new { p.ProviderId, p.Email, p.Phone })
            .ToListAsync();
        int provPlain = 0, provEnc = 0, provNull = 0;
        foreach (var p in providers)
        {
            var canary = p.Email ?? p.Phone;
            if (string.IsNullOrEmpty(canary)) provNull++;
            else if (_encryptionHelper.IsEncrypted(canary)) provEnc++;
            else provPlain++;
        }

        return Ok(new
        {
            patients = new { total = patients.Count, encrypted = patientEnc, plaintext = patientPlain, nullValues = patientNull },
            insurances = new { total = insurances.Count, encrypted = insEnc, plaintext = insPlain, nullValues = insNull },
            providers = new { total = providers.Count, encrypted = provEnc, plaintext = provPlain, nullValues = provNull }
        });
    }
}
