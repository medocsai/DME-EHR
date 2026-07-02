using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Models.Generated;
using System.Text;
using System.Text.RegularExpressions;

namespace EHR.Services;

/// <summary>
/// Generates and stores a patient-friendly visit summary on the Encounter.
/// Spec: rules/technical/encounter-summary.md
///
/// Triggered (fire-and-forget) at three points:
///   1. Close Encounter (Checkout)            — initial generation
///   2. Amendment signed                       — regeneration
///   3. Addendum signed                        — regeneration
///
/// Source data: signed clinical notes for the encounter (latest version per
/// note — max amendment VersionNumber, or original if no amendments) plus
/// all addendums. PHI is scrubbed via ClinicalNotePHIScrubber before any
/// Gemini call.
///
/// Safety: if any note for the encounter is not in Signed status, the
/// manager returns silently without generating. The patient portal must
/// never display content derived from an unsigned note.
/// </summary>
public interface IEncounterSummaryManager
{
    Task GenerateSummaryAsync(int encounterId);
}

public class EncounterSummaryManager : IEncounterSummaryManager
{
    private const int NOTE_STATUS_SIGNED = 2;

    // Gemini sees the concatenated, scrubbed note text. Cap input so a
    // pathologically long note doesn't blow up the prompt or the cost.
    // The output word cap is enforced by the prompt itself, not in code.
    private const int MaxScrubbedInputChars = 12000;

    private readonly EhrDbContext _context;
    private readonly IGeminiService _gemini;
    private readonly EncryptionHelper? _encryptionHelper;
    private readonly ILogger<EncounterSummaryManager> _logger;

    public EncounterSummaryManager(
        EhrDbContext context,
        IGeminiService gemini,
        ILogger<EncounterSummaryManager> logger,
        EncryptionHelper? encryptionHelper = null)
    {
        _context = context;
        _gemini = gemini;
        _logger = logger;
        _encryptionHelper = encryptionHelper;
    }

