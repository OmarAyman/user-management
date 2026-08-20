using Microsoft.AspNetCore.Diagnostics;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.ErrorHandling;

/// <summary>
/// Last handler in the chain. Logs the full failure server-side and returns a response that describes nothing.
/// </summary>
/// <remarks>
/// The body carries the error code and the trace id, and nothing else - no message, no exception type, no
/// stack, no SQL. That is not tidiness: an exception message is where connection strings, table names and
/// internal hostnames leak, and a client has no legitimate use for any of them.
/// </remarks>
public sealed class UnhandledExceptionHandler(
    ILogger<UnhandledExceptionHandler> logger,
    IErrorMessageProvider messages) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path} (trace {TraceId})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        var problem = ProblemDetailsBuilder.Build(
            httpContext,
            StatusCodes.Status500InternalServerError,
            ErrorCodes.InternalError,
            messages.GetTitle(ErrorCodes.InternalError));

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = ProblemDetailsBuilder.ProblemContentType;
        await httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken);

        return true;
    }
}
