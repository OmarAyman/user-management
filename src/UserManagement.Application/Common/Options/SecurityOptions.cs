using System.ComponentModel.DataAnnotations;

namespace UserManagement.Application.Common.Options;

/// <summary>
/// Account lockout thresholds. Configuration rather than literals, so the policy can be tightened for a
/// deployment without touching the code that enforces it.
/// </summary>
public sealed class LockoutOptions
{
    public const string SectionName = "Lockout";

    [Range(1, 20)]
    public int MaxFailedAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutMinutes);
}

/// <summary>
/// Password policy. Length carries the weight and the composition rule is light: mandating symbols pushes
/// people toward predictable substitutions, while length is what actually costs an attacker.
/// </summary>
public sealed class PasswordPolicyOptions
{
    public const string SectionName = "PasswordPolicy";

    [Range(8, 128)]
    public int MinimumLength { get; set; } = 12;

    [Range(16, 512)]
    public int MaximumLength { get; set; } = 128;

    public bool RequireUppercase { get; set; } = true;

    public bool RequireLowercase { get; set; } = true;

    public bool RequireDigit { get; set; } = true;
}
