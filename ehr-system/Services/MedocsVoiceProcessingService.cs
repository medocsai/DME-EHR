using System.Diagnostics;
using EHR.Models.DTOs;

namespace EHR.Services;

public interface IMedocsVoiceProcessingService
{
    Task<MedocsVoiceResultDto> ProcessChunkAsync(
        Stream audioStream,
        string contentType,
        string originalFileName,
        string tabKey,
        decimal durationSeconds,
        string? previousContext,
        int patientId,
        int encounterId,
        int userId);

    /// <summary>
    /// Post-call extraction: processes the FULL accumulated telehealth transcription
    /// to extract all clinical sections (vitals, history, CC/HPI) at once.
    /// Called after the telehealth call ends, with the complete conversation text.
    /// </summary>
    Task<MedocsVoiceResultDto> ExtractFromFullTranscriptionAsync(string fullTranscription);

    /// <summary>
    /// Unified voice extraction: processes accumulated in-person encounter transcription
    /// to extract all clinical sections with full conversation context.
    /// </summary>
    Task<MedocsVoiceResultDto> ExtractFromUnifiedTranscriptionAsync(string fullTranscription, string? existingData = null);
}

public class MedocsVoiceProcessingService : IMedocsVoiceProcessingService
{
    private readonly IGeminiService _geminiService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MedocsVoiceProcessingService> _logger;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly string _tempPath;
    private readonly string _ffmpegPath;

    private static readonly HashSet<string> ValidTabKeys = new() { "vitals", "history", "cc-hpi", "telehealth", "unified" };

    public MedocsVoiceProcessingService(
        IGeminiService geminiService,
        IConfiguration configuration,
        ILogger<MedocsVoiceProcessingService> logger,
        IWebHostEnvironment webHostEnvironment)
    {
        _geminiService = geminiService;
        _configuration = configuration;
        _logger = logger;
        _webHostEnvironment = webHostEnvironment;

        var configuredTempPath = configuration["AudioStorage:TempPath"];
        var configuredFfmpegPath = configuration["AudioStorage:FfmpegPath"];

        _tempPath = ResolveToAbsolutePath(configuredTempPath, webHostEnvironment.ContentRootPath)
            ?? Path.Combine(Path.GetTempPath(), "PTEHR_Audio");
        _ffmpegPath = ResolveToAbsolutePath(configuredFfmpegPath, webHostEnvironment.ContentRootPath)
            ?? FindFfmpegPath();

        Directory.CreateDirectory(_tempPath);
    }

