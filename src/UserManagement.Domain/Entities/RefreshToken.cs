using UserManagement.Domain.Enums;

namespace UserManagement.Domain.Entities;

/// <summary>
/// One link in a refresh-token rotation chain. Only the SHA-256 hash of the token is stored, so a database
/// disclosure does not hand over usable sessions, and the raw value never reaches a log.
/// </summary>
/// <remarks>
/// Tokens are grouped into families: one family per login, inherited by every rotation. Presenting a token
/// that already has a successor means two clients hold tokens from one lineage - theft - so the family is
/// revoked rather than the individual token. Revoking the family instead of every token the user owns means a
/// compromise on one device does not sign them out everywhere (ADR-0005).
/// </remarks>
public sealed class RefreshToken
{
    // EF Core materialisation.
    private RefreshToken()
    {
        TokenHash = string.Empty;
        CreatedByIp = string.Empty;
    }

    private RefreshToken(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string createdByIp)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public User? User { get; private set; }

    /// <summary>Shared by every token in one rotation lineage. The unit of revocation.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the raw token. The raw token exists only in the client's cookie.</summary>
    public string TokenHash { get; private set; }

    /// <summary>Set when this token is rotated. Its presence is what makes reuse detectable.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedByIp { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedByIp { get; private set; }

    public RevocationReason? RevocationReason { get; private set; }

    /// <summary>True when this token has been rotated, whether or not it was also explicitly revoked.</summary>
    public bool IsRotated => ReplacedByTokenId is not null;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public static RefreshToken Issue(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string createdByIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByIp);

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "A refresh token must expire after it is issued.");
        }

        return new RefreshToken(userId, familyId, tokenHash, createdAt, expiresAt, createdByIp);
    }

    /// <summary>
    /// Marks this token as replaced by <paramref name="successorId"/> during normal rotation.
    /// </summary>
    public void MarkRotated(Guid successorId, DateTimeOffset now, string ipAddress)
    {
        ReplacedByTokenId = successorId;
        Revoke(Enums.RevocationReason.Rotated, now, ipAddress);
    }

    /// <summary>
    /// Revokes the token. Idempotent: an already-revoked token keeps its original reason and timestamp, so the
    /// first cause of revocation is the one preserved for investigation.
    /// </summary>
    public void Revoke(RevocationReason reason, DateTimeOffset now, string? ipAddress = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedByIp = ipAddress;
        RevocationReason = reason;
    }
}
