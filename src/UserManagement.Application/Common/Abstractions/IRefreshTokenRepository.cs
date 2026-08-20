using UserManagement.Domain.Entities;

namespace UserManagement.Application.Common.Abstractions;

/// <summary>Persistence for refresh tokens. Lookups are by hash, never by raw token value.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Finds a token by its stored hash, with the owning user and that user's tokens loaded, because a reuse
    /// detection has to revoke siblings in the same family.
    /// </summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Every token in one rotation lineage, for family-wide revocation.</summary>
    Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    /// <summary>Every not-yet-revoked, not-yet-expired token for a user, across all families.</summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}
