namespace UserManagement.Api.Configuration;

/// <summary>
/// Cross-origin settings for the SPA. Credentials must be allowed because the refresh token travels as a
/// cookie, which means the origin list has to be explicit - a wildcard origin with credentials is invalid, and
/// pretending otherwise would break at runtime rather than at review.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "SpaClient";

    public string[] AllowedOrigins { get; set; } = [];
}
