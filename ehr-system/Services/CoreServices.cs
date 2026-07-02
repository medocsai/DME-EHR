using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using EHR.Data;
using EHR.Models;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;
using System.Text.Json;
using EHR.Helpers;

namespace EHR.Services;

// Provider Service
public interface IProviderService
{
    Task<List<ProviderListDto>> GetProvidersAsync(bool? activeOnly = true);
    Task<Provider?> GetProviderByIdAsync(int providerId);
    Task<Provider> CreateProviderAsync(ProviderCreateDto dto);
    Task<Provider?> UpdateProviderAsync(int providerId, ProviderUpdateDto dto);
    Task<bool> DeleteProviderAsync(int providerId);
    Task<ProviderUserInfoDto?> GetProviderUserAsync(int providerId);
    Task<ProviderSignatureResponseDto?> UploadSignatureAsync(int providerId, IFormFile file);
    Task<(Stream stream, string contentType)?> GetSignatureAsync(int providerId);
    Task<bool> DeleteSignatureAsync(int providerId);
    Task<ProfilePictureResponseDto?> UploadProfilePictureAsync(int providerId, IFormFile file);
    Task<(Stream stream, string contentType)?> GetProfilePictureAsync(int providerId);
    Task<bool> DeleteProfilePictureAsync(int providerId);
}

public class ProviderService : IProviderService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EHR.Helpers.EncryptionHelper _encryptionHelper;
    private readonly IUserManagementService _userManagementService;
    private readonly IFileStorageService _storageService;
    private readonly FilePathBuilder _pathBuilder;
    private readonly MetadataBuilder _metadataBuilder;
    private static readonly HashSet<string> AllowedSignatureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png"
    };
    private static readonly HashSet<string> AllowedSignatureContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png"
    };
    private static readonly HashSet<string> AllowedProfilePictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private static readonly HashSet<string> AllowedProfilePictureContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };
    private const long MaxProfilePictureSize = 5 * 1024 * 1024; // 5MB

    public ProviderService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EHR.Helpers.EncryptionHelper encryptionHelper,
        IUserManagementService userManagementService,
        IFileStorageService storageService,
        FilePathBuilder pathBuilder,
        MetadataBuilder metadataBuilder)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
        _userManagementService = userManagementService;
        _storageService = storageService;
        _pathBuilder = pathBuilder;
        _metadataBuilder = metadataBuilder;
    }

    public async Task<List<ProviderListDto>> GetProvidersAsync(bool? activeOnly = true)
    {
        // AsNoTracking: read-only list projection. Providers are decrypted
        // in-place for DTO construction; without NoTracking, any later
        // SaveChangesAsync in this request would flush decrypted values back
        // to the DB and corrupt Provider.Email / Provider.Phone to plaintext.
        var query = _context.Providers.AsNoTracking().AsQueryable();

        // CRITICAL: Tenant data isolation - filter by TenantId
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        if (activeOnly == true)
            query = query.Where(p => p.IsActive == true);

        // Load entities first, then decrypt and map
        var providers = await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();

        // Decrypt PHI fields
        foreach (var provider in providers)
        {
            _encryptionHelper.DecryptEntity(provider);
        }

        // Map to DTOs after decryption
        return providers.Select(p => new ProviderListDto
        {
            ProviderId = p.ProviderId,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Npi = p.Npi,
            FullName = p.FirstName + " " + p.LastName,
            DisplayName = p.LastName + ", " + p.FirstName,
            Specialty = p.Specialty,
            Email = p.Email,
            Phone = p.Phone,
            Color = p.Color,
            CredentialStatus = p.CredentialStatus,
            CredentialExpiry = p.CredentialExpiry,
            Credentials = p.Credentials,
            IsActive = p.IsActive == true,
            HasSignature = !string.IsNullOrEmpty(p.SignatureImagePath),
            HasProfilePicture = !string.IsNullOrEmpty(p.ProfilePicturePath)
        }).ToList();
    }

    public async Task<Provider?> GetProviderByIdAsync(int providerId)
    {
        // AsNoTracking: this method is called only for read/display (see
        // ProvidersController.GetProvider). Updates go through UpdateProviderAsync
        // which re-loads the entity. DecryptEntity below mutates properties via
        // reflection — without NoTracking, any later SaveChangesAsync in the
        // request would flush decrypted Provider.Email/Phone back to the DB.
        var query = _context.Providers
            .AsNoTracking()
            .Include(p => p.CredentialingRecords)
            .Include(p => p.ProviderSchedules)
            .Where(p => p.ProviderId == providerId);

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        var provider = await query.FirstOrDefaultAsync();

        if (provider != null)
        {
            _encryptionHelper.DecryptEntity(provider);
        }

        return provider;
    }

    public async Task<Provider> CreateProviderAsync(ProviderCreateDto dto)
    {
        var provider = new Provider
        {
            TenantId = _tenantProvider.TenantId!.Value,
            Npi = dto.Npi ?? string.Empty,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Credentials = dto.Credentials,
            Specialty = dto.Specialty,
            Taxonomy = dto.Taxonomy,
            Email = dto.Email,
            Phone = dto.Phone,
            Color = dto.Color ?? "#2196F3",
            LicenseNumber = dto.LicenseNumber,
            LicenseState = dto.LicenseState,
            LicenseExpiry = dto.LicenseExpiry.HasValue ? dto.LicenseExpiry.Value.ToDateTime(TimeOnly.MinValue) : null,
            DefaultAppointmentDuration = dto.DefaultAppointmentDuration,
            CredentialStatus = (int)ProviderCredentialStatus.Pending,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(provider);

        _context.Providers.Add(provider);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex) { }

        // Save work schedule if provided
        if (dto.WorkSchedule != null && dto.WorkSchedule.Count > 0)
        {
            foreach (var scheduleDto in dto.WorkSchedule)
            {
                var schedule = new ProviderSchedule
                {
                    TenantId = provider.TenantId,
                    ProviderId = provider.ProviderId,
                    DayOfWeek = scheduleDto.DayOfWeek,
                    StartTime = TimeOnly.Parse(scheduleDto.StartTime),
                    EndTime = TimeOnly.Parse(scheduleDto.EndTime),
                    IsAvailable = scheduleDto.IsAvailable,
                    LocationId = scheduleDto.LocationId
                };
                _context.ProviderSchedules.Add(schedule);
            }
            await _context.SaveChangesAsync();
        }

        // Auto-create user account for this provider if email and password are provided
        if (!string.IsNullOrEmpty(dto.Email) && !string.IsNullOrEmpty(dto.Password))
        {
            try
            {
                var userDto = new UserCreateDto
                {
                    TenantId = _tenantProvider.TenantId!.Value,
                    Email = dto.Email,
                    Password = dto.Password,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    Phone = dto.Phone,
                    Role = (int)UserRole.Clinician, // Providers are typically clinicians
                    ProviderId = provider.ProviderId
                };

                await _userManagementService.CreateUserAsync(userDto);
            }
            catch (Exception ex)
            {
                // Log error but don't fail provider creation if user creation fails
                Console.WriteLine($"Failed to create user for provider: {ex.Message}");
            }
        }

        // CRITICAL (PHI encryption safety):
        // Detach the provider from the change tracker before decrypting.
        // CreateUserAsync above (line ~214) and any downstream caller-side
        // SaveChangesAsync would otherwise flush the decrypted provider back
        // to the DB, corrupting Provider.Email / Provider.Phone to plaintext.
        _context.Entry(provider).State = EntityState.Detached;

        // Decrypt for return
        _encryptionHelper.DecryptEntity(provider);
        return provider;
    }

    public async Task<Provider?> UpdateProviderAsync(int providerId, ProviderUpdateDto dto)
    {
        var provider = await _context.Providers
            .Include(p => p.ProviderSchedules)
            .FirstOrDefaultAsync(p => p.ProviderId == providerId);
        if (provider == null) return null;

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Decrypt existing values first to properly handle partial updates
        _encryptionHelper.DecryptEntity(provider);

        if (dto.Npi != null) provider.Npi = dto.Npi;
        if (dto.FirstName != null) provider.FirstName = dto.FirstName;
        if (dto.LastName != null) provider.LastName = dto.LastName;
        if (dto.Credentials != null) provider.Credentials = dto.Credentials;
        if (dto.Specialty != null) provider.Specialty = dto.Specialty;
        if (dto.Taxonomy != null) provider.Taxonomy = dto.Taxonomy;
        if (dto.Email != null) provider.Email = dto.Email;
        if (dto.Phone != null) provider.Phone = dto.Phone;
        if (dto.Color != null) provider.Color = dto.Color;
        if (dto.LicenseNumber != null) provider.LicenseNumber = dto.LicenseNumber;
        if (dto.LicenseState != null) provider.LicenseState = dto.LicenseState;
        if (dto.LicenseExpiry.HasValue) provider.LicenseExpiry = dto.LicenseExpiry.Value.ToDateTime(TimeOnly.MinValue);
        if (dto.IsActive.HasValue) provider.IsActive = dto.IsActive.Value;
        if (dto.DefaultAppointmentDuration.HasValue) provider.DefaultAppointmentDuration = dto.DefaultAppointmentDuration.Value;

        // ISSUE #10 FIX: Handle work schedule updates
        if (dto.WorkSchedule != null)
        {
            // Remove existing schedules
            _context.ProviderSchedules.RemoveRange(provider.ProviderSchedules);

            // Add new schedules
            foreach (var scheduleDto in dto.WorkSchedule)
            {
                var schedule = new ProviderSchedule
                {
                    TenantId = provider.TenantId,
                    ProviderId = providerId,
                    DayOfWeek = scheduleDto.DayOfWeek,
                    StartTime = TimeOnly.Parse(scheduleDto.StartTime),
                    EndTime = TimeOnly.Parse(scheduleDto.EndTime),
                    IsAvailable = scheduleDto.IsAvailable,
                    LocationId = scheduleDto.LocationId
                };
                _context.ProviderSchedules.Add(schedule);
            }
        }

        provider.UpdatedAt = DateTime.UtcNow;

        // Encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(provider);

        await _context.SaveChangesAsync();

        // CRITICAL (PHI encryption safety):
        // Detach provider from the change tracker now that the encrypted update
        // has been persisted. Subsequent code below calls DecryptEntity and
        // CreateUserAsync — CreateUserAsync invokes SaveChangesAsync on the same
        // DbContext, which would otherwise flush the decrypted provider
        // (Modified) back to the DB and corrupt Provider.Email/Phone to plaintext.
        _context.Entry(provider).State = EntityState.Detached;

        // Create user account for this provider if password is provided and no user exists yet
        if (!string.IsNullOrEmpty(dto.Password))
        {
            // Check if provider already has a user account
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.ProviderId == providerId && u.IsActive == true);

            if (existingUser == null)
            {
                try
                {
                    // Decrypt provider to get email and name (detached — no tracking side-effects)
                    _encryptionHelper.DecryptEntity(provider);

                    var userDto = new UserCreateDto
                    {
                        TenantId = provider.TenantId,
                        Email = provider.Email,
                        Password = dto.Password,
                        FirstName = provider.FirstName,
                        LastName = provider.LastName,
                        Phone = provider.Phone,
                        Role = (int)UserRole.Clinician,
                        ProviderId = provider.ProviderId
                    };

                    await _userManagementService.CreateUserAsync(userDto);

                    // Re-encrypt for return
                    _encryptionHelper.EncryptEntity(provider);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create user for provider: {ex.Message}");
                }
            }
        }

        // Decrypt for return
        _encryptionHelper.DecryptEntity(provider);
        return provider;
    }

    public async Task<bool> DeleteProviderAsync(int providerId)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null) return false;

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return false;

        provider.IsActive = false;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<ProviderUserInfoDto?> GetProviderUserAsync(int providerId)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null) return null;

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Find the user associated with this provider
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.ProviderId == providerId && u.IsActive == true);

        if (user == null)
        {
            return new ProviderUserInfoDto { HasUser = false };
        }

        return new ProviderUserInfoDto
        {
            HasUser = true,
            UserId = user.UserId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<ProviderSignatureResponseDto?> UploadSignatureAsync(int providerId, IFormFile file)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null) return null;

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Validate file extension
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedSignatureExtensions.Contains(extension))
        {
            return new ProviderSignatureResponseDto
            {
                ProviderId = providerId,
                HasSignature = false,
                Message = $"File type '{extension}' is not allowed. Allowed types: .jpg, .jpeg, .png"
            };
        }

        // Validate content type
        if (!AllowedSignatureContentTypes.Contains(file.ContentType))
        {
            return new ProviderSignatureResponseDto
            {
                ProviderId = providerId,
                HasSignature = false,
                Message = $"Content type '{file.ContentType}' is not allowed. Only JPEG and PNG images are supported."
            };
        }

        // Validate file size (max 2MB for signature images)
        if (file.Length > 2 * 1024 * 1024)
        {
            return new ProviderSignatureResponseDto
            {
                ProviderId = providerId,
                HasSignature = false,
                Message = "File size exceeds maximum allowed size of 2MB"
            };
        }

        // Delete existing signature file from cloud storage if it exists
        if (!string.IsNullOrEmpty(provider.SignatureImagePath))
        {
            await _storageService.DeleteFileAsync(provider.SignatureImagePath);
        }

        // Generate unique filename and build cloud path
        var newFileName = _pathBuilder.GenerateStorageFileName(file.FileName);
        var folderPath = _pathBuilder.BuildProviderSignaturePath(provider.TenantId, providerId);
        var metadata = _metadataBuilder.BuildProviderSignatureMetadata(provider.TenantId, providerId);

        // Upload to cloud storage
        using var stream = file.OpenReadStream();
        var uploadResult = await _storageService.UploadFileAsync(
            stream,
            newFileName,
            folderPath,
            file.ContentType,
            metadata);

        if (!uploadResult.Success)
        {
            return new ProviderSignatureResponseDto
            {
                ProviderId = providerId,
                HasSignature = false,
                Message = "Failed to upload signature to cloud storage"
            };
        }

        // Update provider record with cloud path
        provider.SignatureImagePath = uploadResult.CloudPath;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new ProviderSignatureResponseDto
        {
            ProviderId = providerId,
            HasSignature = true,
            Message = "Signature uploaded successfully"
        };
    }

    public async Task<(Stream stream, string contentType)?> GetSignatureAsync(int providerId)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null || string.IsNullOrEmpty(provider.SignatureImagePath))
            return null;

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Download from cloud storage
        var stream = await _storageService.DownloadFileAsync(provider.SignatureImagePath);
        if (stream == null)
            return null;

        var extension = Path.GetExtension(provider.SignatureImagePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };

        return (stream, contentType);
    }

    public async Task<bool> DeleteSignatureAsync(int providerId)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null)
            return false;

        // CRITICAL: Tenant data isolation - verify provider belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return false;

        // Delete the file from cloud storage if it exists
        if (!string.IsNullOrEmpty(provider.SignatureImagePath))
        {
            await _storageService.DeleteFileAsync(provider.SignatureImagePath);
        }

        // Clear the path in database
        provider.SignatureImagePath = null;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    // ============================================
    // PROFILE PICTURE METHODS
    // ============================================

    public async Task<ProfilePictureResponseDto?> UploadProfilePictureAsync(int providerId, IFormFile file)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null) return null;

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Validate file extension
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedProfilePictureExtensions.Contains(extension))
        {
            return new ProfilePictureResponseDto
            {
                EntityId = providerId,
                EntityType = "Provider",
                HasProfilePicture = false,
                Message = $"File type '{extension}' is not allowed. Allowed types: .jpg, .jpeg, .png, .webp"
            };
        }

        // Validate content type
        if (!AllowedProfilePictureContentTypes.Contains(file.ContentType))
        {
            return new ProfilePictureResponseDto
            {
                EntityId = providerId,
                EntityType = "Provider",
                HasProfilePicture = false,
                Message = $"Content type '{file.ContentType}' is not allowed."
            };
        }

        // Validate file size (max 5MB)
        if (file.Length > MaxProfilePictureSize)
        {
            return new ProfilePictureResponseDto
            {
                EntityId = providerId,
                EntityType = "Provider",
                HasProfilePicture = false,
                Message = "File size exceeds maximum allowed size of 5MB"
            };
        }

        // Delete existing profile picture from cloud storage if it exists
        if (!string.IsNullOrEmpty(provider.ProfilePicturePath))
        {
            await _storageService.DeleteFileAsync(provider.ProfilePicturePath);
        }

        // Generate unique filename and build cloud path
        var newFileName = _pathBuilder.GenerateStorageFileName(file.FileName);
        var folderPath = _pathBuilder.BuildProviderProfilePicturePath(provider.TenantId, providerId);
        var metadata = _metadataBuilder.BuildProviderProfilePictureMetadata(provider.TenantId, providerId);

        // Upload to cloud storage
        using var stream = file.OpenReadStream();
        var uploadResult = await _storageService.UploadFileAsync(
            stream, newFileName, folderPath, file.ContentType, metadata);

        if (!uploadResult.Success)
        {
            return new ProfilePictureResponseDto
            {
                EntityId = providerId,
                EntityType = "Provider",
                HasProfilePicture = false,
                Message = "Failed to upload profile picture to cloud storage"
            };
        }

        // Update provider record with cloud path
        provider.ProfilePicturePath = uploadResult.CloudPath;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new ProfilePictureResponseDto
        {
            EntityId = providerId,
            EntityType = "Provider",
            HasProfilePicture = true,
            Message = "Profile picture uploaded successfully"
        };
    }

    public async Task<(Stream stream, string contentType)?> GetProfilePictureAsync(int providerId)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null || string.IsNullOrEmpty(provider.ProfilePicturePath))
            return null;

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return null;

        var stream = await _storageService.DownloadFileAsync(provider.ProfilePicturePath);
        if (stream == null) return null;

        var ext = Path.GetExtension(provider.ProfilePicturePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return (stream, contentType);
    }

    public async Task<bool> DeleteProfilePictureAsync(int providerId)
    {
        var provider = await _context.Providers.FindAsync(providerId);
        if (provider == null) return false;

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && provider.TenantId != _tenantProvider.TenantId.Value)
            return false;

        if (!string.IsNullOrEmpty(provider.ProfilePicturePath))
        {
            await _storageService.DeleteFileAsync(provider.ProfilePicturePath);
        }

        provider.ProfilePicturePath = null;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }
}

