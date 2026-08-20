using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Enums;

namespace UserManagement.Application.Features.Users.UpdateUser;

/// <summary>
/// Updates a user. Admin only.
/// </summary>
/// <remarks>
/// The command carries no username (immutable, BR-10), no password (its own use case), and no <c>isDeleted</c>
/// (deletion has its own endpoint). Those fields are absent rather than ignored: a field that cannot be
/// carried cannot be mass-assigned.
/// </remarks>
public sealed record UpdateUserCommand(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    int RoleId,
    byte[] RowVersion);

public sealed class UpdateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IRefreshTokenService refreshTokens,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateUserCommand, UserDetailsDto>
{
    public async Task<UserDetailsDto> HandleAsync(
        UpdateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Active users only. A deleted user must be restored before it can be edited, which keeps "deleted"
        // meaning one thing rather than "deleted but still maintainable".
        var user = await users.GetByIdAsync(command.Id, cancellationToken)
                   ?? throw NotFoundException.User(command.Id);

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken)
                   ?? throw ValidationException.ForKey("roleId", MessageKeys.RoleNotFound, command.RoleId);

        users.ApplyConcurrencyToken(user, command.RowVersion);

        if (await users.IsEmailTakenAsync(command.Email, user.Id, cancellationToken))
        {
            throw ConflictException.EmailTaken(command.Email);
        }

        var roleChanged = user.RoleId != role.Id;

        if (roleChanged)
        {
            await GuardRoleChangeAsync(user.Id, user.RoleId, cancellationToken);
        }

        user.UpdateProfile(command.FirstName, command.LastName, command.Email);

        if (roleChanged)
        {
            user.ChangeRole(role.Id);

            // A role change alters what every outstanding session is allowed to do, so those sessions end.
            // The already-issued access token still lives out its 15 minutes - documented as residual risk
            // T-04 - but it cannot be renewed under the old privileges.
            await refreshTokens.RevokeAllForUserAsync(user.Id, RevocationReason.RoleChanged, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserProjections.ToDetails(user, role.Name);
    }

    private async Task GuardRoleChangeAsync(Guid userId, int currentRoleId, CancellationToken cancellationToken)
    {
        // Nobody changes their own role, administrators included. Self-elevation and self-demotion are both
        // ruled out by the same check (BR-04).
        if (currentUser.UserId == userId)
        {
            throw UnprocessableEntityException.OwnRoleChange();
        }

        if (currentRoleId != RoleIds.Admin)
        {
            return;
        }

        // Counted inside the same transaction as the change, so two concurrent demotions cannot race the
        // system down to zero administrators: the second transaction re-reads and fails (BR-03).
        if (await users.CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            throw ConflictException.LastAdmin();
        }
    }
}

