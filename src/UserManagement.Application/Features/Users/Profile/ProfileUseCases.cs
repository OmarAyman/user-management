using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Domain.Enums;

namespace UserManagement.Application.Features.Users.Profile;

/// <summary>
/// The authenticated caller's own profile.
/// </summary>
/// <remarks>
/// None of these carry a user id. The subject comes from the validated token through
/// <see cref="ICurrentUserService"/>, so there is no identifier for an attacker to swap - IDOR is prevented by
/// the shape of the request rather than by a check somebody has to remember (T-07).
/// </remarks>
public sealed record GetMyProfileQuery;

public sealed class GetMyProfileQueryHandler(
    IUserRepository users,
    IRoleRepository roles,
    ICurrentUserService currentUser) : IQueryHandler<GetMyProfileQuery, UserDetailsDto>
{
    public async Task<UserDetailsDto> HandleAsync(GetMyProfileQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var user = await users.GetByIdAsync(userId, cancellationToken)
                   ?? throw NotFoundException.User(userId);

        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken);

        return UserProjections.ToDetails(user, role?.Name ?? string.Empty);
    }
}

/// <summary>
/// Updates the caller's own profile.
/// </summary>
/// <remarks>
/// Three fields. No role, no <c>isDeleted</c>, no username: the model cannot carry them, so a payload that
/// includes them is rejected outright rather than silently stripped (T-08, BR-04, BR-10).
/// </remarks>
public sealed record UpdateMyProfileCommand(
    string FirstName,
    string LastName,
    string Email,
    byte[] RowVersion);

public sealed class UpdateMyProfileCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateMyProfileCommand, UserDetailsDto>
{
    public async Task<UserDetailsDto> HandleAsync(
        UpdateMyProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        var user = await users.GetByIdAsync(userId, cancellationToken)
                   ?? throw NotFoundException.User(userId);

        users.ApplyConcurrencyToken(user, command.RowVersion);

        if (await users.IsEmailTakenAsync(command.Email, user.Id, cancellationToken))
        {
            throw ConflictException.EmailTaken(command.Email);
        }

        user.UpdateProfile(command.FirstName, command.LastName, command.Email);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var role = await roles.GetByIdAsync(user.RoleId, cancellationToken);

        return UserProjections.ToDetails(user, role?.Name ?? string.Empty);
    }
}

/// <summary>Changes the caller's own password, proving knowledge of the current one.</summary>
public sealed record ChangeMyPasswordCommand(string CurrentPassword, string NewPassword);

public sealed class ChangeMyPasswordCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IRefreshTokenService refreshTokens,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : ICommandHandler<ChangeMyPasswordCommand>
{
    public async Task HandleAsync(ChangeMyPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        var user = await users.GetByIdAsync(userId, cancellationToken)
                   ?? throw NotFoundException.User(userId);

        // Requiring the current password is what stops a stolen access token from becoming permanent account
        // ownership: an attacker with a token still cannot change the credential.
        if (passwordHasher.Verify(user.PasswordHash, command.CurrentPassword) == PasswordVerificationOutcome.Failed)
        {
            throw AuthenticationFailedException.InvalidCredentials();
        }

        // Rotates the security stamp, which is the domain's way of saying every existing session is stale.
        user.SetPasswordHash(passwordHasher.Hash(command.NewPassword));

        // A password change is the one action a user takes specifically because they fear their sessions are
        // compromised, so every session goes - not just this one (BR-13).
        await refreshTokens.RevokeAllForUserAsync(user.Id, RevocationReason.PasswordChanged, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
