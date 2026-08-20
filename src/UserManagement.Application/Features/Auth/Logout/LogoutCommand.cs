using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Enums;

namespace UserManagement.Application.Features.Auth.Logout;

/// <summary>Signs out by revoking the presented refresh token.</summary>
public sealed record LogoutCommand(string? RawRefreshToken);

/// <summary>
/// Revokes the refresh token that was presented.
/// </summary>
/// <remarks>
/// Always succeeds, even when the token is missing or already revoked: sign-out is idempotent, and reporting
/// an error for "you were already signed out" would leave a client stuck with a session it cannot clear.
/// Access tokens are not revocable by design - their 15-minute lifetime is the bound, recorded as residual
/// risk T-04 rather than paid for with a revocation lookup on every request.
/// </remarks>
public sealed class LogoutCommandHandler(
    IRefreshTokenService refreshTokens,
    IUnitOfWork unitOfWork) : ICommandHandler<LogoutCommand>
{
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RawRefreshToken))
        {
            return;
        }

        var revoked = await refreshTokens.RevokeAsync(
            command.RawRefreshToken,
            RevocationReason.Logout,
            cancellationToken);

        if (revoked)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
