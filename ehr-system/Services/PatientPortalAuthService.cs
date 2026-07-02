using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EHR.Services;

public class PatientPortalAuthService : IPatientPortalAuthService
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IConfiguration _config;
    private readonly IAuditService _auditService;
    private readonly ILogger<PatientPortalAuthService> _logger;

    public PatientPortalAuthService(
        EhrDbContext context,
        EncryptionHelper encryptionHelper,
        IConfiguration config,
        IAuditService auditService,
        ILogger<PatientPortalAuthService> logger)
    {
        _context = context;
        _encryptionHelper = encryptionHelper;
        _config = config;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PatientPortalVerifyResult> VerifyPatientAsync(DateOnly dateOfBirth, string ssnLast4)
    {
        // Normalize and hash the SSN last 4 using the same method as patient creation
        var normalizedSsn = ssnLast4.Replace("-", "").Replace(" ", "").Trim();
        var ssnLast4Hash = _encryptionHelper.GenerateSearchHash(normalizedSsn);

        if (string.IsNullOrEmpty(ssnLast4Hash))
        {
            return new PatientPortalVerifyResult
            {
                Success = false,
                Message = "Unable to verify your identity. Please contact your clinic for assistance."
            };
        }

        _logger.LogInformation("Portal verification attempt (DOB redacted from logs)");

        // Find ALL matching patients across all tenants
        var matchingPatients = await _context.Patients
            .Include(p => p.Tenant)
            .Where(p => p.IsDeleted != true
                && p.SsnLast4Hash == ssnLast4Hash
                && p.DateOfBirth == dateOfBirth)
            .ToListAsync();

        if (matchingPatients.Count == 0)
        {
            _logger.LogWarning("Portal verification failed: no matching patient (DOB redacted from logs)");

            // DOB intentionally NOT logged here — H3 PHI redaction. The
            // audit trail still records the failed verification attempt for
            // forensic correlation; just without the DOB itself.
            await _auditService.LogAccessAsync(
                null, null, "PORTAL_LOGIN_FAILED", "Patient", null,
                null, "ssn+dob lookup, no match", null);

            return new PatientPortalVerifyResult
            {
                Success = false,
                Message = "Unable to verify your identity. Please contact your clinic for assistance."
            };
        }

        // Decrypt patient names for display
        foreach (var p in matchingPatients)
        {
            _encryptionHelper.DecryptEntity(p);
        }

        if (matchingPatients.Count == 1)
        {
            var patient = matchingPatients[0];
            var tenant = patient.Tenant;

            // Check if tenant has multiple active locations
            var locations = await _context.Locations
                .Where(l => l.TenantId == tenant!.TenantId && l.IsActive == true)
                .OrderBy(l => l.Name)
                .ToListAsync();

            if (locations.Count > 1)
            {
                // Patient needs to select a location
                return new PatientPortalVerifyResult
                {
                    Success = true,
                    RequiresLocationSelection = true,
                    PatientId = patient.PatientId,
                    PatientName = $"{patient.FirstName} {patient.LastName}",
                    TenantId = tenant!.TenantId,
                    AvailableLocations = locations.Select(l => new PortalLocationDto
                    {
                        LocationId = l.LocationId,
                        Name = l.Name,
                        Address = l.Address,
                        Phone = l.Phone
                    }).ToList()
                };
            }

            // Single tenant, single (or no) location — auto-login
            var location = locations.FirstOrDefault();
            var token = GeneratePortalToken(patient, tenant!, location);

            await _auditService.LogAccessAsync(
                null, patient.Email, "PORTAL_LOGIN", "Patient", patient.PatientId,
                null, null, null);

            return new PatientPortalVerifyResult
            {
                Success = true,
                Token = token,
                PatientId = patient.PatientId,
                PatientName = $"{patient.FirstName} {patient.LastName}",
                TenantId = tenant!.TenantId,
                TokenExpiry = DateTime.UtcNow.AddHours(4)
            };
        }

        // Multiple matches across different tenants — clinic selection required.
        // Patient name intentionally OMITTED here: returning it would confirm to
        // an attacker (who guessed an SSN+DOB combination) the real name on the
        // record, even before the patient picks a clinic. Clinic list itself is
        // a smaller leak (already-known list of clinics) but PII binding to a
        // name is the worse disclosure.
        return new PatientPortalVerifyResult
        {
            Success = true,
            RequiresClinicSelection = true,
            PatientName = null,
            AvailableClinics = matchingPatients.Select(p => new PortalClinicOption
            {
                TenantId = p.TenantId,
                ClinicName = p.Tenant?.Name ?? "Unknown Clinic",
                PatientId = p.PatientId
            }).ToList()
        };
    }

    public async Task<List<PortalLocationDto>> GetTenantLocationsAsync(int tenantId)
    {
        return await _context.Locations
            .Where(l => l.TenantId == tenantId && l.IsActive == true)
            .OrderBy(l => l.Name)
            .Select(l => new PortalLocationDto
            {
                LocationId = l.LocationId,
                Name = l.Name,
                Address = l.Address,
                Phone = l.Phone
            })
            .ToListAsync();
    }

    public string GeneratePortalToken(Patient patient, Tenant tenant, Location? location = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("PatientId", patient.PatientId.ToString()),
            new(ClaimTypes.Name, $"{patient.FirstName} {patient.LastName}"),
            new(ClaimTypes.Email, patient.Email ?? ""),
            new(ClaimTypes.Role, "8"),
            new("Role", "8"),
            new("TenantId", tenant.TenantId.ToString()),
            new("TenantName", tenant.Name ?? ""),
        };

        if (location != null)
        {
            claims.Add(new Claim("LocationId", location.LocationId.ToString()));
            claims.Add(new Claim("LocationName", location.Name ?? ""));
        }

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(4),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
