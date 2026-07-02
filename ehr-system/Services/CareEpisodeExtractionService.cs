using System.Text.Json;
using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

// =====================================================================================
// LEGACY SERVICE — copied from PT (Physical Therapy) EHR. NOT used in Internal Medicine.
// =====================================================================================
// In Internal Medicine, ICD-10 diagnosis codes are picked by the provider on the
// "Dx & CPT Codes" encounter step and stored in Encounter.IcdSelections.
// AutoCreateDraftClaimAsync reads Encounter.IcdSelections (NOT CareEpisode) to
// populate the claim's BillingClaim.DiagnosisCodes (CMS-1500 Box 21).
//
// This service exists only because the PT EHR codebase was copied. Do NOT call
// any of these methods from new Internal Medicine features.
// =====================================================================================

/// <summary>
/// DTO for extracted care episode data from clinical notes.
/// </summary>
public class CareEpisodeExtractionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    // Extracted fields
    public string? DiagnosisCode { get; set; }
    public string? DiagnosisDescription { get; set; }
    public List<string>? Goals { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? PhysicianName { get; set; }
    public int? ExpectedVisits { get; set; }
    public int? VisitFrequency { get; set; }
    public int? DurationWeeks { get; set; } // Duration in weeks

    // Validation
    public List<string> MissingFields { get; set; } = new();
    public bool IsComplete => MissingFields.Count == 0;

    // Insurance comparison
    /// <summary>Total allowed visits summed from all active non-Self-Pay insurances</summary>
    public int? InsuranceAllowedVisits { get; set; }
    /// <summary>Total visits needed = ExpectedVisits + 1 (for Initial Evaluation itself)</summary>
    public int? TotalVisitsNeeded { get; set; }
    /// <summary>How many visits over the insurance limit (TotalVisitsNeeded - InsuranceAllowedVisits)</summary>
    public int? VisitsOverLimit { get; set; }
    public bool? IsSelfPay { get; set; }
    public bool? HasInsurance { get; set; }
    public bool? HasVisitMismatch { get; set; }
    public string? InsurancePayerName { get; set; }
}

/// <summary>
/// Request DTO for validating clinical note before signing.
/// </summary>
public class ValidateInitialEvaluationRequest
{
    public int ClinicalNoteId { get; set; }
}

/// <summary>
/// Response DTO for validation result.
/// </summary>
public class ValidateInitialEvaluationResponse
{
    public bool IsValid { get; set; }
    public bool IsInitialEvaluation { get; set; }
    public bool IsReevaluation { get; set; }
    public CareEpisodeExtractionResult? ExtractionResult { get; set; }
    public string? ErrorMessage { get; set; }

    // Re-evaluation specific fields
    /// <summary>Number of visits already completed in the current care episode</summary>
    public int? CompletedVisits { get; set; }
    /// <summary>Current expected visits before re-evaluation update</summary>
    public int? CurrentExpectedVisits { get; set; }
    /// <summary>Current care episode ID for re-evaluation</summary>
    public int? CareEpisodeId { get; set; }
}

/// <summary>
/// Request DTO for signing with care episode creation.
/// </summary>
public class SignWithCareEpisodeRequest
{
    public int ClinicalNoteId { get; set; }
    public string? SignatureData { get; set; }
    public bool CreateCareEpisode { get; set; } = true;

    // Pre-validated extraction data from /validate-initial-evaluation
    // If provided, avoids a second Gemini API call
    public string? DiagnosisCode { get; set; }
    public string? DiagnosisDescription { get; set; }
    public List<string>? Goals { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? PhysicianName { get; set; }
    public int? ExpectedVisits { get; set; }
    public int? VisitFrequency { get; set; }
    public int? DurationWeeks { get; set; }
}

/// <summary>
/// Response DTO for signing with care episode.
/// </summary>
public class SignWithCareEpisodeResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ClinicalNoteId { get; set; }
    public int? CareEpisodeId { get; set; }
    /// <summary>True if a new CareEpisode was created (Initial Evaluation)</summary>
    public bool CareEpisodeCreated { get; set; }
    /// <summary>True if an existing CareEpisode was updated (Re-evaluation)</summary>
    public bool CareEpisodeUpdated { get; set; }
}

