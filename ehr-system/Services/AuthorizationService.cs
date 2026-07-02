using Microsoft.EntityFrameworkCore;
using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// Service for managing insurance authorizations.
/// VisitsUsed is calculated dynamically by counting completed appointments for the patient.
/// Authorization tracking is separate from CareEpisode tracking.
/// </summary>
public interface IInsuranceAuthorizationService
{
    /// <summary>
    /// Get all authorizations for an insurance record, sorted by DateOfValidation descending.
    /// VisitsUsed is calculated by counting all completed appointments for the patient.
    /// </summary>
    Task<List<AuthorizationDto>> GetAuthorizationsForInsuranceAsync(int insuranceId);

    /// <summary>
    /// Get the most recent (current) authorization for an insurance record.
    /// VisitsUsed is calculated by counting all completed appointments for the patient.
    /// </summary>
    Task<AuthorizationDto> GetCurrentAuthorizationAsync(int insuranceId);

    /// <summary>
    /// Get authorization history with summary information
    /// </summary>
    Task<AuthorizationHistoryDto> GetAuthorizationHistoryAsync(int insuranceId);

    /// <summary>
    /// Create a new authorization record if the authorization number is different from the existing one.
    /// Returns the created authorization, or null if duplicate (same auth number).
    /// </summary>
    Task<AuthorizationDto> CreateAuthorizationIfNewAsync(AuthorizationCreateDto dto);

    /// <summary>
    /// Get authorization by ID
    /// </summary>
    Task<AuthorizationDto> GetAuthorizationByIdAsync(int authorizationId);

    /// <summary>
    /// Get authorization alerts for patients with low remaining authorized visits.
    /// This is separate from CareEpisode - tracks authorization status across ALL insurances for a patient.
    /// </summary>
    Task<List<PatientAuthorizationAlertDto>> GetAuthorizationAlertsAsync(int threshold);

    /// <summary>
    /// Calculate total authorized visits and total visits used for a patient across all insurances.
    /// </summary>
    Task<(int totalAuthorized, int totalUsed)> GetPatientAuthorizationSummaryAsync(int patientId);

    /// <summary>
    /// Create a new authorization record directly (without duplicate checking).
    /// Used for manual authorization entry by Clinic Admin and Front Desk.
    /// </summary>
    Task<AuthorizationDto> CreateAuthorizationAsync(AuthorizationCreateDto dto);

    /// <summary>
    /// Update an existing authorization record.
    /// </summary>
    Task<AuthorizationDto> UpdateAuthorizationAsync(int authorizationId, AuthorizationUpdateDto dto);

    /// <summary>
    /// Delete an authorization record.
    /// </summary>
    Task<bool> DeleteAuthorizationAsync(int authorizationId);

    /// <summary>
    /// Fetch authorization data from mock API for an insurance record.
    /// </summary>
    Task<MockAuthorizationFetchDto> FetchMockAuthorizationAsync(int insuranceId);
}

