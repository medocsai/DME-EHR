using Microsoft.EntityFrameworkCore;
using EHR.Data;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using System.Text.Json;

namespace EHR.Services;

public interface IPatientService
{
    Task<List<PatientListDto>> GetPatientsAsync(string? search = null, List<PatientStatus>? statuses = null, int? providerId = null, bool noCareEpisode = false, bool? isArchived = null, bool? isProfileComplete = null);
    Task<PatientDetailDto?> GetPatientByIdAsync(int patientId, int? providerId = null);
    Task<Patient> CreatePatientAsync(PatientCreateDto dto);
    Task<Patient?> UpdatePatientAsync(int patientId, PatientUpdateDto dto);
    Task<bool> DeletePatientAsync(int patientId);
    Task<string> GenerateMRNAsync();
    Task<bool> ArchivePatientAsync(int patientId);
    Task<bool> UnarchivePatientAsync(int patientId);
    Task<(bool IsUnique, string? ExistingPatientName)> CheckEmailUniqueAsync(string email, int? excludePatientId = null);
}

public class PatientService : IPatientService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IPatientValidationService _validationService;
    private readonly IBlindIndexService _blindIndexService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public PatientService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EncryptionHelper encryptionHelper,
        IPatientValidationService validationService,
        IBlindIndexService blindIndexService,
        IEmailService emailService,
        IConfiguration config)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryptionHelper = encryptionHelper;
        _validationService = validationService;
        _blindIndexService = blindIndexService;
        _emailService = emailService;
        _config = config;
    }
    
    public async Task<List<PatientListDto>> GetPatientsAsync(
        string? search = null,
        List<PatientStatus>? statuses = null,
        int? providerId = null,
        bool noCareEpisode = false,
        bool? isArchived = null,
        bool? isProfileComplete = null)
    {
        List<Patient> patients;

        // Use blind index for search if query provided (HIPAA-compliant, scalable)
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Generate hash for the search query
            var searchHash = _blindIndexService.GenerateSearchHash(search);

            // Build token query with tenant/location filtering
            var tokenQuery = _context.PatientSearchTokens.AsQueryable();

            if (_tenantProvider.TenantId.HasValue)
            {
                tokenQuery = tokenQuery.Where(t => t.TenantId == _tenantProvider.TenantId.Value);
            }

            if (_locationProvider.LocationId.HasValue)
            {
                tokenQuery = tokenQuery.Where(t => t.LocationId == _locationProvider.LocationId.Value);
            }

            // Find matching patient IDs via blind index
            var matchingPatientIds = await tokenQuery
                .Where(t => t.TokenHash == searchHash)
                .Select(t => t.PatientId)
                .Distinct()
                .ToListAsync();

            // Now load only matching patients with all includes
            // AsNoTracking: this is a read-only list projection; the loaded entities
            // are decrypted in-place for DTO construction below. Without NoTracking,
            // any SaveChangesAsync later in the same request scope (audit log, blind
            // index, validation, etc.) would flush the decrypted patients back to
            // the DB, silently corrupting encrypted PHI to plaintext.
            var query = _context.Patients
                .AsNoTracking()
                .Include(p => p.Insurances)
                .Include(p => p.Appointments)
                .Include(p => p.CareEpisodes)
                .Include(p => p.PatientValidation)
                .Where(p => p.IsDeleted != true && matchingPatientIds.Contains(p.PatientId))
                .AsQueryable();

            // Apply additional filters
            if (providerId.HasValue)
            {
                query = query.Where(p => p.Appointments.Any(a => a.ProviderId == providerId));
            }
            if (isArchived.HasValue)
            {
                query = query.Where(p => p.IsArchived == isArchived.Value);
            }
            if (noCareEpisode)
            {
                query = query.Where(p => !p.CareEpisodes.Any(ce => ce.Status == (int)CareEpisodeStatus.Active));
            }
            if (isProfileComplete.HasValue)
            {
                if (isProfileComplete.Value)
                    query = query.Where(p => p.PatientValidation != null && p.PatientValidation.IsComplete == true);
                else
                    query = query.Where(p => p.PatientValidation == null || p.PatientValidation.IsComplete == false);
            }

            patients = await query.OrderByDescending(p => p.PatientId).ToListAsync();
        }
        else
        {
            // No search query - load all patients with filtering
            // AsNoTracking: read-only list projection; entities are decrypted
            // in-place for DTOs and must not be tracked — otherwise any later
            // SaveChangesAsync in the same request scope would flush decrypted
            // values back to the DB.
            var query = _context.Patients
                .AsNoTracking()
                .Include(p => p.Insurances)
                .Include(p => p.Appointments)
                .Include(p => p.CareEpisodes)
                .Include(p => p.PatientValidation)
                .Where(p => p.IsDeleted != true)
                .AsQueryable();

            // CRITICAL: Tenant data isolation - filter by TenantId
            if (_tenantProvider.TenantId.HasValue)
            {
                query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
            }

            // LOCATION FILTERING: Filter by location within the tenant
            if (_locationProvider.LocationId.HasValue)
            {
                query = query.Where(p => p.PreferredLocationId == _locationProvider.LocationId.Value);
            }

            // Filter by provider
            if (providerId.HasValue)
            {
                query = query.Where(p => p.Appointments.Any(a => a.ProviderId == providerId));
            }

            // Filter by archived status
            if (isArchived.HasValue)
            {
                query = query.Where(p => p.IsArchived == isArchived.Value);
            }

            // Filter by no care episode
            if (noCareEpisode)
            {
                query = query.Where(p => !p.CareEpisodes.Any(ce => ce.Status == (int)CareEpisodeStatus.Active));
            }

            // Filter by profile completeness
            if (isProfileComplete.HasValue)
            {
                if (isProfileComplete.Value)
                    query = query.Where(p => p.PatientValidation != null && p.PatientValidation.IsComplete == true);
                else
                    query = query.Where(p => p.PatientValidation == null || p.PatientValidation.IsComplete == false);
            }

            patients = await query.OrderByDescending(p => p.PatientId).ToListAsync();
        }

        // Decrypt PHI fields for matched patients only
        foreach (var patient in patients)
        {
            _encryptionHelper.DecryptEntity(patient);
        }

        // Build list DTOs with calculated status
        var patientDtos = patients.Select(p => new PatientListDto
        {
            PatientId = p.PatientId,
            MRN = p.Mrn,
            FirstName = p.FirstName,
            LastName = p.LastName,
            DateOfBirth = p.DateOfBirth,
            Phone = p.Phone,
            Email = p.Email,
            Status = CalculateEffectivePatientStatus(p),
            PrimaryInsurance = p.Insurances.FirstOrDefault(i => i.Type == (int)InsuranceType.Primary && i.IsActive == true)?.PayerName,
            LastVisit = p.Appointments
                .Where(a => a.Status == (int)AppointmentStatus.Completed)
                .OrderByDescending(a => a.StartTime)
                .Select(a => (DateTime?)a.StartTime)
                .FirstOrDefault(),
            IsArchived = p.IsArchived,
            // Profile completeness from validation
            ProfileCompleteness = p.PatientValidation != null
                ? (p.PatientValidation.IsComplete ? 100 : CalculateProfileCompleteness(p.PatientValidation.MissingFieldsCount))
                : null,
            ProfileStatus = p.PatientValidation != null
                ? (p.PatientValidation.IsComplete ? 2 : 1)
                : null,
            HasProfilePicture = !string.IsNullOrEmpty(p.ProfilePicturePath)
        }).ToList();

        // Apply status filter AFTER calculating effective status (since status is calculated from care episodes)
        if (statuses != null && statuses.Count > 0)
        {
            var statusValues = statuses.Select(s => (int)s).ToList();
            patientDtos = patientDtos.Where(p => statusValues.Contains(p.Status)).ToList();
        }

        return patientDtos;
    }

    /// <summary>
    /// Calculate profile completeness percentage based on missing fields count.
    /// Total required fields: 16 (Basic: 6, Address: 4, Emergency: 3, Insurance: 3)
    /// Note: SubscriberName and SubscriberRelationship removed from Insurance validation
    /// as these fields are not present in the patient insurance form UI
    /// </summary>
    private static int CalculateProfileCompleteness(int missingFieldsCount)
    {
        const int totalRequiredFields = 16;
        var filledFields = totalRequiredFields - missingFieldsCount;
        if (filledFields < 0) filledFields = 0;
        return (int)Math.Round((double)filledFields / totalRequiredFields * 100);
    }

    /// <summary>
    /// Calculate effective patient status based on care episodes in real-time.
    /// - If patient has no care episodes → Active (new patient, no visits yet)
    /// - If patient has active/overdue care episodes → Active (0)
    /// - If patient has only completed care episodes → Inactive (1)
    /// </summary>
    private int CalculateEffectivePatientStatus(Patient patient)
    {
        // Calculate status based on care episodes
        var careEpisodes = patient.CareEpisodes?.ToList() ?? new List<CareEpisode>();

        if (careEpisodes.Count == 0)
        {
            // No care episodes - patient is still Active (new patient)
            return (int)PatientStatus.Active;
        }

        // Check if patient has any active or overdue care episodes
        var hasActiveCareEpisode = careEpisodes.Any(ce =>
            ce.Status == (int)CareEpisodeStatus.Active ||
            ce.Status == (int)CareEpisodeStatus.Overdue);

        if (hasActiveCareEpisode)
        {
            return (int)PatientStatus.Active;
        }

        // All care episodes are completed or on hold - patient is Inactive
        var hasCompletedCareEpisode = careEpisodes.Any(ce => ce.Status == (int)CareEpisodeStatus.Completed);
        if (hasCompletedCareEpisode)
        {
            return (int)PatientStatus.Inactive;
        }

        // Default to Active
        return (int)PatientStatus.Active;
    }
    
    public async Task<PatientDetailDto?> GetPatientByIdAsync(int patientId, int? providerId = null)
    {
        // AsNoTracking: read-only detail projection. The loaded Patient (and
        // Insurances) are decrypted in-place for DTO construction. Without
        // NoTracking, any later SaveChangesAsync in this request would flush
        // the decrypted values to the DB, corrupting PHI.
        var query = _context.Patients
            .AsNoTracking()
            .Include(p => p.Insurances)
                .ThenInclude(i => i.Authorizations)
            .Include(p => p.Consents)
            .Include(p => p.CareEpisodes)
                .ThenInclude(ce => ce.PrimaryProvider)
            .Include(p => p.PreferredProvider)
            .Include(p => p.Appointments)
            .Include(p => p.PatientValidation)
            .Where(p => p.PatientId == patientId && p.IsDeleted == false);

        // CRITICAL: Tenant data isolation - verify patient belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        // LOCATION FILTERING: Verify patient belongs to current location
        // Note: For GetById, we allow viewing patient details even from different locations
        // within the same tenant, but only if accessed directly (e.g., from appointment)
        // This allows providers to see patient info when they have cross-location appointments
        if (_locationProvider.LocationId.HasValue)
        {
            query = query.Where(p => p.PreferredLocationId == _locationProvider.LocationId.Value);
        }

        // Filter by provider - therapists can only view patients they have appointments with
        if (providerId.HasValue)
        {
            query = query.Where(p => p.Appointments.Any(a => a.ProviderId == providerId));
        }

        var patient = await query.FirstOrDefaultAsync();

        if (patient == null)
            return null;

        // Decrypt patient PHI fields
        _encryptionHelper.DecryptEntity(patient);

        // Decrypt insurance PHI fields
        foreach (var insurance in patient.Insurances)
        {
            _encryptionHelper.DecryptEntity(insurance);
        }

        return new PatientDetailDto
        {
            PatientId = patient.PatientId,
            MRN = patient.Mrn,
            FirstName = patient.FirstName,
            LastName = patient.LastName,
            DateOfBirth = patient.DateOfBirth,
            Phone = patient.Phone,
            Email = patient.Email,
            Status = CalculateEffectivePatientStatus(patient),
            Gender = patient.Gender,
            Ssn = patient.SsnEncrypted,
            Address = patient.Address,
            City = patient.City,
            State = patient.State,
            ZipCode = patient.ZipCode,
            EmergencyContactName = patient.EmergencyContactName,
            EmergencyContactPhone = patient.EmergencyContactPhone,
            EmergencyContactAltPhone = patient.EmergencyContactAltPhone,
            EmergencyContactRelation = patient.EmergencyContactRelation,
            PreferredProviderId = patient.PreferredProviderId,
            PreferredProviderName = patient.PreferredProvider != null ? $"{patient.PreferredProvider.FirstName} {patient.PreferredProvider.LastName}" : null,
            DateOfInjury = patient.DateOfInjury,
            Insurances = patient.Insurances
                .Where(i => i.IsActive == true)
                .Select(i => new InsuranceDto
                {
                    InsuranceId = i.InsuranceId,
                    PatientId = i.PatientId,
                    PayerName = i.PayerName,
                    PayerId = i.PayerId,
                    PolicyNumber = i.PolicyNumber,
                    GroupNumber = i.GroupNumber,
                    SubscriberName = i.SubscriberName,
                    SubscriberFirstName = i.SubscriberFirstName,
                    SubscriberLastName = i.SubscriberLastName,
                    SubscriberId = i.SubscriberId,
                    SubscriberDob = i.SubscriberDob,
                    SubscriberRelationship = i.SubscriberRelationship,
                    Type = i.Type ?? 0,
                    IsActive = i.IsActive ?? false,
                    EligibilityStatus = i.EligibilityStatus,
                    Copay = i.Copay,
                    Coinsurance = i.Coinsurance,
                    Deductible = i.DeductibleTotal,
                    DeductibleMet = i.DeductibleMet,
                    CoverageNotes = i.CoverageNotes,
                    EffectiveStartDate = i.EffectiveFrom,
                    EffectiveEndDate = i.EffectiveTo,
                    LastVerifiedAt = i.LastVerifiedAt,
                    AttorneyEmail = i.AttorneyEmail,
                    AttorneyName = i.AttorneyName,
                    AttorneyPhone = i.AttorneyPhone,
                    InsuranceCategory = i.InsuranceCategory,
                    AllowedVisits = i.AllowedVisits,
                    // Include authorization history for the insurance
                    Authorizations = i.Authorizations
                        .OrderByDescending(a => a.DateOfValidation)
                        .Select(a => new AuthorizationDto
                        {
                            AuthorizationId = a.AuthorizationId,
                            InsuranceId = a.InsuranceId,
                            AuthorizationNumber = a.AuthorizationNumber,
                            ExpiryDate = a.ExpiryDate,
                            AuthorizedVisits = a.AuthorizedVisits,
                            DateOfValidation = a.DateOfValidation,
                            Notes = a.Notes,
                            // IsCurrent is true only for the most recent (first one after ordering)
                            IsCurrent = a.AuthorizationId == i.Authorizations
                                .OrderByDescending(x => x.DateOfValidation)
                                .Select(x => x.AuthorizationId)
                                .FirstOrDefault()
                        }).ToList()
                }).ToList(),
            CareEpisodes = patient.CareEpisodes
                .OrderByDescending(ce => ce.StartDate)
                .Select(ce => new CareEpisodeDto
                {
                    CareEpisodeId = ce.CareEpisodeId,
                    PatientId = ce.PatientId,
                    StartDate = ce.StartDate,
                    EndDate = ce.EndDate,
                    PrimaryDiagnosis = ce.PrimaryDiagnosisCode != null ? $"{ce.PrimaryDiagnosisCode} - {ce.PrimaryDiagnosisDescription}" : null,
                    DiagnosisCodes = ce.SecondaryDiagnoses,
                    PrimaryProviderId = ce.PrimaryProviderId,
                    PrimaryProviderName = ce.PrimaryProvider != null ? $"{ce.PrimaryProvider.FirstName} {ce.PrimaryProvider.LastName}" : null,
                    // Note: VisitsUsed is calculated dynamically from appointments by the service
                    VisitsUsed = CalculateVisitsUsedForEpisode(ce.Appointments),
                    ExpectedVisits = ce.ExpectedVisits,
                    VisitsRemaining = (ce.ExpectedVisits ?? 0) - CalculateVisitsUsedForEpisode(ce.Appointments),
                    MissedVisits = CalculateMissedVisitsForEpisode(ce.Appointments),
                    Status = ce.Status ?? 0
                }).ToList(),
            ValidationStatus = patient.PatientValidation != null ? MapValidationToDto(patient.PatientValidation) : null,
            IsArchived = patient.IsArchived,
            HasProfilePicture = !string.IsNullOrEmpty(patient.ProfilePicturePath)
        };
    }

    /// <summary>
    /// Maps a PatientValidation entity to a PatientValidationStatusDto
    /// </summary>
    private PatientValidationStatusDto MapValidationToDto(PatientValidation validation)
    {
        var missingSections = new List<string>();
        var missingFieldsDetails = new Dictionary<string, List<string>>();

        if (!string.IsNullOrEmpty(validation.MissingSections))
        {
            try
            {
                missingSections = JsonSerializer.Deserialize<List<string>>(validation.MissingSections) ?? new List<string>();
            }
            catch
            {
                missingSections = new List<string>();
            }
        }

        if (!string.IsNullOrEmpty(validation.MissingFieldsDetails))
        {
            try
            {
                missingFieldsDetails = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(validation.MissingFieldsDetails)
                    ?? new Dictionary<string, List<string>>();
            }
            catch
            {
                missingFieldsDetails = new Dictionary<string, List<string>>();
            }
        }

        return new PatientValidationStatusDto
        {
            PatientId = validation.PatientId,
            IsComplete = validation.IsComplete,
            MissingFieldsCount = validation.MissingFieldsCount,
            MissingSections = missingSections,
            MissingFieldsDetails = missingFieldsDetails,
            FirstIncompleteSection = validation.FirstIncompleteSection,
            StatusMessage = validation.IsComplete
                ? "Patient profile is complete"
                : $"Missing information in: {string.Join(", ", missingSections)}",
            LastValidatedAt = validation.LastValidatedAt
        };
    }

    /// <summary>
    /// Calculate completed visits for a care episode from its appointments.
    /// Completed = CheckedIn, InProgress, or Completed status.
    /// </summary>
    private static int CalculateVisitsUsedForEpisode(ICollection<Appointment> appointments)
    {
        if (appointments == null || appointments.Count == 0)
            return 0;

        return appointments.Count(a =>
            a.Status == (int)AppointmentStatus.CheckedIn ||
            a.Status == (int)AppointmentStatus.InProgress ||
            a.Status == (int)AppointmentStatus.Completed);
    }

    /// <summary>
    /// Calculate missed visits for a care episode from its appointments.
    /// Missed = NoShow status.
    /// </summary>
    private static int CalculateMissedVisitsForEpisode(ICollection<Appointment> appointments)
    {
        if (appointments == null || appointments.Count == 0)
            return 0;

        return appointments.Count(a => a.Status == (int)AppointmentStatus.NoShow);
    }

    public async Task<Patient> CreatePatientAsync(PatientCreateDto dto)
    {
        if (!_tenantProvider.TenantId.HasValue)
            throw new InvalidOperationException("Tenant ID is required to create a patient");

        // Email duplicate is a HARD block. The frontend shows an inline red
        // error under the field on blur; if the user somehow bypasses that
        // (stale form, race, scripted call), the server stops the save with
        // a 409 Conflict that the form handler displays inline.
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var (isUnique, existingName) = await CheckEmailUniqueAsync(dto.Email);
            if (!isUnique)
                throw new InvalidOperationException(
                    $"Email '{dto.Email}' is already in use by patient {existingName}");
        }

        // Determine the location for this patient
        // Priority: 1) Explicitly provided location, 2) Current session location, 3) Tenant's default location
        int? patientLocationId = dto.PreferredLocationId;

        if (!patientLocationId.HasValue && _locationProvider.LocationId.HasValue)
        {
            patientLocationId = _locationProvider.LocationId.Value;
        }

        // If still no location, get the tenant's default (primary) location
        if (!patientLocationId.HasValue)
        {
            var defaultLocation = await _context.Locations
                .Where(l => l.TenantId == _tenantProvider.TenantId.Value && l.IsPrimary == true && l.IsActive == true)
                .FirstOrDefaultAsync();

            if (defaultLocation == null)
            {
                defaultLocation = await _context.Locations
                    .Where(l => l.TenantId == _tenantProvider.TenantId.Value && l.IsActive == true)
                    .OrderBy(l => l.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            patientLocationId = defaultLocation?.LocationId;
        }

        var patient = new Patient
        {
            TenantId = _tenantProvider.TenantId.Value,
            Mrn = await GenerateMRNAsync(),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            City = dto.City,
            State = dto.State,
            ZipCode = dto.ZipCode,
            EmergencyContactName = dto.EmergencyContactName,
            EmergencyContactPhone = dto.EmergencyContactPhone,
            EmergencyContactAltPhone = dto.EmergencyContactAltPhone,
            EmergencyContactRelation = dto.EmergencyContactRelation,
            PreferredProviderId = dto.PreferredProviderId,
            PreferredLocationId = patientLocationId,  // Set the patient's location
            DateOfInjury = dto.DateOfInjury,
            CreatedAt = DateTime.UtcNow
        };

        // Handle SSN - encrypt full SSN and hash last 4 digits for kiosk verification
        if (!string.IsNullOrWhiteSpace(dto.Ssn))
        {
            // Remove dashes and spaces from SSN
            var ssnDigitsOnly = System.Text.RegularExpressions.Regex.Replace(dto.Ssn, @"[^\d]", "");

            if (ssnDigitsOnly.Length >= 4)
            {
                // Extract last 4 digits
                var ssnLast4 = ssnDigitsOnly[^4..];

                // Encrypt the full SSN
                patient.SsnEncrypted = _encryptionHelper.Encrypt(dto.Ssn);

                // Create searchable hash of last 4 digits for kiosk verification
                patient.SsnLast4Hash = _encryptionHelper.GenerateSearchHash(ssnLast4);
            }
        }

        // Encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(patient);

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        // Add primary insurance if provided
        if (dto.PrimaryInsurance != null && !string.IsNullOrEmpty(dto.PrimaryInsurance.PayerName))
        {
            var insurance = new Insurance
            {
                TenantId = _tenantProvider.TenantId.Value,
                PatientId = patient.PatientId,
                PayerName = dto.PrimaryInsurance.PayerName,
                PayerId = dto.PrimaryInsurance.PayerId,
                PolicyNumber = dto.PrimaryInsurance.PolicyNumber ?? string.Empty,
                GroupNumber = dto.PrimaryInsurance.GroupNumber,
                SubscriberName = dto.PrimaryInsurance.SubscriberName,
                SubscriberFirstName = dto.PrimaryInsurance.SubscriberFirstName,
                SubscriberLastName = dto.PrimaryInsurance.SubscriberLastName,
                SubscriberId = dto.PrimaryInsurance.SubscriberId,
                SubscriberDob = dto.PrimaryInsurance.SubscriberDob,
                SubscriberRelationship = dto.PrimaryInsurance.SubscriberRelationship,
                Copay = dto.PrimaryInsurance.Copay,
                Deductible = dto.PrimaryInsurance.Deductible,
                EffectiveFrom = dto.PrimaryInsurance.EffectiveFrom,
                EffectiveTo = dto.PrimaryInsurance.EffectiveTo,
                InsuranceCategory = dto.PrimaryInsurance.InsuranceCategory,
                AttorneyName = dto.PrimaryInsurance.AttorneyName,
                AttorneyPhone = dto.PrimaryInsurance.AttorneyPhone,
                AttorneyEmail = dto.PrimaryInsurance.AttorneyEmail,
                AllowedVisits = dto.PrimaryInsurance.AllowedVisits,
                Type = (int)InsuranceType.Primary,
                IsActive = true,
                EligibilityStatus = (int)EligibilityStatus.PendingVerification,
                CreatedAt = DateTime.UtcNow
            };

            // Encrypt insurance PHI fields before saving
            _encryptionHelper.EncryptEntity(insurance);

            _context.Insurances.Add(insurance);
            await _context.SaveChangesAsync();

            // Create authorization record if authorization data was provided (from insurance validation)
            if (dto.PrimaryInsurance.AuthorizedVisits.HasValue && dto.PrimaryInsurance.AuthorizedVisits > 0)
            {
                var authorization = new Authorization
                {
                    TenantId = _tenantProvider.TenantId.Value,
                    InsuranceId = insurance.InsuranceId,
                    AuthorizationNumber = dto.PrimaryInsurance.AuthorizationNumber ?? string.Empty,
                    AuthorizedVisits = dto.PrimaryInsurance.AuthorizedVisits.Value,
                    ExpiryDate = dto.PrimaryInsurance.AuthorizationExpiry,
                    DateOfValidation = DateTime.UtcNow,
                    Notes = "Authorization created during patient registration",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Authorizations.Add(authorization);
                await _context.SaveChangesAsync();
            }
        }

        // Add secondary insurance if provided
        if (dto.SecondaryInsurance != null && !string.IsNullOrEmpty(dto.SecondaryInsurance.PayerName))
        {
            var secondaryInsurance = new Insurance
            {
                TenantId = _tenantProvider.TenantId.Value,
                PatientId = patient.PatientId,
                PayerName = dto.SecondaryInsurance.PayerName,
                PayerId = dto.SecondaryInsurance.PayerId,
                PolicyNumber = dto.SecondaryInsurance.PolicyNumber ?? string.Empty,
                GroupNumber = dto.SecondaryInsurance.GroupNumber,
                SubscriberName = dto.SecondaryInsurance.SubscriberName,
                SubscriberFirstName = dto.SecondaryInsurance.SubscriberFirstName,
                SubscriberLastName = dto.SecondaryInsurance.SubscriberLastName,
                SubscriberId = dto.SecondaryInsurance.SubscriberId,
                SubscriberDob = dto.SecondaryInsurance.SubscriberDob,
                SubscriberRelationship = dto.SecondaryInsurance.SubscriberRelationship,
                Copay = dto.SecondaryInsurance.Copay,
                Deductible = dto.SecondaryInsurance.Deductible,
                EffectiveFrom = dto.SecondaryInsurance.EffectiveFrom,
                EffectiveTo = dto.SecondaryInsurance.EffectiveTo,
                InsuranceCategory = dto.SecondaryInsurance.InsuranceCategory,
                AttorneyName = dto.SecondaryInsurance.AttorneyName,
                AttorneyPhone = dto.SecondaryInsurance.AttorneyPhone,
                AttorneyEmail = dto.SecondaryInsurance.AttorneyEmail,
                AllowedVisits = dto.SecondaryInsurance.AllowedVisits,
                Type = (int)InsuranceType.Secondary,
                IsActive = true,
                EligibilityStatus = (int)EligibilityStatus.PendingVerification,
                CreatedAt = DateTime.UtcNow
            };

            // Encrypt insurance PHI fields before saving
            _encryptionHelper.EncryptEntity(secondaryInsurance);

            _context.Insurances.Add(secondaryInsurance);
            await _context.SaveChangesAsync();

            // Create authorization record if authorization data was provided (from insurance validation)
            if (dto.SecondaryInsurance.AuthorizedVisits.HasValue && dto.SecondaryInsurance.AuthorizedVisits > 0)
            {
                var authorization = new Authorization
                {
                    TenantId = _tenantProvider.TenantId.Value,
                    InsuranceId = secondaryInsurance.InsuranceId,
                    AuthorizationNumber = dto.SecondaryInsurance.AuthorizationNumber ?? string.Empty,
                    AuthorizedVisits = dto.SecondaryInsurance.AuthorizedVisits.Value,
                    ExpiryDate = dto.SecondaryInsurance.AuthorizationExpiry,
                    DateOfValidation = DateTime.UtcNow,
                    Notes = "Authorization created during patient registration",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Authorizations.Add(authorization);
                await _context.SaveChangesAsync();
            }
        }

        // CRITICAL: Trigger validation BEFORE decrypting the entity.
        // The validation service calls SaveChangesAsync() which would save ALL tracked entities.
        // If we decrypt first, the decrypted Patient entity would be persisted back to the database.
        try
        {
            await _validationService.ValidatePatientAsync(patient.PatientId, "Create", null);
        }
        catch
        {
            // Validation failure should not prevent patient creation
            // The validation will be run again during the scheduled bulk validation
        }

        // CRITICAL (PHI encryption safety):
        // Detach the patient from the change tracker BEFORE decrypting. The
        // BlindIndex call below invokes SaveChangesAsync on the same DbContext,
        // which would otherwise flush the decrypted patient (Modified state)
        // back to the DB as plaintext. The previous comment here ("safe now
        // because validation has already called SaveChangesAsync") was incorrect
        // — validation is not the only subsequent SaveChanges.
        _context.Entry(patient).State = EntityState.Detached;

        // Decrypt for return (so caller gets readable data)
        _encryptionHelper.DecryptEntity(patient);

        // Generate blind index search tokens for the patient (on decrypted data)
        try
        {
            await _blindIndexService.IndexPatientAsync(patient);
        }
        catch
        {
            // Index failure should not prevent patient creation
            // Search will fall back to loading all patients if tokens are missing
        }

        // Send welcome email with portal link (fire-and-forget)
        if (!string.IsNullOrWhiteSpace(patient.Email))
        {
            var patientEmail = patient.Email;
            var patientName = $"{patient.FirstName} {patient.LastName}".Trim();
            var tenantId = patient.TenantId;

            // Fetch data BEFORE Task.Run to avoid disposed DbContext
            var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive == true && l.PortalCode != null);
            var portalCode = location?.PortalCode ?? "";
            var tenant = await _context.Tenants.FindAsync(tenantId);
            var clinicName = tenant?.Name ?? "MEDOCS";
            var portalUrl = $"{baseUrl}/Portal/{portalCode}";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendWelcomeEmailAsync(patientEmail, patientName, clinicName, portalUrl);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PatientService] Welcome email warning: {ex.Message}");
                }
            });
        }

        return patient;
    }

    public async Task<Patient?> UpdatePatientAsync(int patientId, PatientUpdateDto dto)
    {
        var patient = await _context.Patients
            .Include(p => p.Insurances)
            .FirstOrDefaultAsync(p => p.PatientId == patientId);
        if (patient == null || patient.IsDeleted == true)
            return null;

        // CRITICAL: Tenant data isolation - verify patient belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Email duplicate hard block — see CreatePatientAsync for the
        // full rationale. patientId is excluded so the patient's own
        // email isn't flagged when they save the form unchanged.
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var (isUnique, existingName) = await CheckEmailUniqueAsync(dto.Email, patientId);
            if (!isUnique)
                throw new InvalidOperationException(
                    $"Email '{dto.Email}' is already in use by patient {existingName}");
        }

        // First decrypt existing encrypted fields so we can update them
        _encryptionHelper.DecryptEntity(patient);

        // Apply updates (to decrypted values)
        if (dto.FirstName != null) patient.FirstName = dto.FirstName;
        if (dto.LastName != null) patient.LastName = dto.LastName;
        if (dto.DateOfBirth.HasValue) patient.DateOfBirth = dto.DateOfBirth.Value;
        if (dto.Gender != null) patient.Gender = dto.Gender;
        if (dto.Phone != null) patient.Phone = dto.Phone;
        if (dto.Email != null) patient.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email;
        if (dto.Address != null) patient.Address = dto.Address;
        if (dto.City != null) patient.City = dto.City;
        if (dto.State != null) patient.State = dto.State;
        if (dto.ZipCode != null) patient.ZipCode = dto.ZipCode;
        if (dto.EmergencyContactName != null) patient.EmergencyContactName = dto.EmergencyContactName;
        if (dto.EmergencyContactPhone != null) patient.EmergencyContactPhone = dto.EmergencyContactPhone;
        if (dto.EmergencyContactAltPhone != null) patient.EmergencyContactAltPhone = dto.EmergencyContactAltPhone;
        if (dto.EmergencyContactRelation != null) patient.EmergencyContactRelation = dto.EmergencyContactRelation;
        if (dto.PreferredProviderId.HasValue) patient.PreferredProviderId = dto.PreferredProviderId;
        if (dto.DateOfInjury.HasValue) patient.DateOfInjury = dto.DateOfInjury;

        // Handle SSN update - encrypt full SSN and hash last 4 digits for kiosk verification
        // Skip if SSN is a masked value (***-**-XXXX) returned from the API
        if (!string.IsNullOrWhiteSpace(dto.Ssn) && !dto.Ssn.StartsWith("***"))
        {
            // Remove dashes and spaces from SSN
            var ssnDigitsOnly = System.Text.RegularExpressions.Regex.Replace(dto.Ssn, @"[^\d]", "");

            if (ssnDigitsOnly.Length >= 4)
            {
                // Extract last 4 digits
                var ssnLast4 = ssnDigitsOnly[^4..];

                // Encrypt the full SSN
                patient.SsnEncrypted = _encryptionHelper.Encrypt(dto.Ssn);

                // Create searchable hash of last 4 digits for kiosk verification
                patient.SsnLast4Hash = _encryptionHelper.GenerateSearchHash(ssnLast4);
            }
        }

        patient.UpdatedAt = DateTime.UtcNow;

        // Re-encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(patient);

        await _context.SaveChangesAsync();

        // Handle primary insurance update/create
        if (dto.PrimaryInsurance != null && !string.IsNullOrEmpty(dto.PrimaryInsurance.PayerName))
        {
            // Find existing primary insurance
            var existingInsurance = patient.Insurances.FirstOrDefault(i => i.Type == (int)InsuranceType.Primary && i.IsActive == true);

            if (existingInsurance != null)
            {
                // Update existing insurance
                _encryptionHelper.DecryptEntity(existingInsurance);
                existingInsurance.PayerName = dto.PrimaryInsurance.PayerName;
                existingInsurance.PayerId = dto.PrimaryInsurance.PayerId ?? existingInsurance.PayerId;
                existingInsurance.PolicyNumber = dto.PrimaryInsurance.PolicyNumber;
                existingInsurance.GroupNumber = dto.PrimaryInsurance.GroupNumber;
                existingInsurance.Copay = dto.PrimaryInsurance.Copay ?? existingInsurance.Copay;
                existingInsurance.SubscriberName = dto.PrimaryInsurance.SubscriberName;
                existingInsurance.SubscriberFirstName = dto.PrimaryInsurance.SubscriberFirstName ?? existingInsurance.SubscriberFirstName;
                existingInsurance.SubscriberLastName = dto.PrimaryInsurance.SubscriberLastName ?? existingInsurance.SubscriberLastName;
                existingInsurance.SubscriberId = dto.PrimaryInsurance.SubscriberId ?? existingInsurance.SubscriberId;
                existingInsurance.SubscriberDob = dto.PrimaryInsurance.SubscriberDob ?? existingInsurance.SubscriberDob;
                existingInsurance.SubscriberRelationship = dto.PrimaryInsurance.SubscriberRelationship ?? existingInsurance.SubscriberRelationship;
                existingInsurance.EffectiveFrom = dto.PrimaryInsurance.EffectiveFrom ?? existingInsurance.EffectiveFrom;
                existingInsurance.EffectiveTo = dto.PrimaryInsurance.EffectiveTo ?? existingInsurance.EffectiveTo;
                existingInsurance.InsuranceCategory = dto.PrimaryInsurance.InsuranceCategory ?? existingInsurance.InsuranceCategory;
                existingInsurance.AttorneyName = dto.PrimaryInsurance.AttorneyName ?? existingInsurance.AttorneyName;
                existingInsurance.AttorneyPhone = dto.PrimaryInsurance.AttorneyPhone ?? existingInsurance.AttorneyPhone;
                existingInsurance.AttorneyEmail = dto.PrimaryInsurance.AttorneyEmail ?? existingInsurance.AttorneyEmail;
                existingInsurance.AllowedVisits = dto.PrimaryInsurance.AllowedVisits ?? existingInsurance.AllowedVisits;
                existingInsurance.UpdatedAt = DateTime.UtcNow;
                _encryptionHelper.EncryptEntity(existingInsurance);
                await _context.SaveChangesAsync();
            }
            else
            {
                // Create new primary insurance
                var insurance = new Insurance
                {
                    TenantId = patient.TenantId,
                    PatientId = patient.PatientId,
                    PayerName = dto.PrimaryInsurance.PayerName,
                    PayerId = dto.PrimaryInsurance.PayerId,
                    PolicyNumber = dto.PrimaryInsurance.PolicyNumber,
                    GroupNumber = dto.PrimaryInsurance.GroupNumber,
                    Copay = dto.PrimaryInsurance.Copay,
                    SubscriberName = dto.PrimaryInsurance.SubscriberName,
                    SubscriberFirstName = dto.PrimaryInsurance.SubscriberFirstName,
                    SubscriberLastName = dto.PrimaryInsurance.SubscriberLastName,
                    SubscriberId = dto.PrimaryInsurance.SubscriberId,
                    SubscriberDob = dto.PrimaryInsurance.SubscriberDob,
                    SubscriberRelationship = dto.PrimaryInsurance.SubscriberRelationship,
                    EffectiveFrom = dto.PrimaryInsurance.EffectiveFrom,
                    EffectiveTo = dto.PrimaryInsurance.EffectiveTo,
                    InsuranceCategory = dto.PrimaryInsurance.InsuranceCategory,
                    AttorneyName = dto.PrimaryInsurance.AttorneyName,
                    AttorneyPhone = dto.PrimaryInsurance.AttorneyPhone,
                    AttorneyEmail = dto.PrimaryInsurance.AttorneyEmail,
                    AllowedVisits = dto.PrimaryInsurance.AllowedVisits,
                    Type = (int)InsuranceType.Primary,
                    IsActive = true,
                    EligibilityStatus = (int)EligibilityStatus.PendingVerification,
                    CreatedAt = DateTime.UtcNow
                };
                _encryptionHelper.EncryptEntity(insurance);
                _context.Insurances.Add(insurance);
                await _context.SaveChangesAsync();

                // Create authorization record if authorization data was provided
                if (dto.PrimaryInsurance.AuthorizedVisits.HasValue && dto.PrimaryInsurance.AuthorizedVisits > 0)
                {
                    var authorization = new Authorization
                    {
                        TenantId = patient.TenantId,
                        InsuranceId = insurance.InsuranceId,
                        AuthorizationNumber = dto.PrimaryInsurance.AuthorizationNumber ?? string.Empty,
                        AuthorizedVisits = dto.PrimaryInsurance.AuthorizedVisits.Value,
                        ExpiryDate = dto.PrimaryInsurance.AuthorizationExpiry,
                        DateOfValidation = DateTime.UtcNow,
                        Notes = "Authorization created during patient update",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Authorizations.Add(authorization);
                    await _context.SaveChangesAsync();
                }
            }

            // Note: For existing insurance, authorization is created during insurance validation (VerifyInsuranceAsync)
            // We don't create authorization here to avoid duplicates
        }

        // Handle secondary insurance update/create
        if (dto.SecondaryInsurance != null && !string.IsNullOrEmpty(dto.SecondaryInsurance.PayerName))
        {
            // Find existing secondary insurance
            var existingSecondary = patient.Insurances.FirstOrDefault(i => i.Type == (int)InsuranceType.Secondary && i.IsActive == true);

            if (existingSecondary != null)
            {
                // Update existing secondary insurance
                _encryptionHelper.DecryptEntity(existingSecondary);
                existingSecondary.PayerName = dto.SecondaryInsurance.PayerName;
                existingSecondary.PayerId = dto.SecondaryInsurance.PayerId ?? existingSecondary.PayerId;
                existingSecondary.PolicyNumber = dto.SecondaryInsurance.PolicyNumber;
                existingSecondary.GroupNumber = dto.SecondaryInsurance.GroupNumber;
                existingSecondary.Copay = dto.SecondaryInsurance.Copay ?? existingSecondary.Copay;
                existingSecondary.SubscriberName = dto.SecondaryInsurance.SubscriberName;
                existingSecondary.SubscriberFirstName = dto.SecondaryInsurance.SubscriberFirstName ?? existingSecondary.SubscriberFirstName;
                existingSecondary.SubscriberLastName = dto.SecondaryInsurance.SubscriberLastName ?? existingSecondary.SubscriberLastName;
                existingSecondary.SubscriberId = dto.SecondaryInsurance.SubscriberId ?? existingSecondary.SubscriberId;
                existingSecondary.SubscriberDob = dto.SecondaryInsurance.SubscriberDob ?? existingSecondary.SubscriberDob;
                existingSecondary.SubscriberRelationship = dto.SecondaryInsurance.SubscriberRelationship ?? existingSecondary.SubscriberRelationship;
                existingSecondary.EffectiveFrom = dto.SecondaryInsurance.EffectiveFrom ?? existingSecondary.EffectiveFrom;
                existingSecondary.EffectiveTo = dto.SecondaryInsurance.EffectiveTo ?? existingSecondary.EffectiveTo;
                existingSecondary.InsuranceCategory = dto.SecondaryInsurance.InsuranceCategory ?? existingSecondary.InsuranceCategory;
                existingSecondary.AttorneyName = dto.SecondaryInsurance.AttorneyName ?? existingSecondary.AttorneyName;
                existingSecondary.AttorneyPhone = dto.SecondaryInsurance.AttorneyPhone ?? existingSecondary.AttorneyPhone;
                existingSecondary.AttorneyEmail = dto.SecondaryInsurance.AttorneyEmail ?? existingSecondary.AttorneyEmail;
                existingSecondary.AllowedVisits = dto.SecondaryInsurance.AllowedVisits ?? existingSecondary.AllowedVisits;
                existingSecondary.UpdatedAt = DateTime.UtcNow;
                _encryptionHelper.EncryptEntity(existingSecondary);
                await _context.SaveChangesAsync();
            }
            else
            {
                // Create new secondary insurance
                var secondaryInsurance = new Insurance
                {
                    TenantId = patient.TenantId,
                    PatientId = patient.PatientId,
                    PayerName = dto.SecondaryInsurance.PayerName,
                    PayerId = dto.SecondaryInsurance.PayerId,
                    PolicyNumber = dto.SecondaryInsurance.PolicyNumber,
                    GroupNumber = dto.SecondaryInsurance.GroupNumber,
                    Copay = dto.SecondaryInsurance.Copay,
                    SubscriberName = dto.SecondaryInsurance.SubscriberName,
                    SubscriberFirstName = dto.SecondaryInsurance.SubscriberFirstName,
                    SubscriberLastName = dto.SecondaryInsurance.SubscriberLastName,
                    SubscriberId = dto.SecondaryInsurance.SubscriberId,
                    SubscriberDob = dto.SecondaryInsurance.SubscriberDob,
                    SubscriberRelationship = dto.SecondaryInsurance.SubscriberRelationship,
                    EffectiveFrom = dto.SecondaryInsurance.EffectiveFrom,
                    EffectiveTo = dto.SecondaryInsurance.EffectiveTo,
                    InsuranceCategory = dto.SecondaryInsurance.InsuranceCategory,
                    AttorneyName = dto.SecondaryInsurance.AttorneyName,
                    AttorneyPhone = dto.SecondaryInsurance.AttorneyPhone,
                    AttorneyEmail = dto.SecondaryInsurance.AttorneyEmail,
                    AllowedVisits = dto.SecondaryInsurance.AllowedVisits,
                    Type = (int)InsuranceType.Secondary,
                    IsActive = true,
                    EligibilityStatus = (int)EligibilityStatus.PendingVerification,
                    CreatedAt = DateTime.UtcNow
                };
                _encryptionHelper.EncryptEntity(secondaryInsurance);
                _context.Insurances.Add(secondaryInsurance);
                await _context.SaveChangesAsync();

                // Create authorization record if authorization data was provided
                if (dto.SecondaryInsurance.AuthorizedVisits.HasValue && dto.SecondaryInsurance.AuthorizedVisits > 0)
                {
                    var authorization = new Authorization
                    {
                        TenantId = patient.TenantId,
                        InsuranceId = secondaryInsurance.InsuranceId,
                        AuthorizationNumber = dto.SecondaryInsurance.AuthorizationNumber ?? string.Empty,
                        AuthorizedVisits = dto.SecondaryInsurance.AuthorizedVisits.Value,
                        ExpiryDate = dto.SecondaryInsurance.AuthorizationExpiry,
                        DateOfValidation = DateTime.UtcNow,
                        Notes = "Authorization created during patient update",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Authorizations.Add(authorization);
                    await _context.SaveChangesAsync();
                }
            }

            // Note: For existing insurance, authorization is created during insurance validation (VerifyInsuranceAsync)
            // We don't create authorization here to avoid duplicates
        }

        // CRITICAL: Trigger validation BEFORE decrypting the entity.
        // The validation service calls SaveChangesAsync() which would save ALL tracked entities.
        // If we decrypt first, the decrypted Patient entity would be persisted back to the database.
        try
        {
            await _validationService.ValidatePatientAsync(patient.PatientId, "Update", null);
        }
        catch
        {
            // Validation failure should not prevent patient update
            // The validation will be run again during the scheduled bulk validation
        }

        // CRITICAL (PHI encryption safety):
        // Detach the patient from the change tracker BEFORE decrypting. The
        // BlindIndex call below invokes SaveChangesAsync on the same DbContext,
        // which would otherwise flush the decrypted patient (Modified state)
        // back to the DB as plaintext. The previous comment here ("safe now
        // because validation has already called SaveChangesAsync") was incorrect.
        _context.Entry(patient).State = EntityState.Detached;

        // Decrypt for return (so caller gets readable data)
        _encryptionHelper.DecryptEntity(patient);

        // Re-generate blind index search tokens for the patient (on decrypted data)
        // This updates tokens if name, phone, or email changed
        try
        {
            await _blindIndexService.IndexPatientAsync(patient);
        }
        catch
        {
            // Index failure should not prevent patient update
            // Search will fall back to loading all patients if tokens are missing
        }

        return patient;
    }

    public async Task<bool> DeletePatientAsync(int patientId)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null)
            return false;

        // CRITICAL: Tenant data isolation - verify patient belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return false;

        patient.IsDeleted = true;
        patient.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        
        return true;
    }
    
    public async Task<string> GenerateMRNAsync()
    {
        var lastPatient = await _context.Patients
            .OrderByDescending(p => p.PatientId)
            .FirstOrDefaultAsync();

        var nextNumber = (lastPatient?.PatientId ?? 0) + 1;
        return $"IM-{nextNumber:D6}";
    }

    /// <summary>
    /// Archive a patient. Archived patients can be viewed but not edited.
    /// </summary>
    public async Task<bool> ArchivePatientAsync(int patientId)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null)
            return false;

        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return false;

        patient.IsArchived = true;
        patient.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Unarchive a patient. Restores the patient to normal editable state.
    /// </summary>
    public async Task<bool> UnarchivePatientAsync(int patientId)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null)
            return false;

        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return false;

        patient.IsArchived = false;
        patient.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Check if an email is unique within the tenant. Returns the name of the existing patient if duplicate found.
    /// </summary>
    public async Task<(bool IsUnique, string? ExistingPatientName)> CheckEmailUniqueAsync(string email, int? excludePatientId = null)
    {
        if (string.IsNullOrWhiteSpace(email) || !_tenantProvider.TenantId.HasValue)
            return (true, null);

        var tenantId = _tenantProvider.TenantId.Value;
        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Use blind index search tokens to find candidate patients with matching email prefix
        var emailHash = _blindIndexService.GenerateSearchHash(normalizedEmail);
        if (string.IsNullOrEmpty(emailHash))
            return (true, null);

        // Find candidate patient IDs via search tokens.
        // Use "EmailExact" (full-length hash) — the prefix-bounded "Email" tokens
        // are capped at MaxPrefixLength (15 chars) and miss longer emails.
        var candidatePatientIds = await _context.PatientSearchTokens
            .Where(t => t.TenantId == tenantId
                && t.FieldType == "EmailExact"
                && t.TokenHash == emailHash)
            .Select(t => t.PatientId)
            .Distinct()
            .ToListAsync();

        if (excludePatientId.HasValue)
            candidatePatientIds.Remove(excludePatientId.Value);

        if (candidatePatientIds.Count == 0)
            return (true, null);

        // Load candidates and decrypt to verify exact email match.
        // AsNoTracking: read-only uniqueness check; decryption mutates properties
        // via reflection, which would be tracked as Modified on the shared context
        // and flushed by any later SaveChangesAsync in the same request.
        var candidates = await _context.Patients
            .AsNoTracking()
            .Where(p => candidatePatientIds.Contains(p.PatientId)
                && p.IsDeleted != true)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            _encryptionHelper.DecryptEntity(candidate);
            if (string.Equals(candidate.Email?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"{candidate.FirstName} {candidate.LastName}");
            }
        }

        return (true, null);
    }
}
