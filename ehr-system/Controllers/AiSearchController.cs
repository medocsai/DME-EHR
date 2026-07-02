using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using System.Text.Json;

namespace EHR.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiSearchController : ControllerBase
{
    private readonly IGeminiService _gemini;
    private readonly ILogger<AiSearchController> _logger;

    public AiSearchController(IGeminiService gemini, ILogger<AiSearchController> logger)
    {
        _gemini = gemini;
        _logger = logger;
    }

    [HttpGet("drugs/search")]
    public async Task<ActionResult> SearchDrugs([FromQuery] string q, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<object>());

        var prompt = $@"You are a pharmaceutical database. Search for medications matching ""{q}"".
Return a JSON array of up to {limit} drug results. Each object must have exactly these fields:
- BrandName (string): the brand/trade name
- GenericName (string): the generic/chemical name
- Strength (string): dosage strength e.g. ""10mg"", ""500mg/5ml""
- DosageForm (int): 0=Tablet, 1=Capsule, 2=Liquid, 3=Injection, 4=Cream, 5=Ointment, 6=Patch, 7=Inhaler, 8=Drops, 9=Suppository, 10=Powder
- Route (int): 0=Oral, 1=Topical, 2=Subcutaneous, 3=Intramuscular, 4=Intravenous, 5=Inhaled, 6=Rectal, 7=Ophthalmic, 8=Otic, 9=Nasal, 10=Transdermal
- DEASchedule (int or null): null if not controlled, 2-5 for schedule
- CommonDirections (string): typical prescribing directions
- Warnings (string or null): important warnings if any

Return ONLY the JSON array, no markdown, no explanation. If no matches, return [].";

        try
        {
            var response = await _gemini.GenerateTextAsync(prompt, temperature: 0.1);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Text))
                return Ok(new List<object>());

            var text = response.Text.Trim();
            // Strip markdown code fences if present
            if (text.StartsWith("```"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0) text = text[(firstNewline + 1)..];
                if (text.EndsWith("```")) text = text[..^3].Trim();
            }

            var drugs = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(text);
            if (drugs == null) return Ok(new List<object>());

            // Map to expected DTO shape with generated IDs
            var results = drugs.Select((d, i) => new
            {
                DrugId = -(i + 1), // Negative IDs to distinguish from DB drugs
                NDCCode = "",
                BrandName = GetString(d, "BrandName"),
                GenericName = GetString(d, "GenericName"),
                Strength = GetString(d, "Strength"),
                DosageForm = GetInt(d, "DosageForm"),
                DosageFormName = GetDosageFormName(GetInt(d, "DosageForm")),
                Route = GetInt(d, "Route"),
                RouteName = GetRouteName(GetInt(d, "Route")),
                DEASchedule = GetNullableInt(d, "DEASchedule"),
                CommonDirections = GetString(d, "CommonDirections"),
                Warnings = GetString(d, "Warnings"),
                DisplayName = $"{GetString(d, "BrandName")} ({GetString(d, "GenericName")}) {GetString(d, "Strength")}",
                IsAiResult = true
            }).ToList();

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI drug search failed for query: {Query}", q);
            return Ok(new List<object>());
        }
    }

    [HttpGet("pharmacies/search")]
    public async Task<ActionResult> SearchPharmacies([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<object>());

        var prompt = $@"You are a US pharmacy database. Search for pharmacies matching ""{q}"".
Return a JSON array of up to 10 pharmacy results. Each object must have exactly these fields:
- Name (string): pharmacy name
- Address (string): street address
- City (string): city
- State (string): 2-letter state code
- Zip (string): zip code
- Phone (string): phone number in (XXX) XXX-XXXX format
- Fax (string or null): fax number

Return ONLY the JSON array, no markdown, no explanation. Include common pharmacy chains and local pharmacies. If no matches, return [].";

        try
        {
            var response = await _gemini.GenerateTextAsync(prompt, temperature: 0.1);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Text))
                return Ok(new List<object>());

            var text = response.Text.Trim();
            if (text.StartsWith("```"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0) text = text[(firstNewline + 1)..];
                if (text.EndsWith("```")) text = text[..^3].Trim();
            }

            var pharmacies = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(text);
            if (pharmacies == null) return Ok(new List<object>());

            var results = pharmacies.Select((p, i) => new
            {
                PharmacyId = -(i + 1),
                Name = GetString(p, "Name"),
                NCPDP = "",
                NPI = "",
                Address = GetString(p, "Address"),
                City = GetString(p, "City"),
                State = GetString(p, "State"),
                Zip = GetString(p, "Zip"),
                Phone = GetString(p, "Phone"),
                Fax = GetString(p, "Fax"),
                FullAddress = $"{GetString(p, "Address")}, {GetString(p, "City")}, {GetString(p, "State")} {GetString(p, "Zip")}",
                IsActive = true,
                IsAiResult = true
            }).ToList();

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI pharmacy search failed for query: {Query}", q);
            return Ok(new List<object>());
        }
    }

    [HttpPost("drugs/suggest")]
    public async Task<ActionResult> SuggestDrugs([FromBody] DiagnosisSuggestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ChiefComplaint) && string.IsNullOrWhiteSpace(request.HPI))
            return Ok(new List<object>());

        // Regex-only PHI scrub. This endpoint has no patient context, so we
        // pass an empty PhiContext — the scrubber skips targeted name/MRN
        // replacement and applies only its regex patterns (phone, SSN,
        // MRN format, email). Names typed inline are not caught here.
        var ccScrubbed = ClinicalNotePHIScrubber.Scrub(request.ChiefComplaint, new PhiContext());
        var hpiScrubbed = ClinicalNotePHIScrubber.Scrub(request.HPI, new PhiContext());

        var prompt = $@"You are an experienced physician assistant for medication recommendations. Based on the following clinical information, suggest commonly prescribed medications.

