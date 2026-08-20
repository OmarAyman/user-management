using System.ComponentModel.DataAnnotations;

namespace UserManagement.Api.Configuration;

/// <summary>
/// Auth rate-limit settings. Configurable rather than hard-coded so a deployment can tighten the window, and
/// so a test can prove the limiter works without every other test tripping over it.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests permitted per window, per client address.</summary>
    [Range(1, 10000)]
    public int AuthPermitLimit { get; set; } = 10;

    [Range(1, 3600)]
    public int AuthWindowSeconds { get; set; } = 60;
}
