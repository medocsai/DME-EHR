using System.Security.Cryptography;
using System.Text;
using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

/// <summary>
/// Service for generating and managing blind index search tokens.
/// Enables HIPAA-compliant patient search without exposing PHI at rest.
///
/// How it works:
/// 1. When a patient is created/updated, we generate HMAC hashes of searchable field prefixes
/// 2. These hashes are stored in PatientSearchTokens table
/// 3. During search, we hash the search query and match against tokens
/// 4. Only matching patients are decrypted, not all patients
/// </summary>
public interface IBlindIndexService
{
    /// <summary>
    /// Generate all search tokens for a patient and save to database.
    /// Call this after patient create/update.
    /// </summary>
    Task IndexPatientAsync(Patient patient);

    /// <summary>
    /// Remove all search tokens for a patient.
    /// Call this before re-indexing or when deleting patient.
    /// </summary>
    Task RemovePatientTokensAsync(int patientId);

    /// <summary>
    /// Generate a search hash for a query string.
    /// Use this hash to query the PatientSearchTokens table.
    /// </summary>
    string GenerateSearchHash(string query);
}

public class BlindIndexService : IBlindIndexService
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryptionHelper;

    // Minimum prefix length for autocomplete (start generating tokens from this length)
    private const int MinPrefixLength = 1;

    // Maximum prefix length to generate (prevents token explosion for long names)
    private const int MaxPrefixLength = 15;

    // Searchable field types
    private static readonly string[] SearchableFields = { "FirstName", "LastName", "MRN", "Phone", "Email", "FullName" };

    public BlindIndexService(EhrDbContext context, EncryptionHelper encryptionHelper)
    {
        _context = context;
        _encryptionHelper = encryptionHelper;
    }

    public async Task IndexPatientAsync(Patient patient)
    {
        // First, remove existing tokens for this patient
        await RemovePatientTokensAsync(patient.PatientId);

        var tokens = new List<PatientSearchToken>();

        // Generate tokens for FirstName
        if (!string.IsNullOrWhiteSpace(patient.FirstName))
        {
            tokens.AddRange(GenerateTokensForField(
                patient,
                NormalizeForSearch(patient.FirstName),
                "FirstName"));
        }

        // Generate tokens for LastName
        if (!string.IsNullOrWhiteSpace(patient.LastName))
        {
            tokens.AddRange(GenerateTokensForField(
                patient,
                NormalizeForSearch(patient.LastName),
                "LastName"));
        }

        // Generate tokens for FullName (FirstName + LastName combined)
        if (!string.IsNullOrWhiteSpace(patient.FirstName) && !string.IsNullOrWhiteSpace(patient.LastName))
        {
            var fullName = NormalizeForSearch($"{patient.FirstName} {patient.LastName}");
            tokens.AddRange(GenerateTokensForField(patient, fullName, "FullName"));

            // Also generate for "LastName, FirstName" format
            var fullNameReversed = NormalizeForSearch($"{patient.LastName}, {patient.FirstName}");
            tokens.AddRange(GenerateTokensForField(patient, fullNameReversed, "FullName"));
        }

        // Generate tokens for MRN (not encrypted, but include for consistency)
        if (!string.IsNullOrWhiteSpace(patient.Mrn))
        {
            tokens.AddRange(GenerateTokensForField(
                patient,
                NormalizeForSearch(patient.Mrn),
                "MRN"));
        }

        // Generate tokens for Phone
        if (!string.IsNullOrWhiteSpace(patient.Phone))
        {
            // Normalize phone: remove all non-digits
            var phoneDigits = new string(patient.Phone.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(phoneDigits))
            {
                tokens.AddRange(GenerateTokensForField(patient, phoneDigits, "Phone"));
            }
        }

        // Generate tokens for Email
        if (!string.IsNullOrWhiteSpace(patient.Email))
        {
            tokens.AddRange(GenerateTokensForField(
                patient,
                NormalizeForSearch(patient.Email),
                "Email"));

            // Also store a single full-length hash under "EmailExact" for the
            // uniqueness check. The prefix-only "Email" tokens are capped at
            // MaxPrefixLength (15 chars), so longer emails (most real ones)
            // would otherwise never match in CheckEmailUniqueAsync.
            var normalizedFullEmail = NormalizeForSearch(patient.Email);
            var fullEmailHash = _encryptionHelper.GenerateSearchHash(normalizedFullEmail);
            if (!string.IsNullOrEmpty(fullEmailHash))
            {
                tokens.Add(new PatientSearchToken
                {
                    PatientId = patient.PatientId,
                    TenantId = patient.TenantId,
                    LocationId = patient.PreferredLocationId,
                    TokenHash = fullEmailHash,
                    FieldType = "EmailExact",
                    PrefixLength = normalizedFullEmail.Length
                });
            }
        }

        // Remove duplicates (same hash can occur from different prefixes)
        tokens = tokens
            .GroupBy(t => new { t.TokenHash, t.FieldType })
            .Select(g => g.First())
            .ToList();

        if (tokens.Count > 0)
        {
            _context.PatientSearchTokens.AddRange(tokens);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemovePatientTokensAsync(int patientId)
    {
        var existingTokens = await _context.PatientSearchTokens
            .Where(t => t.PatientId == patientId)
            .ToListAsync();

        if (existingTokens.Count > 0)
        {
            _context.PatientSearchTokens.RemoveRange(existingTokens);
            await _context.SaveChangesAsync();
        }
    }

    public string GenerateSearchHash(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var normalized = NormalizeForSearch(query);
        return _encryptionHelper.GenerateSearchHash(normalized) ?? string.Empty;
    }

    /// <summary>
    /// Generate prefix tokens for a single field value.
    /// For "John", generates: hash("j"), hash("jo"), hash("joh"), hash("john")
    /// </summary>
    private List<PatientSearchToken> GenerateTokensForField(Patient patient, string value, string fieldType)
    {
        var tokens = new List<PatientSearchToken>();

        if (string.IsNullOrEmpty(value))
            return tokens;

        var maxLength = Math.Min(value.Length, MaxPrefixLength);

        for (int i = MinPrefixLength; i <= maxLength; i++)
        {
            var prefix = value[..i];
            var hash = _encryptionHelper.GenerateSearchHash(prefix);

            if (!string.IsNullOrEmpty(hash))
            {
                tokens.Add(new PatientSearchToken
                {
                    PatientId = patient.PatientId,
                    TenantId = patient.TenantId,
                    LocationId = patient.PreferredLocationId,
                    TokenHash = hash,
                    FieldType = fieldType,
                    PrefixLength = i
                });
            }
        }

        return tokens;
    }

    /// <summary>
    /// Normalize a string for search matching.
    /// Converts to lowercase and removes special characters (apostrophes, hyphens, etc.)
    /// This ensures "O'Brien" matches "obrien" and "Mary-Jane" matches "maryjane".
    /// </summary>
    private static string NormalizeForSearch(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Convert to lowercase
        var normalized = value.ToLowerInvariant().Trim();

        // Remove special characters that users might omit when searching
        // Keep only letters, digits, spaces, @ (for email), and . (for email domains)
        var result = new StringBuilder();
        foreach (char c in normalized)
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '@' || c == '.')
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
