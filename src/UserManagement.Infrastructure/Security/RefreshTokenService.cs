using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Infrastructure.Configuration;

namespace UserManagement.Infrastructure.Security;

/// <summary>
/// Issues, rotates and revokes refresh tokens, organised into families (one per sign-in).
/// </summary>
/// <remarks>
/// Persistence rule: this service mutates and adds tracked entities but leaves committing to the caller, so a
/// sign-in stays one transaction. The single exception is reuse detection, which commits the family revocation
/// itself - the operation then fails, and a caller that never saves would otherwise discard the very defence
/// the detection exists to apply.
/// </remarks>
public sealed class RefreshTokenService(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IClientInfoProvider clientInfo,
    IOptions<RefreshTokenOptions> options,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    private readonly RefreshTokenOptions _options = options.Value;

    public Task<IssuedRefreshToken> IssueAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // A new sign-in starts a new family, so revoking one compromised lineage later does not sign the user
        // out of their other devices.
        var (_, issued) = Issue(user, Guid.CreateVersion7());
        return Task.FromResult(issued);
    }

    public async Task<RefreshTokenRotation> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw AuthenticationFailedException.InvalidCredentials();
        }

        var presented = await refreshTokens.GetByHashAsync(OpaqueTokenGenerator.Hash(rawToken), cancellationToken);

        if (presented is null)
        {
            throw AuthenticationFailedException.InvalidCredentials();
        }

        // Loaded through the repository's sanctioned soft-delete opt-out, so a removed account is visible here
        // and can be refused explicitly instead of looking like an unknown token.
        var owner = await users.GetByIdIncludingDeletedAsync(presented.UserId, cancellationToken);

        if (owner is null)
        {
            throw AuthenticationFailedException.InvalidCredentials();
        }

        var now = clock.UtcNow;

        // A token that already has a successor means two clients hold tokens from one lineage. That is theft,
        // not a race: the legitimate client would be holding the successor.
        if (presented.IsRotated)
        {
            await RevokeFamilyAsync(presented, now, cancellationToken);

            logger.LogWarning(
                "RefreshTokenReuseDetected for user {UserId} in family {FamilyId} from {IpAddress}",
                presented.UserId,
                presented.FamilyId,
                clientInfo.IpAddress);

            throw AuthenticationFailedException.InvalidCredentials();
        }

        if (!presented.IsActive(now))
        {
            throw AuthenticationFailedException.InvalidCredentials();
        }

        // A user removed after the token was issued must not be able to extend their session.
        if (owner.IsDeleted)
        {
            presented.Revoke(RevocationReason.UserDeleted, now, clientInfo.IpAddress);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw AuthenticationFailedException.InvalidCredentials();
        }

        var (replacementEntity, replacement) = Issue(owner, presented.FamilyId);

        presented.MarkRotated(replacementEntity.Id, now, clientInfo.IpAddress);

        logger.LogInformation(
            "RefreshTokenRotated for user {UserId} in family {FamilyId}",
            presented.UserId,
            presented.FamilyId);

        return new RefreshTokenRotation(owner, replacement);
    }

    public async Task<bool> RevokeAsync(string rawToken, RevocationReason reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var token = await refreshTokens.GetByHashAsync(OpaqueTokenGenerator.Hash(rawToken), cancellationToken);

        if (token is null)
        {
            return false;
        }

        token.Revoke(reason, clock.UtcNow, clientInfo.IpAddress);
        return true;
    }

    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        RevocationReason reason,
        CancellationToken cancellationToken)
    {
        var active = await refreshTokens.GetActiveForUserAsync(userId, cancellationToken);
        var now = clock.UtcNow;

        foreach (var token in active)
        {
            token.Revoke(reason, now, clientInfo.IpAddress);
        }

        if (active.Count > 0)
        {
            logger.LogInformation(
                "RefreshTokensRevoked for user {UserId}: {Count} token(s), reason {Reason}",
                userId,
                active.Count,
                reason);
        }

        return active.Count;
    }

    /// <summary>
    /// Creates a token, attaches it to the user's tracked collection, and returns the raw value. The raw value
    /// exists only in this return path and in the response cookie - never in the database and never in a log.
    /// </summary>
    private (RefreshToken Entity, IssuedRefreshToken Issued) Issue(User user, Guid familyId)
    {
        var now = clock.UtcNow;
        var expiresAt = now.AddDays(_options.LifetimeDays);
        var rawToken = OpaqueTokenGenerator.CreateToken();

        var entity = RefreshToken.Issue(
            user.Id,
            familyId,
            OpaqueTokenGenerator.Hash(rawToken),
            now,
            expiresAt,
            clientInfo.IpAddress);

        user.AddRefreshToken(entity);
        refreshTokens.Add(entity);

        return (entity, new IssuedRefreshToken(rawToken, expiresAt, familyId));
    }

    private async Task RevokeFamilyAsync(RefreshToken presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await refreshTokens.GetFamilyAsync(presented.FamilyId, cancellationToken);

        foreach (var token in family)
        {
            token.Revoke(RevocationReason.ReuseDetected, now, clientInfo.IpAddress);
        }

        // Committed here on purpose: see the persistence rule in the class remarks.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
