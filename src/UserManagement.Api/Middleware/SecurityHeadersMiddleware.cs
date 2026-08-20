namespace UserManagement.Api.Middleware;

/// <summary>
/// Adds the response headers that cost nothing and remove whole classes of attack.
/// </summary>
/// <remarks>
/// The API serves JSON, not documents, so the CSP here is a defence-in-depth measure for anything that ever
/// renders a response directly (an error page, a Swagger asset) rather than the SPA's policy, which its own
/// host serves.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        // No MIME sniffing: stops a JSON response being reinterpreted as script.
        headers["X-Content-Type-Options"] = "nosniff";

        // Nothing here should ever be framed.
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        // Removes a free version-fingerprinting signal.
        headers.Remove("Server");

        await next(context);
    }
}
