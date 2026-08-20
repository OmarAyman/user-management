using Microsoft.Extensions.Logging;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;

namespace UserManagement.Application.Features.Auth.RefreshSession;

/// <summary>Exchanges a refresh token for a new access token, rotating the refresh token in the process.</summary>
public sealed record RefreshSessionCommand(string? RawRefreshToken);

/// <summary>
/// Issues a new access token from a valid refresh token.
/// </summary>
/// <remarks>
/// Rotation and reuse detection live in <see cref="IRefreshTokenService"/>; this handler exists to re-issue the
/// access token and to persist the rotation. Every failure surfaces as the same generic error, because a
/// caller presenting a refresh token they should not have must learn nothing from the difference between
/// "expired", "revoked" and "never existed".
/// </remarks>
public sealed class RefreshSessionCommandHandler(
    IRefreshTokenService refreshTokens,
    IAccessTokenIssuer accessTokenIssuer,
    IUnitOfWork unitOfWork,
    ILogger<RefreshSessionCommandHandler> logger)
    : ICommandHandler<RefreshSessionCommand, Login.LoginResult>
{
    public async Task<Login.LoginResult> HandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RawRefreshToken))
        {
            throw AuthenticationFailedException.InvalidCredentials();
        }

        var rotation = await refreshTokens.RotateAsync(command.RawRefreshToken, cancellationToken);
        var user = rotation.User;

        var roleName = user.Role?.Name
                       ?? throw new InvalidOperationException($"User '{user.Id}' has no role loaded.");

        var accessToken = accessTokenIssuer.Issue(user, roleName);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SessionRefreshed for {UserId}", user.Id);

        return new Login.LoginResult(
            accessToken,
            rotation.Token,
            new Login.AuthenticatedUser(
                user.Id,
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                roleName));
    }
}
