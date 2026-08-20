using UserManagement.Domain.Constants;

namespace UserManagement.Api.ErrorHandling;

/// <summary>
/// Resolves a stable error code to human-readable text. The seam through which localization arrives in Phase 6:
/// the error code stays constant, only the sentence changes with the request culture.
/// </summary>
public interface IErrorMessageProvider
{
    string GetTitle(string errorCode);

    string? GetDetail(string errorCode);
}

/// <summary>
/// English messages. Every code in <see cref="ErrorCodes"/> has an entry, and a code with no entry falls back
/// to a generic sentence rather than surfacing the raw code to a user.
/// </summary>
/// <remarks>
/// Phase 6 replaces this with a resource-backed provider that resolves the same keys per request culture. The
/// interface exists now so the exception handlers never had to be rewritten for it.
/// </remarks>
public sealed class EnglishErrorMessageProvider : IErrorMessageProvider
{
    private static readonly Dictionary<string, (string Title, string? Detail)> Messages =
        new(StringComparer.Ordinal)
        {
            [ErrorCodes.ValidationError] = ("One or more validation errors occurred.", "The request contains invalid values."),
            [ErrorCodes.InvalidSortField] = ("Invalid sort field.", "The requested sort field is not supported."),
            [ErrorCodes.InvalidCredentials] = ("Sign-in failed.", "The username or password is incorrect."),
            [ErrorCodes.AccountLocked] = ("Account temporarily locked.", "Too many failed sign-in attempts. Try again later."),
            [ErrorCodes.Unauthenticated] = ("Authentication required.", "A valid access token is required."),
            [ErrorCodes.Forbidden] = ("Not permitted.", "Your role does not allow this operation."),
            [ErrorCodes.CannotDeleteSelf] = ("Not permitted.", "You cannot delete your own account."),
            [ErrorCodes.ResourceNotFound] = ("Not found.", "The requested resource does not exist."),
            [ErrorCodes.ResourceConflict] = ("Conflict.", "The request conflicts with the current state."),
            [ErrorCodes.ResourceModified] = ("The record was changed by someone else.", "Reload the record and apply your changes again."),
            [ErrorCodes.UsernameAlreadyExists] = ("Username already exists.", "Another active user already uses this username."),
            [ErrorCodes.EmailAlreadyExists] = ("Email already exists.", "Another active user already uses this email address."),
            [ErrorCodes.UserAlreadyDeleted] = ("User already deleted.", "This user has already been deleted."),
            [ErrorCodes.UserNotDeleted] = ("User is not deleted.", "Only a deleted user can be restored."),
            [ErrorCodes.LastAdminCannotBeRemoved] = ("Last administrator.", "The system must keep at least one active administrator."),
            [ErrorCodes.CannotChangeOwnRole] = ("Not permitted.", "You cannot change your own role."),
            [ErrorCodes.RateLimited] = ("Too many requests.", "Slow down and try again shortly."),

            // Nothing but the trace id: an unexpected failure must not describe itself to a client.
            [ErrorCodes.InternalError] = ("An unexpected error occurred.", null),
        };

    public string GetTitle(string errorCode) =>
        Messages.TryGetValue(errorCode, out var message) ? message.Title : "Request failed.";

    public string? GetDetail(string errorCode) =>
        Messages.TryGetValue(errorCode, out var message) ? message.Detail : null;

    /// <summary>Used by a test to prove every declared code has copy behind it.</summary>
    public static IReadOnlySet<string> KnownCodes => Messages.Keys.ToHashSet(StringComparer.Ordinal);
}