    public async Task<MedocsVoiceResultDto> ProcessChunkAsync(
        Stream audioStream,
        string contentType,
        string originalFileName,
        string tabKey,
        decimal durationSeconds,
        string? previousContext,
        int patientId,
        int encounterId,
        int userId)
    {
        if (!ValidTabKeys.Contains(tabKey))
        {
            return new MedocsVoiceResultDto
            {
                Success = false,
                Message = $"Invalid tab key: {tabKey}"
            };
        }

        string? tempInputPath = null;
        string? tempWavPath = null;

        try
        {
            // Step 1: Save audio to temp file
            var fileExtension = GetFileExtension(contentType, originalFileName);
            tempInputPath = Path.Combine(_tempPath, $"medocs_{Guid.NewGuid():N}{fileExtension}");

            await using (var fileStream = new FileStream(tempInputPath, FileMode.Create))
            {
                await audioStream.CopyToAsync(fileStream);
            }

            _logger.LogInformation(
                "[MedocsVoice] Chunk received: Tab={TabKey}, Patient={PatientId}, Size={Size}bytes",
                tabKey, patientId, new FileInfo(tempInputPath).Length);

            // Step 2: Convert to WAV
            tempWavPath = await ConvertToWavAsync(tempInputPath);

            // Step 3: Upload to Gemini and transcribe
            var fileUri = await _geminiService.UploadFileAsync(tempWavPath, "audio/wav");
            if (string.IsNullOrEmpty(fileUri))
            {
                return new MedocsVoiceResultDto
                {
                    Success = false,
                    Message = "Failed to upload audio to transcription service"
                };
            }

            var transcriptionResponse = await _geminiService.TranscribeAudioAsync(fileUri, "audio/wav");
            if (!transcriptionResponse.Success)
            {
                return new MedocsVoiceResultDto
                {
                    Success = false,
                    Message = $"Transcription failed: {transcriptionResponse.ErrorMessage}"
                };
            }

            var transcription = transcriptionResponse.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(transcription))
            {
                return new MedocsVoiceResultDto
                {
                    Success = true,
                    Message = "No speech detected in audio",
                    Transcription = "",
                    ExtractedData = null,
                    TabKey = tabKey
                };
            }

            _logger.LogInformation(
                "[MedocsVoice] Transcription received: Tab={TabKey}, Length={Length}",
                tabKey, transcription.Length);

            // For telehealth and unified, skip per-chunk extraction — just return raw transcription.
            // Full extraction happens periodically or on finalize with the COMPLETE accumulated transcription.
            if (tabKey == "telehealth" || tabKey == "unified")
            {
                return new MedocsVoiceResultDto
                {
                    Success = true,
                    Message = $"Chunk transcribed ({tabKey} — extraction deferred)",
                    Transcription = transcription,
                    ExtractedData = null,
                    TabKey = tabKey
                };
            }

            // Step 4: Build tab-specific extraction prompt and call Gemini
            var extractionPrompt = BuildExtractionPrompt(tabKey, transcription, previousContext);
            var extractionResponse = await _geminiService.GenerateTextAsync(extractionPrompt, 0.1);

            if (!extractionResponse.Success)
            {
                // Return transcription even if extraction fails
                return new MedocsVoiceResultDto
                {
                    Success = true,
                    Message = "Transcription succeeded but data extraction failed",
                    Transcription = transcription,
                    ExtractedData = null,
                    TabKey = tabKey
                };
            }

            // Clean markdown code fences if present
            var extractedJson = CleanJsonResponse(extractionResponse.Text ?? "");

            return new MedocsVoiceResultDto
            {
                Success = true,
                Message = "Chunk processed successfully",
                Transcription = transcription,
                ExtractedData = extractedJson,
                TabKey = tabKey
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MedocsVoice] Error processing chunk for tab={TabKey}", tabKey);
            return new MedocsVoiceResultDto
            {
                Success = false,
                Message = "An error occurred processing the audio chunk"
            };
        }
        finally
        {
            // Cleanup temp files
            TryDeleteFile(tempInputPath);
            TryDeleteFile(tempWavPath);
        }
    }

    private string BuildExtractionPrompt(string tabKey, string transcription, string? previousContext)
    {
        var contextBlock = "";
        if (!string.IsNullOrWhiteSpace(previousContext))
        {
            // Tab-specific context instructions to prevent history duplicates
            var contextInstruction = tabKey switch
            {
                "vitals" => @"The current transcription is a continuation. For numeric vital fields (BP, HR, etc.), use the latest mentioned value. For the ""notes"" field, you MUST produce a CUMULATIVE summary that includes ALL observations and commentary from BOTH the previous context AND the current transcription — never discard earlier notes.",
                "history" => @"The current transcription is a continuation. CRITICAL: Only extract items that are NEW in the current transcription segment. Do NOT repeat or re-extract any allergies, medications, problems, family history, social history, or immunizations that were already mentioned in the PREVIOUS CONTEXT. If the current segment only repeats previously mentioned items, return empty arrays for all sections.",
                "cc-hpi" => @"The current transcription is a continuation. Merge the new information with the previous context to provide a comprehensive, updated result that incorporates all details from the full conversation.",
                "telehealth" => @"The current transcription is a continuation of a telehealth visit between a provider and patient. For vitals: use the latest mentioned value for each numeric field; for notes produce a CUMULATIVE summary of ALL clinically relevant observations. For history: ONLY extract items that are NEW — do NOT repeat items from previous context. For CC/HPI: merge new information with previous context for a comprehensive updated result.",
                _ => ""
            };

            contextBlock = $@"
PREVIOUS CONTEXT (from earlier segments of this conversation):
{previousContext}

{contextInstruction}

";
        }

        return tabKey switch
        {
            "vitals" => BuildVitalsPrompt(transcription, contextBlock),
            "history" => BuildHistoryPrompt(transcription, contextBlock),
            "cc-hpi" => BuildCcHpiPrompt(transcription, contextBlock),
            "telehealth" => BuildTelehealthPrompt(transcription, contextBlock),
            "unified" => BuildUnifiedPrompt(transcription, contextBlock),
            _ => throw new ArgumentException($"Invalid tab key: {tabKey}")
        };
    }

    private static string BuildVitalsPrompt(string transcription, string contextBlock)
    {
        return $@"You are a board-certified medical scribe AI named MEDOCS AI generating LEGAL clinical documentation for an internal medicine practice.

The following is a transcription from a healthcare provider during a patient encounter. The provider is speaking naturally while taking vital signs. The audio may include casual conversation, small talk, greetings, or chit-chat that is NOT clinically relevant.

LANGUAGE: The conversation may be in ANY language or a mix of languages (e.g., English, Spanish, Urdu, Arabic). Always extract and output ALL values in English only.

TRANSCRIPTION:
{transcription}

{contextBlock}Extract vital signs mentioned. Return ONLY a JSON object with these fields (use null for any not mentioned):
- ""systolicBp"" (integer, mmHg)
- ""diastolicBp"" (integer, mmHg)
- ""heartRate"" (integer, bpm)
- ""temperature"" (number, Fahrenheit — convert from Celsius if needed)
- ""spO2"" (number, percentage)
- ""respiratoryRate"" (integer, per minute)
- ""weight"" (number, pounds — convert from kg if needed: 1kg = 2.205lbs)
- ""height"" (number, inches — convert from cm if needed: 1cm = 0.3937in)
- ""notes"" (string — ONLY include clinically relevant observations about the patient's health. This is a cumulative note covering the entire conversation. null if no clinically relevant observations exist.)

VITAL SIGN EXTRACTION RULES — critical:
1. Only return a vital value if it is a NEW measurement or a CORRECTED reading in this transcription segment.
2. CORRECTIONS: If the speaker clearly corrects a previous reading (""actually it's..."", ""sorry, it's..."", ""let me redo that..."", ""wait, that's...""), return the CORRECTED value.
3. CASUAL NUMBER MENTIONS: Do NOT extract numbers from non-clinical context. ""I've been waiting 120 minutes"" is NOT a systolic BP. ""It's 98 degrees outside"" is NOT a temperature. Only extract values that are explicitly stated as vital sign readings.
4. REPEATED MENTIONS: If this segment simply re-states the same values already in PREVIOUS CONTEXT without correcting them, return null for those fields — they are already recorded.

FILTERING RULES — this is critical:
1. INCLUDE in notes: abnormal vital readings, patient-reported symptoms (headache, dizziness, pain, nausea, etc.), medication issues (ran out, non-compliance, side effects), clinical concerns, relevant medical observations.
2. EXCLUDE from notes: casual conversation, small talk, greetings, comments about the weather, jokes, talk about work schedules, how busy the day is, weekend plans, non-medical chatter, pleasantries. These are NEVER clinically relevant.
3. If the ONLY speech in the transcription is casual/non-clinical conversation, return ALL fields as null. Do NOT fabricate clinical observations from non-medical talk.
4. The ""notes"" field must be cumulative — include all clinically relevant observations from previous context AND current segment merged together. But NEVER include non-clinical content.

STRICT MEDICAL TERMINOLOGY — this is LEGAL documentation:
- Convert ALL clinical observations into formal medical terminology. ""blood pressure is really high"" → ""Elevated blood pressure noted."" ""feeling dizzy"" → ""Reports vertigo."" ""can't sleep"" → ""Reports insomnia.""
- But ""it's been a crazy day at work"" is NOT clinical and must be ignored entirely.
- NEVER use casual or layperson language in the notes field.

Return ONLY valid JSON, no markdown code fences, no explanation.";
    }

    private static string BuildHistoryPrompt(string transcription, string contextBlock)
    {
        return $@"You are a board-certified medical scribe AI named MEDOCS AI generating LEGAL clinical documentation for an internal medicine practice.

The following is a transcription from a healthcare provider reviewing or updating patient medical history.

LANGUAGE: The conversation may be in ANY language or a mix of languages (e.g., English, Spanish, Urdu, Arabic). Always extract and output ALL values in English only.

TRANSCRIPTION:
{transcription}

{contextBlock}Extract medical history items mentioned. Return ONLY a JSON object with these arrays (use empty array [] for sections not mentioned):

- ""allergies"": [{{ ""allergenName"": string (the allergen name), ""notes"": string or null (all details: type, reaction, severity in free text, e.g. ""Drug allergy, causes rash, moderate severity"") }}]
- ""medications"": [{{ ""drugName"": string (the EXACT brand/product name the clinician spoke, NOT a generic formula), ""notes"": string or null (all details: dosage, frequency, route, form in free text, e.g. ""10mg, oral, once daily, tablet"") }}]
- ""problems"": [{{ ""description"": string (condition name using medical terminology), ""notes"": string or null (patient's own words about duration/onset/severity, e.g. ""for 3 days"", ""started last week"", ""getting worse"". Leave empty or null if no extra context mentioned.) }}]
- ""familyHx"": [{{ ""condition"": string (the condition), ""notes"": string or null (relation, age, deceased status, e.g. ""Mother, age 55, deceased"") }}]
- ""socialHx"": [{{ ""category"": string (e.g. ""Tobacco Use"", ""Alcohol Use"", ""Drug Use"", ""Exercise"", ""Diet"", ""Occupation"", ""Sexual Activity""), ""notes"": string or null (all details in free text, e.g. ""Never smoked, denies tobacco use"") }}]
- ""immunizations"": [{{ ""vaccineName"": string (vaccine name), ""notes"": string or null (date, lot#, manufacturer, site in free text, e.g. ""Given today, left arm, Lot# ABC123"") }}]

STRICT MEDICAL TERMINOLOGY — this is LEGAL documentation:
- Convert problem descriptions, condition names, and allergy names into formal medical terminology.
- Examples: ""bad headaches"" → ""Severe cephalgia"", ""sugar problem"" → ""Diabetes mellitus"", ""high blood pressure"" → ""Hypertension"", ""trouble breathing"" → ""Dyspnea"".
- Problem ""notes"" field is an exception: keep the patient's own words in plain language (e.g. ""for 3 days"").

MEDICATION NAME RULE — CRITICAL:
- Output the EXACT brand or product name the clinician spoke. Do NOT convert brand names to generic chemical formulas (keep ""Panadol"" as ""Panadol""; do NOT change it to ""Acetaminophen"" or ""Paracetamol"").
- Only fix obvious speech-recognition misspellings by matching phonetically to the closest recognized drug, e.g. ""padol"" → ""Panadol"", ""metforming"" → ""Metformin"", ""a moxicillin"" → ""Amoxicillin"", ""lip itor"" → ""Lipitor"", ""lie-sin-o-pril"" → ""Lisinopril"".
- When the spoken name is already recognizable as a real drug, output it EXACTLY as spoken — do not substitute, translate, or standardize to its generic.

NOTES FIELD RULE — critical:
- For ALL notes fields: return null if the transcription does NOT contain specific information about that item.
- NEVER use placeholder text like ""Unknown"", ""N/A"", ""Not mentioned"", ""Not specified"", or ""Not provided"" in notes fields.
- Only populate notes with ACTUAL information spoken in the transcription.

DEDUPLICATION RULE — critical:
- If PREVIOUS CONTEXT is provided above, carefully review it.
- Do NOT include any item that was already extracted in a previous segment.
- Only return items that are genuinely NEW in the current transcription.
- For medications: if the transcription mentions a drug that matches an existing medication (even with different spelling due to speech recognition), it is NOT new — skip it.
- Return empty arrays [] for any section where no new items are found in the current transcription.

PROTECTING EXISTING DATA:
- Items in PREVIOUS CONTEXT represent data already saved. NEVER remove or modify them.
- Only ADD genuinely new items from the current transcription segment.

Return ONLY valid JSON, no markdown code fences, no explanation.";
    }

    private static string BuildCcHpiPrompt(string transcription, string contextBlock)
    {
        return $@"You are a board-certified medical scribe AI named MEDOCS AI generating LEGAL clinical documentation for an internal medicine practice.

The following is a transcription from a healthcare provider describing a patient's chief complaint and history of present illness.

LANGUAGE: The conversation may be in ANY language or a mix of languages (e.g., English, Spanish, Urdu, Arabic). Always extract and output ALL values in English only.

TRANSCRIPTION:
{transcription}

{contextBlock}Extract the chief complaint and HPI narrative. Return ONLY a JSON object with:
- ""chiefComplaint"" (string): A concise clinical statement of the primary reason for the visit (max 500 characters). Must use formal medical terminology, e.g. ""Acute onset substernal chest pain with associated dyspnea"" not ""chest pain and trouble breathing"".
- ""hpiNarrative"" (string): A detailed HPI narrative written in professional medical prose using proper clinical terminology. Cover OLDCARTS elements as available: Onset, Location, Duration, Character, Aggravating/Alleviating factors, Radiation, Timing, Severity. Write as it would appear in an official medical record.

STRICT MEDICAL TERMINOLOGY — this is LEGAL documentation:
- The speaker may use casual, conversational, or layperson language. You MUST convert ALL output into formal medical documentation language.
- Examples: ""really bad headache for a week"" → ""Patient reports severe cephalgia with onset approximately one week prior to presentation."" ""stomach hurts after eating"" → ""Patient reports postprandial epigastric pain."" ""can't breathe good"" → ""Patient reports dyspnea.""
- NEVER use casual or layperson language in any output field.
- Write in third-person clinical prose (""Patient reports..."", ""Patient presents with..."").

If previous context exists, merge the new information with it to provide a comprehensive, updated result that incorporates all details from the full conversation.

Return ONLY valid JSON, no markdown code fences, no explanation.";
    }

    private static string BuildTelehealthPrompt(string transcription, string contextBlock)
    {
        return $@"You are a board-certified medical scribe AI named MEDOCS AI generating LEGAL clinical documentation for an internal medicine practice.

The following is the COMPLETE transcription from a TELEHEALTH VIDEO VISIT between a healthcare provider and a patient. Both the provider's voice and the patient's voice are captured. The conversation may flow naturally between vital signs discussion, medical history, symptoms, and the chief complaint — all in a single continuous dialogue.

IMPORTANT: This is the FULL conversation transcription (not a small chunk). You have the COMPLETE context of the entire visit. Use the full context to accurately distinguish clinical conversation from casual small talk.

LANGUAGE: The conversation may be in ANY language or a mix of languages (e.g., English, Spanish, Urdu, Arabic). Always extract and output ALL values in English only.

TRANSCRIPTION:
{transcription}

{contextBlock}Extract ALL clinical information mentioned into a single JSON object with five top-level sections: ""vitals"", ""history"", ""ccHpi"", ""orders"", and ""prescriptions"". Use null or empty arrays for sections where nothing is mentioned.

Return ONLY a JSON object with this exact structure:
{{
  ""vitals"": {{
    ""systolicBp"": integer or null (mmHg),
    ""diastolicBp"": integer or null (mmHg),
    ""heartRate"": integer or null (bpm),
    ""temperature"": number or null (Fahrenheit — convert from Celsius if needed),
    ""spO2"": number or null (percentage),
    ""respiratoryRate"": integer or null (per minute),
    ""weight"": number or null (pounds — convert from kg if needed: 1kg = 2.205lbs),
    ""height"": number or null (inches — convert from cm if needed: 1cm = 0.3937in),
    ""notes"": string or null (clinically relevant observations ONLY — exclude all casual conversation)
  }},
  ""history"": {{
    ""allergies"": [{{ ""allergenName"": string, ""notes"": string or null (type, reaction, severity in free text) }}],
    ""medications"": [{{ ""drugName"": string (EXACT brand/product name spoken, NOT generic formula), ""notes"": string or null (dosage, frequency, route, form in free text) }}],
    ""problems"": [{{ ""description"": string, ""notes"": string or null (patient's own words about duration/onset, e.g. ""for 3 days"". Leave empty or null if no context.) }}],
    ""familyHx"": [{{ ""condition"": string, ""notes"": string or null (relation, age, deceased status in free text) }}],
    ""socialHx"": [{{ ""category"": string (e.g. ""Tobacco Use"", ""Alcohol Use"", ""Drug Use"", ""Exercise"", ""Diet"", ""Occupation"", ""Sexual Activity""), ""notes"": string or null (details in free text) }}],
    ""immunizations"": [{{ ""vaccineName"": string, ""notes"": string or null (date, lot#, manufacturer, site in free text) }}]
  }},
  ""ccHpi"": {{
    ""chiefComplaint"": string or null (concise clinical statement, max 500 chars, professional medical terminology),
    ""hpiNarrative"": string or null (detailed HPI narrative using OLDCARTS elements, professional medical prose)
  }},
  ""orders"": [{{
    ""orderType"": 0|1|2 (0=Lab, 1=Imaging, 2=Referral),
    ""priority"": 0|1|2 (0=Routine, 1=Urgent, 2=STAT),
    ""diagnosisCode"": string or null (ICD-10 code),
    ""clinicalIndication"": string (reason for order),
    ""notes"": string or null,
    ""labPanelName"": string or null (for labs: e.g. ""CBC"", ""BMP"", ""Lipid Panel"", ""HbA1c"", ""TSH"", ""Urinalysis""),
    ""fastingRequired"": boolean or null (for labs),
    ""specimenType"": string or null (for labs: e.g. ""Blood"", ""Urine""),
    ""modality"": string or null (for imaging: e.g. ""X-Ray"", ""CT"", ""MRI"", ""Ultrasound""),
    ""bodyPart"": string or null (for imaging: e.g. ""Chest"", ""Abdomen"", ""Knee""),
    ""contrastRequired"": boolean or null (for imaging),
    ""referralSpecialty"": string or null (for referrals: e.g. ""Cardiology"", ""Orthopedics""),
    ""referralReason"": string or null (for referrals),
    ""referralUrgency"": ""Routine""|""Urgent""|""Emergent"" or null (for referrals)
  }}],
  ""prescriptions"": [{{
    ""drugName"": string (EXACT brand/product name spoken, NOT generic formula),
    ""strength"": string or null (e.g. ""500mg"", ""10mg/5ml""),
    ""dosageForm"": string or null (e.g. ""Tablet"", ""Capsule"", ""Solution"", ""Cream""),
    ""quantity"": integer or null,
    ""daysSupply"": integer or null,
    ""doseAmount"": string or null (e.g. ""1"", ""2"", ""0.5""),
    ""doseUnit"": string or null (e.g. ""tablet"", ""ml"", ""puff""),
    ""route"": ""Oral""|""Topical""|""SC""|""IM""|""IV""|""Inhaled""|""Rectal""|""Ophthalmic"" or null,
    ""frequency"": string or null (e.g. ""Once daily"", ""Twice daily"", ""Every 8 hours"", ""As needed""),
    ""directionsFreeText"": string or null (complete sig line, e.g. ""Take 1 tablet by mouth twice daily with food""),
    ""refills"": integer or null (0 if not mentioned),
    ""diagnosisCode"": string or null (ICD-10 code for indication),
    ""notes"": string or null
  }}]
}}

CRITICAL RULES:
1. This is a two-party telehealth conversation. The provider asks questions and the patient responds. Extract clinical data from BOTH speakers.
2. STRICT MEDICAL TERMINOLOGY — this is LEGAL documentation: Convert ALL output to formal medical terminology. ""really bad headache"" → ""Severe cephalgia"". ""sugar problem"" → ""Diabetes mellitus"". ""blood pressure is high"" → ""Hypertension"". ""can't sleep"" → ""Insomnia"". ""stomach hurts"" → ""Abdominal pain"". NEVER use casual or layperson language in any output field. EXCEPTION: the problem ""notes"" field stays in plain language (e.g. ""Fever for 3 days"") — do NOT medicalize it.
3. AGGRESSIVE FILTERING — this is the most important rule:
   - ONLY extract information that is genuinely about the patient's health, symptoms, medications, diagnoses, or medical history.
   - COMPLETELY IGNORE all casual conversation, small talk, pleasantries, greetings, discussing personal matters (car repairs, weather, sports, work schedules, family non-medical events, etc.).
   - If the patient or provider mentions non-medical topics (e.g. vehicle problems, home repairs, shopping, food preferences unrelated to medical diet), do NOT extract these as problems, symptoms, or history items.
   - Speech recognition errors that produce non-medical words (e.g. ""ignition coil"", ""car repair"", ""vehicle malfunction"") are NEVER medical problems — ignore them completely.
4. DEDUPLICATION for medications: If the same medication is mentioned multiple times in the conversation (even with different phrasing or speech recognition spellings), include it ONLY ONCE. Deduplicate by drug name.
5. DEDUPLICATION for problems: Include each distinct medical problem only once. Consolidate related mentions.
6. PROBLEMS LIST: The patient's presenting symptoms and chief complaint (e.g. Fever, Headache, Cough, Back pain) MUST also be added to the problems array using proper medical terminology. Active symptoms are active problems. The ICD-10 code is extracted into a SEPARATE structured field (NOT into the notes field).
7. PROBLEM NOTES: The ""notes"" field on a problem is for patient's own words about duration or onset (e.g. ""for 3 days"", ""started last week""). Leave empty or null if no extra context was mentioned. Do not put diagnosis codes in notes.
8. CC/HPI: Synthesize the COMPLETE conversation into a coherent, professional clinical narrative. Focus on why the patient is being seen and their symptom progression.
9. ORDERS: Extract ONLY orders that the provider EXPLICITLY and AFFIRMATIVELY states they are ordering RIGHT NOW. Examples of real orders: ""let's order a CBC"", ""I'm going to send you for an X-ray"", ""I'd like to refer you to cardiology"". Do NOT create orders for: (a) tests discussed hypothetically, (b) tests the provider says are NOT needed, deferred, or can wait (e.g. ""not required now"", ""we can do that later"", ""leave it for now"", ""skip that"", ""hold off on"", ""let's not do that yet"", ""not necessary at this time""). When in doubt, do NOT create the order.
10. PRESCRIPTIONS: Extract ONLY medications the provider explicitly prescribes, starts, changes, or refills during the visit. Examples: ""I'm going to prescribe amoxicillin"", ""let's start you on lisinopril 10mg"", ""I'll refill your metformin"". Do NOT create prescriptions for medications that are merely part of the patient's existing medication history — those go in history.medications instead.
11. If a section has no relevant data, use null for scalar fields or empty arrays for list fields.

MEDICATION NAME RULE — CRITICAL:
- Output the EXACT brand or product name the clinician spoke. Do NOT convert brand names to generic chemical formulas (keep ""Panadol"" as ""Panadol""; do NOT change it to ""Acetaminophen"" or ""Paracetamol""; keep ""Lipitor"" as ""Lipitor""; do NOT change to ""Atorvastatin"").
- Only fix obvious speech-recognition misspellings by matching phonetically to the closest recognized drug, e.g. ""padol"" → ""Panadol"", ""metforming"" → ""Metformin"", ""a moxicillin"" → ""Amoxicillin"", ""lip itor"" → ""Lipitor"", ""lie-sin-o-pril"" → ""Lisinopril"".
- When the spoken name is already recognizable as a real drug, output it EXACTLY as spoken — do not substitute, translate, or standardize to its generic.
- If you cannot confidently identify a drug name at all, output the closest phonetic match with the raw text in parentheses, e.g. ""Atorvastatin (heard: ator-vast-in)"".

Return ONLY valid JSON, no markdown code fences, no explanation.";
    }

    private static string BuildUnifiedPrompt(string transcription, string contextBlock)
    {
        return $@"You are a board-certified medical scribe AI named MEDOCS AI generating LEGAL clinical documentation for an internal medicine practice.

The following is a transcription from an in-person patient encounter. A nurse or medical assistant is speaking with the patient while recording. The conversation flows naturally between vital signs, medical history, symptoms, and the chief complaint. The audio captures one microphone picking up both speakers.

LANGUAGE: The conversation may be in ANY language or a mix of languages (e.g., English, Spanish, Urdu, Arabic). Always extract and output ALL values in English only.

TRANSCRIPTION:
{transcription}

{contextBlock}Extract ALL clinical information mentioned into a single JSON object with five top-level sections: ""vitals"", ""history"", ""ccHpi"", ""orders"", and ""prescriptions"". Use null or empty arrays for sections where nothing is mentioned.

Return ONLY a JSON object with this exact structure:
{{
  ""vitals"": {{
    ""systolicBp"": integer or null (mmHg),
    ""diastolicBp"": integer or null (mmHg),
    ""heartRate"": integer or null (bpm),
    ""temperature"": number or null (Fahrenheit — convert from Celsius if needed),
    ""spO2"": number or null (percentage),
    ""respiratoryRate"": integer or null (per minute),
    ""weight"": number or null (pounds — convert from kg if needed: 1kg = 2.205lbs),
    ""height"": number or null (inches — convert from cm if needed: 1cm = 0.3937in),
    ""notes"": string or null (clinically relevant observations ONLY — exclude all casual conversation)
  }},
  ""history"": {{
    ""allergies"": [{{ ""allergenName"": string, ""notes"": string or null (type, reaction, severity in free text) }}],
    ""medications"": [{{ ""drugName"": string (EXACT brand/product name spoken, NOT generic formula), ""notes"": string or null (dosage, frequency, route, form in free text) }}],
    ""problems"": [{{ ""description"": string, ""notes"": string or null (patient's own words about duration/onset, e.g. ""for 3 days"". Leave empty or null if no context.) }}],
    ""familyHx"": [{{ ""condition"": string, ""notes"": string or null (relation, age, deceased status in free text) }}],
    ""socialHx"": [{{ ""category"": string (e.g. ""Tobacco Use"", ""Alcohol Use"", ""Drug Use"", ""Exercise"", ""Diet"", ""Occupation"", ""Sexual Activity""), ""notes"": string or null (details in free text) }}],
    ""immunizations"": [{{ ""vaccineName"": string, ""notes"": string or null (date, lot#, manufacturer, site in free text) }}]
  }},
  ""ccHpi"": {{
    ""chiefComplaint"": string or null (concise clinical statement, max 500 chars, professional medical terminology),
    ""hpiNarrative"": string or null (detailed HPI narrative using OLDCARTS elements, professional medical prose)
  }},
  ""orders"": [{{
    ""orderType"": 0|1|2 (0=Lab, 1=Imaging, 2=Referral),
    ""priority"": 0|1|2 (0=Routine, 1=Urgent, 2=STAT),
    ""diagnosisCode"": string or null (ICD-10 code),
    ""clinicalIndication"": string (reason for order),
    ""notes"": string or null,
    ""labPanelName"": string or null (for labs: e.g. ""CBC"", ""BMP"", ""Lipid Panel"", ""HbA1c"", ""TSH"", ""Urinalysis""),
    ""fastingRequired"": boolean or null (for labs),
    ""specimenType"": string or null (for labs: e.g. ""Blood"", ""Urine""),
    ""modality"": string or null (for imaging: e.g. ""X-Ray"", ""CT"", ""MRI"", ""Ultrasound""),
    ""bodyPart"": string or null (for imaging: e.g. ""Chest"", ""Abdomen"", ""Knee""),
    ""contrastRequired"": boolean or null (for imaging),
    ""referralSpecialty"": string or null (for referrals: e.g. ""Cardiology"", ""Orthopedics""),
    ""referralReason"": string or null (for referrals),
    ""referralUrgency"": ""Routine""|""Urgent""|""Emergent"" or null (for referrals)
  }}],
  ""prescriptions"": [{{
    ""drugName"": string (EXACT brand/product name spoken, NOT generic formula),
    ""strength"": string or null (e.g. ""500mg"", ""10mg/5ml""),
    ""dosageForm"": string or null (e.g. ""Tablet"", ""Capsule"", ""Solution"", ""Cream""),
    ""quantity"": integer or null,
    ""daysSupply"": integer or null,
    ""doseAmount"": string or null (e.g. ""1"", ""2"", ""0.5""),
    ""doseUnit"": string or null (e.g. ""tablet"", ""ml"", ""puff""),
    ""route"": ""Oral""|""Topical""|""SC""|""IM""|""IV""|""Inhaled""|""Rectal""|""Ophthalmic"" or null,
    ""frequency"": string or null (e.g. ""Once daily"", ""Twice daily"", ""Every 8 hours"", ""As needed""),
    ""directionsFreeText"": string or null (complete sig line, e.g. ""Take 1 tablet by mouth twice daily with food""),
    ""refills"": integer or null (0 if not mentioned),
    ""diagnosisCode"": string or null (ICD-10 code for indication),
    ""notes"": string or null
  }}]
}}

CRITICAL RULES:
1. This is an in-person encounter. The nurse/MA asks questions and takes measurements, the patient responds. Extract clinical data from BOTH speakers.
2. STRICT MEDICAL TERMINOLOGY — this is LEGAL documentation: Convert ALL output to formal medical terminology. ""really bad headache"" → ""Severe cephalgia"". ""sugar problem"" → ""Diabetes mellitus"". ""blood pressure is high"" → ""Hypertension"". ""can't sleep"" → ""Insomnia"". ""stomach hurts"" → ""Abdominal pain"". NEVER use casual or layperson language in any output field. EXCEPTION: the problem ""notes"" field stays in plain language (e.g. ""Fever for 3 days"") — do NOT medicalize it.
3. AGGRESSIVE FILTERING — this is the most important rule:
   - ONLY extract information that is genuinely about the patient's health, symptoms, medications, diagnoses, or medical history.
   - COMPLETELY IGNORE all casual conversation, small talk, pleasantries, greetings, discussing personal matters (car repairs, weather, sports, work schedules, family non-medical events, etc.).
   - Speech recognition errors that produce non-medical words are NEVER medical problems — ignore them completely.
   - If the ENTIRE transcription is casual/non-clinical conversation, return ALL fields as null and ALL arrays as empty.
4. VITAL SIGN EXTRACTION: Only return a vital value if it is a NEW measurement or a CORRECTED reading. If the speaker corrects a reading (""actually it's..."", ""sorry, it's...""), return the corrected value. Do NOT extract numbers from non-clinical context.
5. DEDUPLICATION: If PREVIOUS CONTEXT is provided, do NOT repeat items already extracted. Only return NEW information from this segment. Return empty arrays for sections with no new items.
6. PROBLEMS LIST: The patient's presenting symptoms and chief complaint (e.g. Fever, Headache, Cough, Back pain) MUST also be added to the problems array using proper medical terminology. Active symptoms are active problems. The ICD-10 code is extracted into a SEPARATE structured field (NOT into the notes field).
7. PROBLEM NOTES: The ""notes"" field on a problem is for patient's own words about duration or onset (e.g. ""for 3 days"", ""started last week""). Leave empty or null if no extra context was mentioned. Do not put diagnosis codes in notes.
8. CC/HPI: If previous context exists, merge new information with it for a comprehensive updated narrative.
9. ORDERS: Extract ONLY orders that the provider EXPLICITLY and AFFIRMATIVELY states they are ordering RIGHT NOW. Examples of real orders: ""let's order a CBC"", ""I'm going to send you for an X-ray"", ""I'd like to refer you to cardiology"". Do NOT create orders for: (a) tests discussed hypothetically, (b) tests the provider says are NOT needed, deferred, or can wait (e.g. ""not required now"", ""we can do that later"", ""leave it for now"", ""skip that"", ""hold off on"", ""let's not do that yet"", ""not necessary at this time""). When in doubt, do NOT create the order.
10. PRESCRIPTIONS: Extract ONLY medications the provider explicitly prescribes, starts, changes, or refills during the visit. Examples: ""I'm going to prescribe amoxicillin"", ""let's start you on lisinopril 10mg"", ""I'll refill your metformin"". Do NOT create prescriptions for medications that are merely part of the patient's existing medication history — those go in history.medications instead.
11. If a section has no relevant data, use null for scalar fields or empty arrays for list fields.

MEDICATION NAME RULE — CRITICAL:
- Output the EXACT brand or product name the clinician spoke. Do NOT convert brand names to generic chemical formulas (keep ""Panadol"" as ""Panadol""; do NOT change it to ""Acetaminophen"" or ""Paracetamol""; keep ""Lipitor"" as ""Lipitor""; do NOT change to ""Atorvastatin"").
- Only fix obvious speech-recognition misspellings by matching phonetically to the closest recognized drug, e.g. ""padol"" → ""Panadol"", ""metforming"" → ""Metformin"", ""a moxicillin"" → ""Amoxicillin"", ""lip itor"" → ""Lipitor"", ""lie-sin-o-pril"" → ""Lisinopril"".
- When the spoken name is already recognizable as a real drug, output it EXACTLY as spoken — do not substitute, translate, or standardize to its generic.

Return ONLY valid JSON, no markdown code fences, no explanation.";
    }

    /// <summary>
    /// Post-call extraction: processes the FULL accumulated telehealth transcription at once.
    /// No previous context needed — the full conversation is provided in one call.
    /// </summary>
    public async Task<MedocsVoiceResultDto> ExtractFromFullTranscriptionAsync(string fullTranscription)
    {
        if (string.IsNullOrWhiteSpace(fullTranscription))
        {
            return new MedocsVoiceResultDto
            {
                Success = false,
                Message = "No transcription text provided"
            };
        }

        try
        {
            _logger.LogInformation(
                "[MedocsVoice] Post-call telehealth extraction: TranscriptionLength={Length}",
                fullTranscription.Length);

            // Build telehealth prompt with NO previous context (full conversation in one shot)
            var extractionPrompt = BuildTelehealthPrompt(fullTranscription, "");
            var extractionResponse = await _geminiService.GenerateTextAsync(extractionPrompt, 0.1);

            if (!extractionResponse.Success)
            {
                return new MedocsVoiceResultDto
                {
                    Success = false,
                    Message = "Post-call extraction failed: " + extractionResponse.ErrorMessage,
                    Transcription = fullTranscription,
                    ExtractedData = null,
                    TabKey = "telehealth"
                };
            }

            var extractedJson = CleanJsonResponse(extractionResponse.Text ?? "");

            return new MedocsVoiceResultDto
            {
                Success = true,
                Message = "Post-call extraction completed successfully",
                Transcription = fullTranscription,
                ExtractedData = extractedJson,
                TabKey = "telehealth"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MedocsVoice] Error in post-call telehealth extraction");
            return new MedocsVoiceResultDto
            {
                Success = false,
                Message = "An error occurred during post-call extraction"
            };
        }
    }

    /// <summary>
    /// Unified voice extraction: processes accumulated transcription for in-person encounters.
    /// Extracts all sections (vitals, history, CC/HPI) in one call with full conversation context.
    /// </summary>
    public async Task<MedocsVoiceResultDto> ExtractFromUnifiedTranscriptionAsync(string fullTranscription, string? existingData = null)
    {
        if (string.IsNullOrWhiteSpace(fullTranscription))
        {
            return new MedocsVoiceResultDto
            {
                Success = false,
                Message = "No transcription text provided"
            };
        }

        try
        {
            _logger.LogInformation(
                "[MedocsVoice] Unified extraction: TranscriptionLength={Length}, HasExistingData={HasExisting}",
                fullTranscription.Length, !string.IsNullOrWhiteSpace(existingData));

            // Build context block with existing data so Gemini knows what's already recorded
            var contextBlock = "";
            if (!string.IsNullOrWhiteSpace(existingData))
            {
                contextBlock = $@"ALREADY RECORDED DATA — these items are already saved in the patient's chart for this encounter. Do NOT re-extract or duplicate them. Only return items that are NEW and not already listed below:
{existingData}

";}

            var extractionPrompt = BuildUnifiedPrompt(fullTranscription, contextBlock);
            var extractionResponse = await _geminiService.GenerateTextAsync(extractionPrompt, 0.1);

            if (!extractionResponse.Success)
            {
                return new MedocsVoiceResultDto
                {
                    Success = false,
                    Message = "Unified extraction failed: " + extractionResponse.ErrorMessage,
                    Transcription = fullTranscription,
                    ExtractedData = null,
                    TabKey = "unified"
                };
            }

            var extractedJson = CleanJsonResponse(extractionResponse.Text ?? "");

            return new MedocsVoiceResultDto
            {
                Success = true,
                Message = "Unified extraction completed",
                Transcription = fullTranscription,
                ExtractedData = extractedJson,
                TabKey = "unified"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MedocsVoice] Error in unified extraction");
            return new MedocsVoiceResultDto
            {
                Success = false,
                Message = "An error occurred during unified extraction"
            };
        }
    }

    private static string CleanJsonResponse(string text)
    {
        text = text.Trim();

        // Remove markdown code fences
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..];
        }
        else if (text.StartsWith("```"))
        {
            text = text[3..];
        }

        if (text.EndsWith("```"))
        {
            text = text[..^3];
        }

        return text.Trim();
    }

    private async Task<string> ConvertToWavAsync(string inputPath)
    {
        var outputPath = Path.Combine(_tempPath, $"medocs_{Guid.NewGuid():N}.wav");

        int targetSampleRate = 16000;
        int targetChannels = 1;
        string targetBitDepth = "s16";

        var arguments = $"-i \"{inputPath}\" -ar {targetSampleRate} -ac {targetChannels} -sample_fmt {targetBitDepth} -y \"{outputPath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        process.Start();

        var errorOutput = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("[MedocsVoice] FFmpeg conversion failed: {Error}", errorOutput);
            throw new InvalidOperationException($"FFmpeg conversion failed: {errorOutput}");
        }

        return outputPath;
    }

    private static string GetFileExtension(string contentType, string originalFileName)
    {
        if (!string.IsNullOrEmpty(originalFileName))
        {
            var ext = Path.GetExtension(originalFileName);
            if (!string.IsNullOrEmpty(ext)) return ext;
        }

        return contentType?.ToLower() switch
        {
            "audio/webm" => ".webm",
            "audio/ogg" => ".ogg",
            "audio/mp4" => ".mp4",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            _ => ".webm"
        };
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private string FindFfmpegPath()
    {
        var possiblePaths = new[]
        {
            Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data", "Tools", "ffmpeg.exe"),
            Path.Combine(_webHostEnvironment.WebRootPath, "Content", "tools", "ffmpeg.exe"),
            Path.Combine(_webHostEnvironment.WebRootPath, "tools", "ffmpeg.exe"),
            "/usr/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            "ffmpeg"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) || path == "ffmpeg")
                return path;
        }

        return "ffmpeg";
    }

    private static string? ResolveToAbsolutePath(string? configuredPath, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        return Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }
}
