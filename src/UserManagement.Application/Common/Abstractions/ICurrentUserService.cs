namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// The authenticated caller, as read from the validated token. Implemented in the API layer because it reads
/// <c>HttpContext</c>; declared here so use cases consume identity as plain values and never touch ASP.NET
/// Core types.
/// </summary>
/// <remarks>
/// This is the only source of caller identity. Nothing in the application trusts an identifier that arrived in
/// a request body or route - which is what makes IDOR structurally impossible on the self-service routes.
/// </remarks>
public interface ICurrentUserService
{
    /// <summary>The <c>sub</c> claim, or null when the request is anonymous.</summary>
    Guid? UserId { get; }

    string? Username { get; }

    /// <summary>The single <c>role</c> claim value.</summary>
    string? Role { get; }

    bool IsAuthenticated { get; }
}

/// <summary>Helpers over <see cref="ICurrentUserService"/>.</summary>
public static class CurrentUserServiceExtensions
{
    /// <summary>
    /// The caller's id, or an authentication failure if there is none.
    /// </summary>
    /// <remarks>
    /// The self-service handlers cannot proceed without a subject, and reaching them unauthenticated means the
    /// endpoint is missing its <c>[Authorize]</c> attribute. Failing loudly here turns that wiring mistake into
    /// a 401 instead of a null-reference exception - or, worse, a default id that matches a real row.
    /// </remarks>
    public static Guid RequireUserId(this ICurrentUserService currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return currentUser.UserId
               ?? throw new Exceptions.AuthenticationFailedException(
                   Domain.Constants.ErrorCodes.Unauthenticated,
                   "The request is not authenticated.");
    }
}
