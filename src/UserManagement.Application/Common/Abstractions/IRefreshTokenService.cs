using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;

namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Issues, rotates and revokes refresh tokens. The raw token value is returned to the caller exactly once, to
/// be written into an httpOnly cookie; only its SHA-256 hash is persisted.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Starts a new token family. Called on successful sign-in.</summary>
    Task<IssuedRefreshToken> IssueAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Validates a presented token and rotates it, returning the owning user and the replacement token.
    /// </summary>
    /// <remarks>
    /// Presenting a token that has already been rotated means two clients hold tokens from one lineage, which
    /// is treated as theft: the whole family is revoked and the attempt fails.
    /// </remarks>
    Task<RefreshTokenRotation> RotateAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>Revokes a single presented token. Used by sign-out. Returns false when it was not found.</summary>
    Task<bool> RevokeAsync(string rawToken, RevocationReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every active token for a user, across all families. Used when credentials or privileges change.
    /// Returns the number of tokens revoked.
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId, RevocationReason reason, CancellationToken cancellationToken);
}

/// <summary>The one-time raw token handed back to the client, with its expiry and family.</summary>
public sealed record IssuedRefreshToken(string RawToken, DateTimeOffset ExpiresAt, Guid FamilyId);

/// <summary>The result of a successful rotation: who the token belonged to, and its replacement.</summary>
public sealed record RefreshTokenRotation(User User, IssuedRefreshToken Token);
