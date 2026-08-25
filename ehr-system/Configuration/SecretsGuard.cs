using System.Text;

namespace EHR.Configuration;

/// <summary>
/// Refuses to start the application with missing or publicly-known secrets.
///
/// WHY THIS EXISTS
/// The JWT signing key was read as:
///
///     var jwtKey = config["Jwt:Key"] ?? "YourSecretKeyHere12345678901234567890";
///
/// in two places. If configuration were ever missing or misspelled in
/// production, the app would not fail. It would start, sign tokens with a string
/// that is in the source code, and behave completely normally. Anyone who has
/// read the repository could then mint a token for any user in any tenant,
/// including the Super Admin, and every guard in this codebase would accept it.
///
/// A silent fallback to a known key is worse than no key at all, because nothing
/// looks wrong.
///
/// WHAT IT CHECKS
///   - Jwt:Key and Encryption:Key are present
///   - neither is one of the placeholder values shipped in source or samples
///   - both are long enough to be worth having (HS256 wants >= 32 bytes; a
///     shorter key weakens the signature regardless of how random it looks)
///
/// WHY IT IS RELAXED IN DEVELOPMENT
/// A developer cloning the repo should be able to run the app. In Development
/// the same problems are logged as warnings so they are visible, but do not
/// block. Anywhere else they stop startup.
///
/// WHAT IT DELIBERATELY DOES NOT DO
/// It does not rotate anything. The keys currently in appsettings.json are in
/// git history, so they must be treated as compromised and rotated, but rotating
/// the encryption key means re-encrypting every encrypted row and rotating the
/// JWT key logs everyone out. Both are operational decisions with real
/// consequences, not something a startup check should do on its own.
///
/// WHO CALLS IT
/// Program.cs, once, before the application is built.
/// </summary>
public static class SecretsGuard
{
    /// <summary>
    /// Placeholder values that appear in source, samples or documentation.
    /// A configured value matching any of these is not a secret.
    /// </summary>
    private static readonly string[] KnownPlaceholders =
    {
        "YourSecretKeyHere12345678901234567890",
        "YourSecretKeyHere",
        "ChangeMe",
        "changeme",
        "secret",
        "your-secret-key",
    };

    /// <summary>Minimum key length. HS256 signs with a 256-bit key.</summary>
    private const int MinimumKeyBytes = 32;

    /// <summary>
    /// Validate the secrets this application cannot safely run without.
    /// Throws in every environment except Development, where it warns instead.
    /// </summary>
    public static void Validate(IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        var problems = new List<string>();

        Check(config["Jwt:Key"], "Jwt:Key",
            "tokens would be signed with a key that is in the source code, so anyone " +
            "who has read the repository could mint a Super Admin token", problems);

        Check(config["Encryption:Key"], "Encryption:Key",
            "PHI at rest would be encrypted with a key that is not secret, which is " +
            "the same as not encrypting it", problems);

        if (problems.Count == 0) return;

        var detail = string.Join(Environment.NewLine + "  - ", problems);

        if (env.IsDevelopment())
        {
            logger.LogWarning(
                "Insecure secret configuration detected (allowed in Development, would stop startup elsewhere):{NewLine}  - {Detail}",
                Environment.NewLine, detail);
            return;
        }

        throw new InvalidOperationException(
            "Refusing to start with insecure secret configuration:" + Environment.NewLine +
            "  - " + detail + Environment.NewLine +
            "Set these outside source control, for example as the environment variables " +
            "Jwt__Key and Encryption__Key, which ASP.NET Core layers over appsettings.json.");
    }

    private static void Check(string? value, string name, string consequence, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{name} is not configured: {consequence}.");
            return;
        }

        if (KnownPlaceholders.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{name} is a placeholder value from source or samples: {consequence}.");
            return;
        }

        if (Encoding.UTF8.GetByteCount(value) < MinimumKeyBytes)
        {
            problems.Add(
                $"{name} is shorter than {MinimumKeyBytes} bytes, which weakens it regardless " +
                $"of how random it looks: {consequence}.");
        }
    }
}
