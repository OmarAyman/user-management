using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Api.Contracts.Users;
using UserManagement.Api.Validation;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Models;
using UserManagement.Application.Common.Security;
using UserManagement.Application.Features.Users.CheckAvailability;
using UserManagement.Application.Features.Users.CreateUser;
using UserManagement.Application.Features.Users.DeleteUser;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Application.Features.Users.GetDeletedUsers;
using UserManagement.Application.Features.Users.GetUserById;
using UserManagement.Application.Features.Users.GetUsers;
using UserManagement.Application.Features.Users.Profile;
using UserManagement.Application.Features.Users.RestoreUser;
using UserManagement.Application.Features.Users.UpdateUser;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.Controllers;

/// <summary>
/// User administration and self-service.
/// </summary>
/// <remarks>
/// Thin by design: map the request to a command, hand it to a handler, project the result. No business logic,
/// no try/catch, no authorization checks written by hand - the policies and the handlers own those.
/// </remarks>
[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public sealed class UsersController(
    IQueryHandler<GetUsersQuery, PagedResult<UserListItemDto>> getUsers,
    IQueryHandler<GetDeletedUsersQuery, PagedResult<UserListItemDto>> getDeletedUsers,
    IQueryHandler<GetUserByIdQuery, UserDetailsDto> getUserById,
    IQueryHandler<CheckAvailabilityQuery, AvailabilityDto> checkAvailability,
    ICommandHandler<CreateUserCommand, UserDetailsDto> createUser,
    ICommandHandler<UpdateUserCommand, UserDetailsDto> updateUser,
    ICommandHandler<DeleteUserCommand> deleteUser,
    ICommandHandler<RestoreUserCommand> restoreUser,
    IQueryHandler<GetMyProfileQuery, UserDetailsDto> getMyProfile,
    ICommandHandler<UpdateMyProfileCommand, UserDetailsDto> updateMyProfile,
    ICommandHandler<ChangeMyPasswordCommand> changeMyPassword,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Lists active users with search, role filter, sorting and paging. Any authenticated role.</summary>
    /// <response code="200">A page of users, with paging metadata.</response>
    /// <response code="400">Invalid paging, an unknown role filter, or an unknown sort field.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<UserListItemDto>>> GetUsers(
        [FromQuery] UserQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await getUsers.HandleAsync(
            new GetUsersQuery(
                parameters.PageNumber,
                parameters.PageSize,
                parameters.Search,
                parameters.RoleId,
                parameters.SortBy,
                parameters.SortDirection),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Lists soft-deleted users. Admin only - the single read path over deleted rows.
    /// </summary>
    /// <response code="200">A page of deleted users.</response>
    /// <response code="403">The caller is not an administrator.</response>
    [HttpGet("deleted")]
    [Authorize(Policy = Policies.ManageUsers)]
    [ProducesResponseType<PagedResult<UserListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserListItemDto>>> GetDeletedUsers(
        [FromQuery] UserQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await getDeletedUsers.HandleAsync(
            new GetDeletedUsersQuery(
                parameters.PageNumber,
                parameters.PageSize,
                parameters.Search,
                parameters.RoleId,
                parameters.SortBy,
                parameters.SortDirection),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Whether a username or email is free. A UX aid; the unique index is the authority.</summary>
    [HttpGet("availability")]
    [ProducesResponseType<AvailabilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AvailabilityDto>> CheckAvailability(
        [FromQuery] AvailabilityQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var result = await checkAvailability.HandleAsync(
            new CheckAvailabilityQuery(parameters.Username, parameters.Email, parameters.ExcludeUserId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>The authenticated caller's own profile. No id in the route, so there is none to tamper with.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDetailsDto>> GetMyProfile(CancellationToken cancellationToken) =>
        Ok(await getMyProfile.HandleAsync(new GetMyProfileQuery(), cancellationToken));

    /// <summary>Updates the caller's own first name, last name and email.</summary>
    /// <response code="200">Updated.</response>
    /// <response code="400">Validation failed, or the payload carried a field this endpoint does not accept.</response>
    /// <response code="409">The email belongs to another active user, or the record changed since it was read.</response>
    [HttpPut("me")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailsDto>> UpdateMyProfile(
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await updateMyProfile.HandleAsync(
            new UpdateMyProfileCommand(
                request.FirstName,
                request.LastName,
                request.Email,
                ConcurrencyToken.Parse(request.RowVersion)),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Changes the caller's own password. Requires the current password.</summary>
    /// <response code="204">Changed. Every refresh-token family for this user is revoked.</response>
    /// <response code="401">The current password is wrong.</response>
    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeMyPassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await changeMyPassword.HandleAsync(
            new ChangeMyPasswordCommand(request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return NoContent();
    }

    /// <summary>One user by id. Administrators also see soft-deleted users.</summary>
    /// <response code="404">No such user, or it is deleted and the caller is not an administrator.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailsDto>> GetUserById(Guid id, CancellationToken cancellationToken)
    {
        // Derived from the caller's role, never bound from the request: a non-Admin has no way to ask for a
        // deleted row.
        var includeDeleted = string.Equals(currentUser.Role, RoleNames.Admin, StringComparison.Ordinal);

        var result = await getUserById.HandleAsync(new GetUserByIdQuery(id, includeDeleted), cancellationToken);

        return Ok(result);
    }

    /// <summary>Creates a user. Admin only.</summary>
    /// <response code="201">Created. The Location header points at the new user.</response>
    /// <response code="409">The username or email belongs to another active user.</response>
    [HttpPost]
    [Authorize(Policy = Policies.ManageUsers)]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailsDto>> CreateUser(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await createUser.HandleAsync(
            new CreateUserCommand(
                request.Username,
                request.Email,
                request.FirstName,
                request.LastName,
                request.Password,
                request.RoleId),
            cancellationToken);

        return CreatedAtAction(nameof(GetUserById), new { id = created.Id }, created);
    }

    /// <summary>Updates a user, including their role. Admin only.</summary>
    /// <response code="409">Duplicate email, the last administrator, or a concurrent modification.</response>
    /// <response code="422">An administrator tried to change their own role.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageUsers)]
    [ProducesResponseType<UserDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserDetailsDto>> UpdateUser(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await updateUser.HandleAsync(
            new UpdateUserCommand(
                id,
                request.Email,
                request.FirstName,
                request.LastName,
                request.RoleId,
                ConcurrencyToken.Parse(request.RowVersion)),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Soft-deletes a user. Admin only. No user is ever physically deleted through the API.</summary>
    /// <response code="403">Attempting to delete one's own account.</response>
    /// <response code="409">Already deleted, or this is the last active administrator.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        await deleteUser.HandleAsync(new DeleteUserCommand(id), cancellationToken);

        return NoContent();
    }

    /// <summary>Restores a soft-deleted user. Admin only.</summary>
    /// <response code="409">Not deleted, a concurrent modification, or the identifiers have since been taken.</response>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = Policies.ManageUsers)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreUser(
        Guid id,
        RestoreUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await restoreUser.HandleAsync(
            new RestoreUserCommand(id, ConcurrencyToken.Parse(request.RowVersion)),
            cancellationToken);

        return NoContent();
    }
}
