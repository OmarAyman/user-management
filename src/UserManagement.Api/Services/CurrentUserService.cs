using System.Security.Claims;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Infrastructure.Security;

namespace UserManagement.Api.Services;

/// <summary>
/// Reads the authenticated caller from the validated token.
/// </summary>
/// <remarks>
/// Claim names are the short, unmapped ones this application issues, because the bearer handler is configured
/// with <c>MapInboundClaims = false</c>. That keeps what is written into a token and what is read back out
/// identical, instead of silently translated into WS-Federation URIs.
/// </remarks>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid? UserId =>
        Guid.TryParse(FindClaim(JwtClaimNames.Subject), out var userId) ? userId : null;

    public string? Username => FindClaim(JwtClaimNames.Username);

    public string? Role => FindClaim(JwtClaimNames.Role);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    private string? FindClaim(string type) => Principal?.FindFirst(type)?.Value;
}
