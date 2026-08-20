using System.Linq.Expressions;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Features.Users.Dtos;

/// <summary>
/// The mappings from entity to DTO, in one place so every endpoint returns the same shape.
/// </summary>
public static class UserProjections
{
    /// <summary>
    /// Projection for list queries, evaluated in SQL.
    /// </summary>
    /// <remarks>
    /// Takes <paramref name="now"/> as a parameter because the lockout flag is a comparison against the current
    /// time, and a captured <c>DateTimeOffset.UtcNow</c> inside an expression tree would be evaluated once when
    /// the expression is built rather than per request.
    /// </remarks>
    public static Expression<Func<User, UserListItemDto>> ToListItem(DateTimeOffset now) =>
        user => new UserListItemDto(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.RoleId,
            user.Role!.Name,
            user.IsDeleted,
            user.CreatedAt,
            user.LastModifiedAt,
            user.LastLoginAt,
            user.LockoutEndAt != null && user.LockoutEndAt > now,
            user.DeletedAt,
            user.DeletedBy);

    /// <summary>
    /// Maps a loaded entity to the detail DTO.
    /// </summary>
    /// <remarks>
    /// Done in memory rather than as a SQL projection because the concurrency token has to be base64-encoded,
    /// and <c>Convert.ToBase64String</c> has no SQL translation. This is a single row, so there is nothing to
    /// gain from projecting it server-side anyway.
    /// </remarks>
    public static UserDetailsDto ToDetails(User user, string roleName)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserDetailsDto(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.RoleId,
            roleName,
            user.IsDeleted,
            user.CreatedAt,
            user.CreatedBy,
            user.LastModifiedAt,
            user.LastModifiedBy,
            user.LastLoginAt,
            user.DeletedAt,
            user.DeletedBy,
            // Empty until the row has been written: a not-yet-saved entity has no version to report, and an
            // empty token is honest about that rather than pretending to be one.
            user.RowVersion is { Length: > 0 } version ? Convert.ToBase64String(version) : string.Empty);
    }
}