    public async Task GenerateSummaryAsync(int encounterId)
    {
        try
        {
            // 1. Load encounter with patient + provider (for PHI context)
            var encounter = await _context.Encounters
                .Include(e => e.Patient)
                .Include(e => e.Provider)
                .FirstOrDefaultAsync(e => e.EncounterId == encounterId);

            if (encounter == null)
            {
                _logger.LogWarning("EncounterSummaryManager: encounter {EncounterId} not found", encounterId);
                return;
            }

            // 2. Load all clinical notes for this encounter
            var notes = await _context.ClinicalNotes
                .Where(n => n.EncounterId == encounterId)
                .ToListAsync();

            if (notes.Count == 0)
            {
                _logger.LogInformation("EncounterSummaryManager: encounter {EncounterId} has no notes; skipping", encounterId);
                return;
            }

            // 3. Safety: every note must be signed. If any unsigned, skip.
            //    Status 2 = Signed; any other status (Draft, PendingSignature,
            //    PendingCoSig, Voided) means we shouldn't surface content to a patient.
            if (notes.Any(n => n.Status != NOTE_STATUS_SIGNED))
            {
                _logger.LogInformation(
                    "EncounterSummaryManager: encounter {EncounterId} has unsigned notes; skipping",
                    encounterId);
                return;
            }

            // 4. For each note, take the latest version content
            var noteIds = notes.Select(n => n.ClinicalNoteId).ToList();

            var amendments = await _context.ClinicalNoteAmendments
                .Where(a => noteIds.Contains(a.ClinicalNoteId))
                .ToListAsync();

            var addendums = await _context.ClinicalNoteAddendums
                .Where(a => noteIds.Contains(a.ClinicalNoteId))
                .OrderBy(a => a.SignedAt)
                .ToListAsync();

            // 5. Build PHI scrub context from patient + provider.
            //    Patient name/email/phone are stored encrypted — decrypt before
            //    handing to the scrubber so it can match plaintext occurrences
            //    inside the note body.
            var phi = BuildPhiContext(encounter);

            // 6. Concatenate scrubbed plain text — note(s) first, then addendums
            var sb = new StringBuilder();
            foreach (var note in notes.OrderBy(n => n.ClinicalNoteId))
            {
                var latestHtml = GetLatestVersionHtml(note, amendments);
                var scrubbed = ProcessContent(latestHtml, phi);
                if (!string.IsNullOrWhiteSpace(scrubbed))
                {
                    sb.AppendLine(scrubbed);
                    sb.AppendLine();
                }
            }

            foreach (var add in addendums)
            {
                var scrubbed = ProcessContent(add.Content, phi);
                if (!string.IsNullOrWhiteSpace(scrubbed))
                {
                    sb.AppendLine("Addendum:");
                    sb.AppendLine(scrubbed);
                    sb.AppendLine();
                }
            }

            var scrubbedContent = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(scrubbedContent))
            {
                _logger.LogInformation(
                    "EncounterSummaryManager: encounter {EncounterId} produced empty content after scrub; skipping",
                    encounterId);
                return;
            }

            if (scrubbedContent.Length > MaxScrubbedInputChars)
                scrubbedContent = scrubbedContent[..MaxScrubbedInputChars];

            // 7. Build the prompt and call Gemini
            var prompt = BuildPrompt(scrubbedContent);
            var response = await _gemini.GenerateTextAsync(prompt, temperature: 0.3);

            if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Text))
            {
                _logger.LogWarning(
                    "EncounterSummaryManager: Gemini failed for encounter {EncounterId}: {Error}",
                    encounterId, response?.ErrorMessage);
                // Preserve any existing summary; do not overwrite with empty/failed result.
                return;
            }

            var summary = response.Text.Trim();

            // 8. Persist — encrypt at rest. The summary is derived from
            //    clinical content tied to a PatientId; treat it the same as
            //    the source clinical note (which is also encrypted).
            //    Readers must decrypt on the way out.
            encounter.SummaryText = EncryptSummary(summary);
            encounter.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "EncounterSummaryManager: summary saved for encounter {EncounterId} ({Chars} chars plaintext)",
                encounterId, summary.Length);
        }
        catch (Exception ex)
        {
            // Fire-and-forget — never throw to the caller.
            _logger.LogError(ex,
                "EncounterSummaryManager: unhandled error for encounter {EncounterId}",
                encounterId);
        }
    }

    private PhiContext BuildPhiContext(Encounter encounter)
    {
        return new PhiContext
        {
            PatientFirstName = TryDecrypt(encounter.Patient?.FirstName),
            PatientLastName = TryDecrypt(encounter.Patient?.LastName),
            PatientDob = encounter.Patient?.DateOfBirth,
            PatientMrn = encounter.Patient?.Mrn,
            ProviderFirstName = encounter.Provider?.FirstName,
            ProviderLastName = encounter.Provider?.LastName,
            PatientPhone = TryDecrypt(encounter.Patient?.Phone),
            PatientEmail = TryDecrypt(encounter.Patient?.Email)
        };
    }

    private string? TryDecrypt(string? maybeEncrypted)
    {
        if (string.IsNullOrEmpty(maybeEncrypted)) return maybeEncrypted;
        if (_encryptionHelper == null) return maybeEncrypted;
        try { return _encryptionHelper.Decrypt(maybeEncrypted) ?? maybeEncrypted; }
        catch { return maybeEncrypted; }
    }

    private string EncryptSummary(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (_encryptionHelper == null) return plaintext;
        try { return _encryptionHelper.Encrypt(plaintext) ?? plaintext; }
        catch { return plaintext; }
    }

    private static string GetLatestVersionHtml(ClinicalNote note, List<ClinicalNoteAmendment> allAmendments)
    {
        var noteAmendments = allAmendments
            .Where(a => a.ClinicalNoteId == note.ClinicalNoteId)
            .OrderByDescending(a => a.VersionNumber)
            .ToList();

        // If amendments exist, latest version wins; otherwise original note content.
        if (noteAmendments.Count > 0)
            return noteAmendments[0].HtmlContent ?? string.Empty;

        return note.HtmlContent ?? string.Empty;
    }

    /// <summary>
    /// Decrypt → strip HTML → collapse whitespace → scrub PHI.
    /// Returns empty string if any step fails or produces no content.
    /// </summary>
    private string ProcessContent(string? encrypted, PhiContext phi)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return string.Empty;

        string decrypted = encrypted;
        if (_encryptionHelper != null)
        {
            try { decrypted = _encryptionHelper.Decrypt(encrypted) ?? encrypted; }
            catch { return string.Empty; }
        }

        // Strip HTML tags, collapse whitespace
        var plain = Regex.Replace(decrypted, "<[^>]+>", " ");
        plain = System.Net.WebUtility.HtmlDecode(plain);
        plain = Regex.Replace(plain, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(plain)) return string.Empty;

        return ClinicalNotePHIScrubber.Scrub(plain, phi);
    }

    private static string BuildPrompt(string scrubbedContent)
    {
        return
            "You are summarizing a medical visit for the patient who attended it.\n\n" +
            "Write a clear, friendly summary in plain English that the patient can easily understand.\n" +
            "Rules:\n" +
            "- Do not use medical abbreviations or jargon. Spell out terms.\n" +
            "- Do not include any personal identifiers (names, dates of birth, ID numbers, phone numbers, addresses).\n" +
            "- Maximum 250 words.\n" +
            "- Structure: reason for the visit, key observations or findings, and what happens next (if mentioned).\n" +
            "- Do not speculate or add information not present in the notes below.\n" +
            "- Write in third person or neutral voice (e.g., \"The visit addressed...\" not \"Your visit...\").\n\n" +
            "Clinical notes from the visit (personal identifiers have been removed):\n" +
            "---\n" +
            scrubbedContent + "\n" +
            "---";
    }
}
