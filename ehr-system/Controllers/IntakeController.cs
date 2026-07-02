using System.Text.Json;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EHR.Controllers;

/// <summary>
/// Patient Intake endpoints. Three auth contexts share the same underlying services:
///   - Portal (authed patient session)
///   - Tablet (opaque token + short-lived verify cookie)
///   - Clinic staff (standard JWT)
/// </summary>
[ApiController]
public class IntakeController : ControllerBase
{
    // Cookie format/attributes live in EHR.Services.Intake.IntakeVerifyCookie so
    // KioskController can issue the same cookie during the consent→intake handoff.
    // See rules/technical/consent-to-intake-handoff.md.

    private readonly EhrDbContext _context;
    private readonly IIntakeSubmissionService _submissions;
    private readonly IIntakeProgressCalculator _progress;
    private readonly IPatientEnteredDataReader _reader;
    private readonly IIntakeAccessTokenService _tokens;
    private readonly IPatientIdentityMatcher _identity;
    private readonly IIntakeAttemptThrottler _throttler;
    private readonly EncryptionHelper _encryption;
    private readonly IIntakePrefillService _prefill;
    private readonly ILogger<IntakeController> _logger;

    public IntakeController(
        EhrDbContext context,
        IIntakeSubmissionService submissions,
        IIntakeProgressCalculator progress,
        IPatientEnteredDataReader reader,
        IIntakeAccessTokenService tokens,
        IPatientIdentityMatcher identity,
        IIntakeAttemptThrottler throttler,
        EncryptionHelper encryption,
        IIntakePrefillService prefill,
        ILogger<IntakeController> logger)
    {
        _context = context;
        _submissions = submissions;
        _progress = progress;
        _reader = reader;
        _tokens = tokens;
        _identity = identity;
        _throttler = throttler;
        _encryption = encryption;
        _prefill = prefill;
        _logger = logger;
    }

    // ================================================================
    // Portal path (authed patient)
    // ================================================================

    [HttpGet("/api/intake/progress")]
    [Authorize]
    public async Task<ActionResult<IntakeProgressDto>> GetPortalProgress()
    {
        var patientId = GetClaimInt("PatientId");
        if (patientId == 0) return Unauthorized();
        return Ok(await _progress.CalculateAsync(patientId));
    }

    /// <summary>
    /// Generic pre-fill endpoint for any intake section (portal / authed patient).
    /// The 7 sections (demographics, concerns, medical-history, lifestyle,
    /// medications, gender-health, longevity) all dispatch through
    /// IIntakePrefillService, mirroring the SaveSectionAsync pattern.
    /// </summary>
    [HttpGet("/api/intake/{section}/prefill")]
    [Authorize]
    public async Task<IActionResult> GetPortalPrefill(string section)
    {
        var patientId = GetClaimInt("PatientId");
        if (patientId == 0) return Unauthorized();

        try { return Ok(await _prefill.GetAsync(section, patientId)); }
        catch (ArgumentException) { return NotFound(new { message = $"Unknown section: {section}" }); }
    }

    [HttpPost("/api/intake/section/{name}")]
    [Authorize]
    public async Task<ActionResult<IntakeSubmissionResultDto>> SavePortalSection(string name, [FromBody] JsonElement payload)
    {
        var patientId = GetClaimInt("PatientId");
        var tenantId = GetClaimInt("TenantId");
        if (patientId == 0 || tenantId == 0) return Unauthorized();

        var result = await _submissions.SaveSectionAsync(
            patientId, tenantId, name, payload, IntakeChannel.Portal,
            GetIpAddress(), Request.Headers.UserAgent.ToString());
        return Ok(result);
    }

    [HttpDelete("/api/intake/row/{table}/{id:int}")]
    [Authorize]
    public async Task<IActionResult> DeletePortalRow(string table, int id)
    {
        var patientId = GetClaimInt("PatientId");
        if (patientId == 0) return Unauthorized();
        var ok = await _submissions.DeletePatientEnteredRowAsync(patientId, table, id);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("/api/intake/submit")]
    [Authorize]
    public async Task<ActionResult<IntakeSubmissionResultDto>> SubmitPortal()
    {
        var patientId = GetClaimInt("PatientId");
        var tenantId = GetClaimInt("TenantId");
        if (patientId == 0 || tenantId == 0) return Unauthorized();
        return Ok(await _submissions.FinalizeAsync(patientId, tenantId));
    }

    // ================================================================
    // Tablet path (opaque token + verify cookie)
    // ================================================================

