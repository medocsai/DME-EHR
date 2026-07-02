using System.Collections.Generic;
using EHR.Services.Security.Common;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 1 §5.1: pure-comparison contractors. No DB, no HTTP. These are the
/// peers the guard composes. Tests prove the contract literally: same input
/// → same output, no side effects.
/// </summary>
[Trait("Phase", "1")]
public class Phase1_ContractorTests
{
    // ------------------------------------------------------------------------
    // TenantScopeChecker
    // ------------------------------------------------------------------------
    private readonly TenantScopeChecker _tenant = new();

    [Fact]
    public void TenantScopeChecker_Match_ReturnsTrue()
        => _tenant.IsAllowed(rowTenantId: 7, currentTenantId: 7).Should().BeTrue();

    [Fact]
    public void TenantScopeChecker_Mismatch_ReturnsFalse()
        => _tenant.IsAllowed(rowTenantId: 7, currentTenantId: 8).Should().BeFalse();

    [Fact]
    public void TenantScopeChecker_NullCurrent_ReturnsFalse_NeverAllowUnscoped()
        => _tenant.IsAllowed(rowTenantId: 7, currentTenantId: null).Should().BeFalse();

    // ------------------------------------------------------------------------
    // LocationScopeChecker
    // ------------------------------------------------------------------------
    private readonly LocationScopeChecker _location = new();
    private readonly IReadOnlySet<int> _allowed = new HashSet<int> { 10, 11 };

    [Fact]
    public void LocationScopeChecker_RowInAllowed_ReturnsTrue()
        => _location.IsAllowed(rowLocationId: 10, _allowed, isAdmin: false).Should().BeTrue();

    [Fact]
    public void LocationScopeChecker_RowNotInAllowed_ReturnsFalse()
        => _location.IsAllowed(rowLocationId: 99, _allowed, isAdmin: false).Should().BeFalse();

    [Fact]
    public void LocationScopeChecker_AdminBypassesEvenIfNotInAllowed()
        => _location.IsAllowed(rowLocationId: 99, _allowed, isAdmin: true).Should().BeTrue();

    [Fact]
    public void LocationScopeChecker_LocationLessEntity_ReturnsTrue_ACLDoesntApply()
        => _location.IsAllowed(rowLocationId: null, _allowed, isAdmin: false).Should().BeTrue();

    [Fact]
    public void LocationScopeChecker_EmptyAllowed_NonAdmin_RowLocationDenies()
    {
        var empty = new HashSet<int>();
        _location.IsAllowed(rowLocationId: 10, empty, isAdmin: false).Should().BeFalse();
    }
}
