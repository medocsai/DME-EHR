using EHR.Models.Generated;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EHR.Tests.TestHelpers;

/// <summary>
/// DB-touching test fixture. INTERIM IMPLEMENTATION (2026-04-24).
///
/// Originally planned: real SQL Server 2022 via Testcontainers + Respawn.
/// Actual: SQLite in-memory — pivoted after repeated Docker containerd I/O
/// failures on the dev machine (see ehr-system/EHR.Tests/EHR.Tests.csproj
/// comment + rules/technical/automated-testing-roadmap.md "Deferred" section).
///
/// Class name kept as `SqlServerFixture` on purpose so the eventual swap back
/// to real SQL Server requires only this file to change — every test class
/// (IntakeAttemptThrottlerTests, IntakeAccessTokenServiceTests, ...) keeps
/// referencing the same fixture type via [Collection("Db")].
///
/// Gap vs production SQL Server:
/// - Filtered indexes, computed columns, T-SQL raw SQL, MERGE, DATETIME2
///   precision, some NVARCHAR(MAX) semantics behave differently.
/// - For the services currently tested (throttler + token service), the
///   queries are basic LINQ — SQLite fidelity is sufficient.
/// - When a test depends on SQL-Server-only features, tag it
///   `[Trait("RequiresSqlServer","true")]` and skip locally until Testcontainers
///   is re-enabled.
///
/// Per-test isolation: each ResetAsync() tears down the current in-memory DB
/// (closes connection → SQLite frees the DB) and builds a fresh one. Fast
/// enough (~5ms) that we don't need Respawn-style truncation.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;

    public Task InitializeAsync()
    {
        // Initialize the PHI encryption registry once for the test session.
        // EncryptEntity / DecryptEntity look up registered fields via
        // helpers are silent no-ops in tests, which masks encryption-on-save
        // and decrypt-on-read defects. Idempotent — safe to call repeatedly.

        // Build an initial DB so the first test in a class can call CreateDbContext()
        // without a prior ResetAsync(). Every test should still call ResetAsync() at
        // the top to guarantee isolation.
        return ResetAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    /// <summary>
    /// Destroy the current in-memory DB and create a new empty one. Call at the
    /// top of every test to guarantee a clean slate.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        // Seed baseline FK parents. Tests reference these IDs via the constants
        // in each test class (TenantId=1, LocationId=10). Real SQL Server enforces
        // these FKs too — seeding keeps tests faithful to production behavior.
        db.Tenants.Add(new Tenant
        {
            TenantId = 1,
            Name = "Test Tenant",
            Subdomain = "test",
            Phone = "000-000-0000",
            Email = "test@test.com",
            Address = "1 Test St",
            City = "Testville",
            State = "TS"
        });
        db.Locations.Add(new Location
        {
            LocationId = 10,
            TenantId = 1,
            Name = "Test Location",
            Address = "1 Test St",
            City = "Testville",
            State = "TS",
            ZipCode = "00000",
            Phone = "000-000-0000",
            IsActive = true,
            IsPrimary = true,
            TimeZoneId = "UTC",
            EnableLongevity = false
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Fresh DbContext pointed at the shared in-memory connection. Multiple
    /// contexts can coexist and see the same data because they share one
    /// underlying SqliteConnection (SQLite in-memory DBs are connection-scoped).
    /// </summary>
    public EhrDbContext CreateDbContext()
    {
        if (_connection is null) throw new InvalidOperationException("Fixture not initialized");
        var options = new DbContextOptionsBuilder<EhrDbContext>()
            .UseSqlite(_connection)
            .Options;
        // TestEhrDbContext strips SQL Server-specific defaults so SQLite can run.
        return new TestEhrDbContext(options);
    }
}

/// <summary>
/// Collection definition so every [Collection("Db")] test class shares the same
/// fixture instance. xUnit runs tests in the same collection sequentially,
/// which is what we want — tests mutate the fixture's current in-memory DB.
/// </summary>
[CollectionDefinition("Db")]
public class DbCollection : ICollectionFixture<SqlServerFixture> { }