Chief Complaint: {(string.IsNullOrWhiteSpace(ccScrubbed) ? "Not provided" : ccScrubbed)}
History of Present Illness: {(string.IsNullOrWhiteSpace(hpiScrubbed) ? "Not provided" : hpiScrubbed)}

Return a JSON array of up to 15 medications commonly prescribed for this presentation. Include first-line treatments, alternatives, and supportive medications. Each object must have exactly these fields:
- BrandName (string): the brand/trade name
- GenericName (string): the generic/chemical name
- Strength (string): most common dosage strength e.g. ""10mg"", ""500mg""
- DosageForm (int): 0=Tablet, 1=Capsule, 2=Liquid, 3=Injection, 4=Cream, 5=Ointment, 6=Patch, 7=Inhaler, 8=Drops, 9=Suppository, 10=Powder
- Route (int): 0=Oral, 1=Topical, 2=Subcutaneous, 3=Intramuscular, 4=Intravenous, 5=Inhaled, 6=Rectal, 7=Ophthalmic, 8=Otic, 9=Nasal, 10=Transdermal
- DEASchedule (int or null): null if not controlled, 2-5 for schedule
- CommonDirections (string): typical prescribing directions
- Warnings (string or null): important warnings if any
- Rationale (string): brief reason why this drug is suggested for this presentation

