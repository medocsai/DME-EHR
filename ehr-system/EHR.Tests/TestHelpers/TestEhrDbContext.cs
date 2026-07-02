using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Tests.TestHelpers;

/// <summary>
/// Test subclass of EhrDbContext. Strips SQL Server-specific column defaults
/// (e.g. HasDefaultValueSql("(getutcdate())")) that SQLite doesn't understand.
///
/// Only used by the SQLite-backed SqlServerFixture. When we eventually swap
/// back to real SQL Server via Testcontainers, tests should instantiate
/// EhrDbContext directly — this shim can be deleted at that point.
/// </summary>
public class TestEhrDbContext : EhrDbContext
{
    public TestEhrDbContext(DbContextOptions<EhrDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var sql = property.GetDefaultValueSql();
                if (sql != null && sql.Contains("getutcdate", StringComparison.OrdinalIgnoreCase))
                {
                    property.SetDefaultValueSql(null);
                }
            }
        }
    }
}
