using UserManagement.Application.Common.Exceptions;

namespace UserManagement.Api.Validation;

/// <summary>
/// Converts the base64 concurrency token a client round-trips into the bytes EF Core compares.
/// </summary>
public static class ConcurrencyToken
{
    /// <summary>True when the value is well-formed base64. Used by the validators.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Convert.TryFromBase64String(value, new byte[value.Length], out _);

    /// <summary>
    /// Parses a validated token.
    /// </summary>
    /// <remarks>
    /// The validators reject a malformed token first, so reaching the failure here means the filter was
    /// bypassed. It still throws a proper validation error rather than a format exception, because a 400 that
    /// names the field is more useful than a 500 either way.
    /// </remarks>
    public static byte[] Parse(string? value)
    {
        if (!IsValid(value))
        {
            throw ValidationException.ForField(
                "rowVersion",
                "The concurrency token is missing or malformed. Reload the record and try again.");
        }

        return Convert.FromBase64String(value!);
    }
}