Return ONLY the JSON array, no markdown, no explanation. Order by most commonly prescribed first.";

        try
        {
            var response = await _gemini.GenerateTextAsync(prompt, temperature: 0.2);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Text))
                return Ok(new List<object>());

            var text = response.Text.Trim();
            if (text.StartsWith("```"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0) text = text[(firstNewline + 1)..];
                if (text.EndsWith("```")) text = text[..^3].Trim();
            }

            var drugs = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(text);
            if (drugs == null) return Ok(new List<object>());

            var results = drugs.Select((d, i) => new
            {
                DrugId = -(i + 1),
                NDCCode = "",
                BrandName = GetString(d, "BrandName"),
                GenericName = GetString(d, "GenericName"),
                Strength = GetString(d, "Strength"),
                DosageForm = GetInt(d, "DosageForm"),
                DosageFormName = GetDosageFormName(GetInt(d, "DosageForm")),
                Route = GetInt(d, "Route"),
                RouteName = GetRouteName(GetInt(d, "Route")),
                DEASchedule = GetNullableInt(d, "DEASchedule"),
                CommonDirections = GetString(d, "CommonDirections"),
                Warnings = GetString(d, "Warnings"),
                Rationale = GetString(d, "Rationale"),
                DisplayName = $"{GetString(d, "BrandName")} ({GetString(d, "GenericName")}) {GetString(d, "Strength")}",
                IsAiResult = true
            }).ToList();

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI drug suggestion failed");
            return Ok(new List<object>());
        }
    }

    [HttpPost("diagnosis/suggest")]
    public async Task<ActionResult> SuggestDiagnosis([FromBody] DiagnosisSuggestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ChiefComplaint) && string.IsNullOrWhiteSpace(request.HPI))
            return Ok(new List<object>());

        // Regex-only PHI scrub (no patient context on this endpoint).
        var ccScrubbed = ClinicalNotePHIScrubber.Scrub(request.ChiefComplaint, new PhiContext());
        var hpiScrubbed = ClinicalNotePHIScrubber.Scrub(request.HPI, new PhiContext());

        var prompt = $@"You are a medical coding assistant. Based on the following clinical information, suggest the most likely ICD-10 diagnosis codes.

Chief Complaint: {(string.IsNullOrWhiteSpace(ccScrubbed) ? "Not provided" : ccScrubbed)}
History of Present Illness: {(string.IsNullOrWhiteSpace(hpiScrubbed) ? "Not provided" : hpiScrubbed)}

Return a JSON array of up to 15 ICD-10 diagnosis codes, including primary diagnoses and related/differential diagnoses. Each object must have:
- Code (string): ICD-10 code (e.g. ""E11.9"", ""J06.9"")
- Description (string): short description of the diagnosis

Return ONLY the JSON array, no markdown, no explanation. Order by most likely first.";

        try
        {
            var response = await _gemini.GenerateTextAsync(prompt, temperature: 0.1);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Text))
                return Ok(new List<object>());

            var text = response.Text.Trim();
            if (text.StartsWith("```"))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0) text = text[(firstNewline + 1)..];
                if (text.EndsWith("```")) text = text[..^3].Trim();
            }

            var codes = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(text);
            if (codes == null) return Ok(new List<object>());

            var results = codes.Select(c => new
            {
                Code = GetString(c, "Code"),
                Description = GetString(c, "Description")
            }).ToList();

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI diagnosis suggestion failed");
            return Ok(new List<object>());
        }
    }

    // Helper methods for safe JSON element extraction
    private static string GetString(Dictionary<string, JsonElement> dict, string key)
    {
        if (dict.TryGetValue(key, out var val))
        {
            return val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
        }
        return "";
    }

    private static int GetInt(Dictionary<string, JsonElement> dict, string key)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
            if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out var i)) return i;
        }
        return 0;
    }

    private static int? GetNullableInt(Dictionary<string, JsonElement> dict, string key)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.Null) return null;
            if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
            if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out var i)) return i;
        }
        return null;
    }

    private static string GetDosageFormName(int form) => form switch
    {
        0 => "Tablet", 1 => "Capsule", 2 => "Liquid", 3 => "Injection",
        4 => "Cream", 5 => "Ointment", 6 => "Patch", 7 => "Inhaler",
        8 => "Drops", 9 => "Suppository", 10 => "Powder", _ => "Other"
    };

    private static string GetRouteName(int route) => route switch
    {
        0 => "Oral", 1 => "Topical", 2 => "Subcutaneous", 3 => "Intramuscular",
        4 => "Intravenous", 5 => "Inhaled", 6 => "Rectal", 7 => "Ophthalmic",
        8 => "Otic", 9 => "Nasal", 10 => "Transdermal", _ => "Other"
    };
}

public class DiagnosisSuggestRequest
{
    public string? ChiefComplaint { get; set; }
    public string? HPI { get; set; }
}
