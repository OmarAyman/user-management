using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Exceptions;

namespace UserManagement.UnitTests.Domain;

/// <summary>
/// Invariants of the <see cref="User"/> aggregate. No mocks and no database: the behaviour under test is pure
/// domain logic, which is the payoff for putting it on the entity instead of in a handler.
/// </summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_populates_identity_and_security_stamp()
    {
        var user = CreateUser();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.NotEqual(Guid.Empty, user.SecurityStamp);
        Assert.False(user.IsDeleted);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndAt);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        var user = User.Create("  asmith  ", " a@example.com ", " Alex ", " Smith ", "hash", RoleIds.User);

        Assert.Equal("asmith", user.Username);
        Assert.Equal("a@example.com", user.Email);
        Assert.Equal("Alex", user.FirstName);
        Assert.Equal("Smith", user.LastName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_username(string username)
    {
        Assert.Throws<ArgumentException>(() =>
            User.Create(username, "a@example.com", "Alex", "Smith", "hash", RoleIds.User));
    }

    [Fact]
    public void SoftDelete_sets_the_flag_timestamp_and_actor_together()
    {
        var user = CreateUser();

        user.SoftDelete("admin", Now);

        Assert.True(user.IsDeleted);
        Assert.Equal(Now, user.DeletedAt);
        Assert.Equal("admin", user.DeletedBy);
    }

    [Fact]
    public void SoftDelete_on_an_already_deleted_user_is_a_conflict()
    {
        var user = CreateUser();
        user.SoftDelete("admin", Now);

        var exception = Assert.Throws<DomainRuleViolationException>(() => user.SoftDelete("admin", Now));

        Assert.Equal(ErrorCodes.UserAlreadyDeleted, exception.ErrorCode);
    }

    [Fact]
    public void Restore_clears_the_deletion_state()
    {
        var user = CreateUser();
        user.SoftDelete("admin", Now);

        user.Restore();

        Assert.False(user.IsDeleted);
        Assert.Null(user.DeletedAt);
        Assert.Null(user.DeletedBy);
    }

    [Fact]
    public void Restore_on_an_active_user_is_a_conflict()
    {
        var user = CreateUser();

        var exception = Assert.Throws<DomainRuleViolationException>(user.Restore);

        Assert.Equal(ErrorCodes.UserNotDeleted, exception.ErrorCode);
    }

    [Fact]
    public void ChangeRole_rotates_the_security_stamp()
    {
        var user = CreateUser();
        var before = user.SecurityStamp;

        var changed = user.ChangeRole(RoleIds.Admin);

        Assert.True(changed);
        Assert.Equal(RoleIds.Admin, user.RoleId);
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Fact]
    public void ChangeRole_to_the_same_role_is_a_no_op()
    {
        var user = CreateUser();
        var before = user.SecurityStamp;

        var changed = user.ChangeRole(RoleIds.User);

        // Returning false is what stops a no-op edit from emitting a spurious role-change audit row.
        Assert.False(changed);
        Assert.Equal(before, user.SecurityStamp);
    }

    [Fact]
    public void SetPasswordHash_rotates_the_security_stamp()
    {
        var user = CreateUser();
        var before = user.SecurityStamp;

        user.SetPasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void RecordFailedLogin_locks_the_account_at_the_threshold(int attempts, bool expectedLockout)
    {
        var user = CreateUser();

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            user.RecordFailedLogin(Now, maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
        }

        Assert.Equal(expectedLockout, user.IsLockedOut(Now));
        Assert.Equal(attempts, user.FailedLoginAttempts);
    }

    [Fact]
    public void An_expired_lockout_does_not_relock_on_the_next_single_failure()
    {
        var user = CreateUser();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            user.RecordFailedLogin(Now, maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
        }

        var afterLockout = Now.AddMinutes(20);
        user.RecordFailedLogin(afterLockout, maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        // The counter restarts once a lockout has been served, otherwise one later mistake would re-lock an
        // account that had already waited out its penalty.
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.False(user.IsLockedOut(afterLockout));
    }

    [Fact]
    public void RecordSuccessfulLogin_clears_the_lockout_state()
    {
        var user = CreateUser();
        user.RecordFailedLogin(Now, 5, TimeSpan.FromMinutes(15));

        user.RecordSuccessfulLogin(Now);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndAt);
        Assert.Equal(Now, user.LastLoginAt);
    }

    [Fact]
    public void IsLockedOut_uses_the_supplied_time_rather_than_wall_time()
    {
        var user = CreateUser();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            user.RecordFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        }

        Assert.True(user.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(16)));
    }

    [Fact]
    public void RevokeAllRefreshTokens_only_revokes_the_active_ones()
    {
        var user = CreateUser();
        var family = Guid.CreateVersion7();

        var active = RefreshToken.Issue(user.Id, family, new string('a', 64), Now, Now.AddDays(7), "127.0.0.1");
        var expired = RefreshToken.Issue(user.Id, family, new string('b', 64), Now.AddDays(-8), Now.AddDays(-1), "127.0.0.1");
        var alreadyRevoked = RefreshToken.Issue(user.Id, family, new string('c', 64), Now, Now.AddDays(7), "127.0.0.1");
        alreadyRevoked.Revoke(RevocationReason.Logout, Now);

        user.AddRefreshToken(active);
        user.AddRefreshToken(expired);
        user.AddRefreshToken(alreadyRevoked);

        var revoked = user.RevokeAllRefreshTokens(RevocationReason.PasswordChanged, Now);

        Assert.Equal(1, revoked);
        Assert.Equal(RevocationReason.PasswordChanged, active.RevocationReason);
        Assert.Equal(RevocationReason.Logout, alreadyRevoked.RevocationReason);
    }

    private static User CreateUser() =>
        User.Create("asmith", "asmith@example.com", "Alex", "Smith", "hash", RoleIds.User);
}
