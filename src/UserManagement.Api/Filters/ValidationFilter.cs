using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using ApplicationValidationException = UserManagement.Application.Common.Exceptions.ValidationException;

namespace UserManagement.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument before the action executes.
/// </summary>
/// <remarks>
/// This is why no handler re-validates request shape, and why controllers contain no <c>if (!ModelState.IsValid)</c>
/// boilerplate. Failures are converted into the Application layer's validation exception, so the response shape
/// is produced by the same error pipeline as everything else instead of by a second, parallel format.
/// </remarks>
public sealed class ValidationFilter(IServiceProvider serviceProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        foreach (var argument in context.ActionArguments.Values.Where(value => value is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());

            if (serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (result.IsValid)
            {
                continue;
            }

            var errors = result.Errors
                .GroupBy(failure => ToCamelCase(failure.PropertyName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.ErrorMessage).ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            throw new ApplicationValidationException(errors);
        }

        await next();
    }

    /// <summary>
    /// Field names in the response match the JSON the client sent, not the C# property. A form that posted
    /// "firstName" must get its error back under "firstName".
    /// </summary>
    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var segments = propertyName.Split('.');

        for (var index = 0; index < segments.Length; index++)
        {
            if (segments[index].Length > 0)
            {
                segments[index] = char.ToLowerInvariant(segments[index][0]) + segments[index][1..];
            }
        }

        return string.Join('.', segments);
    }
}
