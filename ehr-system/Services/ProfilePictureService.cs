using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using EHR.Data;
using EHR.Models;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;

namespace EHR.Services;

/// <summary>
/// Service for managing profile pictures for patients and providers.
/// Handles upload, retrieval, and deletion via Google Cloud Storage.
/// </summary>
public interface IProfilePictureService
{
    Task<ProfilePictureResponseDto?> UploadPatientProfilePictureAsync(int patientId, IFormFile file);
    Task<(Stream stream, string contentType)?> GetPatientProfilePictureAsync(int patientId);
    Task<bool> DeletePatientProfilePictureAsync(int patientId);
}

public class ProfilePictureService : IProfilePictureService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IFileStorageService _storageService;
    private readonly FilePathBuilder _pathBuilder;
    private readonly MetadataBuilder _metadataBuilder;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp"
    };
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

    public ProfilePictureService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        IFileStorageService storageService,
        FilePathBuilder pathBuilder,
        MetadataBuilder metadataBuilder)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _storageService = storageService;
        _pathBuilder = pathBuilder;
        _metadataBuilder = metadataBuilder;
    }

    public async Task<ProfilePictureResponseDto?> UploadPatientProfilePictureAsync(int patientId, IFormFile file)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null) return null;

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return null;

        // Validate file extension
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            return new ProfilePictureResponseDto
            {
                EntityId = patientId,
                EntityType = "Patient",
                HasProfilePicture = false,
                Message = $"File type '{extension}' is not allowed. Allowed types: .jpg, .jpeg, .png, .webp"
            };
        }

        // Validate content type
        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return new ProfilePictureResponseDto
            {
                EntityId = patientId,
                EntityType = "Patient",
                HasProfilePicture = false,
                Message = $"Content type '{file.ContentType}' is not allowed."
            };
        }

        // Validate file size (max 5MB)
        if (file.Length > MaxFileSize)
        {
            return new ProfilePictureResponseDto
            {
                EntityId = patientId,
                EntityType = "Patient",
                HasProfilePicture = false,
                Message = "File size exceeds maximum allowed size of 5MB"
            };
        }

        // Delete existing profile picture from cloud storage if it exists
        if (!string.IsNullOrEmpty(patient.ProfilePicturePath))
        {
            await _storageService.DeleteFileAsync(patient.ProfilePicturePath);
        }

        // Generate unique filename and build cloud path
        var newFileName = _pathBuilder.GenerateStorageFileName(file.FileName);
        var folderPath = _pathBuilder.BuildPatientProfilePicturePath(patient.TenantId, patientId);
        var metadata = _metadataBuilder.BuildPatientProfilePictureMetadata(patient.TenantId, patientId);

        // Upload to cloud storage
        using var stream = file.OpenReadStream();
        var uploadResult = await _storageService.UploadFileAsync(
            stream, newFileName, folderPath, file.ContentType, metadata);

        if (!uploadResult.Success)
        {
            return new ProfilePictureResponseDto
            {
                EntityId = patientId,
                EntityType = "Patient",
                HasProfilePicture = false,
                Message = "Failed to upload profile picture to cloud storage"
            };
        }

        // Update patient record with cloud path
        patient.ProfilePicturePath = uploadResult.CloudPath;
        patient.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new ProfilePictureResponseDto
        {
            EntityId = patientId,
            EntityType = "Patient",
            HasProfilePicture = true,
            Message = "Profile picture uploaded successfully"
        };
    }

    public async Task<(Stream stream, string contentType)?> GetPatientProfilePictureAsync(int patientId)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null || string.IsNullOrEmpty(patient.ProfilePicturePath))
            return null;

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return null;

        var stream = await _storageService.DownloadFileAsync(patient.ProfilePicturePath);
        if (stream == null) return null;

        var ext = Path.GetExtension(patient.ProfilePicturePath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return (stream, contentType);
    }

    public async Task<bool> DeletePatientProfilePictureAsync(int patientId)
    {
        var patient = await _context.Patients.FindAsync(patientId);
        if (patient == null) return false;

        // CRITICAL: Tenant data isolation
        if (_tenantProvider.TenantId.HasValue && patient.TenantId != _tenantProvider.TenantId.Value)
            return false;

        if (!string.IsNullOrEmpty(patient.ProfilePicturePath))
        {
            await _storageService.DeleteFileAsync(patient.ProfilePicturePath);
        }

        patient.ProfilePicturePath = null;
        patient.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }
}
