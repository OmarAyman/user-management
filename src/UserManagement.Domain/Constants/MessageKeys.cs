namespace UserManagement.Domain.Constants;

/// <summary>
/// Resource keys for messages that a use case names and the API renders.
/// </summary>
/// <remarks>
/// Alongside <see cref="ErrorCodes"/> for the same reason: both are contract constants shared by the layer
/// that raises a failure and the layer that presents it. Declaring them means a typo is a compile error rather
/// than an English sentence surfacing in an Arabic response.
/// </remarks>
public static class MessageKeys
{
    public const string RoleNotFound = "Message.RoleNotFound";
    public const string DateRangeInverted = "Message.DateRangeInverted";
    public const string UsernamePattern = "Validation.Username.Pattern";
    public const string PasswordUppercase = "Validation.Password.Uppercase";
    public const string PasswordLowercase = "Validation.Password.Lowercase";
    public const string PasswordDigit = "Validation.Password.Digit";
    public const string PasswordNotSameAsCurrent = "Validation.Password.NotSameAsCurrent";
    public const string RowVersionMalformed = "Validation.RowVersion.Malformed";
    public const string AvailabilityAtLeastOne = "Validation.Availability.AtLeastOne";

    /// <summary>Every key the application resolves. Used by the resource parity test.</summary>
    public static IReadOnlyList<string> All =>
    [
        RoleNotFound,
        DateRangeInverted,
        UsernamePattern,
        PasswordUppercase,
        PasswordLowercase,
        PasswordDigit,
        PasswordNotSameAsCurrent,
        RowVersionMalformed,
        AvailabilityAtLeastOne,
    ];
}
