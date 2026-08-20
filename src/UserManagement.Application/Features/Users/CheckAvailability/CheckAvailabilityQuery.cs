using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Features.Users.Dtos;

namespace UserManagement.Application.Features.Users.CheckAvailability;

/// <summary>
/// Whether a username or email is free. Either may be omitted; an omitted one comes back as null rather than
/// a guess.
/// </summary>
public sealed record CheckAvailabilityQuery(string? Username, string? Email, Guid? ExcludeUserId);

/// <summary>
/// Backs the async form validators.
/// </summary>
/// <remarks>
/// <para>
/// A UX aid and nothing more. The unique index decides, and a 409 from create or update maps to a field-level
/// error - which is how the race between "checked" and "submitted" resolves correctly (ADR-0016).
/// </para>
/// <para>
/// Availability is evaluated against active users, so a soft-deleted user's identifiers report as free. This
/// discloses nothing new: every authenticated role may already list and search users, so a boolean here adds
/// no enumeration surface that the list does not already provide. It is authenticated rather than anonymous
/// precisely because anonymous access <i>would</i> add one.
/// </para>
/// </remarks>
public sealed class CheckAvailabilityQueryHandler(IUserRepository users)
    : IQueryHandler<CheckAvailabilityQuery, AvailabilityDto>
{
    public async Task<AvailabilityDto> HandleAsync(
        CheckAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        bool? usernameAvailable = null;
        bool? emailAvailable = null;

        if (!string.IsNullOrWhiteSpace(query.Username))
        {
            usernameAvailable = !await users.IsUsernameTakenAsync(
                query.Username.Trim(),
                query.ExcludeUserId,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            emailAvailable = !await users.IsEmailTakenAsync(
                query.Email.Trim(),
                query.ExcludeUserId,
                cancellationToken);
        }

        return new AvailabilityDto(usernameAvailable, emailAvailable);
    }
}
