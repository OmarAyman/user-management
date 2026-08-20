using NSubstitute;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Features.Users.CreateUser;
using UserManagement.Application.Features.Users.DeleteUser;
using UserManagement.Application.Features.Users.Profile;
using UserManagement.Application.Features.Users.RestoreUser;
using UserManagement.Application.Features.Users.UpdateUser;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.UnitTests.TestSupport;

namespace UserManagement.UnitTests.Application;

/// <summary>
/// The business rules, with dependencies controlled.
/// </summary>
/// <remarks>
/// The last-administrator rules live here rather than in the HTTP suite for a practical reason: proving them
/// over HTTP means reducing the whole system to one administrator, and the integration tests share a database,
/// so that setup would break every other test. Here the count is simply a stubbed return value.
/// </remarks>
public sealed class UserCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IRefreshTokenService _refreshTokens = Substitute.For<IRefreshTokenService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public UserCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _passwordHasher.Hash(Arg.Any<string>()).Returns("hashed");

        foreach (var role in Role.Seed)
        {
            _roles.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(role);
        }
    }

    [Fact]
    public async Task Creating_a_user_hashes_the_password_and_never_stores_the_plaintext()
    {
        User? added = null;
        _users.When(repository => repository.Add(Arg.Any<User>()))
            .Do(call => added = call.Arg<User>());

        await CreateHandler().HandleAsync(
            new CreateUserCommand("asmith", "asmith@example.com", "Alex", "Smith", "Secret@123456", RoleIds.User),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal("hashed", added.PasswordHash);

        // The plaintext exists only as a parameter to the hasher: it never reaches the entity, so it cannot
        // reach the change tracker, the audit trail or a log.
        Assert.DoesNotContain("Secret@123456", added.PasswordHash, StringComparison.Ordinal);
        _passwordHasher.Received(1).Hash("Secret@123456");
    }

    [Fact]
    public async Task Creating_a_user_with_a_taken_username_is_a_conflict()
    {
        _users.IsUsernameTakenAsync("taken", null, Arg.Any<CancellationToken>()).Returns(true);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateHandler().HandleAsync(
                new CreateUserCommand("taken", "a@example.com", "A", "B", "Secret@123456", RoleIds.User),
                CancellationToken.None));

        Assert.Equal(ErrorCodes.UsernameAlreadyExists, exception.ErrorCode);
    }

    [Fact]
    public async Task Creating_a_user_with_an_unknown_role_is_a_validation_error()
    {
        _roles.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Role?)null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateHandler().HandleAsync(
                new CreateUserCommand("asmith", "a@example.com", "A", "B", "Secret@123456", 99),
                CancellationToken.None));
    }

    [Fact]
    public async Task Demoting_the_last_active_administrator_is_a_conflict()
    {
        var target = UserFactory.Create("lastadmin", roleName: RoleNames.Admin);
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);
        _currentUser.UserId.Returns(Guid.CreateVersion7());

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            UpdateHandler().HandleAsync(
                new UpdateUserCommand(target.Id, target.Email, "A", "B", RoleIds.User, [1, 2, 3]),
                CancellationToken.None));

        Assert.Equal(ErrorCodes.LastAdminCannotBeRemoved, exception.ErrorCode);

        // The role must be unchanged: a guard that throws after mutating would leave the entity inconsistent
        // if anything else in the transaction succeeded.
        Assert.Equal(RoleIds.Admin, target.RoleId);
    }

    [Fact]
    public async Task Demoting_an_administrator_is_allowed_while_another_one_remains()
    {
        var target = UserFactory.Create("demotable", roleName: RoleNames.Admin);
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(2);
        _currentUser.UserId.Returns(Guid.CreateVersion7());

        await UpdateHandler().HandleAsync(
            new UpdateUserCommand(target.Id, target.Email, "A", "B", RoleIds.User, [1, 2, 3]),
            CancellationToken.None);

        Assert.Equal(RoleIds.User, target.RoleId);
    }

    [Fact]
    public async Task Changing_your_own_role_is_refused_even_as_an_administrator()
    {
        var self = UserFactory.Create("selfadmin", roleName: RoleNames.Admin);
        _users.GetByIdAsync(self.Id, Arg.Any<CancellationToken>()).Returns(self);
        _currentUser.UserId.Returns(self.Id);

        await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            UpdateHandler().HandleAsync(
                new UpdateUserCommand(self.Id, self.Email, "A", "B", RoleIds.User, [1, 2, 3]),
                CancellationToken.None));

        Assert.Equal(RoleIds.Admin, self.RoleId);
    }

    [Fact]
    public async Task A_role_change_revokes_every_session_of_the_affected_user()
    {
        var target = UserFactory.Create("promoted");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(3);
        _currentUser.UserId.Returns(Guid.CreateVersion7());

        await UpdateHandler().HandleAsync(
            new UpdateUserCommand(target.Id, target.Email, "A", "B", RoleIds.Admin, [1, 2, 3]),
            CancellationToken.None);

        // What a session is allowed to do has changed, so the sessions end.
        await _refreshTokens.Received(1).RevokeAllForUserAsync(
            target.Id,
            RevocationReason.RoleChanged,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_update_that_does_not_change_the_role_leaves_sessions_alone()
    {
        var target = UserFactory.Create("renamed");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _currentUser.UserId.Returns(Guid.CreateVersion7());

        await UpdateHandler().HandleAsync(
            new UpdateUserCommand(target.Id, "new@example.com", "New", "Name", target.RoleId, [1, 2, 3]),
            CancellationToken.None);

        // Signing someone out because their surname was corrected would be a bug dressed as security.
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(
            Arg.Any<Guid>(),
            Arg.Any<RevocationReason>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_update_applies_the_clients_concurrency_token()
    {
        var target = UserFactory.Create("tokened");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _currentUser.UserId.Returns(Guid.CreateVersion7());

        byte[] token = [9, 9, 9];

        await UpdateHandler().HandleAsync(
            new UpdateUserCommand(target.Id, target.Email, "A", "B", target.RoleId, token),
            CancellationToken.None);

        // Without this call the UPDATE would carry no version predicate and the last writer would silently win.
        _users.Received(1).ApplyConcurrencyToken(target, token);
    }

    [Fact]
    public async Task Deleting_your_own_account_is_forbidden()
    {
        var self = UserFactory.Create("selfdelete");
        _currentUser.UserId.Returns(self.Id);

        var exception = await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            DeleteHandler().HandleAsync(new DeleteUserCommand(self.Id), CancellationToken.None));

        Assert.Equal(ErrorCodes.CannotDeleteSelf, exception.ErrorCode);
    }

    [Fact]
    public async Task Deleting_the_last_active_administrator_is_a_conflict()
    {
        var target = UserFactory.Create("lastadmin", roleName: RoleNames.Admin);
        _users.GetByIdIncludingDeletedAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);
        _currentUser.UserId.Returns(Guid.CreateVersion7());

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            DeleteHandler().HandleAsync(new DeleteUserCommand(target.Id), CancellationToken.None));

        Assert.Equal(ErrorCodes.LastAdminCannotBeRemoved, exception.ErrorCode);
        Assert.False(target.IsDeleted);
    }

    [Fact]
    public async Task Deleting_a_user_soft_deletes_them_and_ends_their_sessions()
    {
        var target = UserFactory.Create("deletable");
        _users.GetByIdIncludingDeletedAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _currentUser.UserId.Returns(Guid.CreateVersion7());
        _currentUser.Username.Returns("admin");

        await DeleteHandler().HandleAsync(new DeleteUserCommand(target.Id), CancellationToken.None);

        Assert.True(target.IsDeleted);
        Assert.Equal("admin", target.DeletedBy);
        Assert.Equal(Now, target.DeletedAt);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(
            target.Id,
            RevocationReason.UserDeleted,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_an_unknown_user_is_a_404()
    {
        _currentUser.UserId.Returns(Guid.CreateVersion7());
        _users.GetByIdIncludingDeletedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            DeleteHandler().HandleAsync(new DeleteUserCommand(Guid.CreateVersion7()), CancellationToken.None));
    }

    [Fact]
    public async Task Restoring_a_user_whose_username_was_taken_is_a_conflict()
    {
        var target = UserFactory.Create("restorable");
        target.SoftDelete("admin", Now);

        _users.GetByIdIncludingDeletedAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.IsUsernameTakenAsync(target.Username, target.Id, Arg.Any<CancellationToken>()).Returns(true);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            RestoreHandler().HandleAsync(
                new RestoreUserCommand(target.Id, [1, 2, 3]),
                CancellationToken.None));

        // The failure mode released identifiers introduce, handled deliberately rather than surfacing as a
        // unique-index violation (BR-17, ADR-0009).
        Assert.Equal(ErrorCodes.UsernameAlreadyExists, exception.ErrorCode);
        Assert.True(target.IsDeleted);
    }

    [Fact]
    public async Task Restoring_a_deleted_user_clears_the_deletion_state()
    {
        var target = UserFactory.Create("restored");
        target.SoftDelete("admin", Now);
        _users.GetByIdIncludingDeletedAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        await RestoreHandler().HandleAsync(
            new RestoreUserCommand(target.Id, [1, 2, 3]),
            CancellationToken.None);

        Assert.False(target.IsDeleted);
        Assert.Null(target.DeletedAt);
        Assert.Null(target.DeletedBy);
    }

    [Fact]
    public async Task Updating_your_own_profile_targets_the_token_subject_only()
    {
        var self = UserFactory.Create("selfprofile");
        _currentUser.UserId.Returns(self.Id);
        _users.GetByIdAsync(self.Id, Arg.Any<CancellationToken>()).Returns(self);

        await ProfileHandler().HandleAsync(
            new UpdateMyProfileCommand("New", "Name", "new@example.com", [1, 2, 3]),
            CancellationToken.None);

        Assert.Equal("New", self.FirstName);
        Assert.Equal("new@example.com", self.Email);

        // The command has no id: the only user this handler can reach is the one the token names.
        await _users.Received(1).GetByIdAsync(self.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changing_a_password_requires_the_current_one()
    {
        var self = UserFactory.Create("pwuser", passwordHash: "stored");
        _currentUser.UserId.Returns(self.Id);
        _users.GetByIdAsync(self.Id, Arg.Any<CancellationToken>()).Returns(self);
        _passwordHasher.Verify("stored", "wrong").Returns(PasswordVerificationOutcome.Failed);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            PasswordHandler().HandleAsync(
                new ChangeMyPasswordCommand("wrong", "Replaced@123456"),
                CancellationToken.None));

        Assert.Equal("stored", self.PasswordHash);
    }

    [Fact]
    public async Task Changing_a_password_rotates_the_stamp_and_revokes_every_session()
    {
        var self = UserFactory.Create("pwuser", passwordHash: "stored");
        var stampBefore = self.SecurityStamp;

        _currentUser.UserId.Returns(self.Id);
        _users.GetByIdAsync(self.Id, Arg.Any<CancellationToken>()).Returns(self);
        _passwordHasher.Verify("stored", "current").Returns(PasswordVerificationOutcome.Success);

        await PasswordHandler().HandleAsync(
            new ChangeMyPasswordCommand("current", "Replaced@123456"),
            CancellationToken.None);

        Assert.Equal("hashed", self.PasswordHash);
        Assert.NotEqual(stampBefore, self.SecurityStamp);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(
            self.Id,
            RevocationReason.PasswordChanged,
            Arg.Any<CancellationToken>());
    }

    private CreateUserCommandHandler CreateHandler() =>
        new(_users, _roles, _passwordHasher, _unitOfWork);

    private UpdateUserCommandHandler UpdateHandler() =>
        new(_users, _roles, _refreshTokens, _currentUser, _unitOfWork);

    private DeleteUserCommandHandler DeleteHandler() =>
        new(_users, _refreshTokens, _currentUser, _clock, _unitOfWork);

    private RestoreUserCommandHandler RestoreHandler() => new(_users, _unitOfWork);

    private UpdateMyProfileCommandHandler ProfileHandler() =>
        new(_users, _roles, _currentUser, _unitOfWork);

    private ChangeMyPasswordCommandHandler PasswordHandler() =>
        new(_users, _passwordHasher, _refreshTokens, _currentUser, _unitOfWork);
}
