namespace EHR.Configuration;

/// <summary>
/// Configuration options for Office Ally integration.
/// Loaded from appsettings.json section "OfficeAlly".
/// Currently only supports Real-Time Eligibility API (270/271).
/// </summary>
public class OfficeAllyOptions
{
    public const string SectionName = "OfficeAlly";

    // ── Real-Time Eligibility API (270/271) ─────────────────────────
    /// <summary>Base URL for the OA Real-Time Eligibility REST API.</summary>
    public string EligibilityApiBaseUrl { get; set; } = "https://edi.officeally.io";

    /// <summary>API Key for OA Real-Time Eligibility REST API (assigned by Office Ally).</summary>
    public string ApiKey { get; set; } = string.Empty;

    // ── Helpers ─────────────────────────────────────────────────────
    /// <summary>Returns true if eligibility API key is configured.</summary>
    public bool HasEligibilityCredentials => !string.IsNullOrEmpty(ApiKey);
}
