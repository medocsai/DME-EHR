using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface IPatientSearchService
{
    Task<PatientSearchResponse> SearchPatientsAsync(PatientSearchRequest request);
    Task<PatientSearchResultDto?> GetPatientForAutocompleteAsync(int patientId);
    Task<List<ProviderDropdownDto>> GetProvidersForDropdownAsync(bool activeOnly = true);
    Task<ProviderSearchResultDto?> GetProviderForDropdownAsync(int providerId);
}

/// <summary>
/// HIPAA-compliant patient search service using blind index tokens.
///
/// Performance improvement:
/// - OLD: Load ALL patients, decrypt ALL, filter in memory
/// - NEW: Hash search query, match tokens in DB, decrypt only matching patients
///
/// For 10,000 patients searching "John":
/// - OLD: Load 10,000 rows, decrypt 10,000 times, ~2-5 seconds
/// - NEW: Query tokens, get ~10 patient IDs, decrypt 10 times, ~50ms
/// </summary>
public class PatientSearchService : IPatientSearchService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IBlindIndexService _blindIndexService;

    public PatientSearchService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EncryptionHelper encryptionHelper,
        IBlindIndexService blindIndexService)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryptionHelper = encryptionHelper;
        _blindIndexService = blindIndexService;
    }

    public async Task<PatientSearchResponse> SearchPatientsAsync(PatientSearchRequest request)
    {
        // Determine effective location filter
        // Priority: 1) Explicitly provided in request, 2) Current user's location
        int? effectiveLocationId = request.LocationId ?? _locationProvider.LocationId;

        // If there's a search query, use blind index for efficient lookup
        if (!string.IsNullOrWhiteSpace(request.Query) && request.Query.Trim().Length >= 1)
        {
            return await SearchWithBlindIndexAsync(request, effectiveLocationId);
        }

        // No search query - return all patients with filters (paginated)
        return await GetAllPatientsAsync(request, effectiveLocationId);
    }

    /// <summary>
    /// Search using blind index tokens - efficient for large datasets.
    /// </summary>
    private async Task<PatientSearchResponse> SearchWithBlindIndexAsync(PatientSearchRequest request, int? locationId)
    {
        var searchTerm = request.Query.Trim().ToLowerInvariant();

        // Generate the search hash for the query
        var searchHash = _blindIndexService.GenerateSearchHash(searchTerm);

        if (string.IsNullOrEmpty(searchHash))
        {
            return new PatientSearchResponse { Results = new List<PatientSearchResultDto>(), TotalCount = 0, HasMore = false };
        }

        // Query tokens to find matching patient IDs
        var tokenQuery = _context.PatientSearchTokens
            .Where(t => t.TokenHash == searchHash);

        // Apply tenant filter
        if (_tenantProvider.TenantId.HasValue)
        {
            tokenQuery = tokenQuery.Where(t => t.TenantId == _tenantProvider.TenantId.Value);
        }

        // Apply location filter
        if (locationId.HasValue)
        {
            tokenQuery = tokenQuery.Where(t => t.LocationId == locationId.Value);
        }

        // Get distinct patient IDs that match
        var matchingPatientIds = await tokenQuery
            .Select(t => t.PatientId)
            .Distinct()
            .ToListAsync();

        if (matchingPatientIds.Count == 0)
        {
            return new PatientSearchResponse { Results = new List<PatientSearchResultDto>(), TotalCount = 0, HasMore = false };
        }

        // Now load only the matching patients
        // AsNoTracking: read-only search; Patient entities decrypted in-place
        // for DTOs and must not be tracked.
        var patientQuery = _context.Patients
            .AsNoTracking()
            .Include(p => p.Insurances)
            .Include(p => p.CareEpisodes)
            .Where(p => matchingPatientIds.Contains(p.PatientId));

        // Apply additional filters
        if (request.ActiveOnly)
        {
            patientQuery = patientQuery.Where(p => (p.IsDeleted == null || p.IsDeleted == false) && !p.IsArchived);
        }

        if (request.PreferredProviderId.HasValue)
        {
            patientQuery = patientQuery.Where(p => p.PreferredProviderId == request.PreferredProviderId.Value);
        }

        var patients = await patientQuery.ToListAsync();

        // Decrypt only the matching patients
        foreach (var patient in patients)
        {
            _encryptionHelper.DecryptEntity(patient);
        }

        var totalCount = patients.Count;

        // Sort and paginate
        var results = patients
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Skip(request.Skip)
            .Take(Math.Min(request.Take, 100))
            .Select(MapToSearchResultDto)
            .ToList();

        return new PatientSearchResponse
        {
            Results = results,
            TotalCount = totalCount,
            HasMore = (request.Skip + results.Count) < totalCount
        };
    }

    /// <summary>
    /// Get all patients without search query (with pagination).
    /// Used when user opens patient selector without typing.
    /// </summary>
    private async Task<PatientSearchResponse> GetAllPatientsAsync(PatientSearchRequest request, int? locationId)
    {
        // AsNoTracking: read-only paged list; Patient decrypted in-place for DTO.
        var query = _context.Patients
            .AsNoTracking()
            .Include(p => p.Insurances)
            .Include(p => p.CareEpisodes)
            .AsQueryable();

        // Apply tenant filter
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        // Apply location filter
        if (locationId.HasValue)
        {
            query = query.Where(p => p.PreferredLocationId == locationId.Value);
        }

        // Apply active filter
        if (request.ActiveOnly)
        {
            query = query.Where(p => (p.IsDeleted == null || p.IsDeleted == false) && !p.IsArchived);
        }

        // Apply provider filter
        if (request.PreferredProviderId.HasValue)
        {
            query = query.Where(p => p.PreferredProviderId == request.PreferredProviderId.Value);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Paginate at database level for efficiency
        var patients = await query
            .OrderByDescending(p => p.PatientId)
            .Skip(request.Skip)
            .Take(Math.Min(request.Take, 100))
            .ToListAsync();

        // Decrypt only the paginated results
        foreach (var patient in patients)
        {
            _encryptionHelper.DecryptEntity(patient);
        }

        var results = patients.Select(MapToSearchResultDto).ToList();

        return new PatientSearchResponse
        {
            Results = results,
            TotalCount = totalCount,
            HasMore = (request.Skip + results.Count) < totalCount
        };
    }

    /// <summary>
    /// Map a Patient entity to PatientSearchResultDto.
    /// </summary>
    private PatientSearchResultDto MapToSearchResultDto(Patient p)
    {
        return new PatientSearchResultDto
        {
            PatientId = p.PatientId,
            MRN = p.Mrn,
            FirstName = p.FirstName,
            LastName = p.LastName,
            DateOfBirth = p.DateOfBirth,
            Phone = p.Phone,
            Email = p.Email,
            Gender = p.Gender,
            Status = CalculateEffectivePatientStatus(p),
            City = p.City,
            PrimaryInsurance = p.Insurances?
                .Where(i => i.Type == 0 && i.IsActive == true)
                .Select(i => i.PayerName)
                .FirstOrDefault(),
            IsArchived = p.IsArchived
        };
    }

    public async Task<PatientSearchResultDto?> GetPatientForAutocompleteAsync(int patientId)
    {
        // AsNoTracking: read-only autocomplete lookup; Patient decrypted for DTO.
        var query = _context.Patients
            .AsNoTracking()
            .Include(p => p.Insurances)
            .Include(p => p.CareEpisodes)
            .Where(p => p.PatientId == patientId);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        var patient = await query.FirstOrDefaultAsync();

        if (patient == null) return null;

        // Decrypt PHI fields
        _encryptionHelper.DecryptEntity(patient);

        return MapToSearchResultDto(patient);
    }

    /// <summary>
    /// Calculate effective patient status based on care episodes in real-time.
    /// </summary>
    private static int CalculateEffectivePatientStatus(Patient patient)
    {
        var careEpisodes = patient.CareEpisodes?.ToList() ?? new List<CareEpisode>();

        if (careEpisodes.Count == 0)
            return (int)PatientStatus.Active;

        var hasActiveCareEpisode = careEpisodes.Any(ce =>
            ce.Status == (int)CareEpisodeStatus.Active ||
            ce.Status == (int)CareEpisodeStatus.Overdue);

        if (hasActiveCareEpisode)
            return (int)PatientStatus.Active;

        var hasCompletedCareEpisode = careEpisodes.Any(ce => ce.Status == (int)CareEpisodeStatus.Completed);
        if (hasCompletedCareEpisode)
            return (int)PatientStatus.Inactive;

        return (int)PatientStatus.Active;
    }

    public async Task<List<ProviderDropdownDto>> GetProvidersForDropdownAsync(bool activeOnly = true)
    {
        // AsNoTracking: read-only dropdown list; Provider decrypted for DTO.
        var query = _context.Providers.AsNoTracking().AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);

        if (activeOnly)
            query = query.Where(p => p.IsActive == true);

        var providers = await query.ToListAsync();

        // Decrypt PHI fields
        foreach (var provider in providers)
        {
            _encryptionHelper.DecryptEntity(provider);
        }

        return providers
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Select(p => new ProviderDropdownDto
            {
                ProviderId = p.ProviderId,
                FirstName = p.FirstName,
                LastName = p.LastName,
                DisplayName = p.LastName + ", " + p.FirstName + (p.Credentials != null ? ", " + p.Credentials : ""),
                Credentials = p.Credentials,
                Specialty = p.Specialty,
                Color = p.Color,
                IsActive = p.IsActive ?? false
            })
            .ToList();
    }

    public async Task<ProviderSearchResultDto?> GetProviderForDropdownAsync(int providerId)
    {
        // AsNoTracking: read-only dropdown lookup; Provider decrypted for DTO.
        var query = _context.Providers
            .AsNoTracking()
            .Where(p => p.ProviderId == providerId);

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        var provider = await query.FirstOrDefaultAsync();

        if (provider == null) return null;

        _encryptionHelper.DecryptEntity(provider);

        return new ProviderSearchResultDto
        {
            ProviderId = provider.ProviderId,
            DisplayName = provider.LastName + ", " + provider.FirstName + (provider.Credentials != null ? ", " + provider.Credentials : ""),
            Specialty = provider.Specialty,
            Color = provider.Color,
            IsActive = provider.IsActive ?? false
        };
    }
}
