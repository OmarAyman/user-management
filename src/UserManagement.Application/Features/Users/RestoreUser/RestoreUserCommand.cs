using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;

namespace UserManagement.Application.Features.Users.RestoreUser;

/// <summary>Restores a soft-deleted user. Admin only.</summary>
public sealed record RestoreUserCommand(Guid Id, byte[] RowVersion);

/// <summary>
/// Brings a soft-deleted user back.
/// </summary>
/// <remarks>
/// Restoring has to re-check availability, and that is a direct consequence of scoping uniqueness to active
/// rows (ADR-0009): while the user was deleted, someone else may have taken their username or email. Checking
/// here turns that into a clear 409 naming the field, instead of a filtered-unique-index violation surfacing
/// as an unhandled database error (BR-17).
/// </remarks>
public sealed class RestoreUserCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork) : ICommandHandler<RestoreUserCommand>
{
    public async Task HandleAsync(RestoreUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await users.GetByIdIncludingDeletedAsync(command.Id, cancellationToken)
                   ?? throw NotFoundException.User(command.Id);

        users.ApplyConcurrencyToken(user, command.RowVersion);

        if (await users.IsUsernameTakenAsync(user.Username, user.Id, cancellationToken))
        {
            throw ConflictException.UsernameTaken(user.Username);
        }

        if (await users.IsEmailTakenAsync(user.Email, user.Id, cancellationToken))
        {
            throw ConflictException.EmailTaken(user.Email);
        }

        // Throws if the user is not deleted (BR-08).
        user.Restore();

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