// Insurance Service
public interface IInsuranceService
{
    Task<List<InsuranceDto>> GetPatientInsurancesAsync(int patientId);
    Task<Insurance> AddInsuranceAsync(int patientId, InsuranceCreateDto dto);
    Task<Insurance?> UpdateInsuranceAsync(int insuranceId, InsuranceUpdateDto dto);
    Task<InsuranceVerificationResult> VerifyInsuranceAsync(int insuranceId);
    Task<InsuranceVerificationResult> VerifyInsuranceAsync(InsuranceVerificationRequest request);
    Task<EligibilityDetailsDto> GetEligibilityDetailsAsync(int insuranceId);
}

public class InsuranceService : IInsuranceService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EHR.Helpers.EncryptionHelper _encryptionHelper;
    private readonly IInsuranceAuthorizationService _authorizationService;
    private readonly IEligibilityApiService _eligibilityApi;

    public InsuranceService(EhrDbContext context, ITenantProvider tenantProvider, EHR.Helpers.EncryptionHelper encryptionHelper, IInsuranceAuthorizationService authorizationService, IEligibilityApiService eligibilityApi)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
        _authorizationService = authorizationService;
        _eligibilityApi = eligibilityApi;
    }

    public async Task<List<InsuranceDto>> GetPatientInsurancesAsync(int patientId)
    {
        // AsNoTracking: read-only list projection to DTOs. Decryption mutates
        // properties via reflection; without NoTracking, any later
        // SaveChangesAsync in the request would flush the decrypted insurance
        // back to the DB, corrupting encrypted subscriber/policy fields to
        // plaintext.
        var query = _context.Insurances
            .AsNoTracking()
            .Where(i => i.PatientId == patientId);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(i => i.TenantId == _tenantProvider.TenantId.Value);
        }

        var insurances = await query.OrderBy(i => i.Type).ToListAsync();

        // Decrypt PHI fields
        foreach (var insurance in insurances)
        {
            _encryptionHelper.DecryptEntity(insurance);
        }

        // Build DTOs with authorization data from new Authorization table
        var result = new List<InsuranceDto>();
        foreach (var i in insurances)
        {
            // Get authorizations for this insurance
            var authorizations = await _authorizationService.GetAuthorizationsForInsuranceAsync(i.InsuranceId);
            var currentAuth = authorizations.FirstOrDefault();

            result.Add(new InsuranceDto
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
                InsuranceCategory = i.InsuranceCategory,
                IsActive = i.IsActive == true,
                EligibilityStatus = i.EligibilityStatus,
                Copay = i.Copay,
                Deductible = i.Deductible,
                DeductibleMet = i.DeductibleMet,
                DeductibleRemaining = (i.DeductibleTotal ?? 0) - (i.DeductibleMet ?? 0),
                EffectiveFrom = i.EffectiveFrom,
                EffectiveTo = i.EffectiveTo,
                LastVerifiedAt = i.LastVerifiedAt,
                AttorneyName = i.AttorneyName,
                AttorneyPhone = i.AttorneyPhone,
                AttorneyEmail = i.AttorneyEmail,
                PlanName = i.PlanName,
                InNetwork = i.InNetwork,
                OutOfPocketMax = i.OutOfPocketMax,
                CurrentAuthorization = currentAuth,
                Authorizations = authorizations
            });
        }

        return result;
    }

    public async Task<Insurance> AddInsuranceAsync(int patientId, InsuranceCreateDto dto)
    {
        var insurance = new Insurance
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = patientId,
            PayerName = dto.PayerName,
            PayerId = dto.PayerId,
            PolicyNumber = dto.PolicyNumber,
            GroupNumber = dto.GroupNumber,
            SubscriberName = dto.SubscriberName,
            SubscriberFirstName = dto.SubscriberFirstName,
            SubscriberLastName = dto.SubscriberLastName,
            SubscriberDob = dto.SubscriberDob,
            SubscriberRelationship = dto.SubscriberRelationship,
            SubscriberId = dto.SubscriberId,
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveTo = dto.EffectiveTo,
            Type = dto.Type,
            InsuranceCategory = dto.InsuranceCategory,
            AttorneyName = dto.AttorneyName,
            AttorneyPhone = dto.AttorneyPhone,
            AttorneyEmail = dto.AttorneyEmail,
            AllowedVisits = dto.AllowedVisits,
            Copay = dto.Copay,
            Coinsurance = dto.Coinsurance,
            DeductibleTotal = dto.DeductibleTotal ?? dto.Deductible,
            IsActive = true,
            EligibilityStatus = (int)EligibilityStatus.PendingVerification,
            CreatedAt = DateTime.UtcNow
        };

        // Encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(insurance);

        _context.Insurances.Add(insurance);
        await _context.SaveChangesAsync();

        // CRITICAL (PHI encryption safety):
        // Detach before the return-decrypt so any subsequent SaveChangesAsync
        // in the request pipeline cannot flush the decrypted insurance back
        // to the DB.
        _context.Entry(insurance).State = EntityState.Detached;

        // Decrypt for return
        _encryptionHelper.DecryptEntity(insurance);
        return insurance;
    }

    public async Task<Insurance?> UpdateInsuranceAsync(int insuranceId, InsuranceUpdateDto dto)
    {
        var insurance = await _context.Insurances.FindAsync(insuranceId);
        if (insurance == null) return null;

        // CRITICAL: Tenant data isolation - verify insurance belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Decrypt existing values first
        _encryptionHelper.DecryptEntity(insurance);

        if (dto.PayerName != null) insurance.PayerName = dto.PayerName;
        if (dto.PayerId != null) insurance.PayerId = dto.PayerId;
        if (dto.PolicyNumber != null) insurance.PolicyNumber = dto.PolicyNumber;
        if (dto.GroupNumber != null) insurance.GroupNumber = dto.GroupNumber;
        if (dto.SubscriberName != null) insurance.SubscriberName = dto.SubscriberName;
        if (dto.SubscriberFirstName != null) insurance.SubscriberFirstName = dto.SubscriberFirstName;
        if (dto.SubscriberLastName != null) insurance.SubscriberLastName = dto.SubscriberLastName;
        if (dto.SubscriberDob.HasValue) insurance.SubscriberDob = dto.SubscriberDob;
        if (dto.SubscriberRelationship != null) insurance.SubscriberRelationship = dto.SubscriberRelationship;
        if (dto.SubscriberId != null) insurance.SubscriberId = dto.SubscriberId;
        if (dto.EffectiveFrom.HasValue) insurance.EffectiveFrom = dto.EffectiveFrom;
        if (dto.EffectiveTo.HasValue) insurance.EffectiveTo = dto.EffectiveTo;
        if (dto.Type.HasValue) insurance.Type = dto.Type.Value;
        if (dto.InsuranceCategory.HasValue) insurance.InsuranceCategory = dto.InsuranceCategory.Value;
        if (dto.IsActive.HasValue) insurance.IsActive = dto.IsActive.Value;
        if (dto.AttorneyName != null) insurance.AttorneyName = dto.AttorneyName;
        if (dto.AttorneyPhone != null) insurance.AttorneyPhone = dto.AttorneyPhone;
        if (dto.AttorneyEmail != null) insurance.AttorneyEmail = dto.AttorneyEmail;
        if (dto.AllowedVisits.HasValue) insurance.AllowedVisits = dto.AllowedVisits.Value;
        if (dto.Copay.HasValue) insurance.Copay = dto.Copay;
        if (dto.Coinsurance.HasValue) insurance.Coinsurance = dto.Coinsurance;
        if (dto.DeductibleTotal.HasValue) insurance.DeductibleTotal = dto.DeductibleTotal;

        insurance.UpdatedAt = DateTime.UtcNow;

        // Encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(insurance);

        await _context.SaveChangesAsync();

        // CRITICAL (PHI encryption safety):
        // Detach before the return-decrypt so any subsequent SaveChangesAsync
        // in the request pipeline cannot flush the decrypted insurance back
        // to the DB as plaintext.
        _context.Entry(insurance).State = EntityState.Detached;

        // Decrypt for return
        _encryptionHelper.DecryptEntity(insurance);
        return insurance;
    }

    public async Task<InsuranceVerificationResult> VerifyInsuranceAsync(int insuranceId)
    {
        var insurance = await _context.Insurances.FindAsync(insuranceId);
        if (insurance == null)
            return new InsuranceVerificationResult { Success = false, ErrorMessage = "Insurance not found" };

        // CRITICAL: Tenant data isolation - verify insurance belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            return new InsuranceVerificationResult { Success = false, ErrorMessage = "Insurance not found" };

        // Decrypt first to read insurance fields
        _encryptionHelper.DecryptEntity(insurance);

        // Get organization NPI from tenant
        string organizationNpi = null;
        if (_tenantProvider.TenantId.HasValue)
        {
            var tenant = await _context.Tenants.FindAsync(_tenantProvider.TenantId.Value);
            organizationNpi = tenant?.Npi;
        }

        // Build subscriber name: prefer first/last, fallback to combined name
        var subscriberName = !string.IsNullOrWhiteSpace(insurance.SubscriberFirstName)
            ? $"{insurance.SubscriberFirstName} {insurance.SubscriberLastName}".Trim()
            : insurance.SubscriberName;

        // Call Office Ally Real-Time Eligibility API (270/271)
        var result = await _eligibilityApi.CheckEligibilityAsync(
            insurance.PayerName, insurance.PayerId, insurance.PolicyNumber,
            insurance.GroupNumber, insurance.SubscriberId, subscriberName,
            insurance.SubscriberDob, organizationNpi);

        if (!result.Success)
            return result;

        // Parse rich eligibility details from raw OA response for immediate frontend display
        if (!string.IsNullOrEmpty(result.RawResponseJson))
        {
            try { result.EligibilityDetails = EligibilityApiService.ParseRawResponseToDetails(result.RawResponseJson); }
            catch { /* Non-critical — frontend will still show basic panel */ }
        }

        // Update insurance record with verification results
        insurance.EligibilityStatus = result.IsEligible ? (int)EligibilityStatus.Eligible : (int)EligibilityStatus.NotEligible;
        insurance.Copay = result.Copay;
        insurance.Coinsurance = result.Coinsurance;
        insurance.DeductibleTotal = result.DeductibleTotal;
        insurance.DeductibleMet = result.DeductibleMet;
        insurance.AllowedVisits = result.AllowedVisits;
        insurance.CoverageNotes = result.CoverageNotes;
        insurance.PlanName = result.PlanName;
        insurance.InNetwork = result.InNetwork;
        insurance.OutOfPocketMax = result.OutOfPocketMax;
        insurance.LastEligibilityResponseJson = result.RawResponseJson ?? System.Text.Json.JsonSerializer.Serialize(result);
        insurance.LastVerifiedAt = result.VerifiedAt;
        insurance.UpdatedAt = DateTime.UtcNow;

        // Encrypt PHI fields before saving
        _encryptionHelper.EncryptEntity(insurance);
        await _context.SaveChangesAsync();

        return result;
    }

    public async Task<InsuranceVerificationResult> VerifyInsuranceAsync(InsuranceVerificationRequest request)
    {
        // Always verify using form field values so user can check before saving
        var missingFields = new List<string>();

        if (!request.Type.HasValue)
            missingFields.Add("Insurance Type");
        if (string.IsNullOrWhiteSpace(request.PayerId))
            missingFields.Add("Payer ID");
        if (string.IsNullOrWhiteSpace(request.PayerName))
            missingFields.Add("Insurance Company Name");
        if (string.IsNullOrWhiteSpace(request.PolicyNumber))
            missingFields.Add("Policy Number / Member ID");
        if (string.IsNullOrWhiteSpace(request.SubscriberName) || request.SubscriberName.Trim().Length < 2)
            missingFields.Add("Subscriber Name (First and Last)");
        if (!request.SubscriberDob.HasValue)
            missingFields.Add("Subscriber Date of Birth");

        if (missingFields.Any())
        {
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = $"Missing required fields: {string.Join(", ", missingFields)}"
            };
        }

        // Call eligibility API using form values
        var tenantNpi = _tenantProvider.TenantId.HasValue
            ? (await _context.Tenants.FindAsync(_tenantProvider.TenantId.Value))?.Npi
            : null;
        var result = await _eligibilityApi.CheckEligibilityAsync(
            request.PayerName, request.PayerId, request.PolicyNumber,
            request.GroupNumber, request.SubscriberId, request.SubscriberName,
            request.SubscriberDob, tenantNpi);

        if (!result.Success)
            return result;

        // Parse rich eligibility details from raw OA response for immediate frontend display
        if (!string.IsNullOrEmpty(result.RawResponseJson))
        {
            try { result.EligibilityDetails = EligibilityApiService.ParseRawResponseToDetails(result.RawResponseJson); }
            catch { /* Non-critical — frontend will still show basic panel */ }
        }

        // If insurance exists in DB, update it with verification results
        if (request.InsuranceId.HasValue)
        {
            var insurance = await _context.Insurances.FindAsync(request.InsuranceId.Value);
            if (insurance != null && (!_tenantProvider.TenantId.HasValue || insurance.TenantId == _tenantProvider.TenantId.Value))
            {
                insurance.EligibilityStatus = result.IsEligible ? (int)EligibilityStatus.Eligible : (int)EligibilityStatus.NotEligible;
                insurance.Copay = result.Copay;
                insurance.Coinsurance = result.Coinsurance;
                insurance.DeductibleTotal = result.DeductibleTotal;
                insurance.DeductibleMet = result.DeductibleMet;
                insurance.AllowedVisits = result.AllowedVisits;
                insurance.CoverageNotes = result.CoverageNotes;
                insurance.PlanName = result.PlanName;
                insurance.InNetwork = result.InNetwork;
                insurance.OutOfPocketMax = result.OutOfPocketMax;
                insurance.LastEligibilityResponseJson = result.RawResponseJson ?? System.Text.Json.JsonSerializer.Serialize(result);
                insurance.LastVerifiedAt = result.VerifiedAt;
                insurance.UpdatedAt = DateTime.UtcNow;
                _encryptionHelper.EncryptEntity(insurance);
                await _context.SaveChangesAsync();
            }
        }

        return result;
    }

    public async Task<EligibilityDetailsDto> GetEligibilityDetailsAsync(int insuranceId)
    {
        var insurance = await _context.Insurances.FindAsync(insuranceId);
        if (insurance == null)
            return null;

        // Tenant isolation
        if (_tenantProvider.TenantId.HasValue && insurance.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Decrypt to access LastEligibilityResponseJson
        _encryptionHelper.DecryptEntity(insurance);

        var rawJson = insurance.LastEligibilityResponseJson;
        if (string.IsNullOrEmpty(rawJson))
            return null;

        var details = EligibilityApiService.ParseRawResponseToDetails(rawJson);
        if (details != null)
            details.VerifiedAt = insurance.LastVerifiedAt;

        return details;
    }
}

// Note Service
public interface INoteService
{
    Task<List<NoteListDto>> GetNotesAsync(int? patientId = null, int? providerId = null, NoteStatus? status = null);
    Task<Note?> GetNoteByIdAsync(int noteId);
    Task<Note> CreateNoteAsync(NoteCreateDto dto);
    Task<Note?> UpdateNoteAsync(int noteId, NoteUpdateDto dto);
    Task<Note?> SignNoteAsync(int noteId, int signedBy);
}