public class AuthorizationService : IInsuranceAuthorizationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryptionHelper;

    public AuthorizationService(EhrDbContext context, ITenantProvider tenantProvider, EncryptionHelper encryptionHelper)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
    }

    /// <summary>
    /// Count completed appointments for a patient.
    /// Completed = CheckedIn, InProgress, or Completed status.
    /// </summary>
    private async Task<int> CountCompletedAppointmentsForPatientAsync(int patientId)
    {
        var query = _context.Appointments
            .Where(a => a.PatientId == patientId)
            .Where(a => a.Status == (int)AppointmentStatus.CheckedIn ||
                       a.Status == (int)AppointmentStatus.InProgress ||
                       a.Status == (int)AppointmentStatus.Completed);

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == _tenantProvider.TenantId.Value);
        }

        return await query.CountAsync();
    }

    /// <summary>
    /// Get total authorized visits for a patient across all insurances and authorizations.
    /// </summary>
    private async Task<int> GetTotalAuthorizedVisitsForPatientAsync(int patientId)
    {
        var query = from auth in _context.Authorizations
                   join ins in _context.Insurances on auth.InsuranceId equals ins.InsuranceId
                   where ins.PatientId == patientId
                   select auth.AuthorizedVisits;

        if (_tenantProvider.TenantId.HasValue)
        {
            query = from auth in _context.Authorizations
                   join ins in _context.Insurances on auth.InsuranceId equals ins.InsuranceId
                   where ins.PatientId == patientId && ins.TenantId == _tenantProvider.TenantId.Value
                   select auth.AuthorizedVisits;
        }

        return await query.SumAsync();
    }

    public async Task<(int totalAuthorized, int totalUsed)> GetPatientAuthorizationSummaryAsync(int patientId)
    {
        var totalAuthorized = await GetTotalAuthorizedVisitsForPatientAsync(patientId);
        var totalUsed = await CountCompletedAppointmentsForPatientAsync(patientId);
        return (totalAuthorized, totalUsed);
    }

    public async Task<List<AuthorizationDto>> GetAuthorizationsForInsuranceAsync(int insuranceId)
    {
        // Verify insurance belongs to tenant
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.InsuranceId == insuranceId);

        if (insurance == null)
            return new List<AuthorizationDto>();

        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            return new List<AuthorizationDto>();

        var authorizations = await _context.Authorizations
            .Where(a => a.InsuranceId == insuranceId)
            .OrderByDescending(a => a.DateOfValidation)
            .ToListAsync();

        // Calculate total visits used for this patient
        var visitsUsed = await CountCompletedAppointmentsForPatientAsync(insurance.PatientId);

        // Mark the first one (most recent) as current
        var dtos = authorizations.Select((a, index) => new AuthorizationDto
        {
            AuthorizationId = a.AuthorizationId,
            InsuranceId = a.InsuranceId,
            AuthorizationNumber = a.AuthorizationNumber,
            ExpiryDate = a.ExpiryDate,
            AuthorizedVisits = a.AuthorizedVisits,
            VisitsUsed = visitsUsed, // Calculated dynamically
            VisitsRemaining = a.AuthorizedVisits - visitsUsed,
            DateOfValidation = a.DateOfValidation,
            Notes = a.Notes,
            IsCurrent = index == 0 // First one is current
        }).ToList();

        return dtos;
    }

    public async Task<AuthorizationDto> GetCurrentAuthorizationAsync(int insuranceId)
    {
        // Verify insurance belongs to tenant
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.InsuranceId == insuranceId);

        if (insurance == null)
            return null;

        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            return null;

        var authorization = await _context.Authorizations
            .Where(a => a.InsuranceId == insuranceId)
            .OrderByDescending(a => a.DateOfValidation)
            .FirstOrDefaultAsync();

        if (authorization == null)
            return null;

        // Calculate total visits used for this patient
        var visitsUsed = await CountCompletedAppointmentsForPatientAsync(insurance.PatientId);

        return new AuthorizationDto
        {
            AuthorizationId = authorization.AuthorizationId,
            InsuranceId = authorization.InsuranceId,
            AuthorizationNumber = authorization.AuthorizationNumber,
            ExpiryDate = authorization.ExpiryDate,
            AuthorizedVisits = authorization.AuthorizedVisits,
            VisitsUsed = visitsUsed, // Calculated dynamically
            VisitsRemaining = authorization.AuthorizedVisits - visitsUsed,
            DateOfValidation = authorization.DateOfValidation,
            Notes = authorization.Notes,
            IsCurrent = true
        };
    }

    public async Task<AuthorizationHistoryDto> GetAuthorizationHistoryAsync(int insuranceId)
    {
        // Verify insurance belongs to tenant
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.InsuranceId == insuranceId);

        if (insurance == null)
            return null;

        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            return null;

        var authorizations = await GetAuthorizationsForInsuranceAsync(insuranceId);

        return new AuthorizationHistoryDto
        {
            InsuranceId = insuranceId,
            InsuranceName = insurance.PayerName,
            TotalAuthorizations = authorizations.Count,
            CurrentAuthorization = authorizations.FirstOrDefault(),
            AllAuthorizations = authorizations
        };
    }

    public async Task<AuthorizationDto> CreateAuthorizationIfNewAsync(AuthorizationCreateDto dto)
    {
        // Verify insurance exists and belongs to tenant
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.InsuranceId == dto.InsuranceId);

        if (insurance == null)
            throw new InvalidOperationException("Insurance not found");

        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            throw new InvalidOperationException("Insurance not found");

        // Check if the most recent authorization has the same authorization number
        var existingAuth = await _context.Authorizations
            .Where(a => a.InsuranceId == dto.InsuranceId)
            .OrderByDescending(a => a.DateOfValidation)
            .FirstOrDefaultAsync();

        // If authorization number is the same as existing, don't create a duplicate
        if (existingAuth != null &&
            !string.IsNullOrWhiteSpace(existingAuth.AuthorizationNumber) &&
            !string.IsNullOrWhiteSpace(dto.AuthorizationNumber) &&
            existingAuth.AuthorizationNumber.Equals(dto.AuthorizationNumber, StringComparison.OrdinalIgnoreCase))
        {
            // Calculate total visits used for this patient
            var visitsUsed = await CountCompletedAppointmentsForPatientAsync(insurance.PatientId);

            // Return the existing authorization instead of creating a duplicate
            return new AuthorizationDto
            {
                AuthorizationId = existingAuth.AuthorizationId,
                InsuranceId = existingAuth.InsuranceId,
                AuthorizationNumber = existingAuth.AuthorizationNumber,
                ExpiryDate = existingAuth.ExpiryDate,
                AuthorizedVisits = existingAuth.AuthorizedVisits,
                VisitsUsed = visitsUsed, // Calculated dynamically
                VisitsRemaining = existingAuth.AuthorizedVisits - visitsUsed,
                DateOfValidation = existingAuth.DateOfValidation,
                Notes = existingAuth.Notes,
                IsCurrent = true
            };
        }

        // Create new authorization record (no VisitsUsed stored - it's calculated)
        var authorization = new Authorization
        {
            TenantId = insurance.TenantId,
            InsuranceId = dto.InsuranceId,
            AuthorizationNumber = dto.AuthorizationNumber,
            ExpiryDate = dto.ExpiryDate,
            AuthorizedVisits = dto.AuthorizedVisits,
            DateOfValidation = DateTime.UtcNow,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Authorizations.Add(authorization);
        await _context.SaveChangesAsync();

        // Calculate total visits used for this patient
        var visitsUsedNew = await CountCompletedAppointmentsForPatientAsync(insurance.PatientId);

        return new AuthorizationDto
        {
            AuthorizationId = authorization.AuthorizationId,
            InsuranceId = authorization.InsuranceId,
            AuthorizationNumber = authorization.AuthorizationNumber,
            ExpiryDate = authorization.ExpiryDate,
            AuthorizedVisits = authorization.AuthorizedVisits,
            VisitsUsed = visitsUsedNew, // Calculated dynamically
            VisitsRemaining = authorization.AuthorizedVisits - visitsUsedNew,
            DateOfValidation = authorization.DateOfValidation,
            Notes = authorization.Notes,
            IsCurrent = true
        };
    }

    public async Task<AuthorizationDto> GetAuthorizationByIdAsync(int authorizationId)
    {
        var authorization = await _context.Authorizations
            .Include(a => a.Insurance)
            .FirstOrDefaultAsync(a => a.AuthorizationId == authorizationId);

        if (authorization == null)
            return null;

        if (_tenantProvider.TenantId.HasValue && authorization.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Determine if this is the current authorization
        var isCurrent = await _context.Authorizations
            .Where(a => a.InsuranceId == authorization.InsuranceId)
            .OrderByDescending(a => a.DateOfValidation)
            .Select(a => a.AuthorizationId)
            .FirstOrDefaultAsync() == authorizationId;

        // Calculate total visits used for this patient
        var visitsUsed = await CountCompletedAppointmentsForPatientAsync(authorization.Insurance.PatientId);

        return new AuthorizationDto
        {
            AuthorizationId = authorization.AuthorizationId,
            InsuranceId = authorization.InsuranceId,
            AuthorizationNumber = authorization.AuthorizationNumber,
            ExpiryDate = authorization.ExpiryDate,
            AuthorizedVisits = authorization.AuthorizedVisits,
            VisitsUsed = visitsUsed, // Calculated dynamically
            VisitsRemaining = authorization.AuthorizedVisits - visitsUsed,
            DateOfValidation = authorization.DateOfValidation,
            Notes = authorization.Notes,
            IsCurrent = isCurrent
        };
    }

    /// <summary>
    /// Get authorization alerts for patients with low remaining authorized visits.
    /// This is separate from CareEpisode - tracks authorization status across ALL insurances for a patient.
    /// </summary>
    public async Task<List<PatientAuthorizationAlertDto>> GetAuthorizationAlertsAsync(int threshold)
    {
        if (!_tenantProvider.TenantId.HasValue)
            return new List<PatientAuthorizationAlertDto>();

        // Get all active patients with insurance
        // AsNoTracking: read-only alert projection; Patient is decrypted in-memory
        // before DTO mapping below.
        var patientsWithInsurance = await _context.Insurances
            .AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.Authorizations)
            .Where(i => i.TenantId == _tenantProvider.TenantId.Value)
            .Where(i => i.IsActive == true)
            .GroupBy(i => i.PatientId)
            .Select(g => new
            {
                PatientId = g.Key,
                Patient = g.First().Patient,
                Insurances = g.ToList()
            })
            .ToListAsync();

        // Decrypt patient PHI for each group (previously missing — patient names
        // came through as encrypted base64 in the alert DTOs).
        foreach (var group in patientsWithInsurance)
        {
            if (group.Patient != null)
                _encryptionHelper.DecryptEntity(group.Patient);
        }

        var alerts = new List<PatientAuthorizationAlertDto>();

        foreach (var p in patientsWithInsurance)
        {
            // Calculate total authorized visits across all insurances
            var totalAuthorized = p.Insurances
                .SelectMany(i => i.Authorizations)
                .Sum(a => a.AuthorizedVisits);

            // Calculate total visits used (completed appointments)
            var totalUsed = await CountCompletedAppointmentsForPatientAsync(p.PatientId);

            var remaining = totalAuthorized - totalUsed;

            // Find earliest expiring authorization
            var nextExpiry = p.Insurances
                .SelectMany(i => i.Authorizations)
                .Where(a => a.ExpiryDate.HasValue)
                .OrderBy(a => a.ExpiryDate)
                .FirstOrDefault()?.ExpiryDate;

            // Determine alert type
            string alertType = null;
            string alertMessage = null;

            if (totalAuthorized == 0)
            {
                alertType = "NoActiveAuthorization";
                alertMessage = "Patient has no active insurance authorizations.";
            }
            else if (remaining <= threshold && remaining >= 0)
            {
                alertType = "LowAuthorizedVisits";
                alertMessage = $"Only {remaining} authorized visits remaining. Consider requesting re-authorization.";
            }
            else if (nextExpiry.HasValue && nextExpiry.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(14)))
            {
                alertType = "AuthorizationExpiring";
                alertMessage = $"Authorization expiring on {nextExpiry.Value:MM/dd/yyyy}. Consider requesting re-authorization.";
            }

            if (alertType != null)
            {
                alerts.Add(new PatientAuthorizationAlertDto
                {
                    PatientId = p.PatientId,
                    PatientName = p.Patient != null ? $"{p.Patient.FirstName} {p.Patient.LastName}" : "",
                    AlertType = alertType,
                    AlertMessage = alertMessage,
                    TotalAuthorizedVisits = totalAuthorized,
                    TotalVisitsUsed = totalUsed,
                    RemainingAuthorizedVisits = remaining,
                    AlertThreshold = threshold,
                    NextExpiryDate = nextExpiry,
                    InsuranceNames = p.Insurances.Select(i => i.PayerName).ToList()
                });
            }
        }

        return alerts;
    }

    /// <summary>
    /// Create a new authorization record directly (without duplicate checking).
    /// Used for manual authorization entry by Clinic Admin and Front Desk.
    /// </summary>
    public async Task<AuthorizationDto> CreateAuthorizationAsync(AuthorizationCreateDto dto)
    {
        // Verify insurance exists and belongs to tenant
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.InsuranceId == dto.InsuranceId);

        if (insurance == null)
            throw new InvalidOperationException("Insurance not found");

        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            throw new InvalidOperationException("Insurance not found");

        // Create new authorization record
        var authorization = new Authorization
        {
            TenantId = insurance.TenantId,
            InsuranceId = dto.InsuranceId,
            AuthorizationNumber = dto.AuthorizationNumber,
            ExpiryDate = dto.ExpiryDate,
            AuthorizedVisits = dto.AuthorizedVisits,
            DateOfValidation = DateTime.UtcNow,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Authorizations.Add(authorization);
        await _context.SaveChangesAsync();

        // Calculate total visits used for this patient
        var visitsUsed = await CountCompletedAppointmentsForPatientAsync(insurance.PatientId);

        return new AuthorizationDto
        {
            AuthorizationId = authorization.AuthorizationId,
            InsuranceId = authorization.InsuranceId,
            AuthorizationNumber = authorization.AuthorizationNumber,
            ExpiryDate = authorization.ExpiryDate,
            AuthorizedVisits = authorization.AuthorizedVisits,
            VisitsUsed = visitsUsed,
            VisitsRemaining = authorization.AuthorizedVisits - visitsUsed,
            DateOfValidation = authorization.DateOfValidation,
            Notes = authorization.Notes,
            IsCurrent = true
        };
    }

    /// <summary>
    /// Update an existing authorization record.
    /// </summary>
    public async Task<AuthorizationDto> UpdateAuthorizationAsync(int authorizationId, AuthorizationUpdateDto dto)
    {
        var authorization = await _context.Authorizations
            .Include(a => a.Insurance)
            .FirstOrDefaultAsync(a => a.AuthorizationId == authorizationId);

        if (authorization == null)
            throw new InvalidOperationException("Authorization not found");

        if (_tenantProvider.TenantId.HasValue && authorization.TenantId != _tenantProvider.TenantId.Value)
            throw new InvalidOperationException("Authorization not found");

        // Update fields
        authorization.AuthorizationNumber = dto.AuthorizationNumber;
        authorization.ExpiryDate = dto.ExpiryDate;
        authorization.AuthorizedVisits = dto.AuthorizedVisits;
        authorization.Notes = dto.Notes;
        authorization.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Determine if this is the current authorization
        var isCurrent = await _context.Authorizations
            .Where(a => a.InsuranceId == authorization.InsuranceId)
            .OrderByDescending(a => a.DateOfValidation)
            .Select(a => a.AuthorizationId)
            .FirstOrDefaultAsync() == authorizationId;

        // Calculate total visits used for this patient
        var visitsUsed = await CountCompletedAppointmentsForPatientAsync(authorization.Insurance.PatientId);

        return new AuthorizationDto
        {
            AuthorizationId = authorization.AuthorizationId,
            InsuranceId = authorization.InsuranceId,
            AuthorizationNumber = authorization.AuthorizationNumber,
            ExpiryDate = authorization.ExpiryDate,
            AuthorizedVisits = authorization.AuthorizedVisits,
            VisitsUsed = visitsUsed,
            VisitsRemaining = authorization.AuthorizedVisits - visitsUsed,
            DateOfValidation = authorization.DateOfValidation,
            Notes = authorization.Notes,
            IsCurrent = isCurrent
        };
    }

    /// <summary>
    /// Delete an authorization record.
    /// </summary>
    public async Task<bool> DeleteAuthorizationAsync(int authorizationId)
    {
        var authorization = await _context.Authorizations
            .FirstOrDefaultAsync(a => a.AuthorizationId == authorizationId);

        if (authorization == null)
            return false;

        if (_tenantProvider.TenantId.HasValue && authorization.TenantId != _tenantProvider.TenantId.Value)
            return false;

        _context.Authorizations.Remove(authorization);
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Fetch authorization data from mock API for an insurance record.
    /// This simulates fetching authorization from a payer's system.
    /// </summary>
    public async Task<MockAuthorizationFetchDto> FetchMockAuthorizationAsync(int insuranceId)
    {
        // Verify insurance exists and belongs to tenant
        var insurance = await _context.Insurances
            .FirstOrDefaultAsync(i => i.InsuranceId == insuranceId);

        if (insurance == null)
        {
            return new MockAuthorizationFetchDto
            {
                Success = false,
                ErrorMessage = "Insurance not found"
            };
        }

        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
        {
            return new MockAuthorizationFetchDto
            {
                Success = false,
                ErrorMessage = "Insurance not found"
            };
        }

        // Mock API response - simulate fetching from payer
        // In production, this would call an external API
        var random = new Random();
        var authNumber = $"AUTH-{DateTime.Now:yyyyMMdd}-{random.Next(1000, 9999)}";
        var authorizedVisits = random.Next(10, 30);
        var expiryDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(random.Next(3, 12)));

        return new MockAuthorizationFetchDto
        {
            Success = true,
            AuthorizationNumber = authNumber,
            AuthorizedVisits = authorizedVisits,
            ExpiryDate = expiryDate,
            Notes = $"Auto-fetched authorization for {insurance.PayerName}"
        };
    }
}