    [HttpGet("/api/intake/p/{token:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateTabletToken(Guid token)
    {
        var patientId = await _tokens.ResolvePatientIdAsync(token);
        if (patientId == null) return NotFound(new { valid = false });
        return Ok(new { valid = true });
    }

    public class TabletVerifyRequest
    {
        // Identity verification (2026-05): LastName + DateOfBirth + ZipCode.
        // SSN was removed across all patient validation flows.
        public string LastName { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public string ZipCode { get; set; }
    }

    [HttpPost("/api/intake/p/{token:guid}/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyTablet(Guid token, [FromBody] TabletVerifyRequest req)
    {
        var patientId = await _tokens.ResolvePatientIdAsync(token);
        if (patientId == null) return NotFound(new { verified = false });

        var patient = await _context.Patients
            .Where(p => p.PatientId == patientId.Value)
            .Select(p => new { p.TenantId, p.PreferredLocationId, p.FirstName })
            .FirstOrDefaultAsync();
        if (patient == null) return NotFound(new { verified = false });

        var tenantId = patient.TenantId;
        var locationId = patient.PreferredLocationId ?? 0;
        var ip = GetIpAddress();
        var ua = Request.Headers.UserAgent.ToString();

        var throttle = await _throttler.CheckAsync(tenantId, locationId, ip);
        if (!throttle.Allowed)
        {
            Response.Headers["Retry-After"] = "900";
            return StatusCode(429, new
            {
                verified = false,
                lockoutEndAt = throttle.LockoutEndAt,
                message = "Too many attempts. Please wait and try again, or ask front desk for help."
            });
        }

        var matched = await _identity.MatchesAsync(patientId.Value, req.LastName, req.DateOfBirth, req.ZipCode);

        // Throttler kept its `attemptedSsnLast4Hash` column for legacy audit rows;
        // we now pass null since no SSN is collected.
        await _throttler.LogAttemptAsync(
            tenantId, locationId,
            matched ? patientId : null,
            req.DateOfBirth, null, matched, ip, ua);

        if (!matched)
        {
            return Unauthorized(new { verified = false, message = "Could not verify. Check your entries." });
        }

        SetVerifyCookie(token);

        var firstName = _encryption.Decrypt(patient.FirstName) ?? patient.FirstName;
        return Ok(new { verified = true, firstName });
    }

    [HttpGet("/api/intake/p/{token:guid}/progress")]
    [AllowAnonymous]
    public async Task<IActionResult> TabletProgress(Guid token)
    {
        var patientId = await ResolveVerifiedPatientAsync(token);
        if (patientId == null) return Unauthorized();

        var progress = await _progress.CalculateAsync(patientId.Value);

        // Post-verify, returning the first name is HIPAA-safe: the session already
        // proved identity via LastName + DOB + ZIP. Tablet UI uses this for "Hi, {first}".
        var firstNameRaw = await _context.Patients
            .Where(p => p.PatientId == patientId.Value)
            .Select(p => p.FirstName)
            .FirstOrDefaultAsync();
        var firstName = string.IsNullOrEmpty(firstNameRaw)
            ? null
            : (_encryption.Decrypt(firstNameRaw) ?? firstNameRaw);

        return Ok(new
        {
            completed = progress.Completed,
            total = progress.Total,
            sections = progress.Sections,
            currentSubmissionId = progress.CurrentSubmissionId,
            submittedAt = progress.SubmittedAt,
            firstName
        });
    }

    [HttpPost("/api/intake/p/{token:guid}/section/{name}")]
    [AllowAnonymous]
    public async Task<IActionResult> TabletSaveSection(Guid token, string name, [FromBody] JsonElement payload)
    {
        var patientId = await ResolveVerifiedPatientAsync(token);
        if (patientId == null) return Unauthorized();

        var tenantId = await _context.Patients
            .Where(p => p.PatientId == patientId.Value)
            .Select(p => p.TenantId)
            .FirstOrDefaultAsync();

        var result = await _submissions.SaveSectionAsync(
            patientId.Value, tenantId, name, payload, IntakeChannel.Tablet,
            GetIpAddress(), Request.Headers.UserAgent.ToString());
        return Ok(result);
    }

    [HttpPost("/api/intake/p/{token:guid}/submit")]
    [AllowAnonymous]
    public async Task<IActionResult> TabletSubmit(Guid token)
    {
        var patientId = await ResolveVerifiedPatientAsync(token);
        if (patientId == null) return Unauthorized();

        var tenantId = await _context.Patients
            .Where(p => p.PatientId == patientId.Value)
            .Select(p => p.TenantId)
            .FirstOrDefaultAsync();
        return Ok(await _submissions.FinalizeAsync(patientId.Value, tenantId));
    }

    /// <summary>
    /// Generic pre-fill endpoint for any intake section (tablet / verify-cookie auth).
    /// Resolves patientId from the verify cookie, then delegates to the same
    /// IIntakePrefillService used by the portal endpoint — single source of truth
    /// for prefill data assembly.
    /// </summary>
    [HttpGet("/api/intake/p/{token:guid}/{section}/prefill")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTabletPrefill(Guid token, string section)
    {
        var patientId = await ResolveVerifiedPatientAsync(token);
        if (patientId == null) return Unauthorized();

        try { return Ok(await _prefill.GetAsync(section, patientId.Value)); }
        catch (ArgumentException) { return NotFound(new { message = $"Unknown section: {section}" }); }
    }

    // ================================================================
    // Clinic-side (staff)
    // ================================================================

    [HttpGet("/api/clinic/patients/{id:int}/intake-token")]
    [Authorize]
    public async Task<IActionResult> GetIntakeToken(int id)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();
        var token = await _tokens.GetOrCreateTokenAsync(id);
        return Ok(new
        {
            token,
            url = BuildTabletUrl(token)
        });
    }

    [HttpPost("/api/clinic/patients/{id:int}/intake-token/rotate")]
    [Authorize]
    public async Task<IActionResult> RotateIntakeToken(int id)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();
        var token = await _tokens.RotateTokenAsync(id);
        return Ok(new
        {
            token,
            url = BuildTabletUrl(token)
        });
    }

    [HttpGet("/api/clinic/patients/{id:int}/intake-view")]
    [Authorize]
    public async Task<ActionResult<PatientIntakeViewDto>> GetIntakeView(int id)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();
        return Ok(await _reader.GetIntakeViewAsync(id));
    }

    // ================================================================
    // Clinic-side intake edit (NEW — Step 1)
    // Iframe-mounted wizard at /clinic/patients/{id}/intake-frame posts here.
    // Calls existing IntakeSubmissionService unchanged. No DB changes.
    // Spec: rules/technical/intake-on-clinical-note.md §4.4.
    // ================================================================

    /// <summary>Roles allowed to write through the clinic intake endpoints.</summary>
    /// <remarks>
    /// SuperAdmin(0), ClinicAdmin(1), Clinician(2), MedicalAssistant(6), Nurse(7).
    /// FrontDesk(3), Biller(4), ReadOnly(5) get view-only (GET ok, POST/DELETE 403).
    /// </remarks>
    private static readonly HashSet<int> ClinicIntakeWriteRoles = new() { 0, 1, 2, 6, 7 };

    private bool CallerCanWriteClinicIntake()
    {
        var roleClaim = User.FindFirst("Role")?.Value;
        if (!int.TryParse(roleClaim, out var role)) return false;
        return ClinicIntakeWriteRoles.Contains(role);
    }

    [HttpGet("/api/clinic/patients/{id:int}/intake/progress")]
    [Authorize]
    public async Task<ActionResult<IntakeProgressDto>> GetClinicIntakeProgress(int id)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();
        return Ok(await _progress.CalculateAsync(id));
    }

    [HttpGet("/api/clinic/patients/{id:int}/intake/{section}/prefill")]
    [Authorize]
    public async Task<IActionResult> GetClinicIntakePrefill(int id, string section)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();

        try { return Ok(await _prefill.GetAsync(section, id)); }
        catch (ArgumentException) { return NotFound(new { message = $"Unknown section: {section}" }); }
    }

    [HttpPost("/api/clinic/patients/{id:int}/intake/section/{name}")]
    [Authorize]
    public async Task<ActionResult<IntakeSubmissionResultDto>> SaveClinicIntakeSection(int id, string name, [FromBody] JsonElement payload)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();
        if (!CallerCanWriteClinicIntake()) return Forbid();

        var tenantId = GetClaimInt("TenantId");
        if (tenantId == 0) return Unauthorized();

        // v1: reuse existing service unchanged. IntakeChannel.Portal is reused
        // (no Clinic value yet — Step 2 will add). Source tagging stays Patient
        // for new rows; manager accepted this for v1.
        var result = await _submissions.SaveSectionAsync(
            id, tenantId, name, payload, IntakeChannel.Portal,
            GetIpAddress(), Request.Headers.UserAgent.ToString());
        return Ok(result);
    }