/// <summary>
/// Service for extracting care episode data from clinical notes using Gemini API.
/// </summary>
public interface ICareEpisodeExtractionService
{
    /// <summary>
    /// Extract care episode data from a clinical note.
    /// </summary>
    Task<CareEpisodeExtractionResult> ExtractCareEpisodeDataAsync(int clinicalNoteId);

    /// <summary>
    /// Validate an Initial Evaluation clinical note before signing.
    /// </summary>
    Task<ValidateInitialEvaluationResponse> ValidateInitialEvaluationAsync(int clinicalNoteId);

    /// <summary>
    /// Sign a clinical note and create care episode if it's an Initial Evaluation.
    /// </summary>
    /// <param name="clinicalNoteId">The clinical note ID</param>
    /// <param name="userId">The signing user ID</param>
    /// <param name="request">The sign request with optional pre-validated extraction data</param>
    Task<SignWithCareEpisodeResponse> SignAndCreateCareEpisodeAsync(int clinicalNoteId, int userId, SignWithCareEpisodeRequest request);
}

public class CareEpisodeExtractionService : ICareEpisodeExtractionService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IGeminiService _geminiService;
    private readonly ILogger<CareEpisodeExtractionService> _logger;
    private readonly EncryptionHelper? _encryptionHelper;
    private readonly IClinicalNoteService _noteService;

    public CareEpisodeExtractionService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        IGeminiService geminiService,
        ILogger<CareEpisodeExtractionService> logger,
        IClinicalNoteService noteService,
        EncryptionHelper? encryptionHelper = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _geminiService = geminiService;
        _logger = logger;
        _encryptionHelper = encryptionHelper;
        _noteService = noteService;
    }

    public async Task<CareEpisodeExtractionResult> ExtractCareEpisodeDataAsync(int clinicalNoteId)
    {
        try
        {
            // Get the clinical note with template info
            var note = await _context.ClinicalNotes
                .Include(n => n.Template)
                .Include(n => n.Patient)
                    .ThenInclude(p => p.Insurances)
                .FirstOrDefaultAsync(n => n.ClinicalNoteId == clinicalNoteId);

            if (note == null)
            {
                return new CareEpisodeExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Clinical note not found"
                };
            }

            // Decrypt HTML content if encrypted
            var htmlContent = note.HtmlContent;
            if (_encryptionHelper != null && !string.IsNullOrEmpty(htmlContent))
            {
                try
                {
                    htmlContent = _encryptionHelper.Decrypt(htmlContent);
                }
                catch
                {
                    // Content might not be encrypted, use as-is
                }
            }

            // Extract plain text from HTML for better parsing
            var plainText = StripHtmlTags(htmlContent);

            // Call Gemini API to extract care episode data
            var extractedData = await ExtractWithGeminiAsync(plainText);

            // Validate required fields
            // Note: ICD-10 Code is optional - Gemini tries to infer it from diagnosis
            // Expected Visits is calculated from Frequency × Duration
            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(extractedData.DiagnosisDescription))
            {
                missingFields.Add("Diagnosis");
            }
            if (extractedData.Goals == null || extractedData.Goals.Count == 0)
            {
                missingFields.Add("Goals");
            }
            if (string.IsNullOrWhiteSpace(extractedData.TreatmentPlan))
            {
                missingFields.Add("Treatment Plan / Plan of Care");
            }
            if (!extractedData.VisitFrequency.HasValue || extractedData.VisitFrequency <= 0)
            {
                missingFields.Add("Frequency");
            }
            if (!extractedData.DurationWeeks.HasValue || extractedData.DurationWeeks <= 0)
            {
                missingFields.Add("Duration");
            }
            // Expected Visits is calculated from Frequency × Duration, so only check if we have the inputs
            if (!extractedData.ExpectedVisits.HasValue || extractedData.ExpectedVisits <= 0)
            {
                // If we have both frequency and duration, we can calculate expected visits
                if (extractedData.VisitFrequency.HasValue && extractedData.DurationWeeks.HasValue)
                {
                    extractedData.ExpectedVisits = extractedData.VisitFrequency.Value * extractedData.DurationWeeks.Value;
                }
                else
                {
                    missingFields.Add("Expected Visits (could not calculate from Frequency and Duration)");
                }
            }

            extractedData.MissingFields = missingFields;
            extractedData.Success = true;

            // Get ALL active insurances for the patient
            var allActiveInsurances = note.Patient?.Insurances?
                .Where(i => i.IsActive == true)
                .ToList() ?? new List<Insurance>();

            // Filter out Self-Pay insurances (InsuranceCategory == 3)
            var nonSelfPayInsurances = allActiveInsurances
                .Where(i => i.InsuranceCategory != 3)
                .ToList();

            // Check if patient has any non-Self-Pay insurance
            if (nonSelfPayInsurances.Any())
            {
                extractedData.HasInsurance = true;
                extractedData.IsSelfPay = false;

                // Sum AllowedVisits from ALL non-Self-Pay insurances
                var totalAllowedVisits = nonSelfPayInsurances
                    .Where(i => i.AllowedVisits.HasValue)
                    .Sum(i => i.AllowedVisits!.Value);

                extractedData.InsuranceAllowedVisits = totalAllowedVisits > 0 ? totalAllowedVisits : null;

                // Build payer names list for display
                var payerNames = nonSelfPayInsurances
                    .Where(i => !string.IsNullOrEmpty(i.PayerName))
                    .Select(i => i.PayerName)
                    .Distinct()
                    .ToList();
                extractedData.InsurancePayerName = payerNames.Any()
                    ? string.Join(", ", payerNames)
                    : null;

                // Calculate total visits needed: ExpectedVisits + 1 (for the Initial Evaluation itself)
                if (extractedData.ExpectedVisits.HasValue)
                {
                    extractedData.TotalVisitsNeeded = extractedData.ExpectedVisits.Value + 1;
                }

                // Check for visit mismatch (only if we have both values)
                if (extractedData.InsuranceAllowedVisits.HasValue &&
                    extractedData.TotalVisitsNeeded.HasValue)
                {
                    extractedData.HasVisitMismatch =
                        extractedData.TotalVisitsNeeded > extractedData.InsuranceAllowedVisits;

                    if (extractedData.HasVisitMismatch == true)
                    {
                        extractedData.VisitsOverLimit =
                            extractedData.TotalVisitsNeeded.Value - extractedData.InsuranceAllowedVisits.Value;
                    }
                }
            }
            else if (allActiveInsurances.Any())
            {
                // Patient has only Self-Pay insurance(s) - skip coverage validation
                extractedData.HasInsurance = true;
                extractedData.IsSelfPay = true;
                extractedData.InsurancePayerName = "Self Pay";
            }
            else
            {
                // No insurance at all
                extractedData.HasInsurance = false;
            }

            return extractedData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting care episode data from clinical note {NoteId}", clinicalNoteId);
            return new CareEpisodeExtractionResult
            {
                Success = false,
                ErrorMessage = $"Failed to extract care episode data: {ex.Message}"
            };
        }
    }

    public async Task<ValidateInitialEvaluationResponse> ValidateInitialEvaluationAsync(int clinicalNoteId)
    {
        try
        {
            // Get the clinical note with template and appointment info
            var note = await _context.ClinicalNotes
                .Include(n => n.Template)
                .Include(n => n.Appointment)
                .FirstOrDefaultAsync(n => n.ClinicalNoteId == clinicalNoteId);

            if (note == null)
            {
                return new ValidateInitialEvaluationResponse
                {
                    IsValid = false,
                    IsInitialEvaluation = false,
                    IsReevaluation = false,
                    ErrorMessage = "Clinical note not found"
                };
            }

            // Initial Evaluation / Re-evaluation concepts removed (PT-specific)
            // These flags no longer exist on templates in IMEHR
            var isInitialEval = false;
            var isReevaluation = false;

            if (!isInitialEval && !isReevaluation)
            {
                // Not an Initial Evaluation or Re-evaluation - no extraction needed
                return new ValidateInitialEvaluationResponse
                {
                    IsValid = true,
                    IsInitialEvaluation = false,
                    IsReevaluation = false
                };
            }

            // Extract care episode data
            var extractionResult = await ExtractCareEpisodeDataAsync(clinicalNoteId);

            // For Re-evaluation, we only need Frequency and Duration - not Diagnosis/Goals/TreatmentPlan
            // Override the missing fields check for Re-evaluation
            bool isValidExtraction;
            if (isReevaluation)
            {
                // Re-evaluation only requires Frequency and Duration
                var reevalMissingFields = new List<string>();
                if (!extractionResult.VisitFrequency.HasValue || extractionResult.VisitFrequency <= 0)
                {
                    reevalMissingFields.Add("Frequency");
                }
                if (!extractionResult.DurationWeeks.HasValue || extractionResult.DurationWeeks <= 0)
                {
                    reevalMissingFields.Add("Duration");
                }
                // Calculate expected visits if we have both frequency and duration
                if (extractionResult.VisitFrequency.HasValue && extractionResult.DurationWeeks.HasValue)
                {
                    extractionResult.ExpectedVisits = extractionResult.VisitFrequency.Value * extractionResult.DurationWeeks.Value;
                }
                else if (!extractionResult.ExpectedVisits.HasValue || extractionResult.ExpectedVisits <= 0)
                {
                    reevalMissingFields.Add("Expected Visits (could not calculate from Frequency and Duration)");
                }

                // Override the missing fields for Re-evaluation
                extractionResult.MissingFields = reevalMissingFields;
                isValidExtraction = extractionResult.Success && reevalMissingFields.Count == 0;
            }
            else
            {
                // Initial Evaluation requires all fields
                isValidExtraction = extractionResult.Success && extractionResult.IsComplete;
            }

            var response = new ValidateInitialEvaluationResponse
            {
                IsValid = isValidExtraction,
                IsInitialEvaluation = isInitialEval,
                IsReevaluation = isReevaluation,
                ExtractionResult = extractionResult
            };

            // For Re-evaluation, also get existing care episode data
            if (isReevaluation && note.Appointment?.CareEpisodeId != null)
            {
                var careEpisodeId = note.Appointment.CareEpisodeId.Value;
                var careEpisode = await _context.CareEpisodes
                    .FirstOrDefaultAsync(ce => ce.CareEpisodeId == careEpisodeId);

                if (careEpisode != null)
                {
                    response.CareEpisodeId = careEpisodeId;
                    response.CurrentExpectedVisits = careEpisode.ExpectedVisits;

                    // Count completed visits (appointments with status >= CheckedIn for this patient in this care episode)
                    var completedVisits = await _context.Appointments
                        .Where(a => a.CareEpisodeId == careEpisodeId && a.Status >= 2) // Status 2 = CheckedIn or higher
                        .CountAsync();

                    response.CompletedVisits = completedVisits;
                }
            }
            else if (isReevaluation && note.Appointment?.CareEpisodeId == null)
            {
                // Re-evaluation but no care episode linked to appointment - warn user
                response.IsValid = false;
                response.ErrorMessage = "This appointment is not linked to a Care Episode. Cannot update Care Episode from Re-evaluation note.";
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating evaluation note {NoteId}", clinicalNoteId);
            return new ValidateInitialEvaluationResponse
            {
                IsValid = false,
                IsInitialEvaluation = false,
                IsReevaluation = false,
                ErrorMessage = $"Validation failed: {ex.Message}"
            };
        }
    }

    public async Task<SignWithCareEpisodeResponse> SignAndCreateCareEpisodeAsync(
        int clinicalNoteId,
        int userId,
        SignWithCareEpisodeRequest request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Get the clinical note
            var note = await _context.ClinicalNotes
                .Include(n => n.Template)
                .Include(n => n.Appointment)
                .FirstOrDefaultAsync(n => n.ClinicalNoteId == clinicalNoteId);

            if (note == null)
            {
                return new SignWithCareEpisodeResponse
                {
                    Success = false,
                    ErrorMessage = "Clinical note not found"
                };
            }

            // Check if already signed
            if (note.Status != 0) // 0 = Draft
            {
                return new SignWithCareEpisodeResponse
                {
                    Success = false,
                    ErrorMessage = "Clinical note is already signed"
                };
            }

            // Initial Evaluation / Re-evaluation concepts removed (PT-specific)
            // These flags no longer exist on templates in IMEHR
            var isInitialEval = note.Type == 0; // Keep basic type-based fallback
            var isReevaluation = false;

            CareEpisode? newCareEpisode = null;
            CareEpisode? updatedCareEpisode = null;

            if (isInitialEval)
            {
                // Check if we have pre-validated extraction data from frontend
                // This avoids a second Gemini API call since validation already extracted the data
                bool hasPrevalidatedData = !string.IsNullOrEmpty(request.DiagnosisDescription) &&
                                           request.Goals != null && request.Goals.Count > 0 &&
                                           !string.IsNullOrEmpty(request.TreatmentPlan) &&
                                           request.ExpectedVisits.HasValue &&
                                           request.VisitFrequency.HasValue &&
                                           request.DurationWeeks.HasValue;

                string? diagnosisCode;
                string? diagnosisDescription;
                List<string>? goals;
                string? treatmentPlan;
                string? physicianName;
                int? expectedVisits;
                int? visitFrequency;
                int? durationWeeks;

                if (hasPrevalidatedData)
                {
                    // Use pre-validated data from request (no need to call Gemini again)
                    diagnosisCode = request.DiagnosisCode;
                    diagnosisDescription = request.DiagnosisDescription;
                    goals = request.Goals;
                    treatmentPlan = request.TreatmentPlan;
                    physicianName = request.PhysicianName;
                    expectedVisits = request.ExpectedVisits;
                    visitFrequency = request.VisitFrequency;
                    durationWeeks = request.DurationWeeks;

                    _logger.LogDebug("Using pre-validated extraction data for clinical note {NoteId}", clinicalNoteId);
                }
                else
                {
                    // Fallback: Extract care episode data (this should rarely happen)
                    _logger.LogWarning("No pre-validated data provided, extracting again for clinical note {NoteId}", clinicalNoteId);

                    var extractionResult = await ExtractCareEpisodeDataAsync(clinicalNoteId);

                    if (!extractionResult.Success)
                    {
                        return new SignWithCareEpisodeResponse
                        {
                            Success = false,
                            ErrorMessage = extractionResult.ErrorMessage ?? "Failed to extract care episode data"
                        };
                    }

                    if (!extractionResult.IsComplete)
                    {
                        return new SignWithCareEpisodeResponse
                        {
                            Success = false,
                            ErrorMessage = $"Missing required fields: {string.Join(", ", extractionResult.MissingFields)}"
                        };
                    }

                    diagnosisCode = extractionResult.DiagnosisCode;
                    diagnosisDescription = extractionResult.DiagnosisDescription;
                    goals = extractionResult.Goals;
                    treatmentPlan = extractionResult.TreatmentPlan;
                    physicianName = extractionResult.PhysicianName;
                    expectedVisits = extractionResult.ExpectedVisits;
                    visitFrequency = extractionResult.VisitFrequency;
                    durationWeeks = extractionResult.DurationWeeks;
                }

                // Check if patient already has an active care episode
                var existingEpisode = await _context.CareEpisodes
                    .Where(ce => ce.PatientId == note.PatientId)
                    .Where(ce => ce.Status == 0 || ce.Status == 3) // Active or Overdue
                    .FirstOrDefaultAsync();

                if (existingEpisode != null)
                {
                    return new SignWithCareEpisodeResponse
                    {
                        Success = false,
                        ErrorMessage = "Patient already has an active Care Episode"
                    };
                }

                // Create the care episode
                newCareEpisode = new CareEpisode
                {
                    TenantId = _tenantProvider.TenantId ?? note.TenantId,
                    PatientId = note.PatientId,
                    PrimaryProviderId = note.ProviderId,
                    StartDate = note.ServiceDate,
                    PrimaryDiagnosisCode = diagnosisCode ?? "",
                    PrimaryDiagnosisDescription = diagnosisDescription ?? "",
                    Goals = goals != null ? JsonSerializer.Serialize(goals) : null,
                    PlanOfCare = treatmentPlan,
                    PhysicianName = physicianName,
                    ExpectedVisits = expectedVisits,
                    VisitFrequency = visitFrequency,
                    Status = 0, // Active
                    MissedVisits = 0,
                    CreatedAt = DateTime.UtcNow
                };

                _context.CareEpisodes.Add(newCareEpisode);
                await _context.SaveChangesAsync();

                // Link any appointments from this patient that don't have a care episode
                var unlinkedAppointments = await _context.Appointments
                    .Where(a => a.PatientId == note.PatientId)
                    .Where(a => a.CareEpisodeId == null)
                    .ToListAsync();

                foreach (var apt in unlinkedAppointments)
                {
                    apt.CareEpisodeId = newCareEpisode.CareEpisodeId;
                }

                await _context.SaveChangesAsync();

                // Link any orphan consents for this patient (consents without a care episode)
                var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
                var orphanConsents = await _context.CareEpisodeConsents
                    .Where(c => c.TenantId == newCareEpisode.TenantId)
                    .Where(c => c.PatientId == note.PatientId)
                    .Where(c => c.CareEpisodeId == null)
                    .Where(c => c.SignedAt >= thirtyDaysAgo)
                    .ToListAsync();

                foreach (var consent in orphanConsents)
                {
                    consent.CareEpisodeId = newCareEpisode.CareEpisodeId;
                }

                if (orphanConsents.Any())
                {
                    await _context.SaveChangesAsync();
                }
            }
            else if (isReevaluation)
            {
                // Re-evaluation: Update existing CareEpisode

                // Check if appointment has a CareEpisode linked
                if (note.Appointment?.CareEpisodeId == null)
                {
                    return new SignWithCareEpisodeResponse
                    {
                        Success = false,
                        ErrorMessage = "This appointment is not linked to a Care Episode. Cannot update Care Episode from Re-evaluation note."
                    };
                }

                // Get the existing care episode
                var careEpisodeId = note.Appointment.CareEpisodeId.Value;
                updatedCareEpisode = await _context.CareEpisodes
                    .FirstOrDefaultAsync(ce => ce.CareEpisodeId == careEpisodeId);

                if (updatedCareEpisode == null)
                {
                    return new SignWithCareEpisodeResponse
                    {
                        Success = false,
                        ErrorMessage = "Care Episode not found."
                    };
                }

                // Get extraction data (from request or extract fresh)
                int? newExpectedVisits;
                int? newVisitFrequency;

                if (request.ExpectedVisits.HasValue && request.VisitFrequency.HasValue)
                {
                    // Use pre-validated data from request
                    newExpectedVisits = request.ExpectedVisits;
                    newVisitFrequency = request.VisitFrequency;
                    _logger.LogDebug("Using pre-validated extraction data for Re-evaluation note {NoteId}", clinicalNoteId);
                }
                else
                {
                    // Fallback: Extract care episode data
                    _logger.LogWarning("No pre-validated data provided for Re-evaluation, extracting for clinical note {NoteId}", clinicalNoteId);

                    var extractionResult = await ExtractCareEpisodeDataAsync(clinicalNoteId);

                    if (!extractionResult.Success || !extractionResult.ExpectedVisits.HasValue)
                    {
                        return new SignWithCareEpisodeResponse
                        {
                            Success = false,
                            ErrorMessage = extractionResult.ErrorMessage ?? "Failed to extract expected visits from Re-evaluation note"
                        };
                    }

                    newExpectedVisits = extractionResult.ExpectedVisits;
                    newVisitFrequency = extractionResult.VisitFrequency;
                }

                // ADD new expected visits to existing expected visits (cumulative)
                var currentExpectedVisits = updatedCareEpisode.ExpectedVisits ?? 0;
                updatedCareEpisode.ExpectedVisits = currentExpectedVisits + (newExpectedVisits ?? 0);

                // UPDATE visit frequency to new frequency
                if (newVisitFrequency.HasValue)
                {
                    updatedCareEpisode.VisitFrequency = newVisitFrequency;
                }

                updatedCareEpisode.UpdatedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Updated Care Episode {CareEpisodeId}: ExpectedVisits {OldVisits} + {NewVisits} = {TotalVisits}, VisitFrequency = {Frequency}",
                    careEpisodeId, currentExpectedVisits, newExpectedVisits, updatedCareEpisode.ExpectedVisits, newVisitFrequency);

                await _context.SaveChangesAsync();
            }

            // Sign the clinical note using the note service
            // This ensures signature placeholder replacement happens correctly
            var signedNote = await _noteService.SignNoteAsync(clinicalNoteId, userId, request.SignatureData);

            if (signedNote == null)
            {
                await transaction.RollbackAsync();
                return new SignWithCareEpisodeResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to sign clinical note"
                };
            }

            // Check if this was a Discharge appointment and complete the Care Episode if all notes are signed
            // Note: SignNoteAsync already handles this via TryCompleteCareEpisodeOnDischargeAsync
            // So we don't need to call it again here
            var careEpisodeCompleted = false;

            await transaction.CommitAsync();

            return new SignWithCareEpisodeResponse
            {
                Success = true,
                ClinicalNoteId = clinicalNoteId,
                CareEpisodeId = newCareEpisode?.CareEpisodeId ?? updatedCareEpisode?.CareEpisodeId ?? (careEpisodeCompleted ? note.Appointment?.CareEpisodeId : null),
                CareEpisodeCreated = newCareEpisode != null,
                CareEpisodeUpdated = updatedCareEpisode != null || careEpisodeCompleted
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error signing clinical note {NoteId} and handling care episode", clinicalNoteId);
            return new SignWithCareEpisodeResponse
            {
                Success = false,
                ErrorMessage = $"Failed to sign and handle care episode: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Checks if a clinical note's appointment is a Discharge type and all notes for that appointment are signed.
    /// If so, marks the associated Care Episode as Completed.
    /// </summary>
    /// <returns>True if the Care Episode was marked as completed, false otherwise.</returns>
    private async Task<bool> TryCompleteCareEpisodeOnDischargeAsync(ClinicalNote note, int userId)
    {
        // Only process if the note has an appointment
        if (note.AppointmentId == null || note.Appointment == null)
            return false;

        var appointment = note.Appointment;

        // TODO: IM encounter workflow - no auto-discharge logic needed
        // PT-specific discharge types removed
        return false; // Skip care episode auto-completion for IM

        // Check if the appointment has a Care Episode linked
        if (appointment.CareEpisodeId == null)
            return false;

        // Check if ALL clinical notes for this appointment are now signed (Status == 2)
        var hasUnsignedNotes = await _context.ClinicalNotes
            .AnyAsync(cn => cn.AppointmentId == appointment.AppointmentId && cn.Status != 2);

        if (hasUnsignedNotes)
            return false; // Not all notes are signed yet

        // All notes are signed - mark Care Episode as Completed
        var careEpisode = await _context.CareEpisodes
            .FirstOrDefaultAsync(ce => ce.CareEpisodeId == appointment.CareEpisodeId);

        if (careEpisode == null)
            return false;

        // Only update if not already completed
        if (careEpisode.Status == 2) // Already Completed
            return false;

        careEpisode.Status = 2; // Completed
        careEpisode.CompletionMethod = 1; // DischargeAppointment
        careEpisode.CompletedAt = DateTime.UtcNow;
        careEpisode.CompletedByUserId = userId;
        careEpisode.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Care Episode {CareEpisodeId} marked as Completed via Discharge appointment {AppointmentId}",
            careEpisode.CareEpisodeId, appointment.AppointmentId);

        return true;
    }

    private async Task<CareEpisodeExtractionResult> ExtractWithGeminiAsync(string clinicalNoteText)
    {
        var prompt = $@"You are a medical data extraction assistant for physical therapy. Extract the following information from this Initial Evaluation clinical note.

CLINICAL NOTE:
{clinicalNoteText}

Extract and return a JSON object with these fields:
1. diagnosis_code: The ICD-10 diagnosis code if mentioned or can be inferred from diagnosis. OPTIONAL - set to null if not found.
2. diagnosis_description: The diagnosis description (e.g., ""Low back pain"") - REQUIRED
3. goals: An array of treatment goals mentioned in the note - REQUIRED
4. treatment_plan: The plan of care or treatment plan text - REQUIRED
5. physician_name: The referring physician or PCP name if mentioned - OPTIONAL (look for patterns like ""Physician Name:"", ""Referring Physician:"", ""PCP:"", ""Primary Care Physician:"", ""Physician:"")
6. visit_frequency: How many times per week (integer) - REQUIRED
7. duration_weeks: Duration of treatment in weeks (integer) - REQUIRED
8. expected_visits: Total expected visits (integer) - Calculate from frequency × duration

FREQUENCY EXTRACTION RULES (convert to integer visits per week):
- ""3x/week"" or ""3x a week"" or ""three times a week"" = 3
- ""2 to 3 times a week"" = 2 (use lower/conservative estimate)
- ""twice a week"" or ""2x/week"" = 2
- ""3x/week, then 2x/week"" = 2 (use lower frequency for conservative estimate)
- ""once a week"" or ""1x/week"" = 1

DURATION EXTRACTION RULES (convert to integer weeks):
- ""4 weeks"" or ""four weeks"" = 4
- ""6-8 weeks"" = 6 (use lower estimate)
- ""2 months"" = 8 (convert months to weeks)
- ""30 days"" = 4 (convert days to weeks, round down)

EXPECTED VISITS CALCULATION:
Calculate: expected_visits = visit_frequency × duration_weeks

EXAMPLES:
- Frequency: ""3x/week"", Duration: ""4 weeks"" → visit_frequency: 3, duration_weeks: 4, expected_visits: 12
- Frequency: ""2 to 3 times a week"", Duration: ""6 weeks"" → visit_frequency: 2, duration_weeks: 6, expected_visits: 12
- Frequency: ""3x/week then 2x/week"", Duration: ""4 weeks"" → visit_frequency: 2, duration_weeks: 4, expected_visits: 8
- Frequency: ""twice a week"", Duration: ""8 weeks"" → visit_frequency: 2, duration_weeks: 8, expected_visits: 16

IMPORTANT:
- For diagnosis_code: Try to infer the ICD-10 code from the diagnosis if possible. If you cannot determine it, set to null (it's optional).
- Always use CONSERVATIVE (lower) estimates when ranges are given
- If expected_visits is explicitly stated in the note, use that value instead of calculating
- If a REQUIRED field cannot be found, set it to null
- Return ONLY valid JSON, no additional text or explanation
- Do not include markdown code blocks, just the raw JSON

Example output format:
{{
  ""diagnosis_code"": ""M54.5"",
  ""diagnosis_description"": ""Low back pain"",
  ""goals"": [""Reduce pain to 3/10"", ""Improve ROM to 90%"", ""Return to work activities""],
  ""treatment_plan"": ""Manual therapy, therapeutic exercises, modalities as needed"",
  ""physician_name"": ""John Doe"",
  ""visit_frequency"": 2,
  ""duration_weeks"": 6,
  ""expected_visits"": 12
}}";

        // Use centralized GeminiService with low temperature for deterministic extraction
        var response = await _geminiService.GenerateTextAsync(prompt, temperature: 0.1);

        if (!response.Success || string.IsNullOrEmpty(response.Text))
        {
            _logger.LogWarning("Gemini extraction failed: {Error}", response.ErrorMessage);
            return new CareEpisodeExtractionResult();
        }

        var extractedJson = response.Text;

        // Clean up the JSON (remove markdown code blocks if present)
        extractedJson = extractedJson.Trim();
        if (extractedJson.StartsWith("```json"))
        {
            extractedJson = extractedJson.Substring(7);
        }
        else if (extractedJson.StartsWith("```"))
        {
            extractedJson = extractedJson.Substring(3);
        }
        if (extractedJson.EndsWith("```"))
        {
            extractedJson = extractedJson.Substring(0, extractedJson.Length - 3);
        }
        extractedJson = extractedJson.Trim();

        // Parse the extracted JSON
        try
        {
            using var extractedDoc = JsonDocument.Parse(extractedJson);
            var root = extractedDoc.RootElement;

            var result = new CareEpisodeExtractionResult
            {
                DiagnosisCode = GetStringProperty(root, "diagnosis_code"),
                DiagnosisDescription = GetStringProperty(root, "diagnosis_description"),
                TreatmentPlan = GetStringProperty(root, "treatment_plan"),
                PhysicianName = GetStringProperty(root, "physician_name"),
                ExpectedVisits = GetIntProperty(root, "expected_visits"),
                VisitFrequency = GetIntProperty(root, "visit_frequency"),
                DurationWeeks = GetIntProperty(root, "duration_weeks")
            };

            // Parse goals array
            if (root.TryGetProperty("goals", out var goalsElement) && goalsElement.ValueKind == JsonValueKind.Array)
            {
                result.Goals = new List<string>();
                foreach (var goal in goalsElement.EnumerateArray())
                {
                    var goalText = goal.GetString();
                    if (!string.IsNullOrWhiteSpace(goalText))
                    {
                        result.Goals.Add(goalText);
                    }
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extracted JSON: {Json}", extractedJson);
            return new CareEpisodeExtractionResult();
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static int? GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            if (prop.ValueKind == JsonValueKind.String)
            {
                if (int.TryParse(prop.GetString(), out var intValue))
                {
                    return intValue;
                }
            }
        }
        return null;
    }

    private static string StripHtmlTags(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        // Simple HTML tag stripping
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        return System.Net.WebUtility.HtmlDecode(result.Trim());
    }
}
