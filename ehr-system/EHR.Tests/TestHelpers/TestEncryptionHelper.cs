using EHR.Helpers;
using Microsoft.Extensions.Configuration;

namespace EHR.Tests.TestHelpers;

/// <summary>
/// Builds a real EncryptionHelper with a fixed test key. Deterministic hashes across
/// tests, no Moq gymnastics (GenerateSearchHash isn't virtual so it can't be mocked).
/// </summary>
public static class TestEncryptionHelper
{
    public static EncryptionHelper Create()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = "unit-test-fixed-key-not-for-production-use"
            })
            .Build();
        return new EncryptionHelper(config);
    }
}
