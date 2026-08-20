using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// Refresh-token persistence. Lookups are always by stored hash; the raw token never reaches a query.
/// </summary>
public sealed class RefreshTokenRepository(ApplicationDbContext context) : IRefreshTokenRepository
{
    /// <remarks>
    /// Returns the token alone. The owning user is deliberately not <c>Include</c>d: the global query filter
    /// applies to included navigations too, so a soft-deleted user would silently arrive as <c>null</c> and the
    /// caller could not tell "unknown token" from "token belonging to a removed account" - two cases that need
    /// different handling. The caller loads the user through the repository's sanctioned opt-out instead.
    /// </remarks>
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.RefreshTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken) =>
        await context.RefreshTokens
            .Where(token => token.FamilyId == familyId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);
}
