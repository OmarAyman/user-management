using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Features.Users.CreateUser;

/// <summary>Creates a user. Admin only.</summary>
public sealed record CreateUserCommand(
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    int RoleId);

/// <summary>
/// Creates a user with a hashed password.
/// </summary>
/// <remarks>
/// The uniqueness checks here exist to produce a clean 409 with the field that collided. They are not the
/// guarantee: two concurrent creates can both pass the check, and the filtered unique index is what actually
/// stops the duplicate. That is the correct division - a check for a good message, a constraint for the truth.
/// </remarks>
public sealed class CreateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateUserCommand, UserDetailsDto>
{
    public async Task<UserDetailsDto> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await roles.GetByIdAsync(command.RoleId, cancellationToken)
                   ?? throw ValidationException.ForField(
                       "roleId",
                       $"Role '{command.RoleId}' does not exist.");

        if (await users.IsUsernameTakenAsync(command.Username, null, cancellationToken))
        {
            throw ConflictException.UsernameTaken(command.Username);
        }

        if (await users.IsEmailTakenAsync(command.Email, null, cancellationToken))
        {
            throw ConflictException.EmailTaken(command.Email);
        }

        var user = User.Create(
            command.Username,
            command.Email,
            command.FirstName,
            command.LastName,

            // The plaintext exists only as a parameter on this call: it is never assigned to the entity, so it
            // cannot reach the change tracker, the audit trail or a log.
            passwordHasher.Hash(command.Password),
            role.Id);

        users.Add(user);

        // Created/modified stamps and the audit row are written by the interceptors, not here.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserProjections.ToDetails(user, role.Name);
    }
}
