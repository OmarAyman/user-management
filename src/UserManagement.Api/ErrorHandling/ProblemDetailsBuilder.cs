using Microsoft.AspNetCore.Mvc;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.ErrorHandling;

/// <summary>
/// Builds every error response in the application. One place, so the shape, the type URI and the trace id
/// cannot vary between endpoints.
/// </summary>
public static class ProblemDetailsBuilder
{
    /// <summary>
    /// Base for the <c>type</c> member. A stable, dereferenceable-looking URI rather than a random string, so
    /// the response is a usable RFC 7807 document.
    /// </summary>
    private const string TypeBaseUri = "https://api.usermanagement.local/errors/";

    /// <summary>
    /// The machine-readable code. Deliberately outside the localized members: the SPA and the tests branch on
    /// this, and a localized sentence is a terrible branching key (ADR-0011).
    /// </summary>
    public const string ErrorCodeMember = "errorCode";

    public const string TraceIdMember = "traceId";

    /// <summary>RFC 7807 media type. Set explicitly so a client can content-negotiate on errors.</summary>
    public const string ProblemContentType = "application/problem+json";

    public static ProblemDetails Build(
        HttpContext context,
        int statusCode,
        string errorCode,
        string title,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = new ProblemDetails
        {
            Type = TypeBaseUri + ToSlug(errorCode),
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = $"{context.Request.Method} {context.Request.Path}",
        };

        problem.Extensions[ErrorCodeMember] = errorCode;

        // The same value appears in the logs, so a user quoting it from an error dialog is quoting something
        // that can actually be looked up.
        problem.Extensions[TraceIdMember] = context.TraceIdentifier;

        return problem;
    }

    public static ValidationProblemDetails BuildValidation(
        HttpContext context,
        IDictionary<string, string[]> errors,
        string title)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = new ValidationProblemDetails(errors)
        {
            Type = TypeBaseUri + ToSlug(ErrorCodes.ValidationError),
            Title = title,
            Status = StatusCodes.Status400BadRequest,
            Instance = $"{context.Request.Method} {context.Request.Path}",
        };

        problem.Extensions[ErrorCodeMember] = ErrorCodes.ValidationError;
        problem.Extensions[TraceIdMember] = context.TraceIdentifier;

        return problem;
    }

    /// <summary>USERNAME_ALREADY_EXISTS becomes username-already-exists.</summary>
    private static string ToSlug(string errorCode) => errorCode.Replace('_', '-').ToLowerInvariant();
}
