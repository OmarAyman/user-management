using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using UserManagement.Api.Configuration;
using UserManagement.Api.ErrorHandling;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.RateLimiting;

/// <summary>Named rate-limit policies.</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to the auth endpoints. Account lockout stops many guesses against one account; this stops many
    /// accounts being tried from one source, which lockout cannot see.
    /// </summary>
    public const string Authentication = "authentication";

    public static IServiceCollection AddApplicationRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
                     ?? new RateLimitingOptions();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Authentication, context => RateLimitPartition.GetFixedWindowLimiter(
                // Partitioned by client address, so one noisy client cannot exhaust everyone's budget.
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.AuthPermitLimit,
                    Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),

                    // No queue: a rejected sign-in should fail immediately rather than be held open, which
                    // would tie up connections during exactly the burst the limit exists to shed.
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var problem = ProblemDetailsBuilder.Build(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    ErrorCodes.RateLimited,
                    "Too many requests.",
                    "Slow down and try again shortly.");

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    context.HttpContext.Response.Headers.RetryAfter = seconds;
                    problem.Extensions["retryAfterSeconds"] = (int)retryAfter.TotalSeconds;
                }

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // Rejections go through the same ProblemDetails shape as every other error: a client should
                // never have to parse a second error format just for this one case.
                context.HttpContext.Response.ContentType = ProblemDetailsBuilder.ProblemContentType;
                await context.HttpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken);
            };
        });

        return services;
    }
}
