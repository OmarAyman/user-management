namespace UserManagement.Domain.Constants;

/// <summary>
/// Stable, never-localized error codes. Every <c>ProblemDetails</c> response carries one, the SPA branches on
/// it, and the localized <c>title</c>/<c>detail</c> are looked up from it. A localized message is a terrible
/// branching key; this is the contract instead.
/// </summary>
/// <remarks>
/// Adding a code means adding an entry to the English resource file, the Arabic resource file and the
/// frontend catalogue. Parity tests fail if any of the three is missing.
/// </remarks>
public static class ErrorCodes
{
    // 400
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidSortField = "INVALID_SORT_FIELD";

    // 401 - all authentication failures that must not disclose whether an account exists
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>
    /// Returned only when the supplied password was correct and the account is locked. A caller who knows the
    /// password already knows the account exists, so this discloses nothing new to them.
    /// </summary>
    public const string AccountLocked = "ACCOUNT_LOCKED";

    // 403
    public const string Forbidden = "FORBIDDEN";
    public const string CannotDeleteSelf = "CANNOT_DELETE_SELF";

    // 404
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";

    // 409
    public const string ResourceConflict = "RESOURCE_CONFLICT";

    /// <summary>Concurrency token was stale: someone else changed the row first.</summary>
    public const string ResourceModified = "RESOURCE_MODIFIED";

    public const string UsernameAlreadyExists = "USERNAME_ALREADY_EXISTS";
    public const string EmailAlreadyExists = "EMAIL_ALREADY_EXISTS";
    public const string UserAlreadyDeleted = "USER_ALREADY_DELETED";
    public const string UserNotDeleted = "USER_NOT_DELETED";
    public const string LastAdminCannotBeRemoved = "LAST_ADMIN_CANNOT_BE_REMOVED";

    // 422
    public const string CannotChangeOwnRole = "CANNOT_CHANGE_OWN_ROLE";

    // 429
    public const string RateLimited = "RATE_LIMITED";

    // 500
    public const string InternalError = "INTERNAL_ERROR";
}
