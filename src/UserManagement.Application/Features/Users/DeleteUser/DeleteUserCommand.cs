using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Enums;

namespace UserManagement.Application.Features.Users.DeleteUser;

/// <summary>Soft-deletes a user. Admin only.</summary>
public sealed record DeleteUserCommand(Guid Id);

/// <summary>
/// Soft-deletes a user and ends their sessions.
/// </summary>
/// <remarks>
/// No concurrency token is required. Deletion is idempotent in intent, the domain already refuses a
/// second delete (BR-07), and a stale delete cannot destroy an edit the caller has not seen - so demanding a
/// token would add a round trip without preventing anything.
/// </remarks>
public sealed class DeleteUserCommandHandler(
    IUserRepository users,
    IRefreshTokenService refreshTokens,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteUserCommand>
{
    public async Task HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // An administrator deleting themselves would be one click from locking the system's owner out of it,
        // and no legitimate flow needs it (BR-01).
        if (currentUser.UserId == command.Id)
        {
            throw ForbiddenOperationException.SelfDelete();
        }

        // Includes deleted rows so a second delete is a clear 409 rather than a misleading 404: an Admin needs
        // to know the difference between "already gone" and "never existed".
        var user = await users.GetByIdIncludingDeletedAsync(command.Id, cancellationToken)
                   ?? throw NotFoundException.User(command.Id);

        if (user.RoleId == RoleIds.Admin && await users.CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            throw ConflictException.LastAdmin();
        }

        // Throws if the user is already deleted - the invariant lives on the entity, so every caller gets it.
        user.SoftDelete(currentUser.Username ?? SystemActors.System, clock.UtcNow);

        await refreshTokens.RevokeAllForUserAsync(user.Id, RevocationReason.UserDeleted, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
