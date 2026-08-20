using System.Security.Cryptography;
using UserManagement.Infrastructure.Configuration;

namespace UserManagement.Api.Configuration;

/// <summary>
/// Makes it impossible to run this API on a committed or missing signing key.
/// </summary>
/// <remarks>
/// Outside Development a missing, placeholder or too-short key stops startup. Inside Development an absent key
/// is replaced by an ephemeral random one, so a reviewer can clone the repository and run it without any
/// secret ever being checked in. Tokens then do not survive a restart, which is the correct trade-off locally.
/// </remarks>
public static class JwtKeyGuard
{
    public static void EnsureSigningKey(ConfigurationManager configuration, IHostEnvironment environment, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var key = configuration[$"{JwtOptions.SectionName}:Key"];
        var isUsable = !string.IsNullOrWhiteSpace(key)
                       && key.Length >= JwtOptions.MinimumKeyBytes
                       && !string.Equals(key, JwtOptions.PlaceholderKey, StringComparison.Ordinal);

        if (isUsable)
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{JwtOptions.SectionName}:Key is missing, is still the example placeholder, or is shorter than " +
                $"{JwtOptions.MinimumKeyBytes} characters. Supply it through an environment variable or a secret " +
                "store; it must never be committed.");
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        configuration[$"{JwtOptions.SectionName}:Key"] = generated;

        logger.LogWarning(
            "No usable {Section}:Key was configured, so an ephemeral development key was generated. " +
            "Tokens will not survive a restart. Set the key with 'dotnet user-secrets set \"{Section}:Key\" \"<value>\"' " +
            "to keep sessions across restarts",
            JwtOptions.SectionName,
            JwtOptions.SectionName);
    }
}
