using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Features.Roles.GetRoles;
using UserManagement.Application.Features.Users.Dtos;

namespace UserManagement.Api.Controllers;

/// <summary>
/// The role list. Read-only: the three roles are seeded reference data that the authorization policies are
/// written against, so role CRUD would be a different feature with a different blast radius.
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
[Produces("application/json")]
public sealed class RolesController(IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>> getRoles)
    : ControllerBase
{
    /// <summary>Lists the roles. Any authenticated caller, because the list feeds the role filter.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetRoles(CancellationToken cancellationToken) =>
        Ok(await getRoles.HandleAsync(new GetRolesQuery(), cancellationToken));
}
