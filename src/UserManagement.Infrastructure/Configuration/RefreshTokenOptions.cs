using System.ComponentModel.DataAnnotations;

namespace UserManagement.Infrastructure.Configuration;

/// <summary>Refresh-token lifetime and cookie settings.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    [Range(1, 90)]
    public int LifetimeDays { get; set; } = 7;

    /// <summary>Name of the httpOnly cookie carrying the raw token.</summary>
    [Required]
    public string CookieName { get; set; } = "refreshToken";

    /// <summary>
    /// Scoping the cookie to the auth routes means it is not attached to every API call, so the only requests
    /// that can carry it are the two that need it.
    /// </summary>
    [Required]
    public string CookiePath { get; set; } = "/api/auth";

    /// <summary>
    /// Off only for local HTTP development. Any deployed environment must leave this on; the startup guard
    /// warns when it is disabled outside Development.
    /// </summary>
    public bool SecureCookie { get; set; } = true;
}
