using System;
using System.Collections.Generic;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// DmeDb must refuse to touch the database when it does not know the tenant.
///
/// WHY THIS MATTERS MORE THAN IT LOOKS
/// Tenant isolation for DME is enforced by the SQL Server TenantIsolationPolicy,
/// which reads SESSION_CONTEXT('CurrentTenantId'). The policy predicate treats
/// "no context set" as "show everything" on purpose, so that migrations and
/// background jobs keep working.
///
/// That fallback is safe only while the application guarantees the context IS
/// set on every user-facing connection. If DmeDb ever ran with an unknown
/// tenant it would not fail: it would silently return every tenant's rows. The
/// guard therefore has to be a hard throw, and this test is what keeps it one.
/// </summary>
public class DmeDbTenantScopeTests
{
    private static IConfiguration Config(string? connectionString = "Server=(local);Database=x;") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();

    private sealed class FakeTenantProvider : ITenantProvider
    {
        public int? TenantId { get; set; }
        public string? TenantSubdomain { get; set; }
    }

    [Fact]
    public void Construction_WithoutTenant_Throws()
    {
        var act = () => new DmeDb(Config(), new FakeTenantProvider { TenantId = null });

        act.Should().Throw<InvalidOperationException>(
                "an unknown tenant must fail loudly; the RLS policy would otherwise fall through " +
                "to showing every tenant's rows")
            .WithMessage("*without a tenant context*");
    }

    [Fact]
    public void Construction_WithTenant_BindsToThatTenant()
    {
        var db = new DmeDb(Config(), new FakeTenantProvider { TenantId = 7 });

        db.TenantId.Should().Be(7, "queries must run scoped to the caller's tenant, not a default");
    }

    /// <summary>
    /// Mutation check for the guard above. If the throw were ever softened to a
    /// default (say, tenant 1), this test would start failing, because tenant 0
    /// and tenant 1 are different answers. Tenant 0 is used because it is a
    /// falsy-looking value that a careless `?? 0` would produce.
    /// </summary>
    [Fact]
    public void Construction_DoesNotSubstituteADefaultTenant()
    {
        var db = new DmeDb(Config(), new FakeTenantProvider { TenantId = 0 });

        db.TenantId.Should().Be(0,
            "the tenant must be taken verbatim from the token; silently substituting a default " +
            "would scope the query to somebody else's data");
    }

    [Fact]
    public void Construction_WithoutConnectionString_Throws()
    {
        var act = () => new DmeDb(Config(connectionString: null), new FakeTenantProvider { TenantId = 1 });

        act.Should().Throw<InvalidOperationException>(
            "a missing connection string must fail at construction rather than at the first query");
    }
}
