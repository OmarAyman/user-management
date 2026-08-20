namespace UserManagement.Application.Features.Users.Dtos;

/// <summary>
/// One row of the user list. A fixed, small column set, which is what makes the covering indexes work and
/// keeps the query a single index range scan.
/// </summary>
/// <remarks>
/// No password hash, no security stamp, no lockout internals: what a list needs and nothing that would be a
/// disclosure if it leaked into a log or a browser cache.
/// </remarks>
public sealed record UserListItemDto(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    int RoleId,
    string Role,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    DateTimeOffset? LastLoginAt,
    bool IsLockedOut,
    DateTimeOffset? DeletedAt,
    string? DeletedBy);

/// <summary>
/// A single user, as returned by the detail endpoints.
/// </summary>
/// <remarks>
/// <see cref="RowVersion"/> is the base64 concurrency token. It is returned so a client can send it back on
/// update: without it the client cannot prove which version of the row it edited, and the last writer would
/// silently win (ADR-0013).
/// </remarks>
public sealed record UserDetailsDto(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    int RoleId,
    string Role,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeletedAt,
    string? DeletedBy,
    string RowVersion);

/// <summary>Whether an identifier is free among active users. A UX aid; the unique index is the authority.</summary>
public sealed record AvailabilityDto(bool? UsernameAvailable, bool? EmailAvailable);

/// <summary>A role, for the filter dropdown and the role selector.</summary>
public sealed record RoleDto(int Id, string Name);
