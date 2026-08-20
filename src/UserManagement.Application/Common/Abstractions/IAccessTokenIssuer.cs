using UserManagement.Domain.Entities;

namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Issues short-lived JWT access tokens. The token carries only <c>sub</c>, <c>username</c>, <c>role</c>,
/// <c>jti</c> and the standard registered claims - no email, no permission list, nothing that would have to be
/// re-issued when unrelated data changes.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, string roleName);
}

/// <summary>An issued access token and the moment it stops being valid.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
