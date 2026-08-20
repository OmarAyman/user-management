using UserManagement.Domain.Constants;

namespace UserManagement.Application.Common.Exceptions;

/// <summary>
/// Authentication did not succeed. Carries a stable error code, but callers must be careful which one they
/// choose: <see cref="ErrorCodes.InvalidCredentials"/> is used for an unknown username, a wrong password and a
/// soft-deleted account alike, so the response cannot be used to discover whether an account exists.
/// </summary>
public sealed class AuthenticationFailedException(string errorCode, string message)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    /// <summary>
    /// Populated only for <see cref="ErrorCodes.AccountLocked"/>, which is returned exclusively when the
    /// supplied password was correct.
    /// </summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>The response every failure that must not disclose account existence uses.</summary>
    public static AuthenticationFailedException InvalidCredentials() =>
        new(ErrorCodes.InvalidCredentials, "The username or password is incorrect.");

    public static AuthenticationFailedException AccountLocked(TimeSpan retryAfter) =>
        new(ErrorCodes.AccountLocked, "The account is temporarily locked.")
        {
            RetryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds),
        };
}
