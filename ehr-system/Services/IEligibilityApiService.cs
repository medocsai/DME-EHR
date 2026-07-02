using EHR.Models;

namespace EHR.Services;

/// <summary>
/// Abstraction for insurance eligibility verification (270/271).
/// Implementation: EligibilityApiService (Office Ally Real-Time Eligibility API).
/// </summary>
public interface IEligibilityApiService
{
    /// <summary>
    /// Check insurance eligibility with payer via 270/271 transaction.
    /// </summary>
    Task<InsuranceVerificationResult> CheckEligibilityAsync(
        string payerName, string payerId, string policyNumber,
        string groupNumber, string subscriberId, string subscriberName,
        DateOnly? subscriberDob, string organizationNpi = null);
}
