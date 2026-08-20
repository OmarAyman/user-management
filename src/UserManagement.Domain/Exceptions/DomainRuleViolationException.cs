namespace UserManagement.Domain.Exceptions;

/// <summary>
/// A domain invariant was violated - for example soft-deleting a user who is already deleted. These are
/// state conflicts, so the API maps them to <c>409 Conflict</c> unless a more specific mapping is registered
/// for the code.
/// </summary>
public sealed class DomainRuleViolationException(string errorCode, string message)
    : DomainException(errorCode, message);
