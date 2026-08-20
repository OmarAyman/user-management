using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using UserManagement.Api.Contracts.Auth;
using UserManagement.Api.RateLimiting;
using UserManagement.Api.Services;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Features.Auth.Login;
using UserManagement.Application.Features.Auth.Logout;
using UserManagement.Application.Features.Auth.RefreshSession;

namespace UserManagement.Api.Controllers;

/// <summary>
/// Sign-in, silent refresh and sign-out.
/// </summary>
/// <remarks>
/// Rate limited as a group: lockout protects one account from many guesses, and this protects many accounts
/// from one source. Neither is sufficient alone.
/// </remarks>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Authentication)]
[Produces("application/json")]
public sealed class AuthController(
    ICommandHandler<LoginCommand, LoginResult> login,
    ICommandHandler<RefreshSessionCommand, LoginResult> refresh,
    ICommandHandler<LogoutCommand> logout,
    RefreshTokenCookieWriter cookies) : ControllerBase
{
    /// <summary>Signs in with a username and password.</summary>
    /// <response code="200">Authenticated. The refresh token is set as an httpOnly cookie.</response>
    /// <response code="400">The request is malformed.</response>
    /// <response code="401">Invalid credentials, or the account is temporarily locked.</response>
    /// <response code="429">Too many attempts from this client.</response>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await login.HandleAsync(
            new LoginCommand(request.Username, request.Password),
            cancellationToken);

        return Ok(IssueSession(result));
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token, rotating the cookie.
    /// </summary>
    /// <response code="200">A new access token was issued and the cookie rotated.</response>
    /// <response code="401">The cookie is missing, expired, revoked, or was already used.</response>
    [HttpPost("refresh")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken cancellationToken)
    {
        var result = await refresh.HandleAsync(
            new RefreshSessionCommand(cookies.Read(HttpContext)),
            cancellationToken);

        return Ok(IssueSession(result));
    }

    /// <summary>Signs out by revoking the presented refresh token.</summary>
    /// <response code="204">Signed out. Idempotent: also returned when there was no session to end.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await logout.HandleAsync(new LogoutCommand(cookies.Read(HttpContext)), cancellationToken);

        cookies.Clear(HttpContext);

        return NoContent();
    }

    /// <summary>
    /// Writes the rotated refresh cookie and projects the response. Shared by sign-in and refresh so the two
    /// cannot drift apart in what they set or return.
    /// </summary>
    private LoginResponse IssueSession(LoginResult result)
    {
        cookies.Write(HttpContext, result.RefreshToken.RawToken, result.RefreshToken.ExpiresAt);

        return new LoginResponse(
            result.AccessToken.Value,
            result.AccessToken.ExpiresAt,
            new AuthenticatedUserResponse(
                result.User.Id,
                result.User.Username,
                result.User.Email,
                result.User.FirstName,
                result.User.LastName,
                result.User.Role));
    }
}
