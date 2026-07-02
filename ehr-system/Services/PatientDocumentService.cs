using System.Security.Cryptography;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services
{
    /// <summary>
    /// HIPAA-compliant patient document/attachment service.
    /// Uses Google Cloud Storage for file storage.
    /// Features:
    /// - AES-256 encryption at rest for all files
    /// - Secure filename storage (no PHI in storage paths)
    /// - File type validation and size limits
    /// - Audit trail integration
    /// - Integrity verification via SHA-256 hashing
    /// </summary>
    public interface IPatientDocumentService
    {
        Task<PatientDocumentUploadResultDto> UploadDocumentAsync(int patientId, Stream fileStream, string fileName, string contentType, int category, string description, int userId);
        Task<List<PatientDocumentListDto>> GetPatientDocumentsAsync(int patientId);
        Task<(Stream stream, string fileName, string contentType)?> DownloadDocumentAsync(int documentId);
        Task<bool> DeleteDocumentAsync(int documentId, int userId);
    }

    public class PatientDocumentService : IPatientDocumentService
    {
        private readonly EhrDbContext _context;
        private readonly EncryptionHelper _encryption;
        private readonly IAuditService _auditService;
        private readonly ILogger<PatientDocumentService> _logger;
        private readonly IFileStorageService _storageService;
        private readonly FilePathBuilder _pathBuilder;
        private readonly MetadataBuilder _metadataBuilder;
        private readonly long _maxFileSizeBytes;
        private readonly HashSet<string> _allowedContentTypes;
        private readonly HashSet<string> _allowedExtensions;
        private readonly byte[] _fileEncryptionKey;

        public PatientDocumentService(
            EhrDbContext context,
            EncryptionHelper encryption,
            IAuditService auditService,
            IConfiguration configuration,
            ILogger<PatientDocumentService> logger,
            IFileStorageService storageService,
            FilePathBuilder pathBuilder,
            MetadataBuilder metadataBuilder)
        {
            _context = context;
            _encryption = encryption;
            _auditService = auditService;
            _logger = logger;
            _storageService = storageService;
            _pathBuilder = pathBuilder;
            _metadataBuilder = metadataBuilder;

            // Max file size: 30MB default
            _maxFileSizeBytes = configuration.GetValue<long>("DocumentStorage:MaxFileSizeBytes", 30 * 1024 * 1024);

            // Allowed MIME types - only safe document/image types
            _allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Images
                "image/jpeg",
                "image/jpg",
                "image/png",
                "image/gif",
                "image/webp",
                "image/bmp",
                "image/tiff",

                // Documents
                "application/pdf",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "text/plain"
            };

            // Allowed extensions
            _allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt"
            };

            // Derive file encryption key from main encryption key
            var keyString = configuration["Encryption:Key"] ??
                throw new InvalidOperationException("Encryption:Key not found");
            using var deriveBytes = new Rfc2898DeriveBytes(
                keyString + "_FILE",
                System.Text.Encoding.UTF8.GetBytes("PTEHR_FILE_SALT_2024"),
                100000,
                HashAlgorithmName.SHA256);
            _fileEncryptionKey = deriveBytes.GetBytes(32);
        }

        public async Task<PatientDocumentUploadResultDto> UploadDocumentAsync(
            int patientId,
            Stream fileStream,
            string fileName,
            string contentType,
            int category,
            string description,
            int userId)
        {
            try
            {
                // Validate patient exists and get tenant
                var patient = await _context.Patients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PatientId == patientId && p.IsDeleted != true);

                if (patient == null)
                {
                    return new PatientDocumentUploadResultDto
                    {
                        Success = false,
                        Message = "Patient not found"
                    };
                }

                // Validate file extension
                var extension = Path.GetExtension(fileName);
                if (!_allowedExtensions.Contains(extension))
                {
                    return new PatientDocumentUploadResultDto
                    {
                        Success = false,
                        Message = $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", _allowedExtensions)}"
                    };
                }

                // Validate content type
                if (!_allowedContentTypes.Contains(contentType))
                {
                    return new PatientDocumentUploadResultDto
                    {
                        Success = false,
                        Message = $"Content type '{contentType}' is not allowed"
                    };
                }

                // Read file into memory to check size and compute hash
                using var memoryStream = new MemoryStream();
                await fileStream.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                // Validate file size
                if (fileBytes.Length > _maxFileSizeBytes)
                {
                    return new PatientDocumentUploadResultDto
                    {
                        Success = false,
                        Message = $"File size exceeds maximum allowed size of {_maxFileSizeBytes / (1024 * 1024)}MB"
                    };
                }

                // Validate file content (magic bytes) to prevent disguised files
                if (!IsValidFileContent(fileBytes, extension))
                {
                    return new PatientDocumentUploadResultDto
                    {
                        Success = false,
                        Message = "File content does not match its extension. Possible security risk detected."
                    };
                }

                // Generate secure storage filename (no PHI)
                var storageFileName = _pathBuilder.GenerateEncryptedFileName(fileName);

                // Build cloud storage path
                var folderPath = _pathBuilder.BuildPatientDocumentPath(patient.TenantId, patientId);

                // Compute file hash before encryption
                var fileHash = ComputeSHA256Hash(fileBytes);

                // Encrypt file
                var encryptedBytes = EncryptFile(fileBytes);

                // Build metadata for cloud storage
                var metadata = _metadataBuilder.BuildPatientDocumentMetadata(
                    patient.TenantId,
                    patientId,
                    GetCategoryName(category),
                    userId);

                // Upload to cloud storage
                var uploadResult = await _storageService.UploadBytesAsync(
                    encryptedBytes,
                    storageFileName,
                    folderPath,
                    "application/octet-stream", // Encrypted content
                    metadata);

                if (!uploadResult.Success)
                {
                    _logger.LogError("Failed to upload document to cloud storage: {Error}", uploadResult.ErrorMessage);
                    return new PatientDocumentUploadResultDto
                    {
                        Success = false,
                        Message = "Failed to upload document to storage"
                    };
                }

                // Create database record
                var document = new PatientDocument
                {
                    TenantId = patient.TenantId,
                    PatientId = patientId,
                    FileName = _encryption.Encrypt(fileName), // Encrypt original filename
                    StorageFileName = uploadResult.CloudPath, // Store full cloud path
                    ContentType = contentType,
                    FileSize = fileBytes.Length,
                    Category = category,
                    Description = string.IsNullOrEmpty(description) ? null : _encryption.Encrypt(description),
                    IsEncrypted = true,
                    FileHash = fileHash,
                    UploadedByUserId = userId > 0 ? userId : null,
                    CreatedAt = DateTime.UtcNow
                };

                _context.PatientDocuments.Add(document);
                await _context.SaveChangesAsync();

                // Audit log
                await _auditService.LogAccessAsync(
                    userId,
                    null,
                    "Create",
                    "PatientDocument",
                    document.DocumentId
                );

                _logger.LogInformation("Document uploaded to cloud storage: {DocumentId} for Patient: {PatientId}, Path: {CloudPath}",
                    document.DocumentId, patientId, uploadResult.CloudPath);

                return new PatientDocumentUploadResultDto
                {
                    Success = true,
                    Message = "Document uploaded successfully",
                    DocumentId = document.DocumentId,
                    FileName = fileName,
                    FileSize = fileBytes.Length
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document for patient {PatientId}", patientId);
                return new PatientDocumentUploadResultDto
                {
                    Success = false,
                    Message = "An error occurred while uploading the document"
                };
            }
        }

        public async Task<List<PatientDocumentListDto>> GetPatientDocumentsAsync(int patientId)
        {
            try
            {
                var documents = await _context.PatientDocuments
                    .Include(d => d.UploadedByUser)
                    .Where(d => d.PatientId == patientId && d.IsDeleted != true)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();

                return documents.Select(d => new PatientDocumentListDto
                {
                    DocumentId = d.DocumentId,
                    PatientId = d.PatientId,
                    FileName = SafeDecrypt(d.FileName) ?? d.FileName,
                    ContentType = d.ContentType ?? "application/octet-stream",
                    FileSize = d.FileSize,
                    Category = d.Category,
                    Description = string.IsNullOrEmpty(d.Description) ? null : SafeDecrypt(d.Description),
                    CreatedAt = d.CreatedAt,
                    UploadedByName = d.IsPatientUploaded ? "Patient" : GetUploadedByName(d.UploadedByUser),
                    IsPatientUploaded = d.IsPatientUploaded
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving documents for patient {PatientId}", patientId);
                throw;
            }
        }

        private string? SafeDecrypt(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            try
            {
                return _encryption.Decrypt(value) ?? value;
            }
            catch
            {
                return value;
            }
        }

        private string? GetUploadedByName(User? user)
        {
            if (user == null)
                return null;
            try
            {
                var firstName = SafeDecrypt(user.FirstName) ?? "";
                var lastName = SafeDecrypt(user.LastName) ?? "";
                var fullName = $"{firstName} {lastName}".Trim();
                return string.IsNullOrEmpty(fullName) ? null : fullName;
            }
            catch
            {
                return null;
            }
        }

        public async Task<(Stream stream, string fileName, string contentType)?> DownloadDocumentAsync(int documentId)
        {
            var document = await _context.PatientDocuments
                .FirstOrDefaultAsync(d => d.DocumentId == documentId && d.IsDeleted != true);

            if (document == null)
                return null;

            // Download from cloud storage
            var encryptedBytes = await _storageService.DownloadBytesAsync(document.StorageFileName);

            if (encryptedBytes == null)
            {
                _logger.LogError("Document file not found in cloud storage: {StoragePath}", document.StorageFileName);
                return null;
            }

            // Decrypt file
            var decryptedBytes = DecryptFile(encryptedBytes);

            // Verify integrity
            var currentHash = ComputeSHA256Hash(decryptedBytes);
            if (currentHash != document.FileHash)
            {
                _logger.LogError("Document integrity check failed: {DocumentId}", documentId);
                return null;
            }

            var memoryStream = new MemoryStream(decryptedBytes);
            var decryptedFileName = _encryption.Decrypt(document.FileName) ?? "download";

            return (memoryStream, decryptedFileName, document.ContentType);
        }

        public async Task<bool> DeleteDocumentAsync(int documentId, int userId)
        {
            var document = await _context.PatientDocuments
                .FirstOrDefaultAsync(d => d.DocumentId == documentId);

            if (document == null)
                return false;

            // Soft delete in database (keep file in cloud storage for audit purposes)
            document.IsDeleted = true;
            document.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Audit log
            await _auditService.LogAccessAsync(
                userId,
                null,
                "Delete",
                "PatientDocument",
                documentId
            );

            _logger.LogInformation("Document deleted: {DocumentId}", documentId);
            return true;
        }

        private byte[] EncryptFile(byte[] plainBytes)
        {
            using var aes = Aes.Create();
            aes.Key = _fileEncryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();

            // Write IV first
            msEncrypt.Write(aes.IV, 0, aes.IV.Length);

            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(plainBytes, 0, plainBytes.Length);
            }

            return msEncrypt.ToArray();
        }

        private byte[] DecryptFile(byte[] encryptedBytes)
        {
            using var aes = Aes.Create();
            aes.Key = _fileEncryptionKey;

            // Extract IV from beginning
            var iv = new byte[16];
            Buffer.BlockCopy(encryptedBytes, 0, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream();

            using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Write))
            {
                csDecrypt.Write(encryptedBytes, 16, encryptedBytes.Length - 16);
            }

            return msDecrypt.ToArray();
        }

        private static string ComputeSHA256Hash(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hashBytes);
        }

        private bool IsValidFileContent(byte[] bytes, string extension)
        {
            if (bytes.Length < 4) return false;

            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
                ".png" => bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
                ".gif" => bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46,
                ".pdf" => bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46,
                ".doc" => bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0,
                ".docx" or ".xlsx" => bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04,
                ".xls" => bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0,
                ".txt" => true,
                ".bmp" => bytes[0] == 0x42 && bytes[1] == 0x4D,
                ".webp" => bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46,
                ".tiff" or ".tif" => (bytes[0] == 0x49 && bytes[1] == 0x49) || (bytes[0] == 0x4D && bytes[1] == 0x4D),
                _ => true
            };
        }

        private static string GetCategoryName(int category)
        {
            return category switch
            {
                0 => "insurance-card",
                1 => "referral",
                2 => "medical-record",
                3 => "consent-form",
                4 => "identification",
                5 => "other",
                _ => "document"
            };
        }
    }
}
