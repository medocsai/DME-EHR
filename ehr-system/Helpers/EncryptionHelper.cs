using System.Security.Cryptography;
using System.Text;
using EHR.Configuration;
using Microsoft.Extensions.Configuration;

namespace EHR.Helpers
{
    /// <summary>
    /// HIPAA-compliant AES-256 encryption helper.
    /// Database-First safe - uses EncryptionConfiguration instead of attributes.
    /// </summary>
    public class EncryptionHelper
    {
        private readonly byte[] _key;
        private readonly byte[] _searchKey;

        public EncryptionHelper(IConfiguration configuration)
        {
            var keyString = configuration["Encryption:Key"] 
                ?? throw new InvalidOperationException("Encryption:Key not found in configuration");
            
            // Derive a 256-bit key using PBKDF2
            using var deriveBytes = new Rfc2898DeriveBytes(
                keyString,
                Encoding.UTF8.GetBytes("PTEHR_HIPAA_SALT_2024"),
                100000,
                HashAlgorithmName.SHA256);
            
            _key = deriveBytes.GetBytes(32); // 256 bits for AES-256
            _searchKey = deriveBytes.GetBytes(32); // Separate key for search hashing
        }

        /// <summary>
        /// Encrypt a string using AES-256-GCM (Authenticated Encryption)
        /// </summary>
        public string? Encrypt(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
            
            var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
            RandomNumberGenerator.Fill(nonce);
            
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes
            
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
            
            // Combine: nonce (12) + tag (16) + ciphertext
            var result = new byte[nonce.Length + tag.Length + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, nonce.Length + tag.Length, cipherBytes.Length);
            
            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// Decrypt a string encrypted with AES-256-GCM
        /// </summary>
        public string? Decrypt(string? encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            try
            {
                var fullCipher = Convert.FromBase64String(encryptedText);

                // Minimum size: 12 (nonce) + 16 (tag) + 1 (at least 1 byte ciphertext) = 29 bytes
                // If smaller, it's not valid encrypted data - return original
                var minSize = AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize + 1;
                if (fullCipher.Length < minSize)
                {
                    // Not valid encrypted data - return original (might be plaintext)
                    return encryptedText;
                }

                var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                var tag = new byte[AesGcm.TagByteSizes.MaxSize];
                var cipherBytes = new byte[fullCipher.Length - nonce.Length - tag.Length];

                Buffer.BlockCopy(fullCipher, 0, nonce, 0, nonce.Length);
                Buffer.BlockCopy(fullCipher, nonce.Length, tag, 0, tag.Length);
                Buffer.BlockCopy(fullCipher, nonce.Length + tag.Length, cipherBytes, 0, cipherBytes.Length);

                var plainBytes = new byte[cipherBytes.Length];

                using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                // Return original if decryption fails (might not be encrypted)
                return encryptedText;
            }
            catch (FormatException)
            {
                // Not valid Base64 - return original (plaintext data)
                return encryptedText;
            }
        }

        /// <summary>
        /// Generate deterministic search hash for encrypted field searching.
        /// Allows searching without decrypting all records.
        /// </summary>
        public string? GenerateSearchHash(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // Normalize: lowercase, trim, remove common formatting
            var normalized = value.ToLowerInvariant().Trim()
                .Replace("-", "").Replace("(", "").Replace(")", "").Replace(" ", "");

            using var hmac = new HMACSHA256(_searchKey);
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// Encrypt all configured PHI fields on an entity.
        /// Uses EncryptionConfiguration instead of attributes.
        /// </summary>
        public void EncryptEntity<T>(T entity) where T : class
        {
            if (entity == null) return;

            var entityType = typeof(T);
            if (!EncryptionConfiguration.HasEncryptedFields(entityType)) return;

            foreach (var fieldName in EncryptionConfiguration.GetEncryptedFields(entityType))
            {
                var property = entityType.GetProperty(fieldName);
                if (property == null || property.PropertyType != typeof(string)) continue;

                var currentValue = property.GetValue(entity) as string;
                if (!string.IsNullOrEmpty(currentValue) && !IsEncrypted(currentValue))
                {
                    var encryptedValue = Encrypt(currentValue);
                    property.SetValue(entity, encryptedValue);
                }
            }
        }

        /// <summary>
        /// Decrypt all configured PHI fields on an entity.
        /// Uses EncryptionConfiguration instead of attributes.
        /// </summary>
        public void DecryptEntity<T>(T entity) where T : class
        {
            if (entity == null) return;

            var entityType = typeof(T);
            if (!EncryptionConfiguration.HasEncryptedFields(entityType)) return;

            foreach (var fieldName in EncryptionConfiguration.GetEncryptedFields(entityType))
            {
                var property = entityType.GetProperty(fieldName);
                if (property == null || property.PropertyType != typeof(string)) continue;

                var currentValue = property.GetValue(entity) as string;
                if (!string.IsNullOrEmpty(currentValue) && IsEncrypted(currentValue))
                {
                    var decryptedValue = Decrypt(currentValue);
                    property.SetValue(entity, decryptedValue);
                }
            }
        }

        /// <summary>
        /// Check if a value appears to be encrypted (Base64 with expected length)
        /// </summary>
        public bool IsEncrypted(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            
            try
            {
                var bytes = Convert.FromBase64String(value);
                // Minimum size: 12 (nonce) + 16 (tag) + 1 (at least 1 byte ciphertext)
                return bytes.Length >= 29;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generate a new encryption key (use once during initial setup)
        /// </summary>
        public static string GenerateEncryptionKey()
        {
            var keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            return Convert.ToBase64String(keyBytes) + "_" + Guid.NewGuid().ToString("N")[..8];
        }
    }
}
