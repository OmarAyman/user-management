using System.ComponentModel.DataAnnotations;

namespace UserManagement.Infrastructure.Configuration;

/// <summary>
/// JWT signing and validation settings. Validated at startup with <c>ValidateOnStart</c>, so a misconfigured
/// deployment fails immediately and loudly rather than issuing tokens nobody can validate.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Value that must never survive into a real environment. A startup guard refuses to boot outside
    /// Development if the configured key still equals this.
    /// </summary>
    public const string PlaceholderKey = "REPLACE_WITH_A_32_BYTE_MINIMUM_SECRET";

    /// <summary>HMAC-SHA256 requires at least 256 bits of key material.</summary>
    public const int MinimumKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Signing key. Never committed: supplied through User Secrets in development and an environment variable
    /// or orchestrator secret in production. In Development only, an absent key is replaced by an ephemeral
    /// random one at startup so nothing has to be checked in.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Short by design. It bounds how long an already-issued token stays valid after sign-out or a demotion,
    /// which is the trade-off recorded as residual risk T-04 instead of a revocation lookup on every request.
    /// </summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;
}
