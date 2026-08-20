using UserManagement.Domain.Constants;

namespace UserManagement.Application.Common.Exceptions;

/// <summary>
/// Base type for expected failures raised by a use case. Carrying a stable
/// <see cref="ErrorCodes">error code</see> is what lets the API map the failure to a status and a localized
/// message without string-matching an exception message.
/// </summary>
public abstract class ApplicationLayerException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

/// <summary>
/// The request is understood but unacceptable for a reason that is not a per-field validation failure. Maps to
/// 400 with its own code - an unknown sort field, for instance, is not a "field is invalid" message a form can
/// render next to an input, it is a contract violation.
/// </summary>
public sealed class BadRequestException(string errorCode, string message)
    : ApplicationLayerException(errorCode, message)
{
    public static BadRequestException InvalidSortField(string field, IEnumerable<string> allowed) =>
        new(ErrorCodes.InvalidSortField,
            $"'{field}' is not a sortable field. Allowed values: {string.Join(", ", allowed)}.");
}

/// <summary>The target does not exist, or is not visible to this caller. Maps to 404.</summary>
public sealed class NotFoundException(string message, string errorCode = ErrorCodes.ResourceNotFound)
    : ApplicationLayerException(errorCode, message)
{
    public static NotFoundException User(Guid id) => new($"User '{id}' was not found.");
}

/// <summary>The request conflicts with current state. Maps to 409.</summary>
public sealed class ConflictException(string errorCode, string message)
    : ApplicationLayerException(errorCode, message)
{
    public static ConflictException UsernameTaken(string username) =>
        new(ErrorCodes.UsernameAlreadyExists, $"Username '{username}' is already taken.");

    public static ConflictException EmailTaken(string email) =>
        new(ErrorCodes.EmailAlreadyExists, $"Email '{email}' is already taken.");

    public static ConflictException LastAdmin() =>
        new(ErrorCodes.LastAdminCannotBeRemoved, "The last active administrator cannot be removed or demoted.");
}

/// <summary>
/// The caller is authenticated but not permitted to do this. Maps to 403 - distinct from a role check failing
/// at the policy layer, which never reaches a handler.
/// </summary>
public sealed class ForbiddenOperationException(string errorCode, string message)
    : ApplicationLayerException(errorCode, message)
{
    public static ForbiddenOperationException SelfDelete() =>
        new(ErrorCodes.CannotDeleteSelf, "A user cannot delete their own account.");
}

/// <summary>
/// The payload is well formed but semantically invalid. Maps to 422, which is the honest code for "I understood
/// the request and it is still not allowed" - as distinct from a malformed body (400) or a state clash (409).
/// </summary>
public sealed class UnprocessableEntityException(string errorCode, string message)
    : ApplicationLayerException(errorCode, message)
{
    public static UnprocessableEntityException OwnRoleChange() =>
        new(ErrorCodes.CannotChangeOwnRole, "A user cannot change their own role.");
}

/// <summary>
/// One or more fields failed validation. Maps to 400 with a per-field <c>errors</c> dictionary, so a form can
/// show the message next to the field that caused it instead of a single banner.
/// </summary>
public sealed class ValidationException : ApplicationLayerException
{
    public ValidationException(IDictionary<string, string[]> errors)
        : base(ErrorCodes.ValidationError, "One or more validation errors occurred.") => Errors = errors;

    private ValidationException(IReadOnlyList<FieldMessage> keyedErrors)
        : base(ErrorCodes.ValidationError, "One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        KeyedErrors = keyedErrors;
    }

    /// <summary>Already-rendered messages, as produced by the validators at the API boundary.</summary>
    public IDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Messages a use case wants to say but cannot render.
    /// </summary>
    /// <remarks>
    /// A handler knows which field is wrong and why; it does not know the caller's language, and it must not -
    /// giving Application a localizer would put presentation in the layer that owns rules. So it names a
    /// resource key and the API renders it. This is what keeps handler-produced field errors localized without
    /// an English string escaping from a use case into an Arabic response.
    /// </remarks>
    public IReadOnlyList<FieldMessage> KeyedErrors { get; } = [];

    public static ValidationException ForKey(string field, string messageKey, params object[] arguments) =>
        new([new FieldMessage(field, messageKey, arguments)]);
}

/// <summary>A field error expressed as a resource key, rendered at the API boundary.</summary>
public sealed record FieldMessage(string Field, string MessageKey, object[] Arguments);
