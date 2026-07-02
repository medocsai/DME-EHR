using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Tests.TestHelpers;

/// <summary>
/// Builds a throwaway EhrDbContext backed by EF InMemory, one isolated DB per call.
/// Used by pure-logic unit tests that happen to need a DbContext dependency.
/// </summary>
public static class InMemoryDbFactory
{
    public static EhrDbContext Create()
    {
        var options = new DbContextOptionsBuilder<EhrDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new EhrDbContext(options);
    }
}
