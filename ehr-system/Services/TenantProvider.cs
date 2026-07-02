namespace EHR.Services
{
    public interface ITenantProvider
    {
        int? TenantId { get; set; }
        string? TenantSubdomain { get; set; }
    }

    public class TenantProvider : ITenantProvider
    {
        public int? TenantId { get; set; }
        public string? TenantSubdomain { get; set; }
    }

    /// <summary>
    /// Interface for location context provider - provides location-level filtering within a tenant.
    /// Location filtering is a sub-layer within tenant isolation.
    /// </summary>
    public interface ILocationProvider
    {
        /// <summary>
        /// Current location ID. Null means "all locations" (Super Admin or Admin viewing all).
        /// When set, all patient-related queries are filtered by this location.
        /// </summary>
        int? LocationId { get; set; }

        /// <summary>
        /// Current location name for display purposes
        /// </summary>
        string? LocationName { get; set; }
    }

    /// <summary>
    /// Scoped service that holds the current location context for the request.
    /// Set by the LocationResolutionMiddleware after tenant resolution.
    /// </summary>
    public class LocationProvider : ILocationProvider
    {
        public int? LocationId { get; set; }
        public string? LocationName { get; set; }
    }
}
