using System.Diagnostics;
using System.Text.Json;
using EHR.Data;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EHR.Services;

/// <summary>
/// Internal DTO for holding patient data during validation - prevents modifying tracked entities
/// </summary>
internal class PatientValidationData
{
    public int PatientId { get; set; }
    public int TenantId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }
    public List<InsuranceValidationData> Insurances { get; set; } = new();
}

/// <summary>
/// Internal DTO for holding insurance data during validation - prevents modifying tracked entities
/// </summary>
internal class InsuranceValidationData
{
    public bool? IsActive { get; set; }
    public int? InsuranceCategory { get; set; }
    public int? Type { get; set; }
    public string? PayerName { get; set; }
    public string? PolicyNumber { get; set; }
    public string? SubscriberName { get; set; }
    public string? SubscriberRelationship { get; set; }
}

/// <summary>
/// Interface for patient profile validation service
/// </summary>
public interface IPatientValidationService
{
    /// <summary>
    /// Validates a single patient's profile and stores the result
    /// </summary>
    Task<PatientValidationStatusDto> ValidatePatientAsync(int patientId, string source = "Manual", int? userId = null);

    /// <summary>
    /// Validates all patients for a specific tenant
    /// </summary>
    Task<BulkValidationResultDto> ValidateAllPatientsAsync(int? tenantId = null);

    /// <summary>
    /// Gets the current validation status for a patient
    /// </summary>
    Task<PatientValidationStatusDto> GetValidationStatusAsync(int patientId);

    /// <summary>
    /// Gets validation summary for the dashboard widget
    /// </summary>
    Task<ValidationSummaryDto> GetValidationSummaryAsync();

    /// <summary>
    /// Gets a detailed validation report for a patient
    /// </summary>
    Task<PatientValidationReportDto> GetValidationReportAsync(int patientId);
}

