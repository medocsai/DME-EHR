using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

/// <summary>
/// Service to gather all encounter context data (vitals, CC/HPI, history, etc.)
/// for use in template pre-filling and Scribe prompt enhancement.
/// </summary>
public interface IEncounterContextService
{
    /// <summary>
    /// Gather all encounter context data for a given encounter/patient.
    /// Used by both template pre-fill and Scribe context enhancement.
    /// </summary>
    Task<EncounterContextDto> GetEncounterContextAsync(int patientId, int? encounterId);

    /// <summary>
    /// Pre-fill a clinical note template with encounter context data using Gemini AI.
    /// </summary>
    Task<PrefillTemplateResponse> PrefillTemplateAsync(PrefillTemplateRequest request);
}

public class EncounterContextService : IEncounterContextService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly IGeminiService _geminiService;
    private readonly ILogger<EncounterContextService> _logger;
    private readonly EncryptionHelper? _encryptionHelper;

    public EncounterContextService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        IGeminiService geminiService,
        ILogger<EncounterContextService> logger,
        EncryptionHelper? encryptionHelper = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _geminiService = geminiService;
        _logger = logger;
        _encryptionHelper = encryptionHelper;
    }

    public async Task<EncounterContextDto> GetEncounterContextAsync(int patientId, int? encounterId)
    {
        var result = new EncounterContextDto();
        var tenantId = _tenantProvider.TenantId ?? 0;

        // 1. Get encounter-level data (CC/HPI, ROS, PE, Assessment, Plan)
        if (encounterId.HasValue)
        {
            var encounter = await _context.Encounters
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EncounterId == encounterId.Value);

            if (encounter != null)
            {
                result.ChiefComplaint = encounter.ChiefComplaint;
                result.HistoryOfPresentIllness = encounter.HistoryOfPresentIllness;
                result.ReviewOfSystems = encounter.ReviewOfSystems;
                result.PhysicalExam = encounter.PhysicalExam;
                result.Assessment = encounter.Assessment;
                result.Plan = encounter.Plan;
            }
        }

        // 2. Get vitals for this encounter (most recent set)
        if (encounterId.HasValue)
        {
            var vital = await _context.PatientVitals
                .AsNoTracking()
                .Where(v => v.EncounterId == encounterId.Value)
                .OrderByDescending(v => v.RecordedAt)
                .FirstOrDefaultAsync();

            if (vital != null)
            {
                result.Vitals = new EncounterVitalsDto
                {
                    BloodPressure = vital.SystolicBp.HasValue && vital.DiastolicBp.HasValue
                        ? $"{vital.SystolicBp}/{vital.DiastolicBp} mmHg"
                        : null,
                    HeartRate = vital.HeartRate,
                    RespiratoryRate = vital.RespiratoryRate,
                    Temperature = vital.Temperature,
                    SpO2 = vital.SpO2,
                    Weight = vital.Weight,
                    Height = vital.Height,
                    Bmi = vital.Bmi
                };
            }
        }

        // 3. Get patient-level history data (sequential - DbContext is not thread-safe)
        // EF global query filter on IsDeleted = false is applied automatically.
        // Spec: rules/technical/history-review-soft-delete.md (section 12).
        var allergies = await _context.PatientAllergies
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.PatientId == patientId && a.IsActive)
            .OrderByDescending(a => a.Severity)
            .ToListAsync();

        var medications = await _context.PatientMedications
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.PatientId == patientId && m.Status == 0)
            .ToListAsync();

        var problems = await _context.PatientProblems
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.PatientId == patientId && p.Status == 0)
            .ToListAsync();

        var familyHx = await _context.PatientFamilyHistories
            .AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.PatientId == patientId)
            .ToListAsync();

        var socialHx = await _context.PatientSocialHistories
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.PatientId == patientId)
            .ToListAsync();

        // HISTORICAL block: inactive allergies (no time cutoff — outgrowing
        // a childhood allergy is permanently relevant), discontinued/completed
        // medications in the last 24 months, resolved/inactive problems (PMH).
        var medsCutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-24));

        var inactiveAllergies = await _context.PatientAllergies
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.PatientId == patientId && !a.IsActive)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();

        var discontinuedMeds = await _context.PatientMedications
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.PatientId == patientId
                        && (m.Status == 1 || m.Status == 3)
                        && (m.EndDate == null || m.EndDate >= medsCutoff))
            .OrderByDescending(m => m.EndDate)
            .ToListAsync();

        var resolvedProblems = await _context.PatientProblems
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.PatientId == patientId && p.Status != 0)
            .OrderByDescending(p => p.ResolvedDate)
            .ToListAsync();

        // Format allergies
        result.ActiveAllergies = allergies.Select(a =>
        {
            var severity = a.Severity switch
            {
                0 => "Mild",
                1 => "Moderate",
                2 => "Severe",
                3 => "Life-Threatening",
                _ => ""
            };
            var reaction = !string.IsNullOrEmpty(a.Reaction) ? $" - {a.Reaction}" : "";
            return $"{a.AllergenName} ({severity}{reaction})";
        }).ToList();

        // Format medications
        result.ActiveMedications = medications.Select(m =>
        {
            var dosage = !string.IsNullOrEmpty(m.Dosage) ? $" {m.Dosage}" : "";
            var form = !string.IsNullOrEmpty(m.Form) ? $" {m.Form}" : "";
            var freq = !string.IsNullOrEmpty(m.Frequency) ? $" {m.Frequency}" : "";
            var route = !string.IsNullOrEmpty(m.Route) ? $" ({m.Route})" : "";
            return $"{m.DrugName}{dosage}{form}{freq}{route}".Trim();
        }).ToList();

        // Format problems
        result.ActiveProblems = problems.Select(p =>
        {
            var icd = !string.IsNullOrEmpty(p.IcdCode) ? $" ({p.IcdCode})" : "";
            return $"{p.Description}{icd}";
        }).ToList();

        // Format family history
        result.FamilyHistory = familyHx.Select(f =>
        {
            var age = f.AgeAtOnset.HasValue ? $", onset age {f.AgeAtOnset.Value}" : "";
            var deceased = f.IsDeceased == true ? " (deceased)" : "";
            return $"{f.Relation}: {f.Condition}{age}{deceased}";
        }).ToList();

        // Format social history
        result.SocialHistory = socialHx.Select(s =>
        {
            var status = !string.IsNullOrEmpty(s.Status) ? $" ({s.Status})" : "";
            return $"{s.Category}{status}: {s.Description}";
        }).ToList();

        // ─── HISTORICAL block ───────────────────────────────────────
        // Reason text (DeletedReason isn't applicable here since these are not
        // deleted; for now we surface the Notes / status transition date).
        result.InactiveAllergies = inactiveAllergies.Select(a =>
        {
            var sev = a.Severity switch { 0 => "Mild", 1 => "Moderate", 2 => "Severe", 3 => "Life-Threatening", _ => "" };
            var notes = !string.IsNullOrEmpty(a.Notes) ? $" - {a.Notes}" : "";
            var stamp = a.UpdatedAt.HasValue ? $" (inactive since {a.UpdatedAt.Value:yyyy-MM-dd})" : "";
            return $"{a.AllergenName} ({sev}{notes}){stamp}";
        }).ToList();

        result.DiscontinuedMedications = discontinuedMeds.Select(m =>
        {
            var dosage = !string.IsNullOrEmpty(m.Dosage) ? $" {m.Dosage}" : "";
            var freq = !string.IsNullOrEmpty(m.Frequency) ? $" {m.Frequency}" : "";
            var route = !string.IsNullOrEmpty(m.Route) ? $" ({m.Route})" : "";
            var statusLabel = m.Status switch { 1 => "Discontinued", 3 => "Completed", _ => "Inactive" };
            var endDate = m.EndDate.HasValue ? $" {m.EndDate.Value:yyyy-MM-dd}" : "";
            var reason = !string.IsNullOrEmpty(m.Notes) ? $". Reason: {m.Notes}" : "";
            return $"{m.DrugName}{dosage}{freq}{route}, {statusLabel}{endDate}{reason}";
        }).ToList();

        result.ResolvedProblems = resolvedProblems.Select(p =>
        {
            var icd = !string.IsNullOrEmpty(p.IcdCode) ? $" ({p.IcdCode})" : "";
            var statusLabel = p.Status == 1 ? "Resolved" : "Inactive";
            var resolvedDate = p.ResolvedDate.HasValue ? $" {p.ResolvedDate.Value:yyyy-MM-dd}" : "";
            var notes = !string.IsNullOrEmpty(p.Notes) ? $". {p.Notes}" : "";
            return $"{p.Description}{icd}, {statusLabel}{resolvedDate}{notes}";
        }).ToList();

        return result;
    }

    public async Task<PrefillTemplateResponse> PrefillTemplateAsync(PrefillTemplateRequest request)
    {
        try
        {
            // 1. Get template HTML
            var template = await _context.ClinicalNoteTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TemplateId == request.TemplateId);

            if (template == null || string.IsNullOrEmpty(template.HtmlContent))
            {
                return new PrefillTemplateResponse
                {
                    Success = false,
                    ErrorMessage = "Template not found"
                };
            }

            // 2. Resolve encounterId from appointment if not provided
            var encounterId = request.EncounterId;
            if (!encounterId.HasValue && request.AppointmentId.HasValue)
            {
                var encounter = await _context.Encounters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.AppointmentId == request.AppointmentId.Value);
                encounterId = encounter?.EncounterId;
            }

            // 3. Get encounter context
            var context = await GetEncounterContextAsync(request.PatientId, encounterId);

            // 4. If no meaningful data, return raw template (no need to call Gemini)
            if (!context.HasData)
            {
                return new PrefillTemplateResponse
                {
                    Success = true,
                    PrefilledHtml = template.HtmlContent,
                    FellBackToRawTemplate = true
                };
            }

            // 5. Build Gemini prompt
            var prompt = BuildPrefillPrompt(template.HtmlContent, template.Name, context);

            // 5a. PHI scrub the prompt before it leaves the building.
            //     The encounter's CC/HPI free-text fields can contain provider-typed
            //     patient identifiers; structured fields (vitals, ICD codes, drug
            //     names) won't match the scrubber and pass through unchanged.
            //     Spec: rules/technical/encounter-summary.md
            var patient = await _context.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PatientId == request.PatientId);
            Provider? provider = null;
            if (encounterId.HasValue)
            {
                var enc = await _context.Encounters
                    .AsNoTracking()
                    .Include(e => e.Provider)
                    .FirstOrDefaultAsync(e => e.EncounterId == encounterId.Value);
                provider = enc?.Provider;
            }
            var phi = PhiContext.Build(patient, provider, _encryptionHelper);
            prompt = ClinicalNotePHIScrubber.Scrub(prompt, phi);

            // 6. Call Gemini (low temperature for accurate data placement)
            var response = await _geminiService.GenerateTextAsync(prompt, temperature: 0.3);

            if (!response.Success || string.IsNullOrWhiteSpace(response.Text))
            {
                _logger.LogWarning("Gemini prefill failed: {Error}. Using simple prefill fallback.", response.ErrorMessage);
                return new PrefillTemplateResponse
                {
                    Success = true,
                    PrefilledHtml = SimplePrefill(template.HtmlContent, context, request.PatientId),
                    FellBackToRawTemplate = false
                };
            }

            // 7. Clean up Gemini response (remove markdown code fences if present)
            var prefilledHtml = CleanGeminiHtmlResponse(response.Text);

            return new PrefillTemplateResponse
            {
                Success = true,
                PrefilledHtml = prefilledHtml,
                FellBackToRawTemplate = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pre-filling template {TemplateId} for patient {PatientId}",
                request.TemplateId, request.PatientId);

            // Fallback: try to return raw template with simple prefill
            try
            {
                var fallbackTemplate = await _context.ClinicalNoteTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TemplateId == request.TemplateId);

                // Try to get context for simple prefill
                try
                {
                    int? encId = request.EncounterId;
                    if (!encId.HasValue && request.AppointmentId.HasValue)
                    {
                        var enc = await _context.Encounters
                            .AsNoTracking()
                            .FirstOrDefaultAsync(e => e.AppointmentId == request.AppointmentId.Value);
                        encId = enc?.EncounterId;
                    }
                    var ctx = await GetEncounterContextAsync(request.PatientId, encId);
                    return new PrefillTemplateResponse
                    {
                        Success = true,
                        PrefilledHtml = SimplePrefill(fallbackTemplate?.HtmlContent ?? "", ctx, request.PatientId),
                        FellBackToRawTemplate = false
                    };
                }
                catch
                {
                    return new PrefillTemplateResponse
                    {
                        Success = true,
                        PrefilledHtml = fallbackTemplate?.HtmlContent ?? "",
                        FellBackToRawTemplate = true,
                        ErrorMessage = "Pre-fill failed, loaded raw template"
                    };
                }
            }
            catch
            {
                return new PrefillTemplateResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to load template"
                };
            }
        }
    }

    /// <summary>
    /// Simple non-AI fallback: replace known placeholder patterns in template HTML with encounter data.
    /// Works by finding section labels (e.g., "Chief Complaint:", "Temperature:") and appending data after them.
    /// </summary>
    private string SimplePrefill(string templateHtml, EncounterContextDto context, int patientId)
    {
        if (string.IsNullOrEmpty(templateHtml) || !context.HasData)
            return templateHtml;

        var html = templateHtml;

        // Get patient info for demographics
        try
        {
            var patient = _context.Patients.AsNoTracking()
                .FirstOrDefault(p => p.PatientId == patientId);
            if (patient != null)
            {
                html = ReplaceAfterLabel(html, "Patient Name:", $" {patient.FirstName} {patient.LastName}");
                html = ReplaceAfterLabel(html, "DOB:", $" {patient.DateOfBirth:MM/dd/yyyy}");
                html = ReplaceAfterLabel(html, "MRN:", $" {patient.Mrn}");
            }
        }
        catch { /* ignore demographics errors */ }

        // Fill date and provider
        html = ReplaceAfterLabel(html, "Date:", $" {DateTime.Today:MM/dd/yyyy}");

        // Fill CC / HPI
        if (!string.IsNullOrWhiteSpace(context.ChiefComplaint))
            html = ReplaceAfterLabel(html, "Chief Complaint:", $" {context.ChiefComplaint}");
        if (!string.IsNullOrWhiteSpace(context.HistoryOfPresentIllness))
        {
            html = ReplaceAfterLabel(html, "HPI:", $" {context.HistoryOfPresentIllness}");
            html = ReplaceAfterLabel(html, "History of Present Illness:", $" {context.HistoryOfPresentIllness}");
        }

        // Fill Subjective section with CC + HPI if present
        if (!string.IsNullOrWhiteSpace(context.ChiefComplaint) || !string.IsNullOrWhiteSpace(context.HistoryOfPresentIllness))
        {
            var subjContent = "";
            if (!string.IsNullOrWhiteSpace(context.ChiefComplaint))
                subjContent += $"\n<p><strong>Chief Complaint:</strong> {System.Net.WebUtility.HtmlEncode(context.ChiefComplaint)}</p>";
            if (!string.IsNullOrWhiteSpace(context.HistoryOfPresentIllness))
                subjContent += $"\n<p><strong>HPI:</strong> {System.Net.WebUtility.HtmlEncode(context.HistoryOfPresentIllness)}</p>";
            if (context.ActiveAllergies.Count > 0)
                subjContent += $"\n<p><strong>Allergies:</strong> {System.Net.WebUtility.HtmlEncode(string.Join("; ", context.ActiveAllergies))}</p>";
            if (context.ActiveProblems.Count > 0)
                subjContent += $"\n<p><strong>Active Problems:</strong> {System.Net.WebUtility.HtmlEncode(string.Join("; ", context.ActiveProblems))}</p>";
            if (context.ActiveMedications.Count > 0)
                subjContent += $"\n<p><strong>Current Medications:</strong> {System.Net.WebUtility.HtmlEncode(string.Join("; ", context.ActiveMedications))}</p>";

            // Insert after "Subjective:" heading
            html = InsertAfterSection(html, "Subjective:", subjContent);
        }

        // Fill vitals
        if (context.Vitals != null)
        {
            var v = context.Vitals;
            if (v.Temperature.HasValue)
                html = ReplaceAfterLabel(html, "Temperature:", $" {v.Temperature}°F");
            if (v.HeartRate.HasValue)
                html = ReplaceAfterLabel(html, "Heart rate:", $" {v.HeartRate} bpm");
            if (v.RespiratoryRate.HasValue)
                html = ReplaceAfterLabel(html, "Respiratory rate:", $" {v.RespiratoryRate}/min");
            if (v.BloodPressure != null)
                html = ReplaceAfterLabel(html, "Blood pressure:", $" {v.BloodPressure}");
            if (v.SpO2.HasValue)
                html = ReplaceAfterLabel(html, "Oxygen saturation:", $" {v.SpO2}%");
            if (v.Weight.HasValue)
                html = ReplaceAfterLabel(html, "Weight:", $" {v.Weight} lbs");
            if (v.Height.HasValue)
                html = ReplaceAfterLabel(html, "Height:", $" {v.Height} in");
            if (v.Bmi.HasValue)
                html = ReplaceAfterLabel(html, "BMI:", $" {v.Bmi}");
        }

        // Fill Review of Systems
        if (!string.IsNullOrWhiteSpace(context.ReviewOfSystems))
            html = ReplaceAfterLabel(html, "Review of Systems:", $" {context.ReviewOfSystems}");

        // Fill social/family history in appropriate sections
        if (context.SocialHistory.Count > 0)
            html = ReplaceAfterLabel(html, "Social History:", $" {string.Join("; ", context.SocialHistory)}");
        if (context.FamilyHistory.Count > 0)
            html = ReplaceAfterLabel(html, "Family History:", $" {string.Join("; ", context.FamilyHistory)}");

        return html;
    }

    /// <summary>
    /// Replace content after a label pattern in HTML. Finds "Label:" and appends value.
    /// Handles labels inside HTML tags like &lt;strong&gt;, &lt;b&gt;, list items etc.
    /// </summary>
    private static string ReplaceAfterLabel(string html, string label, string value)
    {
        // Try patterns: plain text, bold, strong, inside list items
        var patterns = new[]
        {
            $"<strong>{label}</strong>",
            $"<b>{label}</b>",
            label
        };

        foreach (var pattern in patterns)
        {
            var idx = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var insertPos = idx + pattern.Length;
                // Check if there's already content (not just whitespace/tags until next section)
                var afterContent = html.Substring(insertPos);
                // Find the next tag or significant content
                var nextTagIdx = afterContent.IndexOf('<');
                var existingContent = nextTagIdx >= 0 ? afterContent.Substring(0, nextTagIdx).Trim() : afterContent.Trim();

                // Only fill if the existing content is empty
                if (string.IsNullOrWhiteSpace(existingContent))
                {
                    html = html.Substring(0, insertPos) + System.Net.WebUtility.HtmlEncode(value) + html.Substring(insertPos);
                }
                return html;
            }
        }
        return html;
    }

    /// <summary>
    /// Insert HTML content after a section heading (e.g., after "Subjective:" bold heading).
    /// </summary>
    private static string InsertAfterSection(string html, string sectionLabel, string contentToInsert)
    {
        // Find the section heading
        var patterns = new[]
        {
            $"<strong>{sectionLabel}</strong>",
            $"<b>{sectionLabel}</b>",
            $"<h3>{sectionLabel}</h3>",
            $"<h2>{sectionLabel}</h2>"
        };

        foreach (var pattern in patterns)
        {
            var idx = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Find the end of the containing paragraph/element
                var afterPattern = idx + pattern.Length;
                var closingTag = html.IndexOf("</p>", afterPattern, StringComparison.OrdinalIgnoreCase);
                if (closingTag >= 0)
                {
                    var insertPos = closingTag + 4; // after </p>
                    html = html.Substring(0, insertPos) + contentToInsert + html.Substring(insertPos);
                }
                return html;
            }
        }
        return html;
    }

    private static string BuildPrefillPrompt(string templateHtml, string templateName, EncounterContextDto context)
    {
        var encounterData = context.ToPromptText();

        return $@"You are a medical documentation assistant. Your task is to pre-fill a clinical note template using encounter data that has already been collected by nursing staff.

TEMPLATE NAME: {templateName}

ENCOUNTER DATA ALREADY COLLECTED:
{encounterData}

TEMPLATE HTML:
{templateHtml}

INSTRUCTIONS:
1. Fill in the template sections using ONLY the encounter data provided above
2. Preserve the EXACT HTML structure and tags of the template — do not add or remove any HTML elements
3. Place data in the appropriate matching sections (e.g., vitals go in Vitals section, CC goes in Chief Complaint section)
4. Leave sections EMPTY if no matching encounter data exists — do NOT invent or fabricate any clinical information
5. Use professional medical documentation style
6. For allergies/medications/problems lists, format them clearly within the existing HTML structure
7. The doctor will review and add their own assessment, plan, and examination findings — do not fill those sections unless the data explicitly contains them
8. Return ONLY the filled-in HTML content, with no additional commentary, explanation, or markdown formatting
9. Do NOT wrap the output in ```html``` code fences

Generate the pre-filled template now:";
    }

    /// <summary>
    /// Clean up Gemini response by removing markdown code fences and extra whitespace.
    /// </summary>
    private static string CleanGeminiHtmlResponse(string response)
    {
        var cleaned = response.Trim();

        // Remove ```html ... ``` wrapping
        if (cleaned.StartsWith("```html", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7).Trim();
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Substring(3).Trim();
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }

        return cleaned;
    }
}
