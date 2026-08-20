using UserManagement.Application.Common.Abstractions;

namespace UserManagement.Application.Features.Auth.Login;

/// <summary>Sign-in with a username and password.</summary>
public sealed record LoginCommand(string Username, string Password);

/// <summary>
/// The result of a successful sign-in. The refresh token is returned so the API can put it in an httpOnly
/// cookie; it is never serialised into the response body, because a token readable by script defeats the point.
/// </summary>
public sealed record LoginResult(
    AccessToken AccessToken,
    IssuedRefreshToken RefreshToken,
    AuthenticatedUser User);

/// <summary>The identity fields the SPA needs to render the shell without a second call.</summary>
public sealed record AuthenticatedUser(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Role);
