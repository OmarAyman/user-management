using UserManagement.Application.Common.Models;

namespace UserManagement.Api.Contracts.Users;

/// <summary>
/// Query parameters for the user listings.
/// </summary>
/// <remarks>
/// There is no <c>includeDeleted</c>. Soft-delete visibility is an authorization decision served by a separate
/// Admin-only route, so there is nothing here for a non-Admin to set (ADR-0004).
/// </remarks>
public sealed record UserQueryParameters
{
    public int PageNumber { get; init; } = PagingDefaults.DefaultPageNumber;

    public int PageSize { get; init; } = PagingDefaults.DefaultPageSize;

    public string? Search { get; init; }

    public int? RoleId { get; init; }

    public string? SortBy { get; init; }

    public SortDirection SortDirection { get; init; } = SortDirection.Descending;
}

/// <summary>Create-user payload. Admin only.</summary>
public sealed record CreateUserRequest(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    int RoleId);

/// <summary>
/// Update-user payload. Admin only.
/// </summary>
/// <remarks>
/// No <c>username</c> (immutable, BR-10), no <c>password</c> (its own endpoint), no <c>isDeleted</c> (deletion
/// has its own endpoint), no timestamps. Those fields are absent, not ignored - the mass-assignment defence is
/// the shape of this record, and with <c>UnmappedMemberHandling.Disallow</c> a payload that includes them is
/// rejected outright.
/// </remarks>
public sealed record UpdateUserRequest(
    string Email,
    string FirstName,
    string LastName,
    int RoleId,
    string RowVersion);

/// <summary>Self-service profile payload. Three fields, and deliberately no role.</summary>
public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string Email,
    string RowVersion);

/// <summary>Self-service password change. The current password is required as proof of possession.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Restore payload. Carries only the concurrency token.</summary>
public sealed record RestoreUserRequest(string RowVersion);

/// <summary>Availability probe for the async form validators.</summary>
public sealed record AvailabilityQueryParameters
{
    public string? Username { get; init; }

    public string? Email { get; init; }

    /// <summary>Excluded from the check, so editing a user does not report their own identifiers as taken.</summary>
    public Guid? ExcludeUserId { get; init; }
}
