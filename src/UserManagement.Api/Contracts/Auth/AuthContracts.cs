namespace UserManagement.Api.Contracts.Auth;

/// <summary>Sign-in payload.</summary>
public sealed record LoginRequest(string Username, string Password);

/// <summary>
/// Sign-in response. It carries the access token and the caller's identity - and deliberately not the refresh
/// token, which travels only as an httpOnly cookie. A refresh token in the body would be readable by any
/// script on the page, which is the exact risk the cookie exists to avoid.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUserResponse User);

public sealed record AuthenticatedUserResponse(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Role);
