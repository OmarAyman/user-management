using Serilog.Context;

namespace UserManagement.Api.Middleware;

/// <summary>
/// Gives every request a correlation id: the client's if it supplied one, otherwise the trace identifier.
/// The value is echoed back, pushed into the log scope, and recorded on audit rows - so a support ticket
/// quoting one id can be followed through the logs and the audit trail without guesswork.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Bounded so a hostile client cannot push an arbitrarily long value into every log line.</summary>
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Resolve(context);

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string Resolve(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(supplied))
        {
            return context.TraceIdentifier;
        }

        return supplied.Length > MaxLength ? supplied[..MaxLength] : supplied;
    }
}
