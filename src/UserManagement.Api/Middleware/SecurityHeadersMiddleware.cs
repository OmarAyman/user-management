namespace UserManagement.Api.Middleware;

/// <summary>
/// Adds the response headers that cost nothing and remove whole classes of attack.
/// </summary>
/// <remarks>
/// The API serves JSON, not documents, so the policy below is deliberately absolute: a JSON response has no
/// business loading anything at all. The SPA's own policy is served by its host, not from here.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>For everything this API answers with. JSON should load nothing, ever.</summary>
    private const string ApiPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>
    /// For Swagger UI, the one HTML document this API serves.
    /// </summary>
    /// <remarks>
    /// It loads a stylesheet and two scripts from this origin, plus an inline initialiser that Swashbuckle
    /// emits. Under <see cref="ApiPolicy"/> the page arrived with a 200 and then rendered nothing, because the
    /// browser blocked every one of them - so the link the README hands an evaluator looked broken while curl
    /// reported success, which is the most annoying shape a defect can take.
    ///
    /// Still restrictive where it matters: same-origin only, no third-party host, and framing is refused. The
    /// `unsafe-inline` allowances are scoped to this path and nothing else, and Swagger is off outside
    /// Development unless a deployment switches it on deliberately.
    /// </remarks>
    private const string SwaggerPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; "
        + "form-action 'self'";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        // No MIME sniffing: stops a JSON response being reinterpreted as script.
        headers["X-Content-Type-Options"] = "nosniff";

        // Nothing here should ever be framed.
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        headers["Content-Security-Policy"] = IsSwagger(context.Request.Path) ? SwaggerPolicy : ApiPolicy;

        // Removes a free version-fingerprinting signal.
        headers.Remove("Server");

        await next(context);
    }

    private static bool IsSwagger(PathString path) =>
        path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase);
}
