using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EHR.Configuration;
using EHR.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EHR.Services;

/// <summary>
/// Office Ally Real-Time Eligibility API service (270/271).
/// Auth: API Key in Authorization header.
/// Endpoint: POST /v1/realtime-eligibility
/// </summary>
public class EligibilityApiService : IEligibilityApiService
{
    private readonly HttpClient _httpClient;
    private readonly OfficeAllyOptions _options;
    private readonly ILogger<EligibilityApiService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new FlexibleStringConverter() }
    };

    /// <summary>
    /// Accepts both JSON strings ("10") and JSON numbers (10) for string properties.
    /// OA inconsistently sends quantity/monetaryAmount/percent as either type.
    /// </summary>
    private class FlexibleStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetDecimal().ToString(),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Unexpected token {reader.TokenType} for string property")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public EligibilityApiService(
        HttpClient httpClient,
        IOptions<OfficeAllyOptions> options,
        ILogger<EligibilityApiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InsuranceVerificationResult> CheckEligibilityAsync(
        string payerName, string payerId, string policyNumber,
        string groupNumber, string subscriberId, string subscriberName,
        DateOnly? subscriberDob, string organizationNpi = null)
    {
        // Validate API key is configured
        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = "Eligibility API key is not configured. Please set OfficeAlly:ApiKey in appsettings.json."
            };
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(payerId))
        {
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = "Payer ID is required for eligibility verification. Please select a payer from the dropdown."
            };
        }

        try
        {
            // Build the OA eligibility request
            var request = BuildEligibilityRequest(payerId, policyNumber, groupNumber, subscriberId, subscriberName, subscriberDob, organizationNpi);

            var jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
            _logger.LogInformation("[EligibilityAPI] Sending eligibility request for Payer: {PayerName} ({PayerId}), Subscriber: {SubscriberId}",
                payerName, payerId, subscriberId);

            // Build HTTP request with API key auth
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/realtime-eligibility")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("Authorization", _options.ApiKey);

            // Send request
            var httpResponse = await _httpClient.SendAsync(httpRequest);
            var responseBody = await httpResponse.Content.ReadAsStringAsync();

            _logger.LogInformation("[EligibilityAPI] Response status: {StatusCode} for Payer: {PayerName}",
                (int)httpResponse.StatusCode, payerName);

            // Handle non-success HTTP status
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("[EligibilityAPI] HTTP {StatusCode} from OA API. Response: {ResponseBody}",
                    (int)httpResponse.StatusCode, responseBody);

                return new InsuranceVerificationResult
                {
                    Success = false,
                    ErrorMessage = $"Office Ally API returned HTTP {(int)httpResponse.StatusCode}: {TruncateForDisplay(responseBody, 500)}"
                };
            }

            // Parse the OA response
            var oaResponse = JsonSerializer.Deserialize<OaEligibilityResponse>(responseBody, _jsonOptions);
            if (oaResponse == null)
            {
                return new InsuranceVerificationResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse eligibility response from Office Ally. Empty response body."
                };
            }

            // Check for transaction errors (AAA rejections)
            if (oaResponse.TransactionErrors != null && oaResponse.TransactionErrors.Count > 0)
            {
                var errorMessages = oaResponse.TransactionErrors
                    .Select(e => $"{e.RejectReason?.Description ?? "Unknown error"} (Follow-up: {e.FollowUpAction?.Description ?? "N/A"})")
                    .ToList();

                _logger.LogWarning("[EligibilityAPI] Transaction errors for {PayerName}: {Errors}",
                    payerName, string.Join("; ", errorMessages));

                return new InsuranceVerificationResult
                {
                    Success = false,
                    ErrorMessage = $"Payer rejected eligibility request: {string.Join("; ", errorMessages)}"
                };
            }

            // Map OA response to our InsuranceVerificationResult
            var result = MapOaResponseToResult(oaResponse, payerName);

            // Store raw JSON for later rich parsing
            result.RawResponseJson = responseBody;

            _logger.LogInformation("[EligibilityAPI] Eligibility check successful for {PayerName}: Eligible={IsEligible}, Plan={PlanName}",
                payerName, result.IsEligible, result.PlanName);

            return result;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("[EligibilityAPI] Request timed out for Payer: {PayerName}", payerName);
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = "Eligibility check timed out. Office Ally API did not respond within 30 seconds. Please try again."
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[EligibilityAPI] Network error for Payer: {PayerName}", payerName);
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = $"Network error connecting to Office Ally: {ex.Message}"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[EligibilityAPI] Failed to parse OA response for Payer: {PayerName}", payerName);
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = $"Failed to parse Office Ally response: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EligibilityAPI] Unexpected error for Payer: {PayerName}", payerName);
            return new InsuranceVerificationResult
            {
                Success = false,
                ErrorMessage = $"Eligibility verification error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Build OA JSON 270 eligibility request.
    /// </summary>
    private static OaEligibilityRequest BuildEligibilityRequest(
        string payerId, string policyNumber, string groupNumber,
        string subscriberId, string subscriberName, DateOnly? subscriberDob,
        string organizationNpi)
    {
        // Parse subscriber name
        var nameParts = (subscriberName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "";
        var lastName = nameParts.Length > 1 ? nameParts[1] : nameParts.Length > 0 ? nameParts[0] : "";

        return new OaEligibilityRequest
        {
            PayerId = payerId,
            Provider = new OaProvider
            {
                LastName = "iMEHR",
                Npi = organizationNpi
            },
            Subscriber = new OaSubscriber
            {
                FirstName = firstName,
                LastName = lastName,
                MemberId = subscriberId ?? policyNumber,
                GroupNumber = string.IsNullOrWhiteSpace(groupNumber) ? "NONE" : groupNumber,
                Dob = subscriberDob.HasValue
                    ? subscriberDob.Value.ToDateTime(TimeOnly.MinValue)
                    : null
            },
            ServiceTypeCodes = new[] { "30" } // Health Benefit Plan Coverage (general)
        };
    }

    /// <summary>
    /// Map OA EligibilityResponse to our InsuranceVerificationResult.
    /// </summary>
    private static InsuranceVerificationResult MapOaResponseToResult(OaEligibilityResponse oa, string payerName)
    {
        var result = new InsuranceVerificationResult
        {
            Success = true,
            VerifiedAt = DateTime.UtcNow
        };

        // Determine data source: use Dependent if it has benefits, otherwise use Subscriber
        var sub = oa.Subscriber;
        var dep = oa.Dependent;
        var benefitSource = dep?.EbResponseDetails?.Benefits?.Count > 0 ? dep : sub;
        var benefits = benefitSource?.EbResponseDetails;

        // Check active coverage
        result.IsEligible = false;
        if (benefits?.Plans != null)
        {
            var activePlan = benefits.Plans
                .FirstOrDefault(p => p.BenefitInformationCode?.CodeValue == "1");
            if (activePlan != null)
                result.IsEligible = true;
        }
        if (benefits?.Benefits != null)
        {
            var activeBenefit = benefits.Benefits
                .FirstOrDefault(b => b.BenefitInformationCode?.CodeValue == "1");
            if (activeBenefit != null)
                result.IsEligible = true;

            var inactiveBenefit = benefits.Benefits
                .FirstOrDefault(b => b.BenefitInformationCode?.CodeValue == "6");
            if (inactiveBenefit != null)
                result.IsEligible = false;
        }
        if (oa.ResponseStatus?.CodeValue == "1")
            result.IsEligible = true;

        // Plan info
        if (benefits?.Plans != null && benefits.Plans.Count > 0)
        {
            var plan = benefits.Plans[0];
            result.PlanName = plan.PlanCoverageDescription;
            if (plan.InsuranceTypeCode != null)
                result.PlanType = plan.InsuranceTypeCode.Description;
        }
        result.PlanName ??= payerName;

        // Member ID
        result.MemberId = benefitSource?.MemberId ?? sub?.MemberId;
        result.GroupName = benefitSource?.GroupName ?? benefitSource?.GroupNumber ?? sub?.GroupNumber;

        // Parse benefits
        if (benefits?.Benefits != null)
        {
            ParseBenefits(benefits.Benefits, result);
        }

        // Coverage dates
        var datesSource = benefitSource?.Dates ?? sub?.Dates;
        if (datesSource != null)
        {
            if (datesSource.PlanBegin?.BeginDate != null)
                result.CoverageEffectiveDate = DateOnly.FromDateTime(datesSource.PlanBegin.BeginDate.Value);
            if (datesSource.EligibilityBegin?.BeginDate != null)
                result.CoverageEffectiveDate ??= DateOnly.FromDateTime(datesSource.EligibilityBegin.BeginDate.Value);
            if (datesSource.EligibilityEnd?.BeginDate != null)
                result.CoverageTerminationDate = DateOnly.FromDateTime(datesSource.EligibilityEnd.BeginDate.Value);
        }

        // Fallback: legacy EligibilityDates format
        var eligDates = benefitSource?.EligibilityDates ?? sub?.EligibilityDates;
        if (eligDates != null)
        {
            if (eligDates.PlanDates != null)
            {
                foreach (var dr in eligDates.PlanDates)
                {
                    if (dr.BeginDate.HasValue)
                        result.CoverageEffectiveDate ??= DateOnly.FromDateTime(dr.BeginDate.Value);
                    if (dr.EndDate.HasValue)
                        result.CoverageTerminationDate ??= DateOnly.FromDateTime(dr.EndDate.Value);
                }
            }
            if (eligDates.EligibilityDateRanges != null)
            {
                foreach (var dr in eligDates.EligibilityDateRanges)
                {
                    if (dr.BeginDate.HasValue && result.CoverageEffectiveDate == null)
                        result.CoverageEffectiveDate = DateOnly.FromDateTime(dr.BeginDate.Value);
                    if (dr.EndDate.HasValue && result.CoverageTerminationDate == null)
                        result.CoverageTerminationDate = DateOnly.FromDateTime(dr.EndDate.Value);
                }
            }
        }

        // Build coverage notes from disclaimers/descriptions
        var notes = new List<string>();
        if (benefits?.Disclaimers != null)
        {
            foreach (var d in benefits.Disclaimers)
            {
                if (d.Messages != null)
                    notes.AddRange(d.Messages);
            }
        }
        if (benefits?.BenefitDescriptions != null)
        {
            foreach (var d in benefits.BenefitDescriptions)
            {
                if (!string.IsNullOrEmpty(d.PlanCoverageDescription))
                    notes.Add(d.PlanCoverageDescription);
                if (d.Messages != null)
                    notes.AddRange(d.Messages);
            }
        }
        if (notes.Count > 0)
            result.CoverageNotes = string.Join(" | ", notes.Take(5));

        // Compute remaining values
        if (result.IndividualDeductible.HasValue && result.IndividualDeductibleMet.HasValue)
            result.IndividualDeductibleRemaining = result.IndividualDeductible.Value - result.IndividualDeductibleMet.Value;
        if (result.IndividualOopMax.HasValue && result.IndividualOopMet.HasValue)
            result.IndividualOopRemaining = result.IndividualOopMax.Value - result.IndividualOopMet.Value;

        // Backward compat fields
        result.DeductibleTotal ??= result.IndividualDeductible;
        result.DeductibleMet ??= result.IndividualDeductibleMet;
        result.OutOfPocketMax ??= result.IndividualOopMax;
        result.Copay ??= result.CopayInNetwork;
        result.Coinsurance ??= result.CoinsuranceInNetwork;

        return result;
    }

    /// <summary>
    /// Parse BenefitContent array to extract copay, deductible, coinsurance, OOP, visits.
    /// </summary>
    private static void ParseBenefits(List<OaBenefitContent> benefits, InsuranceVerificationResult result)
    {
        foreach (var b in benefits)
        {
            var code = b.BenefitInformationCode?.CodeValue;
            var isInNetwork = b.NetworkIndicator?.CodeValue == "Y";
            var isOutOfNetwork = b.NetworkIndicator?.CodeValue == "N";
            var timePeriod = b.TimePeriod?.CodeValue;

            switch (code)
            {
                case "B": // Co-Payment
                    if (TryParseMoney(b.MonetaryAmount, out var copay))
                    {
                        if (isInNetwork) result.CopayInNetwork = copay;
                        else if (isOutOfNetwork) result.CopayOutOfNetwork = copay;
                        else result.CopayInNetwork ??= copay;
                    }
                    break;

                case "A": // Co-Insurance
                    if (TryParsePercent(b.Percent, out var coinsurance))
                    {
                        if (isInNetwork) result.CoinsuranceInNetwork = coinsurance;
                        else if (isOutOfNetwork) result.CoinsuranceOutOfNetwork = coinsurance;
                        else result.CoinsuranceInNetwork ??= coinsurance;
                    }
                    break;

                case "C": // Deductible
                    if (TryParseMoney(b.MonetaryAmount, out var deductible))
                    {
                        var isFamily = b.CoverageLevel?.CodeValue == "FAM";

                        if (isFamily)
                        {
                            if (timePeriod == "24")
                                result.FamilyDeductibleMet ??= deductible;
                            else
                                result.FamilyDeductible ??= deductible;
                        }
                        else
                        {
                            if (timePeriod == "29")
                                result.IndividualDeductibleRemaining ??= deductible;
                            else if (timePeriod == "24")
                                result.IndividualDeductibleMet ??= deductible;
                            else
                                result.IndividualDeductible ??= deductible;
                        }
                    }
                    break;

                case "G": // Out of Pocket (Stop Loss)
                    if (TryParseMoney(b.MonetaryAmount, out var oop))
                    {
                        var isFamily = b.CoverageLevel?.CodeValue == "FAM";

                        if (isFamily)
                        {
                            if (timePeriod == "24")
                                result.FamilyOopMet ??= oop;
                            else
                                result.FamilyOopMax ??= oop;
                        }
                        else
                        {
                            if (timePeriod == "24")
                                result.IndividualOopMet ??= oop;
                            else
                                result.IndividualOopMax ??= oop;
                        }
                    }
                    break;

                case "F": // Limitations (visit limits)
                case "W": // Visit Count
                    if (!string.IsNullOrEmpty(b.Quantity) && int.TryParse(b.Quantity, out var visitCount))
                    {
                        if (timePeriod == "24")
                            result.VisitsUsed ??= visitCount;
                        else if (timePeriod == "29")
                            result.VisitsRemaining ??= visitCount;
                        else
                            result.AllowedVisits ??= visitCount;
                    }
                    break;
            }

            // Check prior auth required
            if (b.RequiresAuthorization?.CodeValue?.Equals("Y", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.RequiresPriorAuthorization = true;
            }

            // Check network indicator for general active coverage
            if (code == "1" && isInNetwork)
                result.InNetwork = true;
            else if (code == "1" && isOutOfNetwork)
                result.InNetwork = false;
        }

        // Compute visits remaining if we have allowed and used
        if (result.AllowedVisits.HasValue && result.VisitsUsed.HasValue && !result.VisitsRemaining.HasValue)
            result.VisitsRemaining = result.AllowedVisits.Value - result.VisitsUsed.Value;

        // Determine benefit period
        var hasCalendarYear = benefits.Any(b => b.TimePeriod?.CodeValue == "23");
        if (hasCalendarYear)
            result.BenefitPeriod = "Calendar Year";
    }

    private static bool TryParseMoney(string value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrEmpty(value)) return false;
        var cleaned = value.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParsePercent(string value, out decimal result)
    {
        result = 0;
        if (string.IsNullOrEmpty(value)) return false;
        var cleaned = value.Replace("%", "").Trim();
        if (!decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result))
            return false;
        if (result > 0 && result < 1)
            result *= 100;
        return true;
    }

    private static string TruncateForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    // ══════════════════════════════════════════════════════════════
    // OA API Request/Response DTOs (internal)
    // ══════════════════════════════════════════════════════════════

    #region OA Request DTOs

    private class OaEligibilityRequest
    {
        public string PayerId { get; set; }
        public OaProvider Provider { get; set; }
        public OaSubscriber Subscriber { get; set; }
        public OaDependent Dependent { get; set; }
        public DateTime? DateOfServiceStart { get; set; }
        public DateTime? DateOfServiceEnd { get; set; }
        public string[] ServiceTypeCodes { get; set; }
    }

    private class OaProvider
    {
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string Npi { get; set; }
    }

    private class OaSubscriber
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string MemberId { get; set; }
        public string GroupNumber { get; set; }
        public DateTime? Dob { get; set; }
        public string Gender { get; set; }
        public string Ssn { get; set; }
    }

    private class OaDependent
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime? Dob { get; set; }
        public string Gender { get; set; }
    }

    #endregion

    #region OA Response DTOs

    private class OaEligibilityResponse
    {
        public OaCode ResponseStatus { get; set; }
        public string TransactionId { get; set; }
        public OaPayer OaPayer { get; set; }
        public List<OaAaaError> TransactionErrors { get; set; }
        public OaResponsePayer ResponsePayer { get; set; }
        public OaResponseSubscriber Subscriber { get; set; }
        public OaResponseSubscriber Dependent { get; set; }
    }

    private class OaCode
    {
        public string CodeValue { get; set; }
        public string Description { get; set; }
    }

    private class OaPayer
    {
        public string Name { get; set; }
        public string PayerId { get; set; }
    }

    private class OaAaaError
    {
        public string LoopId { get; set; }
        public string LoopName { get; set; }
        public OaCode RejectReason { get; set; }
        public OaCode FollowUpAction { get; set; }
    }

    private class OaResponsePayer
    {
        public string Name { get; set; }
        public string PayerId { get; set; }
    }

    private class OaResponseSubscriber
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string MemberId { get; set; }
        public string GroupNumber { get; set; }
        public string GroupName { get; set; }
        public string GroupOrPolicyNumber { get; set; }
        public string GroupOrPolicyName { get; set; }
        public string PlanNumber { get; set; }
        public string PlanName { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public OaCode Gender { get; set; }
        public OaCode RelationshipCode { get; set; }
        public OaAddress Address { get; set; }
        public OaResponseDates Dates { get; set; }
        public OaEligibilityDates EligibilityDates { get; set; }
        /// <summary>OA returns benefits in "ebResponseDetails", not "benefitResponse"</summary>
        public OaEbResponseDetails EbResponseDetails { get; set; }
    }

    private class OaAddress
    {
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zipcode { get; set; }
    }

    private class OaResponseDates
    {
        public OaDateRange Plan { get; set; }
        public OaDateRange Eligibility { get; set; }
        public OaDateRange EligibilityBegin { get; set; }
        public OaDateRange EligibilityEnd { get; set; }
        public OaDateRange PlanBegin { get; set; }
    }

    private class OaEligibilityDates
    {
        public List<OaDateRange> PlanDates { get; set; }
        [JsonPropertyName("eligibilityDates")]
        public List<OaDateRange> EligibilityDateRanges { get; set; }
    }

    private class OaDateRange
    {
        public OaCode DateRange { get; set; }
        public DateTime? BeginDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    private class OaEbResponseDetails
    {
        public List<OaBenefitContent> Plans { get; set; }
        public List<OaBenefitContent> Benefits { get; set; }
        public List<OaBenefitContent> BenefitDescriptions { get; set; }
        public List<OaBenefitContent> Exclusions { get; set; }
        public List<OaBenefitContent> Limitations { get; set; }
        public List<OaBenefitContent> Disclaimers { get; set; }
    }

    private class OaBenefitContent
    {
        public OaCode BenefitInformationCode { get; set; }
        public OaCode CoverageLevel { get; set; }
        public List<OaCode> ServiceTypeCodes { get; set; }
        public OaCode InsuranceTypeCode { get; set; }
        public string PlanCoverageDescription { get; set; }
        public OaCode TimePeriod { get; set; }
        public string MonetaryAmount { get; set; }
        public string Percent { get; set; }
        public OaCode QuantityType { get; set; }
        public string Quantity { get; set; }
        public OaCode RequiresAuthorization { get; set; }
        public OaCode NetworkIndicator { get; set; }
        public List<string> Messages { get; set; }
        public List<string> AdditionalInformation { get; set; }
    }

    #endregion

    // ══════════════════════════════════════════════════════════════
    // Parse stored raw OA response into EligibilityDetailsDto
    // ══════════════════════════════════════════════════════════════

    public static EligibilityDetailsDto ParseRawResponseToDetails(string rawJson)
    {
        if (string.IsNullOrEmpty(rawJson))
            return null;

        // Detect old format: old records stored serialized InsuranceVerificationResult, not raw OA JSON.
        // OA responses always have "responseStatus", our DTO has "success".
        if (!rawJson.Contains("\"responseStatus\"", StringComparison.OrdinalIgnoreCase))
            return null;

        OaEligibilityResponse oa;
        try
        {
            oa = JsonSerializer.Deserialize<OaEligibilityResponse>(rawJson, _jsonOptions);
        }
        catch
        {
            return null;
        }
        if (oa == null)
            return null;

        var dto = new EligibilityDetailsDto();

        // Transaction info
        dto.TransactionId = oa.TransactionId;

        // Payer info
        dto.PayerName = oa.ResponsePayer?.Name ?? oa.OaPayer?.Name;
        dto.PayerId = oa.ResponsePayer?.PayerId ?? oa.OaPayer?.PayerId;

        // Determine benefit source (same logic as MapOaResponseToResult)
        var sub = oa.Subscriber;
        var dep = oa.Dependent;
        var benefitSource = dep?.EbResponseDetails?.Benefits?.Count > 0 ? dep : sub;
        var benefits = benefitSource?.EbResponseDetails;

        // Patient info
        dto.PatientFirstName = benefitSource?.FirstName ?? sub?.FirstName;
        dto.PatientLastName = benefitSource?.LastName ?? sub?.LastName;
        dto.MemberId = benefitSource?.MemberId ?? sub?.MemberId;
        dto.DateOfBirth = benefitSource?.DateOfBirth?.ToString("yyyy-MM-dd");
        dto.Gender = benefitSource?.Gender?.Description;
        dto.Relationship = benefitSource?.RelationshipCode?.Description;
        dto.GroupNumber = benefitSource?.GroupNumber ?? benefitSource?.GroupOrPolicyNumber ?? sub?.GroupNumber;
        dto.GroupName = benefitSource?.GroupName ?? benefitSource?.GroupOrPolicyName ?? sub?.GroupName;

        // Address
        var addr = benefitSource?.Address ?? sub?.Address;
        if (addr != null)
        {
            var parts = new[] { addr.Line1, addr.Line2, addr.City, addr.State, addr.Zipcode }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            dto.Address = string.Join(", ", parts);
        }

        // Coverage status
        dto.IsEligible = oa.ResponseStatus?.CodeValue == "1";
        if (benefits?.Plans != null)
        {
            var activePlan = benefits.Plans.FirstOrDefault(p => p.BenefitInformationCode?.CodeValue == "1");
            if (activePlan != null) dto.IsEligible = true;
        }
        if (benefits?.Benefits != null)
        {
            if (benefits.Benefits.Any(b => b.BenefitInformationCode?.CodeValue == "1"))
                dto.IsEligible = true;
            if (benefits.Benefits.Any(b => b.BenefitInformationCode?.CodeValue == "6"))
                dto.IsEligible = false;
        }

        // Plan info
        if (benefits?.Plans?.Count > 0)
        {
            var plan = benefits.Plans[0];
            dto.PlanName = plan.PlanCoverageDescription;
            dto.PlanType = plan.InsuranceTypeCode?.Description;
        }

        // Coverage dates
        var dates = benefitSource?.Dates ?? sub?.Dates;
        if (dates?.PlanBegin?.BeginDate != null)
            dto.CoverageEffectiveDate = dates.PlanBegin.BeginDate.Value.ToString("yyyy-MM-dd");
        else if (dates?.EligibilityBegin?.BeginDate != null)
            dto.CoverageEffectiveDate = dates.EligibilityBegin.BeginDate.Value.ToString("yyyy-MM-dd");
        if (dates?.EligibilityEnd?.BeginDate != null)
            dto.CoverageTerminationDate = dates.EligibilityEnd.BeginDate.Value.ToString("yyyy-MM-dd");

        // Also check Plan dates
        if (dates?.Plan?.BeginDate != null && dto.CoverageEffectiveDate == null)
            dto.CoverageEffectiveDate = dates.Plan.BeginDate.Value.ToString("yyyy-MM-dd");
        if (dates?.Plan?.EndDate != null && dto.CoverageTerminationDate == null)
            dto.CoverageTerminationDate = dates.Plan.EndDate.Value.ToString("yyyy-MM-dd");

        // Parse benefits into summary + service-specific groups
        dto.InNetworkBenefits = new BenefitAmounts();
        dto.OutOfNetworkBenefits = new BenefitAmounts();

        if (benefits?.Benefits != null)
            ParseBenefitsForDetails(benefits.Benefits, dto);

        // Benefit period
        if (benefits?.Benefits?.Any(b => b.TimePeriod?.CodeValue == "23") == true)
            dto.BenefitPeriod = "Calendar Year";

        // Messages: disclaimers, descriptions, exclusions, limitations
        CollectMessages(benefits?.Disclaimers, dto.Disclaimers);
        CollectBenefitDescriptions(benefits?.BenefitDescriptions, dto.BenefitDescriptions);
        CollectMessages(benefits?.Exclusions, dto.Exclusions);
        CollectMessages(benefits?.Limitations, dto.Limitations);

        return dto;
    }

    private static void ParseBenefitsForDetails(List<OaBenefitContent> benefits, EligibilityDetailsDto dto)
    {
        var serviceGroups = new Dictionary<string, ServiceBenefitGroup>();

        foreach (var b in benefits)
        {
            var code = b.BenefitInformationCode?.CodeValue;
            var benefitType = b.BenefitInformationCode?.Description ?? code;
            var isInNetwork = b.NetworkIndicator?.CodeValue == "Y";
            var isOutOfNetwork = b.NetworkIndicator?.CodeValue == "N";
            var timePeriod = b.TimePeriod?.CodeValue;
            var timePeriodDesc = b.TimePeriod?.Description;
            var coverageLevel = b.CoverageLevel?.Description;
            var isFamily = b.CoverageLevel?.CodeValue == "FAM";

            // Populate summary BenefitAmounts
            var target = isOutOfNetwork ? dto.OutOfNetworkBenefits : dto.InNetworkBenefits;

            switch (code)
            {
                case "B": // Copay
                    if (TryParseMoney(b.MonetaryAmount, out var copay))
                        target.Copay ??= copay;
                    break;

                case "A": // Coinsurance
                    if (TryParsePercent(b.Percent, out var coins))
                        target.Coinsurance ??= coins;
                    break;

                case "C": // Deductible
                    if (TryParseMoney(b.MonetaryAmount, out var ded))
                    {
                        if (isFamily)
                        {
                            if (timePeriod == "24") target.FamilyDeductibleMet ??= ded;
                            else target.FamilyDeductible ??= ded;
                        }
                        else
                        {
                            if (timePeriod == "29") target.IndividualDeductibleRemaining ??= ded;
                            else if (timePeriod == "24") target.IndividualDeductibleMet ??= ded;
                            else target.IndividualDeductible ??= ded;
                        }
                    }
                    break;

                case "G": // OOP
                    if (TryParseMoney(b.MonetaryAmount, out var oop))
                    {
                        if (isFamily)
                        {
                            if (timePeriod == "24") target.FamilyOopMet ??= oop;
                            else target.FamilyOopMax ??= oop;
                        }
                        else
                        {
                            if (timePeriod == "24") target.IndividualOopMet ??= oop;
                            else target.IndividualOopMax ??= oop;
                        }
                    }
                    break;

                case "F": // Limitations (visit limits)
                case "W": // Visit Count
                    if (!string.IsNullOrEmpty(b.Quantity) && int.TryParse(b.Quantity, out var visitCount))
                    {
                        if (timePeriod == "24") dto.VisitsUsed ??= visitCount;
                        else if (timePeriod == "29") dto.VisitsRemaining ??= visitCount;
                        else dto.AllowedVisits ??= visitCount;
                    }
                    break;

                case "1": // Active Coverage — check network
                    if (isInNetwork) dto.InNetwork = true;
                    else if (isOutOfNetwork) dto.InNetwork = false;
                    break;
            }

            // Prior auth
            if (b.RequiresAuthorization?.CodeValue?.Equals("Y", StringComparison.OrdinalIgnoreCase) == true)
                dto.RequiresPriorAuthorization = true;

            // Group by service type for detailed view
            if (b.ServiceTypeCodes != null)
            {
                foreach (var svc in b.ServiceTypeCodes)
                {
                    var key = svc.CodeValue ?? "unknown";
                    if (!serviceGroups.TryGetValue(key, out var group))
                    {
                        group = new ServiceBenefitGroup
                        {
                            ServiceType = svc.Description ?? key,
                            ServiceTypeCode = key
                        };
                        serviceGroups[key] = group;
                    }

                    group.Items.Add(new ServiceBenefitItem
                    {
                        BenefitType = benefitType,
                        Network = b.NetworkIndicator?.Description,
                        CoverageLevel = coverageLevel,
                        TimePeriod = timePeriodDesc,
                        MonetaryAmount = b.MonetaryAmount,
                        Percent = b.Percent,
                        Quantity = b.Quantity,
                        RequiresAuthorization = b.RequiresAuthorization?.CodeValue?.Equals("Y", StringComparison.OrdinalIgnoreCase),
                        Messages = b.Messages
                    });
                }
            }
        }

        // Compute remaining values
        var inNet = dto.InNetworkBenefits;
        if (inNet.IndividualDeductible.HasValue && inNet.IndividualDeductibleMet.HasValue && !inNet.IndividualDeductibleRemaining.HasValue)
            inNet.IndividualDeductibleRemaining = inNet.IndividualDeductible.Value - inNet.IndividualDeductibleMet.Value;
        if (inNet.IndividualOopMax.HasValue && inNet.IndividualOopMet.HasValue)
            inNet.IndividualOopRemaining = inNet.IndividualOopMax.Value - inNet.IndividualOopMet.Value;

        var outNet = dto.OutOfNetworkBenefits;
        if (outNet.IndividualDeductible.HasValue && outNet.IndividualDeductibleMet.HasValue && !outNet.IndividualDeductibleRemaining.HasValue)
            outNet.IndividualDeductibleRemaining = outNet.IndividualDeductible.Value - outNet.IndividualDeductibleMet.Value;
        if (outNet.IndividualOopMax.HasValue && outNet.IndividualOopMet.HasValue)
            outNet.IndividualOopRemaining = outNet.IndividualOopMax.Value - outNet.IndividualOopMet.Value;

        // Compute visits remaining
        if (dto.AllowedVisits.HasValue && dto.VisitsUsed.HasValue && !dto.VisitsRemaining.HasValue)
            dto.VisitsRemaining = dto.AllowedVisits.Value - dto.VisitsUsed.Value;

        dto.ServiceBenefits = serviceGroups.Values.OrderBy(g => g.ServiceType).ToList();
    }

    private static void CollectMessages(List<OaBenefitContent> items, List<string> target)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item.Messages != null)
                target.AddRange(item.Messages);
        }
    }

    private static void CollectBenefitDescriptions(List<OaBenefitContent> items, List<string> target)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.PlanCoverageDescription))
                target.Add(item.PlanCoverageDescription);
            if (item.Messages != null)
                target.AddRange(item.Messages);
        }
    }
}