public class NoteService : INoteService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    
    public NoteService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }
    
    public async Task<List<NoteListDto>> GetNotesAsync(int? patientId = null, int? providerId = null, NoteStatus? status = null)
    {
        var query = _context.Notes
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.InverseParentNote)
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);
        }

        if (patientId.HasValue) query = query.Where(n => n.PatientId == patientId);
        if (providerId.HasValue) query = query.Where(n => n.ProviderId == providerId);
        if (status.HasValue) query = query.Where(n => n.Status == (int)status.Value);
        
        return await query
            .Where(n => n.IsAddendum != true)
            .OrderByDescending(n => n.ServiceDate)
            .Select(n => new NoteListDto
            {
                NoteId = n.NoteId,
                PatientId = n.PatientId,
                PatientName = n.Patient!.FirstName + " " + n.Patient.LastName,
                ProviderId = n.ProviderId,
                ProviderName = n.Provider!.FirstName + " " + n.Provider.LastName,
                Type = n.Type,
                ServiceDate = n.ServiceDate,
                Status = n.Status ?? 0,
                SignedAt = n.SignedAt,
                HasAddendum = n.InverseParentNote.Any(),
                CreatedAt = n.CreatedAt ?? DateTime.MinValue
            })
            .ToListAsync();
    }
    
    public async Task<Note?> GetNoteByIdAsync(int noteId)
    {
        var query = _context.Notes
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.Appointment)
            .Include(n => n.CareEpisode)
            .Include(n => n.InverseParentNote)
            .Where(n => n.NoteId == noteId);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);
        }

        return await query.FirstOrDefaultAsync();
    }
    
    public async Task<Note> CreateNoteAsync(NoteCreateDto dto)
    {
        var note = new Note
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = dto.PatientId,
            ProviderId = dto.ProviderId,
            AppointmentId = dto.AppointmentId,
            CareEpisodeId = dto.CareEpisodeId,
            Type = dto.Type,
            ServiceDate = dto.ServiceDate,
            Subjective = dto.Subjective,
            Objective = dto.Objective,
            Assessment = dto.Assessment,
            Plan = dto.Plan,
            VitalSigns = dto.VitalSigns,
            FunctionalTests = dto.FunctionalTests,
            Interventions = dto.Interventions,
            PatientEducation = dto.PatientEducation,
            HomeExerciseProgram = dto.HomeExerciseProgram,
            Cptcodes = dto.CPTCodes,
            Icdcodes = dto.ICDCodes,
            TotalMinutes = dto.TotalMinutes,
            DirectMinutes = dto.DirectMinutes,
            Status = (int)NoteStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Notes.Add(note);
        await _context.SaveChangesAsync();
        return note;
    }
    
    public async Task<Note?> UpdateNoteAsync(int noteId, NoteUpdateDto dto)
    {
        var note = await _context.Notes.FindAsync(noteId);
        if (note == null || note.Status == (int)NoteStatus.Finalized) return null;

        // CRITICAL: Tenant data isolation - verify note belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && note.TenantId != _tenantProvider.TenantId.Value)
            return null;

        if (dto.Subjective != null) note.Subjective = dto.Subjective;
        if (dto.Objective != null) note.Objective = dto.Objective;
        if (dto.Assessment != null) note.Assessment = dto.Assessment;
        if (dto.Plan != null) note.Plan = dto.Plan;
        if (dto.VitalSigns != null) note.VitalSigns = dto.VitalSigns;
        if (dto.FunctionalTests != null) note.FunctionalTests = dto.FunctionalTests;
        if (dto.Interventions != null) note.Interventions = dto.Interventions;
        if (dto.PatientEducation != null) note.PatientEducation = dto.PatientEducation;
        if (dto.HomeExerciseProgram != null) note.HomeExerciseProgram = dto.HomeExerciseProgram;
        if (dto.CPTCodes != null) note.Cptcodes = dto.CPTCodes;
        if (dto.ICDCodes != null) note.Icdcodes = dto.ICDCodes;
        if (dto.TotalMinutes.HasValue) note.TotalMinutes = dto.TotalMinutes;
        if (dto.DirectMinutes.HasValue) note.DirectMinutes = dto.DirectMinutes;
        
        note.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return note;
    }
    
    public async Task<Note?> SignNoteAsync(int noteId, int signedBy)
    {
        var note = await _context.Notes.FindAsync(noteId);
        if (note == null || note.Status == (int)NoteStatus.Finalized) return null;

        // CRITICAL: Tenant data isolation - verify note belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && note.TenantId != _tenantProvider.TenantId.Value)
            return null;

        note.Status = (int)NoteStatus.Signed;
        note.SignedAt = DateTime.UtcNow;
        note.SignedBy = signedBy;
        note.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return note;
    }
}

// Billing Service
public interface IBillingService
{
    Task<List<ChargeListDto>> GetChargesAsync(int? patientId = null, ChargeStatus? status = null);
    Task<Charge> CreateChargeAsync(ChargeCreateDto dto);
    Task<List<ClaimListDto>> GetClaimsAsync(ClaimStatus? status = null);
    Task<BillingClaim> CreateClaimAsync(ClaimCreateDto dto);
    Task<BillingClaim?> SubmitClaimAsync(int claimId);
    Task<ARAgingDto> GetARAgingAsync();
    // CMS 1500 / UB04 Claim Management
    Task<BillingClaim> AutoCreateDraftClaimAsync(int clinicalNoteId);
    Task<ClaimDetailDto?> GetClaimDetailAsync(int claimId);
    Task<BillingClaim?> UpdateClaimFieldsAsync(int claimId, ClaimUpdateDto dto);
    Task<Charge> AddChargeToClaimAsync(ClaimChargeCreateDto dto);
    Task<bool> UpdateChargeOnClaimAsync(int chargeId, ClaimChargeCreateDto dto);
    Task<bool> RemoveChargeFromClaimAsync(int chargeId);
    Task<BillingClaim?> MarkClaimReadyAsync(int claimId);
    Task<byte[]> GenerateClaimPdfAsync(int claimId);
    Task<BillingPagedResult<ClaimListDto>> GetClaimsPagedAsync(int? patientId = null, int? providerId = null, string? payerSearch = null, List<int>? statuses = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, int page = 1, int pageSize = 25);
    Task<BillingPagedResult<ChargeListDto>> GetChargesPagedAsync(int? patientId = null, int? providerId = null, List<int>? statuses = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, int page = 1, int pageSize = 25);
    Task<CptSuggestionResponse> SuggestCptCodesAsync(CptSuggestionRequest request);
    Task<IcdSuggestionResponse> SuggestIcdCodesAsync(IcdSuggestionRequest request);
}

