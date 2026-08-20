using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Common.Options;
using UserManagement.Application.Features.Auth.Login;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.UnitTests.TestSupport;

namespace UserManagement.UnitTests.Application;

/// <summary>
/// The sign-in decision table. Four outcomes, exactly one of which is distinguishable from outside, and each
/// one has a test here so the disclosure rule cannot be weakened by accident (ADR-0006).
/// </summary>
public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAccessTokenIssuer _tokenIssuer = Substitute.For<IAccessTokenIssuer>();
    private readonly IRefreshTokenService _refreshTokens = Substitute.For<IRefreshTokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IClientInfoProvider _clientInfo = Substitute.For<IClientInfoProvider>();

    public LoginCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _clientInfo.IpAddress.Returns("203.0.113.24");

        _tokenIssuer.Issue(Arg.Any<User>(), Arg.Any<string>())
            .Returns(new AccessToken("access-token", Now.AddMinutes(15)));

        _refreshTokens.IssueAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedRefreshToken("raw-refresh-token", Now.AddDays(7), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task An_unknown_username_still_verifies_a_dummy_hash()
    {
        _users.GetForAuthenticationAsync("ghost", Arg.Any<CancellationToken>()).Returns((User?)null);

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand("ghost", "whatever"), CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidCredentials, exception.ErrorCode);

        // Without this call the unknown-username path returns faster than a real verification, and response
        // timing alone becomes a user-enumeration oracle.
        _passwordHasher.Received(1).VerifyDummy("whatever");
    }

    [Fact]
    public async Task A_wrong_password_increments_the_failure_counter_and_persists_it()
    {
        var user = UserFactory.Create();
        Arrange(user, PasswordVerificationOutcome.Failed);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand(user.Username, "wrong"), CancellationToken.None));

        Assert.Equal(1, user.FailedLoginAttempts);

        // The counter has to survive the rejection, or lockout could never trigger.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_fifth_wrong_password_locks_the_account()
    {
        var user = UserFactory.Create();
        Arrange(user, PasswordVerificationOutcome.Failed);
        var handler = Handler();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
                handler.HandleAsync(new LoginCommand(user.Username, "wrong"), CancellationToken.None));
        }

        Assert.True(user.IsLockedOut(Now));
    }

    [Fact]
    public async Task A_wrong_password_on_a_locked_account_reports_invalid_credentials()
    {
        var user = UserFactory.Create();
        LockOut(user);
        Arrange(user, PasswordVerificationOutcome.Failed);

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand(user.Username, "wrong"), CancellationToken.None));

        // Not ACCOUNT_LOCKED: the caller has not proved they know the password, so revealing the lockout would
        // confirm the account exists.
        Assert.Equal(ErrorCodes.InvalidCredentials, exception.ErrorCode);
    }

    [Fact]
    public async Task A_correct_password_on_a_locked_account_reports_the_lockout_with_a_retry_time()
    {
        var user = UserFactory.Create();
        LockOut(user);
        Arrange(user, PasswordVerificationOutcome.Success);

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand(user.Username, "correct"), CancellationToken.None));

        Assert.Equal(ErrorCodes.AccountLocked, exception.ErrorCode);
        Assert.NotNull(exception.RetryAfterSeconds);
        Assert.True(exception.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task A_soft_deleted_account_reports_invalid_credentials_even_with_the_right_password()
    {
        var user = UserFactory.Create();
        user.SoftDelete("admin", Now);
        Arrange(user, PasswordVerificationOutcome.Success);

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand(user.Username, "correct"), CancellationToken.None));

        // A removed account must not be discoverable, so it shares the generic response (BR-05).
        Assert.Equal(ErrorCodes.InvalidCredentials, exception.ErrorCode);
        Assert.Null(exception.RetryAfterSeconds);
    }

    [Fact]
    public async Task A_successful_sign_in_issues_both_tokens_and_stamps_the_login()
    {
        var user = UserFactory.Create(roleName: RoleNames.Admin);
        user.RecordFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        Arrange(user, PasswordVerificationOutcome.Success);

        var result = await Handler().HandleAsync(
            new LoginCommand(user.Username, "correct"),
            CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken.Value);
        Assert.Equal("raw-refresh-token", result.RefreshToken.RawToken);
        Assert.Equal(RoleNames.Admin, result.User.Role);
        Assert.Equal(user.Username, result.User.Username);

        Assert.Equal(Now, user.LastLoginAt);
        Assert.Equal(0, user.FailedLoginAttempts);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rehash_upgrades_the_stored_hash_without_ending_other_sessions()
    {
        var user = UserFactory.Create(passwordHash: "old-weak-hash");
        var stampBefore = user.SecurityStamp;

        Arrange(user, PasswordVerificationOutcome.SuccessRehashNeeded);
        _passwordHasher.Hash("correct").Returns("upgraded-hash");

        await Handler().HandleAsync(new LoginCommand(user.Username, "correct"), CancellationToken.None);

        Assert.Equal("upgraded-hash", user.PasswordHash);

        // The credential did not change, so signing the user out of their other devices because the iteration
        // count moved would be a bug rather than a security measure.
        Assert.Equal(stampBefore, user.SecurityStamp);
    }

    private void Arrange(User user, PasswordVerificationOutcome outcome)
    {
        _users.GetForAuthenticationAsync(user.Username, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(user.PasswordHash, Arg.Any<string>()).Returns(outcome);
    }

    private static void LockOut(User user)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            user.RecordFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        }
    }

    private LoginCommandHandler Handler() => new(
        _users,
        _passwordHasher,
        _tokenIssuer,
        _refreshTokens,
        _unitOfWork,
        _clock,
        _clientInfo,
        Options.Create(new LockoutOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }),
        NullLogger<LoginCommandHandler>.Instance);
}