/// <summary>
/// Service for validating patient profiles and identifying missing required information
/// </summary>
public class PatientValidationService : IPatientValidationService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly ILogger<PatientValidationService> _logger;

    // Section names used for navigation
    private const string SectionBasicInfo = "BasicInfo";
    private const string SectionAddress = "Address";
    private const string SectionEmergencyContact = "EmergencyContact";
    private const string SectionInsurance = "Insurance";

    // User-friendly section names for display
    private static readonly Dictionary<string, string> SectionFriendlyNames = new()
    {
        { SectionBasicInfo, "Basic Information" },
        { SectionAddress, "Address" },
        { SectionEmergencyContact, "Emergency Contact" },
        { SectionInsurance, "Insurance" }
    };

    public PatientValidationService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EncryptionHelper encryptionHelper,
        ILogger<PatientValidationService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryptionHelper = encryptionHelper;
        _logger = logger;
    }

    /// <summary>
    /// Validates a single patient's profile and stores the result
    /// </summary>
    public async Task<PatientValidationStatusDto> ValidatePatientAsync(int patientId, string source = "Manual", int? userId = null)
    {
        // CRITICAL: Use projection to copy data into DTOs instead of loading entities.
        // This prevents any possibility of EF tracking changes and persisting decrypted data.
        // Even AsNoTracking can return tracked entities if they're already in the context.
        var patientData = await _context.Patients
            .AsNoTracking()
            .Where(p => p.PatientId == patientId && p.IsDeleted != true)
            .Select(p => new PatientValidationData
            {
                PatientId = p.PatientId,
                TenantId = p.TenantId,
                FirstName = p.FirstName,
                LastName = p.LastName,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                Phone = p.Phone,
                Email = p.Email,
                Address = p.Address,
                City = p.City,
                State = p.State,
                ZipCode = p.ZipCode,
                EmergencyContactName = p.EmergencyContactName,
                EmergencyContactPhone = p.EmergencyContactPhone,
                EmergencyContactRelation = p.EmergencyContactRelation,
                Insurances = p.Insurances.Select(i => new InsuranceValidationData
                {
                    IsActive = i.IsActive,
                    InsuranceCategory = i.InsuranceCategory,
                    Type = i.Type,
                    PayerName = i.PayerName,
                    PolicyNumber = i.PolicyNumber,
                    SubscriberName = i.SubscriberName,
                    SubscriberRelationship = i.SubscriberRelationship
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (patientData == null)
        {
            throw new ArgumentException($"Patient with ID {patientId} not found");
        }

        // Decrypt the DTO fields (not entity fields) - this is safe as DTOs are not tracked
        DecryptValidationData(patientData);

        var validationResult = ValidatePatientData(patientData);

        // Store the validation result
        await StoreValidationResultAsync(patientData.TenantId, patientId, validationResult, source, userId);

        return validationResult;
    }

    /// <summary>
    /// Validates all patients for a specific tenant or all tenants (for scheduled job)
    /// </summary>
    public async Task<BulkValidationResultDto> ValidateAllPatientsAsync(int? tenantId = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // CRITICAL: Use projection to DTOs to prevent EF from tracking entities.
            // Loading entities directly can cause decrypted data to be persisted.
            var baseQuery = _context.Patients
                .AsNoTracking()
                .Where(p => p.IsDeleted != true);

            // If tenant ID is provided, filter by tenant
            if (tenantId.HasValue)
            {
                baseQuery = baseQuery.Where(p => p.TenantId == tenantId.Value);
            }
            // If no tenant specified but we have a tenant context, use it
            else if (_tenantProvider.TenantId.HasValue)
            {
                baseQuery = baseQuery.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
            }

            // Project to DTOs - this ensures no entities are tracked
            var patientDataList = await baseQuery
                .Select(p => new PatientValidationData
                {
                    PatientId = p.PatientId,
                    TenantId = p.TenantId,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    DateOfBirth = p.DateOfBirth,
                    Gender = p.Gender,
                    Phone = p.Phone,
                    Email = p.Email,
                    Address = p.Address,
                    City = p.City,
                    State = p.State,
                    ZipCode = p.ZipCode,
                    EmergencyContactName = p.EmergencyContactName,
                    EmergencyContactPhone = p.EmergencyContactPhone,
                    EmergencyContactRelation = p.EmergencyContactRelation,
                    Insurances = p.Insurances.Select(i => new InsuranceValidationData
                    {
                        IsActive = i.IsActive,
                        InsuranceCategory = i.InsuranceCategory,
                        Type = i.Type,
                        PayerName = i.PayerName,
                        PolicyNumber = i.PolicyNumber,
                        SubscriberName = i.SubscriberName,
                        SubscriberRelationship = i.SubscriberRelationship
                    }).ToList()
                })
                .ToListAsync();

            int completeCount = 0;
            int incompleteCount = 0;

            // Process patients in batches for better performance
            const int batchSize = 100;

            for (int i = 0; i < patientDataList.Count; i += batchSize)
            {
                var batch = patientDataList.Skip(i).Take(batchSize).ToList();

                foreach (var patientData in batch)
                {
                    // Decrypt the DTO fields (safe - not tracked by EF)
                    DecryptValidationData(patientData);

                    var validationResult = ValidatePatientData(patientData);

                    if (validationResult.IsComplete)
                    {
                        completeCount++;
                    }
                    else
                    {
                        incompleteCount++;
                    }

                    // Create or update validation record
                    var existingValidation = await _context.PatientValidations
                        .FirstOrDefaultAsync(v => v.PatientId == patientData.PatientId);

                    if (existingValidation != null)
                    {
                        UpdateValidationEntity(existingValidation, validationResult, "Scheduled", null);
                    }
                    else
                    {
                        var newValidation = CreateValidationEntity(patientData.TenantId, patientData.PatientId, validationResult, "Scheduled", null);
                        _context.PatientValidations.Add(newValidation);
                    }
                }

                // Save batch - only PatientValidation entities will be saved
                await _context.SaveChangesAsync();
            }

            stopwatch.Stop();

            return new BulkValidationResultDto
            {
                Success = true,
                TotalPatientsChecked = patientDataList.Count,
                CompleteProfilesCount = completeCount,
                IncompleteProfilesCount = incompleteCount,
                ValidationTimestamp = DateTime.UtcNow,
                Message = $"Successfully validated {patientDataList.Count} patient records",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error during bulk patient validation");

            return new BulkValidationResultDto
            {
                Success = false,
                TotalPatientsChecked = 0,
                CompleteProfilesCount = 0,
                IncompleteProfilesCount = 0,
                ValidationTimestamp = DateTime.UtcNow,
                Message = $"Validation failed: {ex.Message}",
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Gets the current validation status for a patient
    /// </summary>
    public async Task<PatientValidationStatusDto> GetValidationStatusAsync(int patientId)
    {
        var validation = await _context.PatientValidations
            .FirstOrDefaultAsync(v => v.PatientId == patientId);

        if (validation == null)
        {
            // If no validation exists, run validation now
            return await ValidatePatientAsync(patientId);
        }

        return MapValidationToDto(validation);
    }

    /// <summary>
    /// Gets validation summary for the dashboard widget
    /// </summary>
    public async Task<ValidationSummaryDto> GetValidationSummaryAsync()
    {
        var query = _context.Patients
            .Where(p => p.IsDeleted != true);

        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        var totalPatients = await query.CountAsync();

        var validationQuery = _context.PatientValidations.AsQueryable();
        if (_tenantProvider.TenantId.HasValue)
        {
            validationQuery = validationQuery.Where(v => v.TenantId == _tenantProvider.TenantId.Value);
        }

        var completeCount = await validationQuery.CountAsync(v => v.IsComplete);
        var lastValidation = await validationQuery
            .OrderByDescending(v => v.LastValidatedAt)
            .Where(v => v.ValidationSource == "Scheduled")
            .Select(v => v.LastValidatedAt)
            .FirstOrDefaultAsync();

        return new ValidationSummaryDto
        {
            TotalPatients = totalPatients,
            CompleteProfiles = completeCount,
            IncompleteProfiles = totalPatients - completeCount,
            LastBulkValidationAt = lastValidation
        };
    }

    /// <summary>
    /// Gets a detailed validation report for a patient
    /// </summary>
    public async Task<PatientValidationReportDto> GetValidationReportAsync(int patientId)
    {
        // Use AsNoTracking for read-only operation and to prevent accidental persistence of decrypted data
        var patient = await _context.Patients
            .AsNoTracking()
            .Include(p => p.Insurances)
            .FirstOrDefaultAsync(p => p.PatientId == patientId && p.IsDeleted != true);

        if (patient == null)
        {
            throw new ArgumentException($"Patient with ID {patientId} not found");
        }

        // Decrypt patient data for validation (safe because entity is not tracked)
        _encryptionHelper.DecryptEntity(patient);
        foreach (var insurance in patient.Insurances)
        {
            _encryptionHelper.DecryptEntity(insurance);
        }

        var sections = new List<ValidationSectionDto>();
        // Check if patient has a self-pay insurance record (InsuranceCategory = 3)
        var isSelfPay = patient.Insurances.Any(i => i.IsActive == true && i.InsuranceCategory == 3);

        // Validate Basic Info section
        var basicInfoMissing = new List<MissingFieldDto>();
        if (string.IsNullOrWhiteSpace(patient.FirstName))
            basicInfoMissing.Add(new MissingFieldDto { FieldName = "FirstName", FriendlyLabel = "First Name", Section = SectionBasicInfo });
        if (string.IsNullOrWhiteSpace(patient.LastName))
            basicInfoMissing.Add(new MissingFieldDto { FieldName = "LastName", FriendlyLabel = "Last Name", Section = SectionBasicInfo });
        if (patient.DateOfBirth == default)
            basicInfoMissing.Add(new MissingFieldDto { FieldName = "DateOfBirth", FriendlyLabel = "Date of Birth", Section = SectionBasicInfo });
        if (string.IsNullOrWhiteSpace(patient.Gender))
            basicInfoMissing.Add(new MissingFieldDto { FieldName = "Gender", FriendlyLabel = "Gender", Section = SectionBasicInfo });
        if (string.IsNullOrWhiteSpace(patient.Phone))
            basicInfoMissing.Add(new MissingFieldDto { FieldName = "Phone", FriendlyLabel = "Phone Number", Section = SectionBasicInfo });
        if (string.IsNullOrWhiteSpace(patient.Email))
            basicInfoMissing.Add(new MissingFieldDto { FieldName = "Email", FriendlyLabel = "Email Address", Section = SectionBasicInfo });

        sections.Add(new ValidationSectionDto
        {
            SectionName = SectionBasicInfo,
            FriendlyName = SectionFriendlyNames[SectionBasicInfo],
            IsComplete = !basicInfoMissing.Any(),
            MissingFields = basicInfoMissing
        });

        // Validate Address section
        var addressMissing = new List<MissingFieldDto>();
        if (string.IsNullOrWhiteSpace(patient.Address))
            addressMissing.Add(new MissingFieldDto { FieldName = "Address", FriendlyLabel = "Street Address", Section = SectionAddress });
        if (string.IsNullOrWhiteSpace(patient.City))
            addressMissing.Add(new MissingFieldDto { FieldName = "City", FriendlyLabel = "City", Section = SectionAddress });
        if (string.IsNullOrWhiteSpace(patient.State))
            addressMissing.Add(new MissingFieldDto { FieldName = "State", FriendlyLabel = "State", Section = SectionAddress });
        if (string.IsNullOrWhiteSpace(patient.ZipCode))
            addressMissing.Add(new MissingFieldDto { FieldName = "ZipCode", FriendlyLabel = "ZIP Code", Section = SectionAddress });

        sections.Add(new ValidationSectionDto
        {
            SectionName = SectionAddress,
            FriendlyName = SectionFriendlyNames[SectionAddress],
            IsComplete = !addressMissing.Any(),
            MissingFields = addressMissing
        });

        // Validate Emergency Contact section
        var emergencyMissing = new List<MissingFieldDto>();
        if (string.IsNullOrWhiteSpace(patient.EmergencyContactName))
            emergencyMissing.Add(new MissingFieldDto { FieldName = "EmergencyContactName", FriendlyLabel = "Emergency Contact Name", Section = SectionEmergencyContact });
        if (string.IsNullOrWhiteSpace(patient.EmergencyContactPhone))
            emergencyMissing.Add(new MissingFieldDto { FieldName = "EmergencyContactPhone", FriendlyLabel = "Emergency Contact Phone", Section = SectionEmergencyContact });
        if (string.IsNullOrWhiteSpace(patient.EmergencyContactRelation))
            emergencyMissing.Add(new MissingFieldDto { FieldName = "EmergencyContactRelation", FriendlyLabel = "Relationship to Patient", Section = SectionEmergencyContact });

        sections.Add(new ValidationSectionDto
        {
            SectionName = SectionEmergencyContact,
            FriendlyName = SectionFriendlyNames[SectionEmergencyContact],
            IsComplete = !emergencyMissing.Any(),
            MissingFields = emergencyMissing
        });

        // Validate Insurance section (skip if self-pay)
        var insuranceMissing = new List<MissingFieldDto>();
        if (!isSelfPay)
        {
            var activeInsurance = patient.Insurances
                .Where(i => i.IsActive == true)
                .ToList();

            if (!activeInsurance.Any())
            {
                insuranceMissing.Add(new MissingFieldDto { FieldName = "Insurance", FriendlyLabel = "Active Insurance Record", Section = SectionInsurance });
            }
            else
            {
                // Check if primary insurance has required fields
                var primaryInsurance = activeInsurance.FirstOrDefault(i => i.Type == 0);
                if (primaryInsurance != null)
                {
                    if (string.IsNullOrWhiteSpace(primaryInsurance.PayerName))
                        insuranceMissing.Add(new MissingFieldDto { FieldName = "PayerName", FriendlyLabel = "Insurance Company Name", Section = SectionInsurance });
                    if (string.IsNullOrWhiteSpace(primaryInsurance.PolicyNumber))
                        insuranceMissing.Add(new MissingFieldDto { FieldName = "PolicyNumber", FriendlyLabel = "Policy Number", Section = SectionInsurance });
                    // Note: SubscriberName and SubscriberRelationship removed from validation
                    // as these fields are not present in the patient insurance form UI
                }
            }
        }

        sections.Add(new ValidationSectionDto
        {
            SectionName = SectionInsurance,
            FriendlyName = SectionFriendlyNames[SectionInsurance],
            IsComplete = isSelfPay || !insuranceMissing.Any(),
            MissingFields = insuranceMissing
        });

        var isComplete = sections.All(s => s.IsComplete);

        return new PatientValidationReportDto
        {
            PatientId = patientId,
            PatientName = $"{patient.FirstName} {patient.LastName}",
            IsComplete = isComplete,
            Sections = sections,
            OverallStatus = isComplete ? "Complete" : "Incomplete",
            ValidatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Validates a patient profile and returns the validation status
    /// </summary>
    private PatientValidationStatusDto ValidatePatientProfile(Patient patient)
    {
        var missingSections = new List<string>();
        var missingFieldsDetails = new Dictionary<string, List<string>>();
        // Check if patient has a self-pay insurance record (InsuranceCategory = 3)
        var isSelfPay = patient.Insurances.Any(i => i.IsActive == true && i.InsuranceCategory == 3);

        // Validate Basic Info section
        var basicInfoMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(patient.FirstName))
            basicInfoMissing.Add("First Name");
        if (string.IsNullOrWhiteSpace(patient.LastName))
            basicInfoMissing.Add("Last Name");
        if (patient.DateOfBirth == default)
            basicInfoMissing.Add("Date of Birth");
        if (string.IsNullOrWhiteSpace(patient.Gender))
            basicInfoMissing.Add("Gender");
        if (string.IsNullOrWhiteSpace(patient.Phone))
            basicInfoMissing.Add("Phone Number");
        if (string.IsNullOrWhiteSpace(patient.Email))
            basicInfoMissing.Add("Email Address");

        if (basicInfoMissing.Any())
        {
            missingSections.Add(SectionFriendlyNames[SectionBasicInfo]);
            missingFieldsDetails[SectionBasicInfo] = basicInfoMissing;
        }

        // Validate Address section
        var addressMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(patient.Address))
            addressMissing.Add("Street Address");
        if (string.IsNullOrWhiteSpace(patient.City))
            addressMissing.Add("City");
        if (string.IsNullOrWhiteSpace(patient.State))
            addressMissing.Add("State");
        if (string.IsNullOrWhiteSpace(patient.ZipCode))
            addressMissing.Add("ZIP Code");

        if (addressMissing.Any())
        {
            missingSections.Add(SectionFriendlyNames[SectionAddress]);
            missingFieldsDetails[SectionAddress] = addressMissing;
        }

        // Validate Emergency Contact section
        var emergencyMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(patient.EmergencyContactName))
            emergencyMissing.Add("Emergency Contact Name");
        if (string.IsNullOrWhiteSpace(patient.EmergencyContactPhone))
            emergencyMissing.Add("Emergency Contact Phone");
        if (string.IsNullOrWhiteSpace(patient.EmergencyContactRelation))
            emergencyMissing.Add("Relationship to Patient");

        if (emergencyMissing.Any())
        {
            missingSections.Add(SectionFriendlyNames[SectionEmergencyContact]);
            missingFieldsDetails[SectionEmergencyContact] = emergencyMissing;
        }

        // Validate Insurance section (skip if self-pay)
        if (!isSelfPay)
        {
            var insuranceMissing = new List<string>();
            var activeInsurance = patient.Insurances
                .Where(i => i.IsActive == true)
                .ToList();

            if (!activeInsurance.Any())
            {
                insuranceMissing.Add("Active Insurance Record");
            }
            else
            {
                // Check if primary insurance has required fields
                var primaryInsurance = activeInsurance.FirstOrDefault(i => i.Type == 0);
                if (primaryInsurance != null)
                {
                    if (string.IsNullOrWhiteSpace(primaryInsurance.PayerName))
                        insuranceMissing.Add("Insurance Company Name");
                    if (string.IsNullOrWhiteSpace(primaryInsurance.PolicyNumber))
                        insuranceMissing.Add("Policy Number");
                    // Note: SubscriberName and SubscriberRelationship removed from validation
                    // as these fields are not present in the patient insurance form UI
                }
            }

            if (insuranceMissing.Any())
            {
                missingSections.Add(SectionFriendlyNames[SectionInsurance]);
                missingFieldsDetails[SectionInsurance] = insuranceMissing;
            }
        }

        // Calculate total missing fields
        var totalMissingFields = missingFieldsDetails.Values.Sum(list => list.Count);

        // Determine first incomplete section for navigation
        string firstIncompleteSection = null;
        if (missingFieldsDetails.ContainsKey(SectionBasicInfo))
            firstIncompleteSection = SectionBasicInfo;
        else if (missingFieldsDetails.ContainsKey(SectionAddress))
            firstIncompleteSection = SectionAddress;
        else if (missingFieldsDetails.ContainsKey(SectionEmergencyContact))
            firstIncompleteSection = SectionEmergencyContact;
        else if (missingFieldsDetails.ContainsKey(SectionInsurance))
            firstIncompleteSection = SectionInsurance;

        var isComplete = !missingSections.Any();

        return new PatientValidationStatusDto
        {
            PatientId = patient.PatientId,
            IsComplete = isComplete,
            MissingFieldsCount = totalMissingFields,
            MissingSections = missingSections,
            MissingFieldsDetails = missingFieldsDetails,
            FirstIncompleteSection = firstIncompleteSection,
            StatusMessage = isComplete
                ? "Patient profile is complete"
                : $"Missing information in: {string.Join(", ", missingSections)}",
            LastValidatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Decrypts the validation data DTO fields that are encrypted in the database.
    /// This is safe because we're modifying a DTO copy, not a tracked entity.
    /// </summary>
    private void DecryptValidationData(PatientValidationData data)
    {
        // Decrypt patient fields (matching EncryptionConfiguration for Patient)
        data.FirstName = _encryptionHelper.Decrypt(data.FirstName);
        data.LastName = _encryptionHelper.Decrypt(data.LastName);
        data.Phone = _encryptionHelper.Decrypt(data.Phone);
        data.Email = _encryptionHelper.Decrypt(data.Email);
        data.Address = _encryptionHelper.Decrypt(data.Address);
        data.City = _encryptionHelper.Decrypt(data.City);
        data.State = _encryptionHelper.Decrypt(data.State);
        data.ZipCode = _encryptionHelper.Decrypt(data.ZipCode);
        data.EmergencyContactName = _encryptionHelper.Decrypt(data.EmergencyContactName);
        data.EmergencyContactPhone = _encryptionHelper.Decrypt(data.EmergencyContactPhone);
        data.EmergencyContactRelation = _encryptionHelper.Decrypt(data.EmergencyContactRelation);

        // Decrypt insurance fields (matching EncryptionConfiguration for Insurance)
        foreach (var insurance in data.Insurances)
        {
            insurance.SubscriberName = _encryptionHelper.Decrypt(insurance.SubscriberName);
            insurance.PolicyNumber = _encryptionHelper.Decrypt(insurance.PolicyNumber);
        }
    }

    /// <summary>
    /// Validates patient data from DTO and returns validation status.
    /// Uses DTO instead of entity to prevent any possibility of EF tracking issues.
    /// </summary>
    private PatientValidationStatusDto ValidatePatientData(PatientValidationData data)
    {
        var missingSections = new List<string>();
        var missingFieldsDetails = new Dictionary<string, List<string>>();
        // Check if patient has a self-pay insurance record (InsuranceCategory = 3)
        var isSelfPay = data.Insurances.Any(i => i.IsActive == true && i.InsuranceCategory == 3);

        // Validate Basic Info section
        var basicInfoMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(data.FirstName))
            basicInfoMissing.Add("First Name");
        if (string.IsNullOrWhiteSpace(data.LastName))
            basicInfoMissing.Add("Last Name");
        if (data.DateOfBirth == default)
            basicInfoMissing.Add("Date of Birth");
        if (string.IsNullOrWhiteSpace(data.Gender))
            basicInfoMissing.Add("Gender");
        if (string.IsNullOrWhiteSpace(data.Phone))
            basicInfoMissing.Add("Phone Number");
        if (string.IsNullOrWhiteSpace(data.Email))
            basicInfoMissing.Add("Email Address");

        if (basicInfoMissing.Any())
        {
            missingSections.Add(SectionFriendlyNames[SectionBasicInfo]);
            missingFieldsDetails[SectionBasicInfo] = basicInfoMissing;
        }

        // Validate Address section
        var addressMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(data.Address))
            addressMissing.Add("Street Address");
        if (string.IsNullOrWhiteSpace(data.City))
            addressMissing.Add("City");
        if (string.IsNullOrWhiteSpace(data.State))
            addressMissing.Add("State");
        if (string.IsNullOrWhiteSpace(data.ZipCode))
            addressMissing.Add("ZIP Code");

        if (addressMissing.Any())
        {
            missingSections.Add(SectionFriendlyNames[SectionAddress]);
            missingFieldsDetails[SectionAddress] = addressMissing;
        }

        // Validate Emergency Contact section
        var emergencyMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(data.EmergencyContactName))
            emergencyMissing.Add("Emergency Contact Name");
        if (string.IsNullOrWhiteSpace(data.EmergencyContactPhone))
            emergencyMissing.Add("Emergency Contact Phone");
        if (string.IsNullOrWhiteSpace(data.EmergencyContactRelation))
            emergencyMissing.Add("Relationship to Patient");

        if (emergencyMissing.Any())
        {
            missingSections.Add(SectionFriendlyNames[SectionEmergencyContact]);
            missingFieldsDetails[SectionEmergencyContact] = emergencyMissing;
        }

        // Validate Insurance section (skip if self-pay)
        if (!isSelfPay)
        {
            var insuranceMissing = new List<string>();
            var activeInsurance = data.Insurances
                .Where(i => i.IsActive == true)
                .ToList();

            if (!activeInsurance.Any())
            {
                insuranceMissing.Add("Active Insurance Record");
            }
            else
            {
                // Check if primary insurance has required fields
                var primaryInsurance = activeInsurance.FirstOrDefault(i => i.Type == 0);
                if (primaryInsurance != null)
                {
                    if (string.IsNullOrWhiteSpace(primaryInsurance.PayerName))
                        insuranceMissing.Add("Insurance Company Name");
                    if (string.IsNullOrWhiteSpace(primaryInsurance.PolicyNumber))
                        insuranceMissing.Add("Policy Number");
                    // Note: SubscriberName and SubscriberRelationship removed from validation
                    // as these fields are not present in the patient insurance form UI
                }
            }

            if (insuranceMissing.Any())
            {
                missingSections.Add(SectionFriendlyNames[SectionInsurance]);
                missingFieldsDetails[SectionInsurance] = insuranceMissing;
            }
        }

        // Calculate total missing fields
        var totalMissingFields = missingFieldsDetails.Values.Sum(list => list.Count);

        // Determine first incomplete section for navigation
        string firstIncompleteSection = null;
        if (missingFieldsDetails.ContainsKey(SectionBasicInfo))
            firstIncompleteSection = SectionBasicInfo;
        else if (missingFieldsDetails.ContainsKey(SectionAddress))
            firstIncompleteSection = SectionAddress;
        else if (missingFieldsDetails.ContainsKey(SectionEmergencyContact))
            firstIncompleteSection = SectionEmergencyContact;
        else if (missingFieldsDetails.ContainsKey(SectionInsurance))
            firstIncompleteSection = SectionInsurance;

        var isComplete = !missingSections.Any();

        return new PatientValidationStatusDto
        {
            PatientId = data.PatientId,
            IsComplete = isComplete,
            MissingFieldsCount = totalMissingFields,
            MissingSections = missingSections,
            MissingFieldsDetails = missingFieldsDetails,
            FirstIncompleteSection = firstIncompleteSection,
            StatusMessage = isComplete
                ? "Patient profile is complete"
                : $"Missing information in: {string.Join(", ", missingSections)}",
            LastValidatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Stores validation result in the database
    /// </summary>
    private async Task StoreValidationResultAsync(int tenantId, int patientId, PatientValidationStatusDto result, string source, int? userId)
    {
        var existingValidation = await _context.PatientValidations
            .FirstOrDefaultAsync(v => v.PatientId == patientId);

        if (existingValidation != null)
        {
            UpdateValidationEntity(existingValidation, result, source, userId);
        }
        else
        {
            var newValidation = CreateValidationEntity(tenantId, patientId, result, source, userId);
            _context.PatientValidations.Add(newValidation);
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a new validation entity from the validation result
    /// </summary>
    private PatientValidation CreateValidationEntity(int tenantId, int patientId, PatientValidationStatusDto result, string source, int? userId)
    {
        return new PatientValidation
        {
            TenantId = tenantId,
            PatientId = patientId,
            IsComplete = result.IsComplete,
            MissingFieldsCount = result.MissingFieldsCount,
            MissingSections = JsonSerializer.Serialize(result.MissingSections),
            MissingFieldsDetails = JsonSerializer.Serialize(result.MissingFieldsDetails),
            FirstIncompleteSection = result.FirstIncompleteSection,
            LastValidatedAt = DateTime.UtcNow,
            ValidatedByUserId = userId,
            ValidationSource = source,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Updates an existing validation entity from the validation result
    /// </summary>
    private void UpdateValidationEntity(PatientValidation entity, PatientValidationStatusDto result, string source, int? userId)
    {
        entity.IsComplete = result.IsComplete;
        entity.MissingFieldsCount = result.MissingFieldsCount;
        entity.MissingSections = JsonSerializer.Serialize(result.MissingSections);
        entity.MissingFieldsDetails = JsonSerializer.Serialize(result.MissingFieldsDetails);
        entity.FirstIncompleteSection = result.FirstIncompleteSection;
        entity.LastValidatedAt = DateTime.UtcNow;
        entity.ValidatedByUserId = userId;
        entity.ValidationSource = source;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Maps a validation entity to a DTO
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
}
