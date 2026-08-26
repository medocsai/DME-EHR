using System.Security.Claims;

namespace EHR.Services;

/// <summary>
/// Decides which branches the current caller is allowed to see.
///
/// WHY THIS EXISTS
/// Locations arrived as a working filter: a user picked a branch and the screens
/// followed, but nothing stopped them picking a different one. That is right for
/// the owner of a three-depot supplier and wrong for the driver at one of them.
///
/// The allowed set is derived on the SERVER from dbo.UserLocations and nothing
/// else. A location the client asks for is a request, never a permission.
///
/// THE RULE THAT MATTERS MOST
/// An empty grant set sees NOTHING. A restricted user with no rows is a
/// misconfigured user, and a misconfigured user must be shown nothing rather
/// than everything. The shape to avoid, which RehabDox documents finding in its
/// own codebase, is:
///
///     if (allowed.Any()) { query = query.Where(...); }
///
/// which silently drops the filter for exactly the caller it was meant to
/// contain. Nothing here is written that way: the predicate this produces for an
/// empty set is `1=0`, which returns no rows, loudly and on purpose.
///
/// WHO BYPASSES IT, AND WHY THAT IS A DECISION
/// Role 0 (Super Admin) and role 1 (Clinic Admin). A clinic admin administers the
/// whole supplier and is usually the owner; restricting them would defeat the
/// reason branches were added, which is that one person wants to see the whole
/// business AND each depot. Roles 2 and up are the people who work at a depot and
/// are restricted to theirs.
///
/// Do not fold role 1 back in without deciding to. DmeUserLocationScopeTests
/// pins it so the decision fails loudly rather than drifting.
///
/// WHO CALLS IT
/// DmeDb, which turns the set into the SQL predicate every DME read carries.
/// </summary>
public interface IDmeLocationScope
{
    /// <summary>
    /// True when this caller bypasses branch scoping entirely.
    ///
    /// Check this FIRST. An unrestricted caller usually has no grant rows at all,
    /// so their allowed set is empty and would otherwise read as "sees nothing".
    /// </summary>
    bool IsUnrestricted { get; }

    /// <summary>
    /// Branch ids this caller may see. Empty means exactly that: nothing.
    /// Meaningless for an unrestricted caller, who is not filtered at all.
    /// </summary>
    IReadOnlyCollection<int> AllowedLocationIds { get; }
}

/// <inheritdoc cref="IDmeLocationScope"/>
public sealed class DmeLocationScope : IDmeLocationScope
{
    // 0 = Super Admin, 1 = Clinic Admin, 2 = Clinician, 3 = Front Desk,
    // 4 = Biller, 5 = Read Only, 6 = Medical Assistant, 7 = Nurse.
    private const int SuperAdminRole = 0;
    private const int ClinicAdminRole = 1;

    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _config;

    /// <summary>
    /// Resolved once per request and never across requests. The service is
    /// registered scoped, so this field lives exactly as long as the request
    /// whose identity it describes.
    /// </summary>
    private IReadOnlyCollection<int>? _allowed;
    private bool? _unrestricted;

    public DmeLocationScope(IHttpContextAccessor http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public bool IsUnrestricted
    {
        get
        {
            if (_unrestricted.HasValue) return _unrestricted.Value;

            var role = RoleOfCaller();
            _unrestricted = role is SuperAdminRole or ClinicAdminRole;
            return _unrestricted.Value;
        }
    }

    public IReadOnlyCollection<int> AllowedLocationIds
    {
        get
        {
            if (_allowed != null) return _allowed;

            var userId = UserIdOfCaller();
            if (userId == null)
            {
                // No identity means no grants. Reaching here at all implies
                // [Authorize] has already passed, so this is a bug rather than a
                // user state, and an empty set is the safe way to fail.
                _allowed = Array.Empty<int>();
                return _allowed;
            }

            var ids = new List<int>();
            var connectionString = _config.GetConnectionString("DefaultConnection");

            using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            conn.Open();

            // Only ACTIVE branches count. A deactivated branch is not somewhere
            // work happens, and leaving it in the set would let a stale grant
            // keep it alive on screen.
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
                SELECT ul.LocationId
                FROM dbo.UserLocations ul
                JOIN dbo.Locations l ON l.LocationId = ul.LocationId AND l.IsActive = 1
                WHERE ul.UserId = @userId", conn);
            cmd.Parameters.AddWithValue("@userId", userId.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt32(0));

            _allowed = ids;
            return _allowed;
        }
    }

    private int RoleOfCaller()
    {
        var user = _http.HttpContext?.User;
        var raw = user?.FindFirst("Role")?.Value ?? user?.FindFirst(ClaimTypes.Role)?.Value;

        // An unreadable role is treated as the MOST restricted, not the least.
        // Defaulting to 0 here would hand a bypass to anyone whose token was
        // shaped slightly differently than expected.
        return int.TryParse(raw, out var role) ? role : int.MaxValue;
    }

    private int? UserIdOfCaller()
    {
        var user = _http.HttpContext?.User;
        var raw = user?.FindFirst("UserId")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }
}
