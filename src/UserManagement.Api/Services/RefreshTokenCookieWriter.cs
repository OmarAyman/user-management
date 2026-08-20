using Microsoft.Extensions.Options;
using UserManagement.Infrastructure.Configuration;

namespace UserManagement.Api.Services;

/// <summary>
/// Writes, reads and clears the refresh-token cookie. The one place that knows the cookie's flags, so they
/// cannot be set inconsistently by two endpoints.
/// </summary>
public sealed class RefreshTokenCookieWriter(IOptions<RefreshTokenOptions> options)
{
    private readonly RefreshTokenOptions _options = options.Value;

    public void Write(HttpContext context, string rawToken, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Append(_options.CookieName, rawToken, new CookieOptions
        {
            // Not readable by script: the entire reason the access token can stay in memory and still survive
            // a page reload.
            HttpOnly = true,

            Secure = _options.SecureCookie,

            // Strict rather than Lax: nothing in this application needs the cookie to survive a cross-site
            // navigation, and Strict removes the CSRF surface that Lax leaves on top-level GETs.
            SameSite = SameSiteMode.Strict,

            // Scoped to the auth routes, so it is not attached to every API call - only the two requests that
            // actually need it can carry it.
            Path = _options.CookiePath,

            Expires = expiresAt,
            IsEssential = true,
        });
    }

    public string? Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.Cookies.TryGetValue(_options.CookieName, out var value) ? value : null;
    }

    public void Clear(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Deleting must repeat the path, or the browser keeps a cookie set on a different one.
        context.Response.Cookies.Delete(_options.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.SecureCookie,
            SameSite = SameSiteMode.Strict,
            Path = _options.CookiePath,
        });
    }
}
