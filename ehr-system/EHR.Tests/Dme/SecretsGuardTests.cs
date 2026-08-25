using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EHR.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// The application must not start with a signing key that anyone can read.
///
/// WHY THIS EXISTS
/// The JWT signing key was read in two places as:
///
///     config["Jwt:Key"] ?? "YourSecretKeyHere12345678901234567890"
///
/// A deployment with a missing or misspelled setting would not fail. It would
/// start, sign tokens with a string printed in the source, and look completely
/// healthy. Anyone who had read the repository could then mint a token for any
/// user in any tenant, Super Admin included, and every guard in this codebase
/// would happily accept it.
///
/// A silent fallback to a known key is worse than no key, because nothing looks
/// wrong.
/// </summary>
public class SecretsGuardTests
{
    private static IConfiguration Config(string? jwt, string? encryption) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = jwt,
                ["Encryption:Key"] = encryption,
            })
            .Build();

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "EHR";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private const string Good = "a-real-secret-that-is-long-enough-to-matter";

    private static void Validate(string? jwt, string? encryption, string environment) =>
        SecretsGuard.Validate(Config(jwt, encryption), new Env { EnvironmentName = environment },
                              NullLogger.Instance);

    [Fact]
    public void Production_RefusesToStartWithThePlaceholderSigningKey()
    {
        var act = () => Validate("YourSecretKeyHere12345678901234567890", Good, Environments.Production);

        act.Should().Throw<InvalidOperationException>(
                "this exact string was the hardcoded fallback; a deployment using it can be " +
                "forged against by anyone with the source")
            .WithMessage("*Jwt:Key*");
    }

    [Fact]
    public void Production_RefusesToStartWithNoSigningKey()
    {
        var act = () => Validate(null, Good, Environments.Production);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Key*");
    }

    [Fact]
    public void Production_RefusesToStartWithNoEncryptionKey()
    {
        var act = () => Validate(Good, null, Environments.Production);

        act.Should().Throw<InvalidOperationException>(
                "PHI encrypted with a key nobody set is not encrypted")
            .WithMessage("*Encryption:Key*");
    }

    [Fact]
    public void Production_RefusesAKeyTooShortToSignWith()
    {
        var act = () => Validate("short", Good, Environments.Production);

        act.Should().Throw<InvalidOperationException>(
            "HS256 signs with a 256-bit key; a shorter one weakens the signature no matter " +
            "how random it looks");
    }

    [Fact]
    public void Production_StartsWhenBothSecretsAreProperlySet()
    {
        var act = () => Validate(Good, Good, Environments.Production);
        act.Should().NotThrow();
    }

    [Fact]
    public void Development_WarnsButStillStarts()
    {
        var act = () => Validate("YourSecretKeyHere12345678901234567890", null, Environments.Development);

        act.Should().NotThrow(
            "a developer cloning the repository must be able to run the app; the same problems " +
            "are logged as warnings there so they stay visible");
    }

    /// <summary>
    /// The fallback itself must be gone from production code, not merely guarded
    /// against at startup. A guard can be bypassed by a future code path; a
    /// missing fallback cannot.
    /// </summary>
    [Fact]
    public void NoProductionCode_FallsBackToAHardcodedSigningKey()
    {
        var root = ProductionRoot();
        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}EHR.Tests{Path.DirectorySeparatorChar}"))
            // SecretsGuard names the placeholders on purpose, in order to reject them.
            .Where(f => !f.EndsWith("SecretsGuard.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("YourSecretKeyHere"))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty(
            "a default signing key must not exist anywhere in production code. " +
            $"Offending files: {string.Join(", ", offenders)}");
    }

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}