public class BillingService : IBillingService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EHR.Helpers.EncryptionHelper _encryptionHelper;
    private readonly IHtmlToPdfService _htmlToPdfService;
    private readonly IGeminiService _geminiService;
    private readonly IConfiguration _config;

    public BillingService(EhrDbContext context, ITenantProvider tenantProvider, EHR.Helpers.EncryptionHelper encryptionHelper, IHtmlToPdfService htmlToPdfService, IGeminiService geminiService, IConfiguration config)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
        _htmlToPdfService = htmlToPdfService;
        _geminiService = geminiService;
        _config = config;
    }

    // Amendment-window helper — computes whether a claim's Submit is currently
    // locked given its linked appointment's CheckOutTime. Shared by the list
    // and detail paths so the two surfaces stay in lock-step. Returns UTC-kinded
    // DateTimes so the JSON wire format includes 'Z' and the browser parses
    // correctly (same rule as ClinicalNoteAmendmentService).
    private (bool isLocked, DateTime? windowEndsAt) ComputeSubmitLock(DateTime? checkOutTime)
    {
        var windowHours = _config.GetValue<int?>("ClinicalNotes:AmendmentWindowHours") ?? 24;
        if (!checkOutTime.HasValue || windowHours <= 0) return (false, null);
        var end = checkOutTime.Value.AddHours(windowHours);
        var endUtc = DateTime.SpecifyKind(end, DateTimeKind.Utc);
        var isLocked = DateTime.UtcNow < end;
        return (isLocked, endUtc);
    }

    public async Task<List<ChargeListDto>> GetChargesAsync(int? patientId = null, ChargeStatus? status = null)
    {
        // AsNoTracking: read-only list projection to DTOs. The included Patient
        // and Provider are decrypted in-place below; without NoTracking, any
        // later SaveChangesAsync in the same request would flush decrypted
        // Patient/Provider fields back to the DB as plaintext.
        var query = _context.Charges
            .AsNoTracking()
            .Include(c => c.Patient)
            .Include(c => c.Provider)
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(c => c.TenantId == _tenantProvider.TenantId.Value);
        }

        if (patientId.HasValue) query = query.Where(c => c.PatientId == patientId);
        if (status.HasValue) query = query.Where(c => c.Status == (int)status.Value);

        // Load entities first
        var charges = await query.OrderByDescending(c => c.ServiceDate).ToListAsync();

        // Decrypt PHI fields in memory
        foreach (var charge in charges)
        {
            if (charge.Patient != null)
                _encryptionHelper.DecryptEntity(charge.Patient);
            if (charge.Provider != null)
                _encryptionHelper.DecryptEntity(charge.Provider);
        }

        // Map to DTOs after decryption
        return charges.Select(c => new ChargeListDto
        {
            ChargeId = c.ChargeId,
            PatientId = c.PatientId,
            PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}" : "Unknown",
            ProviderName = c.Provider != null ? $"{c.Provider.FirstName} {c.Provider.LastName}" : "Unknown",
            ServiceDate = c.ServiceDate,
            CptCode = c.Cptcode,
            CPTDescription = c.Cptdescription,
            Units = c.Units ?? 1,
            Modifiers = string.Join(", ", new[] { c.Modifier1, c.Modifier2, c.Modifier3, c.Modifier4 }.Where(m => m != null)),
            ChargeAmount = c.ChargeAmount,
            PaidAmount = c.PaidAmount,
            Balance = c.ChargeAmount - (c.PaidAmount ?? 0) - (c.AdjustmentAmount ?? 0),
            Status = c.Status ?? 0,
            ClaimId = c.ClaimId
        }).ToList();
    }
    
    public async Task<Charge> CreateChargeAsync(ChargeCreateDto dto)
    {
        var charge = new Charge
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = dto.PatientId,
            NoteId = dto.NoteId,
            AppointmentId = dto.AppointmentId,
            ProviderId = dto.ProviderId ?? _tenantProvider.TenantId!.Value, // Default to tenant if not provided
            ServiceDate = dto.ServiceDate,
            Cptcode = dto.CPTCode ?? dto.CptCode,
            Cptdescription = dto.CPTDescription,
            Units = dto.Units,
            Modifier1 = dto.Modifier1,
            Modifier2 = dto.Modifier2,
            Icdpointers = dto.ICDCodes ?? dto.DiagnosisCodes,
            ChargeAmount = dto.ChargeAmount,
            Status = (int)ChargeStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Charges.Add(charge);
        await _context.SaveChangesAsync();
        return charge;
    }
    
    public async Task<List<ClaimListDto>> GetClaimsAsync(ClaimStatus? status = null)
    {
        // AsNoTracking: read-only list projection; included Patient and Insurance
        // are decrypted in-place for DTOs and must not be tracked.
        var query = _context.BillingClaims
            .AsNoTracking()
            .Include(c => c.Patient)
            .Include(c => c.Insurance)
            .Include(c => c.Appointment)   // for submit-lock computation
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(c => c.TenantId == _tenantProvider.TenantId.Value);
        }

        if (status.HasValue) query = query.Where(c => c.Status == (int)status.Value);

        // Load entities first
        var claims = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

        // Decrypt PHI fields in memory
        foreach (var claim in claims)
        {
            if (claim.Patient != null)
                _encryptionHelper.DecryptEntity(claim.Patient);
            if (claim.Insurance != null)
                _encryptionHelper.DecryptEntity(claim.Insurance);
        }

        // Map to DTOs after decryption
        return claims.Select(c =>
        {
            var (isLocked, windowEnd) = ComputeSubmitLock(c.Appointment?.CheckOutTime);
            return new ClaimListDto
            {
                ClaimId = c.ClaimId,
                ClaimNumber = c.ClaimNumber,
                PatientId = c.PatientId,
                PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}" : "Unknown",
                PayerName = c.Insurance != null ? c.Insurance.PayerName : "Unknown Payer",
                ServiceDateFrom = c.ServiceDateFrom,
                ServiceDateTo = c.ServiceDateTo,
                TotalCharged = c.TotalCharged,
                TotalPaid = c.TotalPaid ?? 0,
                Balance = c.TotalCharged - (c.TotalPaid ?? 0) - (c.TotalAdjustment ?? 0),
                Status = c.Status ?? 0,
                SubmittedAt = c.SubmittedAt,
                DaysInAR = c.SubmittedAt.HasValue ? (int)(DateTime.UtcNow - c.SubmittedAt.Value).TotalDays : 0,
                DenialReason = c.DenialReason,
                IsSubmitLocked = isLocked,
                AmendmentWindowEndsAt = windowEnd
            };
        }).ToList();
    }

    public async Task<BillingPagedResult<ClaimListDto>> GetClaimsPagedAsync(int? patientId = null, int? providerId = null, string? payerSearch = null, List<int>? statuses = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, int page = 1, int pageSize = 25)
    {
        // AsNoTracking: read-only paged list; Patient/Insurance/Provider are
        // decrypted in-place for DTO construction, must not be tracked.
        var query = _context.BillingClaims
            .AsNoTracking()
            .Include(c => c.Patient)
            .Include(c => c.Insurance)
            .Include(c => c.Provider)
            .Include(c => c.Appointment)   // for CheckOutTime → submit-lock computation
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(c => c.TenantId == _tenantProvider.TenantId.Value);

        if (patientId.HasValue) query = query.Where(c => c.PatientId == patientId.Value);
        if (providerId.HasValue) query = query.Where(c => c.ProviderId == providerId.Value);
        if (statuses != null && statuses.Any()) query = query.Where(c => statuses.Contains(c.Status ?? 0));
        if (dateFrom.HasValue) query = query.Where(c => c.ServiceDateFrom >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(c => c.ServiceDateFrom <= dateTo.Value);

        var allClaims = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

        foreach (var claim in allClaims)
        {
            if (claim.Patient != null) _encryptionHelper.DecryptEntity(claim.Patient);
            if (claim.Insurance != null) _encryptionHelper.DecryptEntity(claim.Insurance);
            if (claim.Provider != null) _encryptionHelper.DecryptEntity(claim.Provider);
        }

        // Post-decryption payer name filter
        if (!string.IsNullOrWhiteSpace(payerSearch))
            allClaims = allClaims.Where(c => c.Insurance != null && (c.Insurance.PayerName ?? "").Contains(payerSearch, StringComparison.OrdinalIgnoreCase)).ToList();

        var totalCount = allClaims.Count;
        var totalBilled = allClaims.Sum(c => c.TotalCharged);
        var totalPaid = allClaims.Sum(c => c.TotalPaid ?? 0);

        var paged = allClaims.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new BillingPagedResult<ClaimListDto>
        {
            Items = paged.Select(c =>
            {
                var (isLocked, windowEnd) = ComputeSubmitLock(c.Appointment?.CheckOutTime);
                return new ClaimListDto
                {
                    ClaimId = c.ClaimId,
                    ClaimNumber = c.ClaimNumber,
                    PatientId = c.PatientId,
                    PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}" : "Unknown",
                    ProviderId = c.ProviderId,
                    ProviderName = c.Provider != null ? $"{c.Provider.FirstName} {c.Provider.LastName}" : "",
                    PayerName = c.Insurance != null ? c.Insurance.PayerName : "Unknown Payer",
                    ServiceDateFrom = c.ServiceDateFrom,
                    ServiceDateTo = c.ServiceDateTo,
                    TotalCharged = c.TotalCharged,
                    TotalPaid = c.TotalPaid ?? 0,
                    Balance = c.TotalCharged - (c.TotalPaid ?? 0) - (c.TotalAdjustment ?? 0),
                    Status = c.Status ?? 0,
                    SubmittedAt = c.SubmittedAt,
                    DaysInAR = c.SubmittedAt.HasValue ? (int)(DateTime.UtcNow - c.SubmittedAt.Value).TotalDays : 0,
                    DenialReason = c.DenialReason,
                    IsSubmitLocked = isLocked,
                    AmendmentWindowEndsAt = windowEnd
                };
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalBilled = totalBilled,
            TotalPaid = totalPaid,
            TotalOutstanding = totalBilled - totalPaid
        };
    }

    public async Task<BillingPagedResult<ChargeListDto>> GetChargesPagedAsync(int? patientId = null, int? providerId = null, List<int>? statuses = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, int page = 1, int pageSize = 25)
    {
        // AsNoTracking: read-only paged list; Patient/Provider decrypted in-place
        // for DTO construction, must not be tracked.
        var query = _context.Charges
            .AsNoTracking()
            .Include(c => c.Patient)
            .Include(c => c.Provider)
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(c => c.TenantId == _tenantProvider.TenantId.Value);

        if (patientId.HasValue) query = query.Where(c => c.PatientId == patientId.Value);
        if (providerId.HasValue) query = query.Where(c => c.ProviderId == providerId.Value);
        if (statuses != null && statuses.Any()) query = query.Where(c => statuses.Contains(c.Status ?? 0));
        if (dateFrom.HasValue) query = query.Where(c => c.ServiceDate >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(c => c.ServiceDate <= dateTo.Value);

        var allCharges = await query.OrderByDescending(c => c.ServiceDate).ToListAsync();

        foreach (var charge in allCharges)
        {
            if (charge.Patient != null) _encryptionHelper.DecryptEntity(charge.Patient);
            if (charge.Provider != null) _encryptionHelper.DecryptEntity(charge.Provider);
        }

        var totalCount = allCharges.Count;
        var totalAmount = allCharges.Sum(c => c.ChargeAmount);
        var totalPaid = allCharges.Sum(c => c.PaidAmount ?? 0);
        var pendingCount = allCharges.Count(c => (c.Status ?? 0) == 0);
        var billedCount = allCharges.Count(c => (c.Status ?? 0) == 1);

        var paged = allCharges.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new BillingPagedResult<ChargeListDto>
        {
            Items = paged.Select(c => new ChargeListDto
            {
                ChargeId = c.ChargeId,
                PatientId = c.PatientId,
                PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}" : "Unknown",
                ProviderName = c.Provider != null ? $"{c.Provider.FirstName} {c.Provider.LastName}" : "Unknown",
                ServiceDate = c.ServiceDate,
                CptCode = c.Cptcode,
                CPTDescription = c.Cptdescription,
                Units = c.Units ?? 1,
                Modifiers = string.Join(", ", new[] { c.Modifier1, c.Modifier2, c.Modifier3, c.Modifier4 }.Where(m => m != null)),
                ChargeAmount = c.ChargeAmount,
                PaidAmount = c.PaidAmount,
                Balance = c.ChargeAmount - (c.PaidAmount ?? 0) - (c.AdjustmentAmount ?? 0),
                Status = c.Status ?? 0,
                ClaimId = c.ClaimId
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalBilled = totalAmount,
            TotalPaid = totalPaid,
            TotalOutstanding = totalAmount - totalPaid,
            PendingCount = pendingCount,
            BilledCount = billedCount
        };
    }

    public async Task<BillingClaim> CreateClaimAsync(ClaimCreateDto dto)
    {
        var charges = await _context.Charges
            .Where(c => dto.ChargeIds.Contains(c.ChargeId))
            .ToListAsync();
        
        if (!charges.Any())
            throw new InvalidOperationException("No charges found");
        
        var claim = new BillingClaim
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = dto.PatientId,
            InsuranceId = dto.InsuranceId,
            ClaimNumber = $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
            ServiceDateFrom = charges.Min(c => c.ServiceDate),
            ServiceDateTo = charges.Max(c => c.ServiceDate),
            TotalCharged = charges.Sum(c => c.ChargeAmount),
            Status = (int)ClaimStatus.Draft,
            Type = (int)ClaimType.Professional,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.BillingClaims.Add(claim);
        await _context.SaveChangesAsync();
        
        // Link charges to claim
        foreach (var charge in charges)
        {
            charge.ClaimId = claim.ClaimId;
            charge.Status = (int)ChargeStatus.Billed;
        }
        await _context.SaveChangesAsync();
        
        return claim;
    }
    
    public async Task<BillingClaim?> SubmitClaimAsync(int claimId)
    {
        var claim = await _context.BillingClaims.FindAsync(claimId);
        if (claim == null) return null;

        // CRITICAL: Tenant data isolation - verify claim belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && claim.TenantId != _tenantProvider.TenantId.Value)
            return null;

        claim.Status = (int)ClaimStatus.Submitted;
        claim.SubmittedAt = DateTime.UtcNow;
        claim.UpdatedAt = DateTime.UtcNow;
        
        // Add status history
        _context.ClaimStatusHistories.Add(new ClaimStatusHistory
        {
            TenantId = _tenantProvider.TenantId!.Value,
            ClaimId = claimId,
            Status = (int)ClaimStatus.Submitted,
            Notes = "Claim submitted to payer",
            CreatedAt = DateTime.UtcNow
        });
        
        await _context.SaveChangesAsync();
        return claim;
    }
    
    public async Task<ARAgingDto> GetARAgingAsync()
    {
        var query = _context.BillingClaims
            .Where(c => c.Status == (int)ClaimStatus.Submitted || c.Status == (int)ClaimStatus.Pending);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(c => c.TenantId == _tenantProvider.TenantId.Value);
        }

        var claims = await query.ToListAsync();
        
        var now = DateTime.UtcNow;
        var result = new ARAgingDto
        {
            Current = claims.Where(c => c.SubmittedAt.HasValue && (now - c.SubmittedAt.Value).TotalDays <= 30)
                           .Sum(c => c.TotalCharged - (c.TotalPaid ?? 0)),
            Days31To60 = claims.Where(c => c.SubmittedAt.HasValue && (now - c.SubmittedAt.Value).TotalDays > 30 && (now - c.SubmittedAt.Value).TotalDays <= 60)
                              .Sum(c => c.TotalCharged - (c.TotalPaid ?? 0)),
            Days61To90 = claims.Where(c => c.SubmittedAt.HasValue && (now - c.SubmittedAt.Value).TotalDays > 60 && (now - c.SubmittedAt.Value).TotalDays <= 90)
                              .Sum(c => c.TotalCharged - (c.TotalPaid ?? 0)),
            Days91To120 = claims.Where(c => c.SubmittedAt.HasValue && (now - c.SubmittedAt.Value).TotalDays > 90 && (now - c.SubmittedAt.Value).TotalDays <= 120)
                               .Sum(c => c.TotalCharged - (c.TotalPaid ?? 0)),
            Over120Days = claims.Where(c => c.SubmittedAt.HasValue && (now - c.SubmittedAt.Value).TotalDays > 120)
                               .Sum(c => c.TotalCharged - (c.TotalPaid ?? 0))
        };
        result.Total = result.Current + result.Days31To60 + result.Days61To90 + result.Days91To120 + result.Over120Days;
        return result;
    }

    // ── Auto-Create Draft Claim from Signed Clinical Note ─────
    public async Task<BillingClaim> AutoCreateDraftClaimAsync(int clinicalNoteId)
    {
        var tenantId = _tenantProvider.TenantId!.Value;

        // Prevent duplicate claims for the same note
        var existingClaim = await _context.BillingClaims
            .FirstOrDefaultAsync(c => c.ClinicalNoteId == clinicalNoteId && c.TenantId == tenantId);
        if (existingClaim != null)
            return existingClaim;

        // Load clinical note with related entities
        // NOTE: CareEpisode is included for legacy fields only (CareEpisodeId / referring provider).
        //       It is NOT used for diagnosis codes in Internal Medicine — use Encounter.IcdSelections instead.
        var note = await _context.ClinicalNotes
            .Include(n => n.Appointment)
                .ThenInclude(a => a.Location)
            .Include(n => n.Appointment)
                .ThenInclude(a => a.CareEpisode)
            .Include(n => n.Encounter)
            .Include(n => n.Provider)
            .Include(n => n.Patient)
                .ThenInclude(p => p.PreferredLocation)
            .FirstOrDefaultAsync(n => n.ClinicalNoteId == clinicalNoteId && n.TenantId == tenantId);

        if (note == null)
            throw new InvalidOperationException($"Clinical note {clinicalNoteId} not found");

        // Detach patient & provider BEFORE decrypting to prevent EF writing plaintext back to DB
        _context.Entry(note.Patient).State = EntityState.Detached;
        _encryptionHelper.DecryptEntity(note.Patient);
        if (note.Provider != null)
        {
            _context.Entry(note.Provider).State = EntityState.Detached;
            _encryptionHelper.DecryptEntity(note.Provider);
        }

        var tenant = await _context.Tenants.FindAsync(tenantId);

        // Find patient's active primary insurance (Type=0=Primary)
        var insurance = await _context.Insurances
            .Where(i => i.PatientId == note.PatientId && i.TenantId == tenantId && i.IsActive == true && i.Type == 0)
            .FirstOrDefaultAsync();

        // Check if patient is self-pay (InsuranceCategory=3 or no insurance at all)
        if (insurance == null)
        {
            // Check if patient has ONLY self-pay insurance entries
            var hasSelfPayOnly = await _context.Insurances
                .Where(i => i.PatientId == note.PatientId && i.TenantId == tenantId && i.IsActive == true)
                .AllAsync(i => i.InsuranceCategory == 3);

            var hasAnyInsurance = await _context.Insurances
                .AnyAsync(i => i.PatientId == note.PatientId && i.TenantId == tenantId && i.IsActive == true);

            if (!hasAnyInsurance || hasSelfPayOnly)
            {
                // Self-pay patient — no claim needed, charges go directly to patient balance
                // Charges are already created from the clinical note; just skip claim generation
                return null;
            }
        }

        if (insurance != null)
        {
            _context.Entry(insurance).State = EntityState.Detached;
            _encryptionHelper.DecryptEntity(insurance);
        }

        // Convert UTC to location timezone for correct service date
        // TimezoneHelper.ConvertFromUtc falls back to DefaultTimeZoneId when TimeZoneId is null/empty
        var serviceDateUtc = note.Appointment?.StartTime ?? DateTime.UtcNow;
        var serviceDate = DateOnly.FromDateTime(
            TimezoneHelper.ConvertFromUtc(serviceDateUtc, note.Appointment?.Location?.TimeZoneId));

        // Find authorization matching insurance + service date range
        Authorization authorization = null;
        if (insurance != null)
        {
            authorization = await _context.Authorizations
                .Where(a => a.InsuranceId == insurance.InsuranceId && a.TenantId == tenantId)
                .Where(a => !a.ExpiryDate.HasValue || a.ExpiryDate.Value >= serviceDate)
                .OrderByDescending(a => a.DateOfValidation)
                .FirstOrDefaultAsync();
        }

        // Get care episode
        var careEpisode = note.Appointment?.CareEpisode
            ?? await _context.CareEpisodes
                .Where(ce => ce.PatientId == note.PatientId && ce.TenantId == tenantId && ce.Status == 0)
                .OrderByDescending(ce => ce.StartDate)
                .FirstOrDefaultAsync();

        var location = note.Appointment?.Location
            ?? note.Patient?.PreferredLocation
            ?? await _context.Locations
                .Where(l => l.TenantId == tenantId && l.IsActive == true && l.IsPrimary == true)
                .FirstOrDefaultAsync()
            ?? await _context.Locations
                .Where(l => l.TenantId == tenantId && l.IsActive == true)
                .FirstOrDefaultAsync();
        var provider = note.Provider;
        var patient = note.Patient;

        // Build diagnosis codes JSON from the encounter's ICD-10 selections (Internal Medicine flow).
        // Source of truth = Encounter.IcdSelections (a JSON array of {code, description, aiSuggested}).
        // Order in the array preserves the provider's intended order:
        //   first item = primary diagnosis = CMS-1500 Box 21 letter A.
        // CareEpisode.PrimaryDiagnosisCode / SecondaryDiagnoses are LEGACY (PT EHR) and NOT read here.
        var diagnosisCodes = new List<string>();
        var encounter = note.Encounter;
        if (!string.IsNullOrWhiteSpace(encounter?.IcdSelections))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(encounter.IcdSelections);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        // Each item is an object like {"code":"E11.65","description":"...","aiSuggested":true}
                        // We only need the code for Box 21.
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            item.TryGetProperty("code", out var codeProp) &&
                            codeProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var code = codeProp.GetString();
                            if (!string.IsNullOrWhiteSpace(code)) diagnosisCodes.Add(code.Trim());
                        }
                        // Tolerate plain string entries too (defensive — in case format ever changes)
                        else if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var code = item.GetString();
                            if (!string.IsNullOrWhiteSpace(code)) diagnosisCodes.Add(code.Trim());
                        }
                    }
                }
            }
            catch
            {
                // Malformed JSON — leave diagnosisCodes empty; biller will fill Box 21 manually.
            }
        }

        var claim = new BillingClaim
        {
            TenantId = tenantId,
            PatientId = note.PatientId,
            InsuranceId = insurance?.InsuranceId,
            ClinicalNoteId = clinicalNoteId,
            AppointmentId = note.AppointmentId,
            CareEpisodeId = careEpisode?.CareEpisodeId,
            AuthorizationId = authorization?.AuthorizationId,
            ProviderId = provider?.ProviderId,
            LocationId = location?.LocationId,

            ClaimNumber = $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
            ServiceDateFrom = serviceDate,
            ServiceDateTo = serviceDate,
            TotalCharged = 0,
            Status = (int)ClaimStatus.Draft,
            Type = (int)ClaimType.Professional,

            InsuranceTypeCode = DetectCmsInsuranceType(insurance),
            PatientSignatureOnFile = true,
            InsuredSignatureOnFile = true,
            ReferringProviderName = careEpisode?.PhysicianName,
            ReferringProviderNpi = careEpisode?.ReferringProviderNpi,
            DiagnosisCodes = diagnosisCodes.Any()
                ? System.Text.Json.JsonSerializer.Serialize(diagnosisCodes) : null,
            PriorAuthorizationNumber = authorization?.AuthorizationNumber,
            PlaceOfServiceCode = location?.PlaceOfServiceCode ?? "11",
            FederalTaxId = tenant?.TaxId,
            PatientAccountNumber = patient?.Mrn,
            AcceptAssignment = true,
            RenderingProviderName = provider != null ? $"{provider.FirstName} {provider.LastName}" : null,
            RenderingProviderNpi = provider?.Npi,
            FacilityName = location?.Name,
            FacilityAddress = location != null ? $"{location.Address}, {location.City}, {location.State} {location.ZipCode}" : null,
            FacilityNpi = location?.FacilityNpi,
            BillingProviderName = tenant?.Name,
            BillingProviderAddress = tenant != null ? $"{tenant.Address}, {tenant.City}, {tenant.State} {tenant.ZipCode}" : null,
            BillingProviderNpi = tenant?.Npi,
            BillingProviderTaxonomy = provider?.Taxonomy,

            InsuredName = !string.IsNullOrEmpty(insurance?.SubscriberName) ? insurance.SubscriberName
                : !string.IsNullOrEmpty(insurance?.SubscriberFirstName) || !string.IsNullOrEmpty(insurance?.SubscriberLastName)
                    ? $"{insurance.SubscriberLastName}, {insurance.SubscriberFirstName}".Trim(',', ' ')
                : (insurance?.SubscriberRelationship == null || insurance?.SubscriberRelationship == "Self")
                    ? $"{patient?.LastName}, {patient?.FirstName}".Trim(',', ' ') : null,
            // IMEHR doesn't have subscriber address/city/state/zip/gender on Insurance — fall back to patient
            InsuredAddress = patient?.Address,
            InsuredCity = patient?.City,
            InsuredState = patient?.State,
            InsuredZip = patient?.ZipCode,
            InsuredDob = insurance?.SubscriberDob ?? patient?.DateOfBirth,
            InsuredGender = patient?.Gender,
            InsuredPolicyNumber = insurance?.PolicyNumber,
            InsuredGroupNumber = insurance?.GroupNumber,
            SubscriberRelationship = insurance?.SubscriberRelationship,

            CreatedAt = DateTime.UtcNow
        };

        _context.BillingClaims.Add(claim);
        await _context.SaveChangesAsync();

        _context.ClaimStatusHistories.Add(new ClaimStatusHistory
        {
            TenantId = tenantId,
            ClaimId = claim.ClaimId,
            Status = (int)ClaimStatus.Draft,
            Notes = "Auto-created from signed clinical note",
            CreatedAt = DateTime.UtcNow
        });

        // Link orphaned charges to this claim
        var orphanedCharges = await _context.Charges
            .Where(c => c.TenantId == tenantId && c.ClinicalNoteId == clinicalNoteId && c.ClaimId == null)
            .ToListAsync();
        if (orphanedCharges.Any())
        {
            foreach (var charge in orphanedCharges)
                charge.ClaimId = claim.ClaimId;
            claim.TotalCharged = orphanedCharges.Sum(c => c.ChargeAmount);
        }

        await _context.SaveChangesAsync();
        return claim;
    }

    private static int? DetectCmsInsuranceType(Insurance? insurance)
    {
        if (insurance == null) return null;
        var name = (insurance.PayerName ?? "").ToLowerInvariant();
        if (name.Contains("medicare")) return 1;
        if (name.Contains("medicaid")) return 2;
        if (name.Contains("tricare") || name.Contains("champus")) return 3;
        if (name.Contains("champva")) return 4;
        if (name.Contains("feca") || name.Contains("black lung")) return 6;
        return 5; // Group Health Plan default
    }

    // ── Get full CMS 1500 / UB04 claim detail ─────────────────
    public async Task<ClaimDetailDto?> GetClaimDetailAsync(int claimId)
    {
        var tenantId = _tenantProvider.TenantId!.Value;

        var claim = await _context.BillingClaims
            .Include(c => c.Charges)
            .Include(c => c.Patient)
            .Include(c => c.Insurance)
            .Include(c => c.Appointment)   // for submit-lock computation
            .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.TenantId == tenantId);

        if (claim == null) return null;

        if (claim.Patient != null)
            _encryptionHelper.DecryptEntity(claim.Patient);
        if (claim.Insurance != null)
            _encryptionHelper.DecryptEntity(claim.Insurance);

        CareEpisode? careEpisode = null;
        if (claim.CareEpisodeId.HasValue &&
            (string.IsNullOrEmpty(claim.ReferringProviderName) || string.IsNullOrEmpty(claim.ReferringProviderNpi)))
        {
            careEpisode = await _context.CareEpisodes.FindAsync(claim.CareEpisodeId.Value);
        }

        // Fallback: fill empty snapshot fields from linked Location and Tenant
        if (claim.LocationId.HasValue && (string.IsNullOrEmpty(claim.FacilityAddress) || string.IsNullOrEmpty(claim.FacilityName)))
        {
            var loc = await _context.Locations.FindAsync(claim.LocationId.Value);
            if (loc != null)
            {
                if (string.IsNullOrEmpty(claim.FacilityName)) claim.FacilityName = loc.Name;
                if (string.IsNullOrEmpty(claim.FacilityAddress)) claim.FacilityAddress = $"{loc.Address}, {loc.City}, {loc.State} {loc.ZipCode}";
                if (string.IsNullOrEmpty(claim.FacilityNpi)) claim.FacilityNpi = loc.FacilityNpi;
                if (string.IsNullOrEmpty(claim.PlaceOfServiceCode)) claim.PlaceOfServiceCode = loc.PlaceOfServiceCode ?? "11";
            }
        }
        if (string.IsNullOrEmpty(claim.BillingProviderAddress) || string.IsNullOrEmpty(claim.BillingProviderName))
        {
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant != null)
            {
                if (string.IsNullOrEmpty(claim.BillingProviderName)) claim.BillingProviderName = tenant.Name;
                if (string.IsNullOrEmpty(claim.BillingProviderAddress)) claim.BillingProviderAddress = $"{tenant.Address}, {tenant.City}, {tenant.State} {tenant.ZipCode}";
                if (string.IsNullOrEmpty(claim.BillingProviderNpi)) claim.BillingProviderNpi = tenant.Npi;
                if (string.IsNullOrEmpty(claim.FederalTaxId)) claim.FederalTaxId = tenant.TaxId;
            }
        }
        // Also fill rendering provider from linked Provider if empty
        if (claim.ProviderId.HasValue && string.IsNullOrEmpty(claim.RenderingProviderName))
        {
            var prov = await _context.Providers.FindAsync(claim.ProviderId.Value);
            if (prov != null)
            {
                _encryptionHelper.DecryptEntity(prov);
                claim.RenderingProviderName = $"{prov.FirstName} {prov.LastName}";
                if (string.IsNullOrEmpty(claim.RenderingProviderNpi)) claim.RenderingProviderNpi = prov.Npi;
                if (string.IsNullOrEmpty(claim.BillingProviderTaxonomy)) claim.BillingProviderTaxonomy = prov.Taxonomy;
            }
        }

        var patient = claim.Patient;
        var (detailIsLocked, detailWindowEnd) = ComputeSubmitLock(claim.Appointment?.CheckOutTime);
        return new ClaimDetailDto
        {
            ClaimId = claim.ClaimId,
            ClaimNumber = claim.ClaimNumber,
            Type = claim.Type,
            Status = claim.Status,
            ServiceDateFrom = claim.ServiceDateFrom,
            ServiceDateTo = claim.ServiceDateTo,
            SubmittedAt = claim.SubmittedAt,
            CreatedAt = claim.CreatedAt,
            IsSubmitLocked = detailIsLocked,
            AmendmentWindowEndsAt = detailWindowEnd,
            PatientId = claim.PatientId,
            InsuranceId = claim.InsuranceId,
            ClinicalNoteId = claim.ClinicalNoteId,
            AppointmentId = claim.AppointmentId,
            CareEpisodeId = claim.CareEpisodeId,
            AuthorizationId = claim.AuthorizationId,
            ProviderId = claim.ProviderId,
            LocationId = claim.LocationId,
            PatientFirstName = patient?.FirstName,
            PatientLastName = patient?.LastName,
            PatientAddress = patient?.Address,
            PatientCity = patient?.City,
            PatientState = patient?.State,
            PatientZip = patient?.ZipCode,
            PatientPhone = patient?.Phone,
            PatientDob = patient?.DateOfBirth,
            PatientGender = patient?.Gender,
            PatientMrn = patient?.Mrn,
            PayerName = claim.Insurance?.PayerName,
            PayerId = claim.Insurance?.PayerId,
            InsuranceTypeCode = claim.InsuranceTypeCode,
            InsuredName = claim.InsuredName,
            InsuredAddress = claim.InsuredAddress,
            InsuredCity = claim.InsuredCity,
            InsuredState = claim.InsuredState,
            InsuredZip = claim.InsuredZip,
            InsuredDob = claim.InsuredDob,
            InsuredGender = claim.InsuredGender,
            InsuredPolicyNumber = claim.InsuredPolicyNumber,
            InsuredGroupNumber = claim.InsuredGroupNumber,
            SubscriberRelationship = claim.SubscriberRelationship,
            PatientSignatureOnFile = claim.PatientSignatureOnFile,
            InsuredSignatureOnFile = claim.InsuredSignatureOnFile,
            ReferringProviderName = !string.IsNullOrEmpty(claim.ReferringProviderName)
                ? claim.ReferringProviderName : careEpisode?.PhysicianName,
            ReferringProviderNpi = !string.IsNullOrEmpty(claim.ReferringProviderNpi)
                ? claim.ReferringProviderNpi : careEpisode?.ReferringProviderNpi,
            DiagnosisCodes = claim.DiagnosisCodes,
            PriorAuthorizationNumber = claim.PriorAuthorizationNumber,
            PlaceOfServiceCode = claim.PlaceOfServiceCode,
            FederalTaxId = claim.FederalTaxId,
            PatientAccountNumber = claim.PatientAccountNumber,
            AcceptAssignment = claim.AcceptAssignment,
            AmountPaid = claim.AmountPaid,
            RenderingProviderName = claim.RenderingProviderName,
            RenderingProviderNpi = claim.RenderingProviderNpi,
            FacilityName = claim.FacilityName,
            FacilityAddress = claim.FacilityAddress,
            FacilityNpi = claim.FacilityNpi,
            BillingProviderName = claim.BillingProviderName,
            BillingProviderAddress = claim.BillingProviderAddress,
            BillingProviderNpi = claim.BillingProviderNpi,
            BillingProviderTaxonomy = claim.BillingProviderTaxonomy,
            TypeOfBill = claim.TypeOfBill,
            AdmissionDate = claim.AdmissionDate,
            AdmissionType = claim.AdmissionType,
            PatientDischargeStatus = claim.PatientDischargeStatus,
            ConditionCodes = claim.ConditionCodes,
            ValueCodes = claim.ValueCodes,
            OccurrenceCodes = claim.OccurrenceCodes,
            TotalCharged = claim.TotalCharged,
            TotalAllowed = claim.TotalAllowed,
            TotalPaid = claim.TotalPaid,
            TotalAdjustment = claim.TotalAdjustment,
            PatientResponsibility = claim.PatientResponsibility,
            Notes = claim.Notes,
            DenialReasonCode = claim.DenialReasonCode,
            DenialReason = claim.DenialReason,
            Edidata = claim.Edidata,
            ResponseData = claim.ResponseData,
            ChargeLines = claim.Charges?.OrderBy(ch => ch.ServiceDate).Select(ch => new ChargeLineDto
            {
                ChargeId = ch.ChargeId,
                ServiceDate = ch.ServiceDate,
                CptCode = ch.Cptcode,
                CptDescription = ch.Cptdescription,
                Modifier1 = ch.Modifier1,
                Modifier2 = ch.Modifier2,
                Modifier3 = ch.Modifier3,
                Modifier4 = ch.Modifier4,
                IcdPointers = ch.Icdpointers,
                ChargeAmount = ch.ChargeAmount,
                Units = ch.Units,
                RevenueCode = ch.RevenueCode,
                Status = ch.Status,
                AllowedAmount = ch.AllowedAmount,
                PaidAmount = ch.PaidAmount,
                AdjustmentAmount = ch.AdjustmentAmount
            }).ToList() ?? new List<ChargeLineDto>()
        };
    }

    // ── Update CMS 1500 / UB04 claim fields ───────────────────
    public async Task<BillingClaim?> UpdateClaimFieldsAsync(int claimId, ClaimUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId!.Value;
        var claim = await _context.BillingClaims
            .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.TenantId == tenantId);
        if (claim == null) return null;

        if (dto.Type.HasValue) claim.Type = dto.Type;
        if (dto.InsuranceTypeCode.HasValue) claim.InsuranceTypeCode = dto.InsuranceTypeCode;
        if (dto.PatientSignatureOnFile.HasValue) claim.PatientSignatureOnFile = dto.PatientSignatureOnFile;
        if (dto.InsuredSignatureOnFile.HasValue) claim.InsuredSignatureOnFile = dto.InsuredSignatureOnFile;
        if (dto.AcceptAssignment.HasValue) claim.AcceptAssignment = dto.AcceptAssignment;
        if (dto.AmountPaid.HasValue) claim.AmountPaid = dto.AmountPaid;
        if (dto.ReferringProviderName != null) claim.ReferringProviderName = dto.ReferringProviderName;
        if (dto.ReferringProviderNpi != null) claim.ReferringProviderNpi = dto.ReferringProviderNpi;
        if (dto.DiagnosisCodes != null) claim.DiagnosisCodes = dto.DiagnosisCodes;
        if (dto.PriorAuthorizationNumber != null) claim.PriorAuthorizationNumber = dto.PriorAuthorizationNumber;
        if (dto.PlaceOfServiceCode != null) claim.PlaceOfServiceCode = dto.PlaceOfServiceCode;
        if (dto.FederalTaxId != null) claim.FederalTaxId = dto.FederalTaxId;
        if (dto.PatientAccountNumber != null) claim.PatientAccountNumber = dto.PatientAccountNumber;
        if (dto.RenderingProviderName != null) claim.RenderingProviderName = dto.RenderingProviderName;
        if (dto.RenderingProviderNpi != null) claim.RenderingProviderNpi = dto.RenderingProviderNpi;
        if (dto.FacilityName != null) claim.FacilityName = dto.FacilityName;
        if (dto.FacilityAddress != null) claim.FacilityAddress = dto.FacilityAddress;
        if (dto.FacilityNpi != null) claim.FacilityNpi = dto.FacilityNpi;
        if (dto.BillingProviderName != null) claim.BillingProviderName = dto.BillingProviderName;
        if (dto.BillingProviderAddress != null) claim.BillingProviderAddress = dto.BillingProviderAddress;
        if (dto.BillingProviderNpi != null) claim.BillingProviderNpi = dto.BillingProviderNpi;
        if (dto.BillingProviderTaxonomy != null) claim.BillingProviderTaxonomy = dto.BillingProviderTaxonomy;
        if (dto.InsuredName != null) claim.InsuredName = dto.InsuredName;
        if (dto.InsuredAddress != null) claim.InsuredAddress = dto.InsuredAddress;
        if (dto.InsuredCity != null) claim.InsuredCity = dto.InsuredCity;
        if (dto.InsuredState != null) claim.InsuredState = dto.InsuredState;
        if (dto.InsuredZip != null) claim.InsuredZip = dto.InsuredZip;
        if (dto.InsuredDob.HasValue) claim.InsuredDob = dto.InsuredDob;
        if (dto.InsuredGender != null) claim.InsuredGender = dto.InsuredGender;
        if (dto.InsuredPolicyNumber != null) claim.InsuredPolicyNumber = dto.InsuredPolicyNumber;
        if (dto.InsuredGroupNumber != null) claim.InsuredGroupNumber = dto.InsuredGroupNumber;
        if (dto.SubscriberRelationship != null) claim.SubscriberRelationship = dto.SubscriberRelationship;
        if (dto.Notes != null) claim.Notes = dto.Notes;
        if (dto.TypeOfBill != null) claim.TypeOfBill = dto.TypeOfBill;
        if (dto.AdmissionDate.HasValue) claim.AdmissionDate = dto.AdmissionDate;
        if (dto.AdmissionType.HasValue) claim.AdmissionType = dto.AdmissionType;
        if (dto.PatientDischargeStatus != null) claim.PatientDischargeStatus = dto.PatientDischargeStatus;
        if (dto.ConditionCodes != null) claim.ConditionCodes = dto.ConditionCodes;
        if (dto.ValueCodes != null) claim.ValueCodes = dto.ValueCodes;
        if (dto.OccurrenceCodes != null) claim.OccurrenceCodes = dto.OccurrenceCodes;

        claim.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return claim;
    }

    // ── Add charge line to claim ───────────────────────────────
    public async Task<Charge> AddChargeToClaimAsync(ClaimChargeCreateDto dto)
    {
        var tenantId = _tenantProvider.TenantId!.Value;
        var claim = await _context.BillingClaims
            .FirstOrDefaultAsync(c => c.ClaimId == dto.ClaimId && c.TenantId == tenantId);
        if (claim == null)
            throw new InvalidOperationException("Claim not found");

        var chargeAmount = dto.ChargeAmount;

        var charge = new Charge
        {
            TenantId = tenantId,
            PatientId = claim.PatientId,
            ProviderId = claim.ProviderId ?? 0,
            ClaimId = claim.ClaimId,
            ClinicalNoteId = claim.ClinicalNoteId,
            AppointmentId = claim.AppointmentId,
            ServiceDate = dto.ServiceDate,
            Cptcode = dto.CptCode,
            Cptdescription = dto.CptDescription,
            Units = dto.Units ?? 1,
            ChargeAmount = chargeAmount,
            Modifier1 = dto.Modifier1,
            Modifier2 = dto.Modifier2,
            Modifier3 = dto.Modifier3,
            Modifier4 = dto.Modifier4,
            Icdpointers = dto.IcdPointers,
            RevenueCode = dto.RevenueCode,
            Status = (int)ChargeStatus.Billed,
            CreatedAt = DateTime.UtcNow
        };

        _context.Charges.Add(charge);
        await _context.SaveChangesAsync();

        claim.TotalCharged = await _context.Charges
            .Where(c => c.ClaimId == claim.ClaimId)
            .SumAsync(c => c.ChargeAmount * (c.Units ?? 1));
        claim.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return charge;
    }

    // ── Update charge line on claim ────────────────────────────
    public async Task<bool> UpdateChargeOnClaimAsync(int chargeId, ClaimChargeCreateDto dto)
    {
        var tenantId = _tenantProvider.TenantId!.Value;
        var charge = await _context.Charges
            .FirstOrDefaultAsync(c => c.ChargeId == chargeId && c.TenantId == tenantId);
        if (charge == null) return false;

        charge.ServiceDate = dto.ServiceDate;
        charge.Cptcode = dto.CptCode;
        charge.Cptdescription = dto.CptDescription;
        charge.Units = dto.Units ?? 1;
        charge.ChargeAmount = dto.ChargeAmount;
        charge.Modifier1 = dto.Modifier1;
        charge.Modifier2 = dto.Modifier2;
        charge.Modifier3 = dto.Modifier3;
        charge.Modifier4 = dto.Modifier4;
        charge.Icdpointers = dto.IcdPointers;
        charge.RevenueCode = dto.RevenueCode;
        charge.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (charge.ClaimId.HasValue)
        {
            var claim = await _context.BillingClaims.FindAsync(charge.ClaimId.Value);
            if (claim != null)
            {
                claim.TotalCharged = await _context.Charges
                    .Where(c => c.ClaimId == claim.ClaimId)
                    .SumAsync(c => c.ChargeAmount * (c.Units ?? 1));
                claim.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        return true;
    }

    // ── Remove charge line from claim ─────────────────────────
    public async Task<bool> RemoveChargeFromClaimAsync(int chargeId)
    {
        var tenantId = _tenantProvider.TenantId!.Value;
        var charge = await _context.Charges
            .FirstOrDefaultAsync(c => c.ChargeId == chargeId && c.TenantId == tenantId);
        if (charge == null) return false;

        var claimId = charge.ClaimId;
        _context.Charges.Remove(charge);
        await _context.SaveChangesAsync();

        if (claimId.HasValue)
        {
            var claim = await _context.BillingClaims.FindAsync(claimId.Value);
            if (claim != null)
            {
                claim.TotalCharged = await _context.Charges
                    .Where(c => c.ClaimId == claim.ClaimId)
                    .SumAsync(c => c.ChargeAmount * (c.Units ?? 1));
                claim.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        return true;
    }

    // ── Mark claim as Ready ────────────────────────────────────
    public async Task<BillingClaim?> MarkClaimReadyAsync(int claimId)
    {
        var tenantId = _tenantProvider.TenantId!.Value;
        var claim = await _context.BillingClaims
            .Include(c => c.Charges)
            .FirstOrDefaultAsync(c => c.ClaimId == claimId && c.TenantId == tenantId);
        if (claim == null) return null;

        var errors = new List<string>();
        if (claim.Charges == null || !claim.Charges.Any())
            errors.Add("At least one charge line is required");
        if (string.IsNullOrEmpty(claim.DiagnosisCodes))
            errors.Add("Diagnosis codes are required (Box 21)");
        if (!claim.InsuranceId.HasValue || claim.InsuranceId == 0)
            errors.Add("Insurance information is required");
        if (string.IsNullOrEmpty(claim.BillingProviderNpi))
            errors.Add("Billing provider NPI is required (Box 33a)");

        if (errors.Any())
            throw new InvalidOperationException("Claim validation failed: " + string.Join("; ", errors));

        claim.Status = (int)ClaimStatus.Ready;
        claim.UpdatedAt = DateTime.UtcNow;

        _context.ClaimStatusHistories.Add(new ClaimStatusHistory
        {
            TenantId = tenantId,
            ClaimId = claimId,
            Status = (int)ClaimStatus.Ready,
            Notes = "Claim marked ready for submission",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return claim;
    }

    // ── Generate CMS 1500 PDF ──────────────────────────────────
    public async Task<byte[]> GenerateClaimPdfAsync(int claimId)
    {
        var detail = await GetClaimDetailAsync(claimId);
        if (detail == null)
            throw new InvalidOperationException("Claim not found");
        var html = BuildCMS1500Html(detail);
        return await _htmlToPdfService.GeneratePdfFromHtmlAsync(html);
    }

    private static string _e(string? val) => System.Net.WebUtility.HtmlEncode(val ?? "");

    private static string BuildCMS1500Html(ClaimDetailDto claim)
    {
        var diagCodes = new List<string>();
        if (!string.IsNullOrEmpty(claim.DiagnosisCodes))
        {
            try { diagCodes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(claim.DiagnosisCodes) ?? new(); }
            catch { }
        }

        var chargeRows = new System.Text.StringBuilder();
        foreach (var line in claim.ChargeLines)
        {
            chargeRows.Append($@"<tr>
                <td>{line.ServiceDate:MM/dd/yyyy}</td><td>{line.ServiceDate:MM/dd/yyyy}</td>
                <td>{claim.PlaceOfServiceCode}</td><td>{_e(line.CptCode)}</td>
                <td>{_e(line.Modifier1)} {_e(line.Modifier2)} {_e(line.Modifier3)} {_e(line.Modifier4)}</td>
                <td>{_e(line.IcdPointers)}</td><td style='text-align:right'>${line.ChargeAmount:F2}</td>
                <td style='text-align:center'>{line.Units ?? 1}</td>
                <td>{_e(claim.RenderingProviderNpi)}</td></tr>");
        }

        var diagLetters = "ABCDEFGHIJKL";
        var diagHtml = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Min(diagCodes.Count, 12); i++)
            diagHtml.Append($"<span style='display:inline-block;margin-right:12px;font-weight:bold'>{diagLetters[i]}. {_e(diagCodes[i])}</span> ");

        return $@"<!DOCTYPE html><html><head><meta charset='utf-8'/>
<style>@page{{size:letter;margin:0.4in}}body{{font-family:'Courier New',monospace;font-size:9pt;margin:0;color:#000}}.form-title{{text-align:center;font-size:14pt;font-weight:bold;color:#c00;margin:8px 0;border:2px solid #c00;padding:4px}}.section{{border:1px solid #999;margin-bottom:4px;padding:4px 6px}}.section-title{{font-weight:bold;font-size:8pt;color:#c00;margin-bottom:2px;text-transform:uppercase}}.row{{display:flex;gap:10px;margin-bottom:2px}}.field{{flex:1}}.field-label{{font-size:7pt;color:#666;text-transform:uppercase}}.field-value{{font-size:9pt;font-weight:bold;min-height:12px;border-bottom:1px dotted #ccc}}table{{width:100%;border-collapse:collapse;font-size:8pt}}th,td{{border:1px solid #999;padding:2px 4px;text-align:left}}th{{background:#f0f0f0;font-size:7pt;text-transform:uppercase}}.totals{{text-align:right;font-weight:bold;font-size:10pt;margin-top:4px}}.box-num{{font-size:7pt;color:#c00}}</style></head><body>
<div class='form-title'>HEALTH INSURANCE CLAIM FORM (CMS-1500)</div>
<div class='section'><div class='row'>
<div class='field' style='flex:2'><div class='field-label'><span class='box-num'>1.</span> Insurance Type</div><div class='field-value'>{GetInsuranceTypeName(claim.InsuranceTypeCode)}</div></div>
<div class='field' style='flex:3'><div class='field-label'><span class='box-num'>1a.</span> Insured's ID</div><div class='field-value'>{_e(claim.InsuredPolicyNumber)}</div></div></div></div>
<div class='section'><div class='section-title'>Patient Information</div>
<div class='row'><div class='field'><div class='field-label'><span class='box-num'>2.</span> Patient Name</div><div class='field-value'>{_e(claim.PatientLastName)}, {_e(claim.PatientFirstName)}</div></div>
<div class='field'><div class='field-label'><span class='box-num'>3.</span> DOB / Sex</div><div class='field-value'>{claim.PatientDob:MM/dd/yyyy} {_e(claim.PatientGender)}</div></div></div>
<div class='row'><div class='field'><div class='field-label'><span class='box-num'>5.</span> Address</div><div class='field-value'>{_e(claim.PatientAddress)}, {_e(claim.PatientCity)}, {_e(claim.PatientState)} {_e(claim.PatientZip)}</div></div></div></div>
<div class='section'><div class='section-title'>Insured Information</div>
<div class='row'><div class='field'><div class='field-label'><span class='box-num'>4.</span> Insured Name</div><div class='field-value'>{_e(claim.InsuredName)}</div></div>
<div class='field'><div class='field-label'><span class='box-num'>6.</span> Relationship</div><div class='field-value'>{_e(claim.SubscriberRelationship)}</div></div></div>
<div class='row'><div class='field'><div class='field-label'><span class='box-num'>11.</span> Group Number</div><div class='field-value'>{_e(claim.InsuredGroupNumber)}</div></div></div></div>
<div class='section'><div class='row'>
<div class='field'><div class='field-label'><span class='box-num'>12.</span> Patient Signature</div><div class='field-value'>{(claim.PatientSignatureOnFile == true ? "SIGNATURE ON FILE" : "")}</div></div>
<div class='field'><div class='field-label'><span class='box-num'>13.</span> Insured Signature</div><div class='field-value'>{(claim.InsuredSignatureOnFile == true ? "SIGNATURE ON FILE" : "")}</div></div></div></div>
<div class='section'><div class='row'>
<div class='field'><div class='field-label'><span class='box-num'>17.</span> Referring Provider</div><div class='field-value'>{_e(claim.ReferringProviderName)}</div></div>
<div class='field'><div class='field-label'><span class='box-num'>17a.</span> NPI</div><div class='field-value'>{_e(claim.ReferringProviderNpi)}</div></div></div></div>
<div class='section'><div class='field-label'><span class='box-num'>21.</span> Diagnosis Codes (ICD-10)</div><div class='field-value'>{diagHtml}</div></div>
<div class='section'><div class='field-label'><span class='box-num'>23.</span> Prior Authorization</div><div class='field-value'>{_e(claim.PriorAuthorizationNumber)}</div></div>
<div class='section'><div class='section-title'><span class='box-num'>24.</span> Service Lines</div>
<table><thead><tr><th>Date From</th><th>Date To</th><th>POS</th><th>CPT/HCPCS</th><th>Modifier</th><th>Dx Ptr</th><th>Charges</th><th>Units</th><th>Rendering NPI</th></tr></thead>
<tbody>{chargeRows}</tbody></table>
<div class='totals'><span class='box-num'>28.</span> Total Charge: ${claim.TotalCharged:F2} &nbsp;&nbsp; <span class='box-num'>29.</span> Amount Paid: ${claim.AmountPaid ?? 0:F2}</div></div>
<div class='section'><div class='row'>
<div class='field'><div class='field-label'><span class='box-num'>25.</span> Federal Tax ID</div><div class='field-value'>{_e(claim.FederalTaxId)}</div></div>
<div class='field'><div class='field-label'><span class='box-num'>26.</span> Patient Account</div><div class='field-value'>{_e(claim.PatientAccountNumber)}</div></div>
<div class='field'><div class='field-label'><span class='box-num'>27.</span> Accept Assignment</div><div class='field-value'>{(claim.AcceptAssignment == true ? "YES" : "NO")}</div></div></div></div>
<div class='section'><div class='row'>
<div class='field'><div class='field-label'><span class='box-num'>31.</span> Rendering Provider</div><div class='field-value'>{_e(claim.RenderingProviderName)} NPI: {_e(claim.RenderingProviderNpi)}</div></div></div></div>
<div class='section'><div class='row'>
<div class='field'><div class='field-label'><span class='box-num'>32.</span> Facility</div><div class='field-value'>{_e(claim.FacilityName)} {_e(claim.FacilityAddress)} NPI: {_e(claim.FacilityNpi)}</div></div></div></div>
<div class='section'><div class='row'>
<div class='field'><div class='field-label'><span class='box-num'>33.</span> Billing Provider</div><div class='field-value'>{_e(claim.BillingProviderName)} {_e(claim.BillingProviderAddress)} NPI: {_e(claim.BillingProviderNpi)} Tax: {_e(claim.BillingProviderTaxonomy)}</div></div></div></div>
</body></html>";
    }

    private static string GetInsuranceTypeName(int? code) => code switch
    {
        1 => "Medicare",
        2 => "Medicaid",
        3 => "Tricare",
        4 => "CHAMPVA",
        5 => "Group Health Plan",
        6 => "FECA",
        _ => "Other"
    };

    /// <summary>
    /// Use Gemini AI to suggest CPT codes based on clinical note content and diagnosis codes
    /// </summary>
    public async Task<CptSuggestionResponse> SuggestCptCodesAsync(CptSuggestionRequest request)
    {
        try
        {
            // Load available CPT codes from the database for context
            var availableCpts = await _context.Cptcodes
                .Where(c => c.IsActive == true)
                .Select(c => new { c.Code, c.Description })
                .ToListAsync();

            var cptListText = availableCpts.Any()
                ? string.Join("\n", availableCpts.Select(c => $"  {c.Code} - {c.Description}"))
                : "";

            // Get note content from clinical notes for the claim's appointment
            var noteContent = request.NoteContent ?? "";
            if (string.IsNullOrEmpty(noteContent) && request.ClaimId > 0)
            {
                var claim = await _context.BillingClaims
                    .FirstOrDefaultAsync(bc => bc.ClaimId == request.ClaimId && bc.TenantId == _tenantProvider.TenantId);

                if (claim != null)
                {
                    var notes = claim.AppointmentId.HasValue
                        ? await _context.ClinicalNotes
                            .Where(cn => cn.AppointmentId == claim.AppointmentId && cn.TenantId == _tenantProvider.TenantId)
                            .OrderBy(cn => cn.Type)
                            .ToListAsync()
                        : claim.ClinicalNoteId.HasValue
                            ? await _context.ClinicalNotes
                                .Where(cn => cn.ClinicalNoteId == claim.ClinicalNoteId)
                                .ToListAsync()
                            : new List<ClinicalNote>();

                    var contentParts = new List<string>();
                    foreach (var note in notes)
                    {
                        try
                        {
                            var decrypted = _encryptionHelper.Decrypt(note.HtmlContent);
                            if (!string.IsNullOrEmpty(decrypted))
                                contentParts.Add($"--- Note (Type {note.Type}, Date {note.ServiceDate}) ---\n{decrypted}");
                        }
                        catch { /* skip unreadable notes */ }
                    }
                    noteContent = string.Join("\n\n", contentParts);

                    // PHI scrub — claim has PatientId directly; load patient + provider
                    // (provider sourced from the appointment when available) and scrub
                    // the aggregated note before it reaches Gemini.
                    if (!string.IsNullOrWhiteSpace(noteContent))
                    {
                        var patient = await _context.Patients
                            .FirstOrDefaultAsync(p => p.PatientId == claim.PatientId);
                        Provider? provider = null;
                        if (claim.AppointmentId.HasValue)
                        {
                            var appt = await _context.Appointments
                                .Include(a => a.Provider)
                                .FirstOrDefaultAsync(a => a.AppointmentId == claim.AppointmentId);
                            provider = appt?.Provider;
                        }
                        var phi = EHR.Helpers.PhiContext.Build(patient, provider, _encryptionHelper);
                        noteContent = EHR.Helpers.ClinicalNotePHIScrubber.Scrub(noteContent, phi);
                    }
                }
            }

            // Strip HTML tags for cleaner AI input
            noteContent = System.Text.RegularExpressions.Regex.Replace(noteContent, "<[^>]+>", " ");
            noteContent = System.Text.RegularExpressions.Regex.Replace(noteContent, @"\s+", " ").Trim();
            if (noteContent.Length > 4000) noteContent = noteContent[..4000];

            if (string.IsNullOrWhiteSpace(noteContent))
            {
                return new CptSuggestionResponse { Success = false, ErrorMessage = "No clinical note content found for this claim" };
            }

            var cptContext = !string.IsNullOrEmpty(cptListText)
                ? $"AVAILABLE CPT CODES (only suggest from this list):\n{cptListText}\n\n"
                : "Suggest standard E/M and procedure CPT codes appropriate for Internal Medicine / Primary Care.\n\n";

            // Appointment-type context (optional). When the caller passes the
            // type int, surface a readable name + type-specific guidance to
            // Gemini. This matters most for Telehealth (modifier 95, POS 02/10),
            // preventive visits, procedure visits, and Longevity visits.
            var appointmentTypeBlock = "";
            var appointmentTypeInstruction = "";
            if (request.AppointmentType.HasValue)
            {
                var typeName = GetAppointmentTypeNameForPrompt(request.AppointmentType.Value);
                appointmentTypeBlock = $"APPOINTMENT TYPE: {typeName}\n\n";
                appointmentTypeInstruction =
                    "- Use the APPOINTMENT TYPE above as strong context for code selection. " +
                    "If the type is \"Telehealth\", prefer telehealth-eligible E/M codes (99202-99215) and note that the provider must add modifier 95 with Place of Service 02 or 10; for phone-only encounters use 99441-99443. " +
                    "If the type is \"Annual Physical\" or \"Wellness Exam\", prefer preventive E/M codes (99381-99397 or 99391-99397 for established patients). " +
                    "If the type is \"Procedure Visit\", prefer the relevant procedure CPT over E/M. " +
                    "If the type is \"New Longevity Patient\" or \"Follow-Up Longevity Patient\", consider extended/time-based E/M (e.g., 99205 / 99215 with prolonged-service add-ons 99417 when documented).\n";
            }

            var prompt = $@"You are a medical billing coding assistant for an Internal Medicine / Primary Care clinic.
Given the appointment type, clinical note content, and diagnosis codes below, suggest the most appropriate CPT codes.

{appointmentTypeBlock}{cptContext}DIAGNOSIS CODES (ICD-10): {request.DiagnosisCodes ?? "None provided"}

CLINICAL NOTE CONTENT:
{noteContent}

INSTRUCTIONS:
- Suggest 1-6 CPT codes that best match the services documented in the clinical note.
- For each code, provide: the CPT code, description, suggested units, and a brief rationale.
- Consider the diagnosis codes when selecting appropriate codes.
- For E/M codes (99202-99215), suggest based on complexity documented.
- For procedures, estimate units from the note content.
{appointmentTypeInstruction}- Return your answer as valid JSON array (no markdown, no code fences):
[{{""cptCode"": ""99213"", ""description"": ""Office visit, established patient, low complexity"", ""units"": 1, ""rationale"": ""Follow-up visit with straightforward decision making""}}]";

            var result = await _geminiService.GenerateTextAsync(prompt, 0.2);

            if (!result.Success || string.IsNullOrEmpty(result.Text))
            {
                return new CptSuggestionResponse
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage ?? "AI service returned no suggestions"
                };
            }

            // Parse the JSON response
            var text = result.Text.Trim();
            if (text.StartsWith("```")) text = text.Substring(text.IndexOf('\n') + 1);
            if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")].Trim();

            var suggestions = System.Text.Json.JsonSerializer.Deserialize<List<CptSuggestionDto>>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (suggestions == null || !suggestions.Any())
            {
                return new CptSuggestionResponse { Success = false, ErrorMessage = "AI could not generate suggestions" };
            }

            return new CptSuggestionResponse { Success = true, Suggestions = suggestions };
        }
        catch (Exception ex)
        {
            return new CptSuggestionResponse { Success = false, ErrorMessage = $"CPT suggestion failed: {ex.Message}" };
        }
    }

    // ============================================================================
    // ICD-10 AI SUGGESTIONS
    // Mirrors SuggestCptCodesAsync — analyzes a clinical note and suggests
    // ICD-10 diagnosis codes the provider should consider for the encounter.
    // Returns up to 6 suggestions with confidence (0-100) and rationale.
    // ============================================================================
    public async Task<IcdSuggestionResponse> SuggestIcdCodesAsync(IcdSuggestionRequest request)
    {
        try
        {
            var noteContent = request.NoteContent ?? "";

            // Strip HTML tags for cleaner AI input
            noteContent = System.Text.RegularExpressions.Regex.Replace(noteContent, "<[^>]+>", " ");
            noteContent = System.Text.RegularExpressions.Regex.Replace(noteContent, @"\s+", " ").Trim();
            if (noteContent.Length > 4000) noteContent = noteContent[..4000];

            if (string.IsNullOrWhiteSpace(noteContent))
            {
                return new IcdSuggestionResponse { Success = false, ErrorMessage = "No clinical note content found" };
            }

            // Appointment-type context (optional). When the caller passes the
            // type int, surface a readable name + type-specific guidance so
            // Gemini can bias diagnosis selection appropriately (preventive
            // Z-codes for wellness, lifestyle/age codes for longevity, etc.).
            var icdAppointmentTypeBlock = "";
            var icdAppointmentTypeInstruction = "";
            if (request.AppointmentType.HasValue)
            {
                var typeName = GetAppointmentTypeNameForPrompt(request.AppointmentType.Value);
                icdAppointmentTypeBlock = $"APPOINTMENT TYPE: {typeName}\n\n";
                icdAppointmentTypeInstruction =
                    "- Use the APPOINTMENT TYPE above as additional context. " +
                    "For \"Annual Physical\" or \"Wellness Exam\", include preventive Z-codes (e.g., Z00.00, Z00.01) when no acute findings dominate. " +
                    "For \"Telehealth\", do NOT change code selection based on the modality itself — pick codes that match the documented diagnoses. " +
                    "For \"Lab Review\", prioritize the diagnoses being reviewed (Z01.812 if results are unremarkable, or the underlying chronic condition codes). " +
                    "For \"Medication Review\", consider Z51.81 (encounter for therapeutic drug monitoring) alongside the underlying condition. " +
                    "For \"New Longevity Patient\" or \"Follow-Up Longevity Patient\", consider lifestyle/age-related codes (e.g., Z71.3 dietary counseling, Z72.0-Z72.9 lifestyle issues, Z13.* screening) alongside any documented conditions.\n";
            }

            var prompt = $@"You are a medical coding assistant for an Internal Medicine / Primary Care clinic.
Given the appointment type and clinical note content below, suggest the most appropriate ICD-10 diagnosis codes.

{icdAppointmentTypeBlock}CLINICAL NOTE CONTENT:
{noteContent}

INSTRUCTIONS:
- Suggest 1-6 ICD-10 codes that best match the diagnoses documented or strongly implied in the clinical note.
- Order them by clinical priority (most relevant first — that becomes the primary diagnosis).
- For each suggestion provide:
    code         : the ICD-10 code (e.g. ""E11.65"")
    description  : the official short description
    confidence   : an integer 0-100 (90+ = clearly documented, 60-89 = strongly implied, <60 = possible/comorbidity)
    rationale    : one short sentence pointing to the supporting evidence in the note
- Use ICD-10-CM format (alphanumeric, with decimal where applicable, max ~7 chars).
- Do NOT include CPT/procedure codes.
{icdAppointmentTypeInstruction}- Return your answer as a valid JSON array (no markdown, no code fences):
[{{""code"": ""E11.65"", ""description"": ""Type 2 diabetes mellitus with hyperglycemia"", ""confidence"": 95, ""rationale"": ""HbA1c 9.2% with documented Type 2 DM""}}]";

            var result = await _geminiService.GenerateTextAsync(prompt, 0.2);

            if (!result.Success || string.IsNullOrEmpty(result.Text))
            {
                return new IcdSuggestionResponse
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage ?? "AI service returned no suggestions"
                };
            }

            // Strip markdown fences if Gemini included them
            var text = result.Text.Trim();
            if (text.StartsWith("```")) text = text.Substring(text.IndexOf('\n') + 1);
            if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")].Trim();

            var suggestions = System.Text.Json.JsonSerializer.Deserialize<List<IcdSuggestionDto>>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (suggestions == null || !suggestions.Any())
            {
                return new IcdSuggestionResponse { Success = false, ErrorMessage = "AI could not generate suggestions" };
            }

            // Defensive: clamp confidence into 0..100
            foreach (var s in suggestions)
            {
                if (s.Confidence < 0) s.Confidence = 0;
                if (s.Confidence > 100) s.Confidence = 100;
            }

            return new IcdSuggestionResponse { Success = true, Suggestions = suggestions };
        }
        catch (Exception ex)
        {
            return new IcdSuggestionResponse { Success = false, ErrorMessage = $"ICD-10 suggestion failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Readable AppointmentType name for the Gemini prompt only. Mirrors the
    /// switches in AppointmentService / KioskService / ConsentService / Telehealth /
    /// KioskController — duplicated locally to avoid cross-service coupling for
    /// one short string. Future cleanup: extract to a shared
    /// <c>AppointmentTypeExtensions.DisplayName()</c> on the enum.
    /// </summary>
    private static string GetAppointmentTypeNameForPrompt(int type) => type switch
    {
        0 => "New Patient Visit",
        1 => "Follow-Up Visit",
        2 => "Annual Physical",
        3 => "Wellness Exam",
        4 => "Consultation",
        5 => "Telehealth",
        6 => "Procedure Visit",
        7 => "Urgent Visit",
        8 => "Lab Review",
        9 => "Medication Review",
        10 => "New Longevity Patient",
        11 => "Follow-Up Longevity Patient",
        _ => "Other"
    };
}

// Payment Service
public interface IPaymentService
{
    Task<List<PaymentListDto>> GetPaymentsAsync(int? patientId = null, DateTime? startDate = null, DateTime? endDate = null);
    Task<Payment> CreatePaymentAsync(PaymentCreateDto dto);
    Task<PatientBalanceDto> GetPatientBalanceAsync(int patientId);
    Task<PortalBalanceDto> GetPortalBalanceAsync(int patientId, int tenantId);
    Task<List<PatientTransactionDto>> GetPatientTransactionsAsync(int patientId, int tenantId);
    Task<List<PatientLedgerEntryDto>> GetPatientLedgerAsync(int patientId, int tenantId);
    Task<Payment> CompleteStripePaymentAsync(
        string stripePaymentIntentId, int patientId, int tenantId, decimal amount,
        int? locationId = null, int? stripeConnectAccountId = null,
        int? clinicTotalFeeCents = null, int? stripeProcessingFeeCents = null,
        int? applicationFeeCents = null, int? netToClinicCents = null,
        string stripeChargeId = null, int? appointmentId = null);
    Task<object> GetCopayBalancesAsync(int tenantId, string search, int page, int pageSize);
}

public class PaymentService : IPaymentService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EHR.Helpers.EncryptionHelper _encryptionHelper;

    public PaymentService(EhrDbContext context, ITenantProvider tenantProvider, EHR.Helpers.EncryptionHelper encryptionHelper)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
    }

    public async Task<List<PaymentListDto>> GetPaymentsAsync(int? patientId = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        // AsNoTracking: read-only list; Patient decrypted in-place for DTOs,
        // must not be tracked.
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Patient)
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        if (patientId.HasValue) query = query.Where(p => p.PatientId == patientId);
        if (startDate.HasValue)
        {
            var startDateOnly = DateOnly.FromDateTime(startDate.Value);
            query = query.Where(p => p.PaymentDate >= startDateOnly);
        }
        if (endDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(endDate.Value);
            query = query.Where(p => p.PaymentDate <= endDateOnly);
        }

        // Load entities first
        var payments = await query.OrderByDescending(p => p.PaymentDate).ToListAsync();

        // Decrypt PHI fields in memory
        foreach (var payment in payments)
        {
            if (payment.Patient != null)
                _encryptionHelper.DecryptEntity(payment.Patient);
        }

        // Map to DTOs after decryption
        return payments.Select(p => new PaymentListDto
        {
            PaymentId = p.PaymentId,
            PatientId = p.PatientId,
            PatientName = p.Patient != null ? $"{p.Patient.FirstName} {p.Patient.LastName}" : "Unknown",
            Type = p.Type,
            Method = p.Method,
            Amount = p.Amount,
            TransactionId = p.TransactionId,
            PayerName = p.PayerName,
            Status = p.Status ?? 0,
            PaymentDate = p.PaymentDate,
            IsRefund = p.IsRefund == true
        }).ToList();
    }
    
    public async Task<Payment> CreatePaymentAsync(PaymentCreateDto dto)
    {
        var payment = new Payment
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = dto.PatientId,
            AppointmentId = dto.AppointmentId,
            ClaimId = dto.ClaimId,
            Type = dto.Type,
            Method = dto.Method,
            Amount = dto.Amount,
            TransactionId = dto.TransactionId,
            CheckNumber = dto.CheckNumber,
            CheckDate = dto.CheckDate,
            CardReference = dto.CardReference,
            ReferenceNumber = dto.ReferenceNumber,
            PayerName = dto.PayerName,
            PaymentDate = dto.PaymentDate,
            Notes = dto.Notes,
            Status = (int)PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }
    
    public async Task<PatientBalanceDto> GetPatientBalanceAsync(int patientId)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null)
            throw new InvalidOperationException("Patient not found");

        // CRITICAL: Tenant data isolation - verify patient belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            throw new InvalidOperationException("Patient not found");

        // Decrypt patient PHI fields
        _encryptionHelper.DecryptEntity(patient);

        var chargesQuery = _context.Charges.Where(c => c.PatientId == patientId);
        var paymentsQuery = _context.Payments.Where(p => p.PatientId == patientId);

        // Apply tenant filter to related queries
        if (_tenantProvider.TenantId.HasValue)
        {
            chargesQuery = chargesQuery.Where(c => c.TenantId == _tenantProvider.TenantId.Value);
            paymentsQuery = paymentsQuery.Where(p => p.TenantId == _tenantProvider.TenantId.Value);
        }

        var charges = await chargesQuery.SumAsync(c => c.ChargeAmount);

        var insurancePayments = await paymentsQuery
            .Where(p => p.Type == (int)PaymentType.InsurancePayment && p.Status == (int)PaymentStatus.Completed)
            .SumAsync(p => p.Amount);

        var patientPayments = await paymentsQuery
            .Where(p => p.Type != (int)PaymentType.InsurancePayment && p.Status == (int)PaymentStatus.Completed && p.IsRefund != true)
            .SumAsync(p => p.Amount);

        var adjustments = await chargesQuery.SumAsync(c => c.AdjustmentAmount ?? 0);

        return new PatientBalanceDto
        {
            PatientId = patientId,
            PatientName = $"{patient.FirstName} {patient.LastName}",
            TotalCharges = charges,
            InsurancePayments = insurancePayments,
            PatientPayments = patientPayments,
            Adjustments = adjustments,
            CurrentBalance = charges - insurancePayments - patientPayments - adjustments,
            TotalPayments = insurancePayments + patientPayments,
            TotalAdjustments = adjustments,
            Balance = charges - insurancePayments - patientPayments - adjustments
        };
    }

    public async Task<PortalBalanceDto> GetPortalBalanceAsync(int patientId, int tenantId)
    {
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId && p.TenantId == tenantId);
        if (patient == null) throw new InvalidOperationException("Patient not found");
        _encryptionHelper.DecryptEntity(patient);

        // Patient-facing balance — simple: Total Owed - Total Paid = Balance

        // 1. Copay due from all checked-in appointments
        var totalCopayDue = await _context.Appointments
            .Where(a => a.PatientId == patientId && a.TenantId == tenantId
                && a.CopayDue.HasValue && a.CopayDue > 0
                && a.Status >= (int)AppointmentStatus.CheckedIn)
            .SumAsync(a => a.CopayDue ?? 0);

        // 2. Biller-posted patient responsibility (from claims after ERA)
        var postedResponsibility = await _context.BillingClaims
            .Where(c => c.PatientId == patientId && c.TenantId == tenantId && c.PatientResponsibility.HasValue && c.PatientResponsibility > 0)
            .SumAsync(c => c.PatientResponsibility ?? 0);

        // 3. Self-pay charges (no claim = no insurance, full amount is patient's)
        var selfPayCharges = await _context.Charges
            .Where(c => c.PatientId == patientId && c.TenantId == tenantId && c.ClaimId == null)
            .SumAsync(c => c.ChargeAmount);

        // Total owed = copay + posted responsibility + self-pay
        var totalResponsibility = totalCopayDue + postedResponsibility + selfPayCharges;

        // 4. What patient has already paid
        var patientPaid = await _context.Payments
            .Where(p => p.PatientId == patientId && p.TenantId == tenantId
                && p.Type != (int)PaymentType.InsurancePayment
                && p.Type != (int)PaymentType.Adjustment
                && p.Status == (int)PaymentStatus.Completed && p.IsRefund != true)
            .SumAsync(p => p.Amount);

        var currentBalance = totalResponsibility - patientPaid;
        if (currentBalance < 0) currentBalance = 0; // Patient can't owe negative

        var hasActivePlan = await _context.InstallmentPlans
            .AnyAsync(p => p.PatientId == patientId && p.TenantId == tenantId && p.Status == (int)InstallmentPlanStatus.Active);

        return new PortalBalanceDto
        {
            PatientId = patientId,
            PatientName = $"{patient.FirstName} {patient.LastName}",
            TotalCharges = totalResponsibility,
            InsurancePaid = 0, // Patient doesn't see insurance details
            Adjustments = 0,
            PatientPaid = patientPaid,
            CurrentBalance = currentBalance,
            HasActivePlan = hasActivePlan
        };
    }

    public async Task<List<PatientTransactionDto>> GetPatientTransactionsAsync(int patientId, int tenantId)
    {
        // Patient portal: show only patient payments (not insurance), simple format
        var payments = await _context.Payments
            .Where(p => p.PatientId == patientId && p.TenantId == tenantId
                && p.Type != (int)PaymentType.InsurancePayment
                && p.Status == (int)PaymentStatus.Completed)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        return payments.Select(p => new PatientTransactionDto
        {
            Date = p.PaymentDate.ToDateTime(TimeOnly.MinValue),
            Description = GetTransactionDescription(p),
            Amount = p.Amount,
            Method = ((PaymentMethod)p.Method).ToString()
        }).ToList();
    }

    public async Task<List<PatientLedgerEntryDto>> GetPatientLedgerAsync(int patientId, int tenantId)
    {
        // Staff view: all charges + all payments, with calculated running balance
        var entries = new List<PatientLedgerEntryDto>();

        // Get charges
        var charges = await _context.Charges
            .Where(c => c.PatientId == patientId && c.TenantId == tenantId)
            .OrderBy(c => c.ServiceDate)
            .ToListAsync();

        foreach (var c in charges)
        {
            entries.Add(new PatientLedgerEntryDto
            {
                Date = c.ServiceDate.ToDateTime(TimeOnly.MinValue),
                EntryType = 1, // Charge
                EntryTypeName = "Charge",
                CptCode = c.Cptcode,
                Description = c.Cptdescription ?? $"Charge - {c.Cptcode}",
                ChargeAmount = c.ChargeAmount,
                RunningBalance = 0 // calculated below
            });

            // Add adjustment as separate entry if exists
            if ((c.AdjustmentAmount ?? 0) > 0)
            {
                entries.Add(new PatientLedgerEntryDto
                {
                    Date = c.ServiceDate.ToDateTime(TimeOnly.MinValue),
                    EntryType = 3, // Adjustment
                    EntryTypeName = "Adjustment",
                    CptCode = c.Cptcode,
                    Description = $"Contractual Adjustment - {c.Cptcode}",
                    AdjustmentAmount = c.AdjustmentAmount,
                    RunningBalance = 0
                });
            }
        }

        // Get payments
        var payments = await _context.Payments
            .Where(p => p.PatientId == patientId && p.TenantId == tenantId && p.Status == (int)PaymentStatus.Completed)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync();

        foreach (var p in payments)
        {
            entries.Add(new PatientLedgerEntryDto
            {
                Date = p.PaymentDate.ToDateTime(TimeOnly.MinValue),
                EntryType = p.Type == (int)PaymentType.InsurancePayment ? 4 : 2,
                EntryTypeName = p.Type == (int)PaymentType.InsurancePayment ? "Insurance Payment" : "Patient Payment",
                Description = GetLedgerPaymentDescription(p),
                PaymentAmount = p.Amount,
                PayerName = p.PayerName,
                PaymentMethod = ((PaymentMethod)p.Method).ToString(),
                RunningBalance = 0
            });
        }

        // Sort by date and calculate running balance
        entries = entries.OrderBy(e => e.Date).ThenBy(e => e.EntryType).ToList();
        decimal runningBalance = 0;
        foreach (var entry in entries)
        {
            runningBalance += (entry.ChargeAmount ?? 0) - (entry.PaymentAmount ?? 0) - (entry.AdjustmentAmount ?? 0);
            entry.RunningBalance = runningBalance;
        }

        return entries;
    }

    public async Task<Payment> CompleteStripePaymentAsync(
        string stripePaymentIntentId, int patientId, int tenantId, decimal amount,
        int? locationId = null, int? stripeConnectAccountId = null,
        int? clinicTotalFeeCents = null, int? stripeProcessingFeeCents = null,
        int? applicationFeeCents = null, int? netToClinicCents = null,
        string stripeChargeId = null, int? appointmentId = null)
    {
        // Idempotency check — prevent duplicate payments if webhook fires twice
        var existing = await _context.Payments
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == stripePaymentIntentId);
        if (existing != null) return existing;

        // Create completed payment from Stripe webhook
        var payment = new Payment
        {
            TenantId = tenantId,
            PatientId = patientId,
            LocationId = locationId,
            AppointmentId = appointmentId,
            StripeConnectAccountId = stripeConnectAccountId,
            PaymentMethodType = (int)StripePaymentMethodType.Online,
            Type = (int)PaymentType.SelfPay,
            Method = (int)PaymentMethod.CreditCard,
            Amount = amount,
            StripePaymentIntentId = stripePaymentIntentId,
            StripeChargeId = stripeChargeId,
            PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = (int)PaymentStatus.Completed,
            Notes = "Online payment via patient portal",
            CreatedAt = DateTime.UtcNow,
            ClinicTotalFeeCents = clinicTotalFeeCents,
            StripeProcessingFeeCents = stripeProcessingFeeCents,
            ApplicationFeeCents = applicationFeeCents,
            NetToClinicCents = netToClinicCents
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        // Create ledger entry
        var ledgerEntry = new PatientLedger
        {
            TenantId = tenantId,
            PatientId = patientId,
            EntryType = 2, // Payment
            PaymentId = payment.PaymentId,
            TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount = -amount,
            Description = "Patient Payment (Online)",
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientLedgers.Add(ledgerEntry);
        await _context.SaveChangesAsync();

        return payment;
    }

    private static string GetTransactionDescription(Payment p)
    {
        var type = ((PaymentType)p.Type).ToString();
        var method = ((PaymentMethod)p.Method).ToString();
        return $"Payment - {method}";
    }

    private static string GetLedgerPaymentDescription(Payment p)
    {
        var method = ((PaymentMethod)p.Method).ToString();
        if (p.Type == (int)PaymentType.InsurancePayment)
            return $"{p.PayerName ?? "Insurance"} - Payment";
        var type = ((PaymentType)p.Type).ToString();
        return $"Patient Payment ({method}){(p.InstallmentDetailId.HasValue ? " - Installment" : "")}";
    }

    public async Task<object> GetCopayBalancesAsync(int tenantId, string search, int page, int pageSize)
    {
        // Get all patients for tenant
        var patientsQuery = _context.Patients
            .Where(p => p.TenantId == tenantId && p.IsDeleted != true);

        var allPatients = await patientsQuery.ToListAsync();

        // Decrypt names for search and display
        var decryptedPatients = allPatients.Select(p => {
            var detached = p;
            _context.Entry(detached).State = EntityState.Detached;
            _encryptionHelper.DecryptEntity(detached);
            return detached;
        }).ToList();

        // Filter by search term
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLower();
            decryptedPatients = decryptedPatients.Where(p =>
                (p.FirstName ?? "").ToLower().Contains(q) ||
                (p.LastName ?? "").ToLower().Contains(q) ||
                (p.Mrn ?? "").ToLower().Contains(q)
            ).ToList();
        }

        // Calculate copay balance for each patient
        var balanceResults = new List<object>();
        foreach (var patient in decryptedPatients)
        {
            try
            {
                var balance = await GetPortalBalanceAsync(patient.PatientId, tenantId);
                if (balance.CurrentBalance > 0)
                {
                    balanceResults.Add(new
                    {
                        PatientId = patient.PatientId,
                        PatientName = $"{patient.FirstName} {patient.LastName}".Trim(),
                        FirstName = patient.FirstName,
                        LastName = patient.LastName,
                        Mrn = patient.Mrn,
                        Email = patient.Email,
                        Phone = patient.Phone,
                        ProfilePicturePath = patient.ProfilePicturePath,
                        Balance = balance.CurrentBalance
                    });
                }
            }
            catch { /* skip patients with errors */ }
        }

        // Sort by balance descending
        balanceResults = balanceResults.OrderByDescending(b => ((dynamic)b).Balance).ToList();

        var totalCount = balanceResults.Count;
        var totalBalance = balanceResults.Sum(b => (decimal)((dynamic)b).Balance);
        var items = balanceResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new
        {
            Items = items,
            TotalCount = totalCount,
            TotalBalance = totalBalance,
            Page = page,
            PageSize = pageSize
        };
    }
}

// Care Episode Service
public interface ICareEpisodeService
{
    Task<List<CareEpisodeListDto>> GetCareEpisodesAsync(int? patientId = null, CareEpisodeStatus? status = null);
    Task<CareEpisode?> GetCareEpisodeByIdAsync(int careEpisodeId);
    Task<CareEpisode> CreateCareEpisodeAsync(CareEpisodeCreateDto dto);
    Task<CareEpisode?> UpdateCareEpisodeAsync(int careEpisodeId, CareEpisodeUpdateDto dto);
}

public class CareEpisodeService : ICareEpisodeService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IInsuranceAuthorizationService _authorizationService;

    public CareEpisodeService(EhrDbContext context, ITenantProvider tenantProvider, IInsuranceAuthorizationService authorizationService)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _authorizationService = authorizationService;
    }

    public async Task<List<CareEpisodeListDto>> GetCareEpisodesAsync(int? patientId = null, CareEpisodeStatus? status = null)
    {
        var query = _context.CareEpisodes
            .Include(ce => ce.Patient)
            .Include(ce => ce.PrimaryProvider)
            .Include(ce => ce.Appointments)
            .AsQueryable();

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(ce => ce.TenantId == _tenantProvider.TenantId.Value);
        }

        if (patientId.HasValue) query = query.Where(ce => ce.PatientId == patientId);
        if (status.HasValue) query = query.Where(ce => ce.Status == (int)status.Value);

        var careEpisodes = await query.OrderByDescending(ce => ce.StartDate).ToListAsync();

        // Build DTOs - CareEpisode is purely clinical, no Insurance relationship
        var result = new List<CareEpisodeListDto>();
        foreach (var ce in careEpisodes)
        {
            // Calculate visits dynamically from appointments
            var visitsUsed = CalculateVisitsUsedForEpisode(ce.Appointments);
            var missedVisits = CalculateMissedVisitsForEpisode(ce.Appointments);
            var visitsRemaining = (ce.ExpectedVisits ?? 0) - visitsUsed;

            result.Add(new CareEpisodeListDto
            {
                CareEpisodeId = ce.CareEpisodeId,
                PatientId = ce.PatientId,
                PatientName = ce.Patient?.FirstName + " " + ce.Patient?.LastName,
                PrimaryProviderName = ce.PrimaryProvider != null ? ce.PrimaryProvider.FirstName + " " + ce.PrimaryProvider.LastName : null,
                StartDate = ce.StartDate,
                EndDate = ce.EndDate,
                PrimaryDiagnosis = ce.PrimaryDiagnosisCode + " - " + ce.PrimaryDiagnosisDescription,
                ExpectedVisits = ce.ExpectedVisits,
                VisitsUsed = visitsUsed,
                VisitsRemaining = visitsRemaining,
                MissedVisits = missedVisits,
                Status = ce.Status ?? 0
            });
        }

        return result;
    }

    /// <summary>
    /// Calculate completed visits for a care episode from its appointments.
    /// </summary>
    private static int CalculateVisitsUsedForEpisode(ICollection<Appointment> appointments)
    {
        if (appointments == null || appointments.Count == 0) return 0;
        return appointments.Count(a =>
            a.Status == (int)AppointmentStatus.CheckedIn ||
            a.Status == (int)AppointmentStatus.InProgress ||
            a.Status == (int)AppointmentStatus.Completed);
    }

    /// <summary>
    /// Calculate missed visits for a care episode from its appointments.
    /// </summary>
    private static int CalculateMissedVisitsForEpisode(ICollection<Appointment> appointments)
    {
        if (appointments == null || appointments.Count == 0) return 0;
        return appointments.Count(a => a.Status == (int)AppointmentStatus.NoShow);
    }
    
    public async Task<CareEpisode?> GetCareEpisodeByIdAsync(int careEpisodeId)
    {
        var query = _context.CareEpisodes
            .Include(ce => ce.Patient)
            .Include(ce => ce.PrimaryProvider)
            .Include(ce => ce.Appointments)
            .Where(ce => ce.CareEpisodeId == careEpisodeId);

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(ce => ce.TenantId == _tenantProvider.TenantId.Value);
        }

        return await query.FirstOrDefaultAsync();
    }
    
    public async Task<CareEpisode> CreateCareEpisodeAsync(CareEpisodeCreateDto dto)
    {
        // CareEpisode is purely clinical - no Insurance relationship
        var careEpisode = new CareEpisode
        {
            TenantId = _tenantProvider.TenantId!.Value,
            PatientId = dto.PatientId,
            PrimaryProviderId = dto.PrimaryProviderId,
            StartDate = dto.StartDate,
            PrimaryDiagnosisCode = dto.PrimaryDiagnosisCode,
            PrimaryDiagnosisDescription = dto.PrimaryDiagnosisDescription,
            SecondaryDiagnoses = dto.SecondaryDiagnoses != null ? JsonSerializer.Serialize(dto.SecondaryDiagnoses) : null,
            Goals = dto.Goals != null ? JsonSerializer.Serialize(dto.Goals) : null,
            PlanOfCare = dto.PlanOfCare,
            ExpectedVisits = dto.ExpectedVisits,
            VisitFrequency = dto.VisitFrequency,
            CopayAmount = dto.CopayAmount,
            CopayVisits = dto.CopayVisits,
            Status = (int)CareEpisodeStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.CareEpisodes.Add(careEpisode);
        await _context.SaveChangesAsync();
        return careEpisode;
    }
    
    public async Task<CareEpisode?> UpdateCareEpisodeAsync(int careEpisodeId, CareEpisodeUpdateDto dto)
    {
        var ce = await _context.CareEpisodes.FindAsync(careEpisodeId);
        if (ce == null) return null;

        // CRITICAL: Tenant data isolation - verify care episode belongs to user's tenant
        if (_tenantProvider.TenantId.HasValue && ce.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // CareEpisode is purely clinical - no Insurance relationship
        if (dto.PrimaryProviderId.HasValue) ce.PrimaryProviderId = dto.PrimaryProviderId;
        if (dto.EndDate.HasValue) ce.EndDate = dto.EndDate;
        if (dto.PrimaryDiagnosisCode != null) ce.PrimaryDiagnosisCode = dto.PrimaryDiagnosisCode;
        if (dto.PrimaryDiagnosisDescription != null) ce.PrimaryDiagnosisDescription = dto.PrimaryDiagnosisDescription;
        if (dto.SecondaryDiagnoses != null) ce.SecondaryDiagnoses = JsonSerializer.Serialize(dto.SecondaryDiagnoses);
        if (dto.Goals != null) ce.Goals = JsonSerializer.Serialize(dto.Goals);
        if (dto.PlanOfCare != null) ce.PlanOfCare = dto.PlanOfCare;
        if (dto.ExpectedVisits.HasValue) ce.ExpectedVisits = dto.ExpectedVisits;
        if (dto.VisitFrequency.HasValue) ce.VisitFrequency = dto.VisitFrequency;
        if (dto.CopayAmount.HasValue) ce.CopayAmount = dto.CopayAmount;
        if (dto.CopayVisits.HasValue) ce.CopayVisits = dto.CopayVisits;
        if (dto.Status.HasValue) ce.Status = dto.Status.Value;
        if (dto.DischargeReason != null) ce.DischargeReason = dto.DischargeReason;

        ce.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return ce;
    }
}

// ============================================
// PROVIDER FAVORITE CODES SERVICE (Dx & CPT step)
// Per-user favorite ICD-10 / CPT codes.
// STRICT per-user isolation — every query filters by current user's UserId.
// No clinic-wide visibility, no cross-provider read.
// ============================================

public interface IFavoritesService
{
    Task<List<ProviderFavoriteCodeDto>> GetFavoritesAsync(int userId, string codeType);
    Task<ProviderFavoriteCodeDto> AddFavoriteAsync(int userId, AddFavoriteRequest request);
    Task<bool> RemoveFavoriteAsync(int userId, string codeType, string code);
}

public class FavoritesService : IFavoritesService
{
    private readonly EhrDbContext _context;

    public FavoritesService(EhrDbContext context)
    {
        _context = context;
    }

    private static readonly HashSet<string> ValidCodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ICD10", "CPT"
    };

    private static string NormalizeCodeType(string codeType)
    {
        if (string.IsNullOrWhiteSpace(codeType))
            throw new ArgumentException("CodeType is required.");
        var upper = codeType.Trim().ToUpperInvariant();
        if (!ValidCodeTypes.Contains(upper))
            throw new ArgumentException($"Invalid CodeType '{codeType}'. Must be 'ICD10' or 'CPT'.");
        return upper;
    }

    public async Task<List<ProviderFavoriteCodeDto>> GetFavoritesAsync(int userId, string codeType)
    {
        var type = NormalizeCodeType(codeType);
        return await _context.ProviderFavoriteCodes
            .Where(f => f.UserId == userId && f.CodeType == type)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new ProviderFavoriteCodeDto
            {
                Id          = f.Id,
                CodeType    = f.CodeType,
                Code        = f.Code,
                Description = f.Description
            })
            .ToListAsync();
    }

    public async Task<ProviderFavoriteCodeDto> AddFavoriteAsync(int userId, AddFavoriteRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var type = NormalizeCodeType(request.CodeType);

        var code = (request.Code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException("Code is required.");

        var description = (request.Description ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(description))
            throw new ArgumentException("Description is required.");
        if (description.Length > 500) description = description.Substring(0, 500);

        // Idempotent: if already a favorite, return existing
        var existing = await _context.ProviderFavoriteCodes
            .FirstOrDefaultAsync(f => f.UserId == userId && f.CodeType == type && f.Code == code);
        if (existing != null)
        {
            return new ProviderFavoriteCodeDto
            {
                Id          = existing.Id,
                CodeType    = existing.CodeType,
                Code        = existing.Code,
                Description = existing.Description
            };
        }

        var entity = new ProviderFavoriteCode
        {
            UserId      = userId,
            CodeType    = type,
            Code        = code,
            Description = description,
            CreatedAt   = DateTime.UtcNow
        };
        _context.ProviderFavoriteCodes.Add(entity);
        await _context.SaveChangesAsync();

        return new ProviderFavoriteCodeDto
        {
            Id          = entity.Id,
            CodeType    = entity.CodeType,
            Code        = entity.Code,
            Description = entity.Description
        };
    }

    public async Task<bool> RemoveFavoriteAsync(int userId, string codeType, string code)
    {
        var type = NormalizeCodeType(codeType);
        var trimmed = (code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        var entity = await _context.ProviderFavoriteCodes
            .FirstOrDefaultAsync(f => f.UserId == userId && f.CodeType == type && f.Code == trimmed);
        if (entity == null) return false;

        _context.ProviderFavoriteCodes.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }
}
