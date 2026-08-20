using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Features.Users.Dtos;

namespace UserManagement.Application.Features.Roles.GetRoles;

/// <summary>The role list, for the filter dropdown and the role selector.</summary>
public sealed record GetRolesQuery;

/// <summary>
/// Returns the closed role set.
/// </summary>
/// <remarks>
/// Read-only by design: the three roles are seeded reference data, and the authorization policies are written
/// against them. Adding role CRUD would mean revisiting those policies, which is a different feature and not
/// one the assignment asks for.
/// </remarks>
public sealed class GetRolesQueryHandler(IRoleRepository roles)
    : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> HandleAsync(
        GetRolesQuery query,
        CancellationToken cancellationToken)
    {
        var all = await roles.GetAllAsync(cancellationToken);

        return [.. all.Select(role => new RoleDto(role.Id, role.Name))];
    }
}
