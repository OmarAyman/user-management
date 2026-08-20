namespace UserManagement.Domain.Exceptions;

/// <summary>
/// Base type for failures raised by the domain itself. Carries a stable
/// <see cref="Constants.ErrorCodes">error code</see> so the API layer can map it to a status and a localized
/// message without string-matching an exception message.
/// </summary>
public abstract class DomainException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
