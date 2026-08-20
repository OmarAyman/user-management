using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Common.Options;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Features.Auth.Login;

/// <summary>
/// Authenticates a user, or fails in a way that tells an attacker nothing.
/// </summary>
/// <remarks>
/// <para>
/// Four outcomes, only one of which is distinguishable from outside:
/// </para>
/// <list type="table">
///   <item><description>unknown username -> INVALID_CREDENTIALS</description></item>
///   <item><description>wrong password, locked or not -> INVALID_CREDENTIALS</description></item>
///   <item><description>soft-deleted account, correct password -> INVALID_CREDENTIALS</description></item>
///   <item><description>correct password, account locked -> ACCOUNT_LOCKED with a retry time</description></item>
/// </list>
/// <para>
/// Disclosing the lockout only after the password has been proved correct is what reconciles "never reveal
/// whether an account exists" with "tell a legitimate user why they cannot get in": a caller who supplied the
/// right password already knows the account exists (ADR-0006).
/// </para>
/// </remarks>
public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IRefreshTokenService refreshTokens,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    IClientInfoProvider clientInfo,
    IOptions<LockoutOptions> lockoutOptions,
    ILogger<LoginCommandHandler> logger) : ICommandHandler<LoginCommand, LoginResult>
{
    private readonly LockoutOptions _lockout = lockoutOptions.Value;

    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetForAuthenticationAsync(command.Username, cancellationToken);

        if (user is null)
        {
            // Verify against a fixed dummy hash so an unknown username costs the same as a wrong password.
            // Without this, response timing alone enumerates accounts.
            passwordHasher.VerifyDummy(command.Password);
            LogFailure(command.Username, "UnknownUser");

            throw AuthenticationFailedException.InvalidCredentials();
        }

        var verification = passwordHasher.Verify(user.PasswordHash, command.Password);

        if (verification == PasswordVerificationOutcome.Failed)
        {
            return await RejectFailedPasswordAsync(user, cancellationToken);
        }

        // The password is correct from here on, so anything below discloses nothing the caller did not know.
        var now = clock.UtcNow;

        if (user.IsDeleted)
        {
            // Same response as a wrong password: a removed account must not be discoverable (BR-05).
            LogFailure(user.Username, "Deleted");
            throw AuthenticationFailedException.InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            LogFailure(user.Username, "LockedOut");
            throw AuthenticationFailedException.AccountLocked(user.LockoutEndAt!.Value - now);
        }

        if (verification == PasswordVerificationOutcome.SuccessRehashNeeded)
        {
            // Upgrades the stored hash to current parameters without rotating the security stamp: the
            // credential has not changed, so existing sessions must survive.
            user.UpgradePasswordHash(passwordHasher.Hash(command.Password));
        }

        user.RecordSuccessfulLogin(now);

        var roleName = user.Role?.Name
                       ?? throw new InvalidOperationException($"User '{user.Id}' has no role loaded.");

        var accessToken = accessTokenIssuer.Issue(user, roleName);
        var refreshToken = await refreshTokens.IssueAsync(user, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "LoginSucceeded for {Username} ({UserId}) from {IpAddress}",
            user.Username,
            user.Id,
            clientInfo.IpAddress);

        return new LoginResult(
            accessToken,
            refreshToken,
            new AuthenticatedUser(user.Id, user.Username, user.Email, user.FirstName, user.LastName, roleName));
    }

    private async Task<LoginResult> RejectFailedPasswordAsync(User user, CancellationToken cancellationToken)
    {
        var wasLockedOut = user.IsLockedOut(clock.UtcNow);

        user.RecordFailedLogin(clock.UtcNow, _lockout.MaxFailedAttempts, _lockout.LockoutDuration);

        if (!wasLockedOut && user.IsLockedOut(clock.UtcNow))
        {
            logger.LogWarning(
                "AccountLocked for {Username} ({UserId}) until {LockoutEnd} from {IpAddress}",
                user.Username,
                user.Id,
                user.LockoutEndAt,
                clientInfo.IpAddress);
        }

        // The counter must survive the failed attempt, so this is saved before the exception is thrown.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogFailure(user.Username, "BadPassword");

        throw AuthenticationFailedException.InvalidCredentials();
    }

    /// <remarks>
    /// The attempted username and a failure category, never the password. The category is safe to record and is
    /// what makes a credential-stuffing pattern visible in the logs.
    /// </remarks>
    private void LogFailure(string username, string category) =>
        logger.LogWarning(
            "LoginFailed for {Username} from {IpAddress}: {Category}",
            username,
            clientInfo.IpAddress,
            category);
}
