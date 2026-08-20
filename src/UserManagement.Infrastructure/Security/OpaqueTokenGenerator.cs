using System.Security.Cryptography;

namespace UserManagement.Infrastructure.Security;

/// <summary>
/// Generates and hashes opaque refresh tokens.
/// </summary>
/// <remarks>
/// Refresh tokens are random bytes rather than JWTs on purpose: they carry no claims, so there is nothing to
/// read out of one, and revocation is a database fact rather than a signature problem. Only the hash is stored,
/// so a database disclosure yields no usable session.
/// </remarks>
public static class OpaqueTokenGenerator
{
    /// <summary>256 bits of entropy - far beyond guessing range, and it hashes to a fixed 64-character column.</summary>
    private const int TokenBytes = 32;

    /// <summary>Creates a new URL-safe token. The raw value is returned exactly once and never persisted.</summary>
    public static string CreateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>
    /// Lowercase hex SHA-256 of the token. A plain hash without a salt is correct here: the input is 256 bits
    /// of uniform randomness, so there is no dictionary to attack and nothing for a work factor to slow down.
    /// </summary>
    public static string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
