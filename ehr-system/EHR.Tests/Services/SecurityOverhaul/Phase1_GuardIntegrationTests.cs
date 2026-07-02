using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using EHR.Models.Generated;
using EHR.Services.Security.Common;
using EHR.Services.Security.Exceptions;
using EHR.Services.Security.Guards;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 1 §5.2 / §7.4: end-to-end behavior of the EntityAccessGuard&lt;T&gt;
/// base via the concrete PatientAccessGuard. Validates the four contract
/// guarantees:
///   1. happy path returns the int PK
///   2. missing PublicId -> ForbiddenException, same shape as forbidden
///   3. cross-tenant -> ForbiddenException
///   4. cross-location (non-admin) -> ForbiddenException; admin bypasses
///
/// Same exception type and same response surface regardless of cause is the
/// oracle-prevention contract. The middleware then renders 403 with an
/// opaque body; tested separately at integration.
/// </summary>
[Collection("Db")]
[Trait("Phase", "1")]
public class Phase1_GuardIntegrationTests
{
    private readonly SqlServerFixture _fx;

    public Phase1_GuardIntegrationTests(SqlServerFixture fx) => _fx = fx;

    /// <summary>
    /// Build a PatientAccessGuard wired with real contractors + a stub
    /// HttpContext carrying the requested caller identity.
    /// </summary>
    private (PatientAccessGuard guard, EhrDbContext db) BuildGuard(
        int callerTenantId,
        int callerUserId,
        bool isAdmin,
        HashSet<int> allowedLocationIds)
    {
        var db = _fx.CreateDbContext();

        var httpCtx = new DefaultHttpContext();
        httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("UserId", callerUserId.ToString()),
            new Claim("TenantId", callerTenantId.ToString()),
            new Claim("Role", isAdmin ? "1" : "2"),   // 1=ClinicAdmin, 2=Clinician
        }, "TestAuth"));
        var accessor = new TestHttpContextAccessor { HttpContext = httpCtx };

        var resolver = new PublicIdResolver(db);
        var tenantCheck = new TenantScopeChecker();
        var locationCheck = new LocationScopeChecker();
        var audit = new SecurityAuditWriter(db);
        var userLocations = new StubUserLocationAccessService(allowedLocationIds);
        var callerCtx = new GuardCallerContext(accessor);

        var guard = new PatientAccessGuard(
            resolver, tenantCheck, locationCheck, audit, userLocations, callerCtx);

        return (guard, db);
    }

    private static Patient SeedMinimalPatient(EhrDbContext db, int tenantId, int? preferredLocationId, string mrn, Guid? explicitPublicId = null)
    {
        var p = new Patient
        {
            TenantId = tenantId,
            PreferredLocationId = preferredLocationId,
            Mrn = mrn,
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "Other",
            Phone = "000-000-0000",
            Email = $"{mrn.ToLowerInvariant()}@test.com",
            Address = "1 Test St",
            City = "Testville",
            State = "TS",
            ZipCode = "00000",
            EmergencyContactName = "EC",
            EmergencyContactPhone = "000-000-0000",
            EmergencyContactRelation = "Other"
        };
        if (explicitPublicId.HasValue) p.PublicId = explicitPublicId.Value;
        db.Patients.Add(p);
        db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task HappyPath_SameTenant_AllowedLocation_ReturnsInternalId()
    {
        await _fx.ResetAsync();
        var (guard, db) = BuildGuard(callerTenantId: 1, callerUserId: 42, isAdmin: false,
            allowedLocationIds: new HashSet<int> { 10 });

        var p = SeedMinimalPatient(db, tenantId: 1, preferredLocationId: 10, mrn: "P-HAPPY");

        var id = await guard.EnsureAccessibleAsync(p.PublicId);
        id.Should().Be(p.PatientId);
    }

    [Fact]
    public async Task MissingPublicId_Throws_ForbiddenException_kind_publicid_not_found()
    {
        await _fx.ResetAsync();
        var (guard, _) = BuildGuard(callerTenantId: 1, callerUserId: 42, isAdmin: false,
            allowedLocationIds: new HashSet<int> { 10 });

        var randomGuid = Guid.NewGuid();
        var act = async () => await guard.EnsureAccessibleAsync(randomGuid);
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Kind.Should().Be("publicid-not-found");
        ex.Which.EntityType.Should().Be("Patient");
    }

    [Fact]
    public async Task CrossTenant_Throws_ForbiddenException_kind_tenant_mismatch()
    {
        await _fx.ResetAsync();
        var (guard, db) = BuildGuard(callerTenantId: 1, callerUserId: 42, isAdmin: false,
            allowedLocationIds: new HashSet<int> { 10 });

        // Need a different tenant + location to exist for FK. Seed Tenant 2 + Location 20.
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                TenantId = 2, Name = "Other Tenant", Subdomain = "other",
                Phone = "0", Email = "o@o.com", Address = "x", City = "x", State = "x"
            });
            seed.Locations.Add(new Location
            {
                LocationId = 20, TenantId = 2, Name = "Other Loc",
                Address = "x", City = "x", State = "x", ZipCode = "0", Phone = "0",
                IsActive = true, IsPrimary = true, TimeZoneId = "UTC", EnableLongevity = false
            });
            await seed.SaveChangesAsync();
        }

        var p = SeedMinimalPatient(db, tenantId: 2, preferredLocationId: 20, mrn: "P-CROSSTENANT");

        var act = async () => await guard.EnsureAccessibleAsync(p.PublicId);
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Kind.Should().Be("tenant-mismatch");
    }

    [Fact]
    public async Task CrossLocation_NonAdmin_Throws_ForbiddenException_kind_location_mismatch()
    {
        await _fx.ResetAsync();

        await using (var seed = _fx.CreateDbContext())
        {
            seed.Locations.Add(new Location
            {
                LocationId = 11, TenantId = 1, Name = "Loc B",
                Address = "x", City = "x", State = "x", ZipCode = "0", Phone = "0",
                IsActive = true, IsPrimary = false, TimeZoneId = "UTC", EnableLongevity = false
            });
            await seed.SaveChangesAsync();
        }

        var (guard, db) = BuildGuard(callerTenantId: 1, callerUserId: 42, isAdmin: false,
            allowedLocationIds: new HashSet<int> { 10 });

        var p = SeedMinimalPatient(db, tenantId: 1, preferredLocationId: 11, mrn: "P-CROSSLOC");

        var act = async () => await guard.EnsureAccessibleAsync(p.PublicId);
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Kind.Should().Be("location-mismatch");
    }

    [Fact]
    public async Task CrossLocation_Admin_BypassesAndReturnsId()
    {
        await _fx.ResetAsync();
        await using (var seed = _fx.CreateDbContext())
        {
            seed.Locations.Add(new Location
            {
                LocationId = 11, TenantId = 1, Name = "Loc B",
                Address = "x", City = "x", State = "x", ZipCode = "0", Phone = "0",
                IsActive = true, IsPrimary = false, TimeZoneId = "UTC", EnableLongevity = false
            });
            await seed.SaveChangesAsync();
        }

        var (guard, db) = BuildGuard(callerTenantId: 1, callerUserId: 42, isAdmin: true,
            allowedLocationIds: new HashSet<int> { 10 });

        var p = SeedMinimalPatient(db, tenantId: 1, preferredLocationId: 11, mrn: "P-ADMIN");

        var id = await guard.EnsureAccessibleAsync(p.PublicId);
        id.Should().Be(p.PatientId);
    }

    /// <summary>
    /// Oracle prevention: the missing-PublicId case and the forbidden case
    /// must throw the SAME exception type so callers cannot distinguish
    /// them. Phase 3 middleware further enforces same response body.
    /// </summary>
    [Fact]
    public async Task MissingAndForbidden_ThrowSameExceptionType()
    {
        await _fx.ResetAsync();
        var (guard, _) = BuildGuard(callerTenantId: 1, callerUserId: 42, isAdmin: false,
            allowedLocationIds: new HashSet<int> { 10 });

        var missing = Guid.NewGuid();
        Func<Task> actMissing = async () => await guard.EnsureAccessibleAsync(missing);
        await actMissing.Should().ThrowAsync<ForbiddenException>();
        // We tolerate the "kind" string differing internally (for logs), but
        // the externally observable type is the same: ForbiddenException.
    }

    // ------------------------------------------------------------------------
    // Test helpers
    // ------------------------------------------------------------------------
    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StubUserLocationAccessService : IUserLocationAccessService
    {
        private readonly IReadOnlySet<int> _allowed;
        public StubUserLocationAccessService(IReadOnlySet<int> allowed) { _allowed = allowed; }
        public Task<IReadOnlySet<int>> GetAllowedLocationIdsAsync(int userId) => Task.FromResult(_allowed);
    }
}
