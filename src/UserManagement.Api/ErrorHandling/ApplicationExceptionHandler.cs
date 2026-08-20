using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Exceptions;

namespace UserManagement.Api.ErrorHandling;

/// <summary>
/// Translates expected failures into RFC 7807 responses. Runs first in the handler chain; anything it does not
/// recognise falls through to <see cref="UnhandledExceptionHandler"/>.
/// </summary>
/// <remarks>
/// A single handler with one mapping method, rather than one class per exception type: the whole
/// exception-to-status contract is then readable in one screen, which is exactly the property that stops two
/// endpoints from disagreeing about what a conflict looks like.
/// </remarks>
public sealed class ApplicationExceptionHandler(
    ILogger<ApplicationExceptionHandler> logger,
    IErrorMessageProvider messages,
    IMessageLocalizer localizer) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = Map(httpContext, exception);

        if (problem is null)
        {
            return false;
        }

        // Expected failures are information, not incidents: logged at Information so a wrong password does not
        // look like a fault, with enough context to spot a pattern.
        logger.LogInformation(
            "Request {Method} {Path} failed with {ErrorCode} ({StatusCode})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            problem.Extensions[ProblemDetailsBuilder.ErrorCodeMember],
            problem.Status);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status400BadRequest;
        httpContext.Response.ContentType = ProblemDetailsBuilder.ProblemContentType;
        await httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken);

        return true;
    }

    private ProblemDetails? Map(HttpContext context, Exception exception) => exception switch
    {
        ValidationException validation => ProblemDetailsBuilder.BuildValidation(
            context,
            RenderFieldErrors(validation),
            messages.GetTitle(ErrorCodes.ValidationError)),

        // Not a per-field validation failure, so it carries its own code rather than VALIDATION_ERROR: an
        // unknown sort field is a contract violation, not a message a form renders next to an input.
        BadRequestException badRequest => Build(context, StatusCodes.Status400BadRequest, badRequest.ErrorCode),

        AuthenticationFailedException auth => BuildAuthentication(context, auth),

        NotFoundException notFound => Build(context, StatusCodes.Status404NotFound, notFound.ErrorCode),

        ForbiddenOperationException forbidden => Build(context, StatusCodes.Status403Forbidden, forbidden.ErrorCode),

        ConflictException conflict => Build(context, StatusCodes.Status409Conflict, conflict.ErrorCode),

        UnprocessableEntityException unprocessable => Build(
            context,
            StatusCodes.Status422UnprocessableEntity,
            unprocessable.ErrorCode),

        // A domain invariant breach is a state conflict by default; the specific code still travels with it.
        DomainRuleViolationException domain => Build(context, StatusCodes.Status409Conflict, domain.ErrorCode),

        // Someone else changed the row first. The response deliberately carries no server state: merging is a
        // UI decision, and shipping the winning row inside a conflict invites a blind retry that recreates the
        // lost update this exists to prevent (ADR-0013).
        DbUpdateConcurrencyException => Build(context, StatusCodes.Status409Conflict, ErrorCodes.ResourceModified),

        _ => null,
    };

    /// <summary>
    /// Merges the validators' already-rendered messages with the use cases' keyed ones, rendering the latter in
    /// the request's culture. This is the point where an Application-layer field error becomes text, so a
    /// handler never has to know which language the caller reads.
    /// </summary>
    private IDictionary<string, string[]> RenderFieldErrors(ValidationException exception)
    {
        if (exception.KeyedErrors.Count == 0)
        {
            return exception.Errors;
        }

        var merged = new Dictionary<string, string[]>(exception.Errors, StringComparer.OrdinalIgnoreCase);

        foreach (var group in exception.KeyedErrors.GroupBy(error => error.Field, StringComparer.OrdinalIgnoreCase))
        {
            var rendered = group
                .Select(error => localizer.Get(error.MessageKey, error.Arguments))
                .ToArray();

            merged[group.Key] = merged.TryGetValue(group.Key, out var existing)
                ? [.. existing, .. rendered]
                : rendered;
        }

        return merged;
    }

    private ProblemDetails Build(HttpContext context, int statusCode, string errorCode) =>
        ProblemDetailsBuilder.Build(
            context,
            statusCode,
            errorCode,
            messages.GetTitle(errorCode),
            messages.GetDetail(errorCode));

    private ProblemDetails BuildAuthentication(HttpContext context, AuthenticationFailedException exception)
    {
        var problem = Build(context, StatusCodes.Status401Unauthorized, exception.ErrorCode);

        if (exception.RetryAfterSeconds is { } retryAfter)
        {
            problem.Extensions["retryAfterSeconds"] = retryAfter;
            context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return problem;
    }
}

