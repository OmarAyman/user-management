using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Features.Users.Dtos;

namespace UserManagement.Application.Features.Users.GetUserById;

/// <summary>
/// One user by id. <paramref name="IncludeDeleted"/> is set by the endpoint from the caller's role - never
/// bound from the request - so a non-Admin cannot ask for a deleted row.
/// </summary>
public sealed record GetUserByIdQuery(Guid Id, bool IncludeDeleted);

public sealed class GetUserByIdQueryHandler(
    IUserRepository users,
    IRoleRepository roles) : IQueryHandler<GetUserByIdQuery, UserDetailsDto>
{
    public async Task<UserDetailsDto> HandleAsync(GetUserByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = query.IncludeDeleted
            ? await users.GetByIdIncludingDeletedAsync(query.Id, cancellationToken)
            : await users.GetByIdAsync(query.Id, cancellationToken);

        // 404 rather than 403 for a soft-deleted user seen by a non-Admin: a caller with no right to know the
        // account exists must not be told that it does.
        if (user is null)
        {
            throw NotFoundException.User(query.Id);
        }

        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken);

        return UserProjections.ToDetails(user, role?.Name ?? string.Empty);
    }
}
