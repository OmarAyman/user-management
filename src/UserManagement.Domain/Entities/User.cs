using UserManagement.Domain.Common;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Exceptions;

namespace UserManagement.Domain.Entities;

/// <summary>
/// A user of the system. Behaviour lives here rather than in handlers, so the invariants hold no matter which
/// use case calls them and the domain tests need no mocks at all.
/// </summary>
public sealed class User : IAuditableEntity, ISoftDeletable
{
    private readonly List<RefreshToken> _refreshTokens = [];

    // EF Core materialisation.
    private User()
    {
        Username = string.Empty;
        Email = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        PasswordHash = string.Empty;
        CreatedBy = string.Empty;
    }

    private User(string username, string email, string firstName, string lastName, string passwordHash, int roleId)
    {
        // Version 7 GUIDs are time-ordered, so inserts stay clustered without a database round trip for a
        // sequential value.
        Id = Guid.CreateVersion7();
        Username = username;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PasswordHash = passwordHash;
        RoleId = roleId;
        SecurityStamp = Guid.NewGuid();
        CreatedBy = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Immutable after creation (BR-10): it is the login identifier and appears in audit snapshots.</summary>
    public string Username { get; private set; }

    public string Email { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    /// <summary>PBKDF2 hash in the ASP.NET v3 format. Never leaves the Application layer.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>
    /// Rotated whenever credentials or privileges change. Refresh tokens issued before the rotation are
    /// revoked, which is what bounds the window after a password change or a demotion.
    /// </summary>
    public Guid SecurityStamp { get; private set; }

    public int RoleId { get; private set; }

    public Role? Role { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public string? DeletedBy { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockoutEndAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    public string? LastModifiedBy { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c>. Maintained by the engine and never assigned here; a stale value makes an
    /// UPDATE match zero rows, which EF surfaces as a concurrency conflict (ADR-0013).
    /// </summary>
    public byte[]? RowVersion { get; private set; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens;

    public static User Create(
        string username,
        string email,
        string firstName,
        string lastName,
        string passwordHash,
        int roleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(username.Trim(), email.Trim(), firstName.Trim(), lastName.Trim(), passwordHash, roleId);
    }

    /// <summary>
    /// Assigns a new role. Returns <see langword="false"/> when the role is unchanged, so callers can avoid
    /// emitting a spurious role-change audit row for a no-op edit.
    /// </summary>
    public bool ChangeRole(int newRoleId)
    {
        if (RoleId == newRoleId)
        {
            return false;
        }

        RoleId = newRoleId;
        Role = null;
        SecurityStamp = Guid.NewGuid();
        return true;
    }

    public void UpdateProfile(string firstName, string lastName, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = email.Trim();
    }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
    }

    /// <summary>Soft-deletes the user (BR-07: deleting an already-deleted user is a conflict).</summary>
    public void SoftDelete(string performedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(performedBy);

        if (IsDeleted)
        {
            throw new DomainRuleViolationException(
                ErrorCodes.UserAlreadyDeleted,
                $"User '{Id}' is already deleted.");
        }

        IsDeleted = true;
        DeletedAt = now;
        DeletedBy = performedBy;
    }

    /// <summary>Restores a soft-deleted user (BR-08: restoring an active user is a conflict).</summary>
    public void Restore()
    {
        if (!IsDeleted)
        {
            throw new DomainRuleViolationException(
                ErrorCodes.UserNotDeleted,
                $"User '{Id}' is not deleted.");
        }

        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
    }

    /// <summary>
    /// Records a failed sign-in attempt and locks the account once the threshold is reached.
    /// </summary>
    public void RecordFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        // A previous lockout that has expired starts the count again, otherwise a single later failure would
        // immediately re-lock an account that had already served its time.
        if (LockoutEndAt is not null && LockoutEndAt <= now)
        {
            FailedLoginAttempts = 0;
            LockoutEndAt = null;
        }

        FailedLoginAttempts++;

        if (FailedLoginAttempts >= maxAttempts)
        {
            LockoutEndAt = now.Add(lockoutDuration);
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEndAt = null;
        LastLoginAt = now;
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndAt is not null && LockoutEndAt > now;

    public void AddRefreshToken(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        _refreshTokens.Add(token);
    }

    /// <summary>
    /// Revokes every active refresh token the user holds, across all families. Used when credentials or
    /// privileges change, where every session is invalidated regardless of lineage.
    /// </summary>
    public int RevokeAllRefreshTokens(RevocationReason reason, DateTimeOffset now, string? ipAddress = null)
    {
        var revoked = 0;

        foreach (var token in _refreshTokens.Where(token => token.IsActive(now)))
        {
            token.Revoke(reason, now, ipAddress);
            revoked++;
        }

        return revoked;
    }
}