    [HttpDelete("/api/clinic/patients/{id:int}/intake/row/{table}/{rowId:int}")]
    [Authorize]
    public async Task<IActionResult> DeleteClinicIntakeRow(int id, string table, int rowId)
    {
        if (!await PatientInCallerTenantAsync(id)) return Forbid();
        if (!CallerCanWriteClinicIntake()) return Forbid();

        var ok = await _submissions.DeletePatientEnteredRowAsync(id, table, rowId);
        return ok ? NoContent() : NotFound();
    }

    // ================================================================
    // Helpers
    // ================================================================

    private int GetClaimInt(string name)
    {
        var c = User.FindFirst(name);
        return c != null && int.TryParse(c.Value, out var v) ? v : 0;
    }

    private string GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private async Task<bool> PatientInCallerTenantAsync(int patientId)
    {
        var tenantId = GetClaimInt("TenantId");
        if (tenantId == 0) return false;
        return await _context.Patients.AnyAsync(p => p.PatientId == patientId && p.TenantId == tenantId);
    }

    private string BuildTabletUrl(Guid token)
    {
        var scheme = Request.Scheme;
        var host = Request.Host.Value;
        return $"{scheme}://{host}/intake/p/{token}";
    }

    private void SetVerifyCookie(Guid token)
    {
        Response.Cookies.Append(
            IntakeVerifyCookie.Name(token),
            IntakeVerifyCookie.Value(token),
            IntakeVerifyCookie.Options(Request.IsHttps, IntakeVerifyCookie.DefaultTtl));
    }

    private async Task<int?> ResolveVerifiedPatientAsync(Guid token)
    {
        var cookieVal = Request.Cookies[IntakeVerifyCookie.Name(token)];
        if (!IntakeVerifyCookie.Matches(cookieVal, token)) return null;
        return await _tokens.ResolvePatientIdAsync(token);
    }
}
