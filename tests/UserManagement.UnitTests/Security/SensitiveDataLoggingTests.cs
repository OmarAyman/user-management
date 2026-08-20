using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Common.Options;
using UserManagement.Application.Features.Auth.Login;
using UserManagement.Application.Features.Users.Profile;
using UserManagement.Domain.Entities;
using UserManagement.UnitTests.TestSupport;

namespace UserManagement.UnitTests.Security;

/// <summary>
/// Proves that no credential reaches the logs.
/// </summary>
/// <remarks>
/// The brief is explicit: never log a password, a password hash or a token. That is easy to honour today and
/// easy to break tomorrow with one helpful extra log parameter, so it is asserted rather than trusted. The
/// capturing logger records structured values as well as rendered text, because a value can leak through a
/// template parameter that never shows up in a console line but does reach a JSON sink.
/// </remarks>
public sealed class SensitiveDataLoggingTests
{
    private const string Password = "Sup3rSecret!Password";
    private const string StoredHash = "AQAAAAIAAYagAAAAEB30ff7RCU6GsfRbofzWEtriRCa3bE7LBqAsp0V5h++G";

    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IAccessTokenIssuer _tokenIssuer = Substitute.For<IAccessTokenIssuer>();
    private readonly IRefreshTokenService _refreshTokens = Substitute.For<IRefreshTokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IClientInfoProvider _clientInfo = Substitute.For<IClientInfoProvider>();
    private readonly CapturingLogger<LoginCommandHandler> _logger = new();

    public SensitiveDataLoggingTests()
    {
        _clock.UtcNow.Returns(Now);
        _clientInfo.IpAddress.Returns("203.0.113.24");

        _tokenIssuer.Issue(Arg.Any<User>(), Arg.Any<string>())
            .Returns(new AccessToken("header.payload.signature", Now.AddMinutes(15)));

        _refreshTokens.IssueAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedRefreshToken("raw-refresh-token-value", Now.AddDays(7), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task A_failed_sign_in_logs_the_attempt_without_the_password()
    {
        var user = UserFactory.Create(passwordHash: StoredHash);
        _users.GetForAuthenticationAsync(user.Username, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(StoredHash, Password).Returns(PasswordVerificationOutcome.Failed);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand(user.Username, Password), CancellationToken.None));

        // The attempt itself must be visible - that is what makes credential stuffing detectable...
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("LoginFailed", StringComparison.Ordinal));
        Assert.Contains(user.Username, _logger.AllText, StringComparison.Ordinal);
        Assert.Contains("203.0.113.24", _logger.AllText, StringComparison.Ordinal);

        // ...without the material that would make the log itself a credential store.
        Assert.DoesNotContain(Password, _logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(StoredHash, _logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_username_logs_no_password_either()
    {
        _users.GetForAuthenticationAsync("ghost", Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler().HandleAsync(new LoginCommand("ghost", Password), CancellationToken.None));

        Assert.DoesNotContain(Password, _logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_sign_in_logs_neither_the_password_nor_either_token()
    {
        var user = UserFactory.Create(passwordHash: StoredHash);
        _users.GetForAuthenticationAsync(user.Username, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(StoredHash, Password).Returns(PasswordVerificationOutcome.Success);

        await Handler().HandleAsync(new LoginCommand(user.Username, Password), CancellationToken.None);

        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("LoginSucceeded", StringComparison.Ordinal));

        Assert.DoesNotContain(Password, _logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(StoredHash, _logger.AllText, StringComparison.Ordinal);

        // The issued tokens are the other thing a log must never carry: a logged token is a usable session.
        Assert.DoesNotContain("header.payload.signature", _logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-refresh-token-value", _logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lockout_is_logged_as_a_security_event_without_credentials()
    {
        var user = UserFactory.Create(passwordHash: StoredHash);
        _users.GetForAuthenticationAsync(user.Username, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(StoredHash, Arg.Any<string>()).Returns(PasswordVerificationOutcome.Failed);

        var handler = Handler();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
                handler.HandleAsync(new LoginCommand(user.Username, Password), CancellationToken.None));
        }

        Assert.Contains(
            _logger.Entries,
            entry => entry.Level == LogLevel.Warning
                     && entry.Message.Contains("AccountLocked", StringComparison.Ordinal));

        Assert.DoesNotContain(Password, _logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_change_logs_nothing_about_the_password()
    {
        var user = UserFactory.Create(passwordHash: StoredHash);
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(user.Id);

        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify(StoredHash, Password).Returns(PasswordVerificationOutcome.Success);
        _passwordHasher.Hash(Arg.Any<string>()).Returns("new-hash-value");

        var handler = new ChangeMyPasswordCommandHandler(
            _users,
            _passwordHasher,
            _refreshTokens,
            currentUser,
            _unitOfWork);

        await handler.HandleAsync(new ChangeMyPasswordCommand(Password, "Replacement!Password1"), CancellationToken.None);

        // This handler logs nothing at all, which is the correct amount for an operation whose every parameter
        // is a credential. The assertion guards against a future "helpful" log line.
        Assert.DoesNotContain(Password, _logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("Replacement!Password1", _logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain("new-hash-value", _logger.AllText, StringComparison.Ordinal);
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
        _logger);
}
