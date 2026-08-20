using FluentValidation;
using Microsoft.Extensions.Options;
using UserManagement.Api.Contracts.Users;
using UserManagement.Application.Common.Models;
using UserManagement.Application.Common.Options;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.Validation;

/// <summary>
/// Reusable field rules, so "what a valid email is" is defined once rather than in five validators that
/// slowly diverge.
/// </summary>
internal static class UserFieldRules
{
    internal static IRuleBuilderOptions<T, string> Username<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(3)
            .MaximumLength(UserConstraints.UsernameMaxLength)
            // Letters, digits, dot, underscore and hyphen. Restrictive on purpose: a username appears in audit
            // history and in URLs, so exotic characters buy nothing and cost display and comparison bugs.
            .Matches("^[a-zA-Z0-9._-]+$")
            .WithMessage("Username may contain only letters, digits, dots, underscores and hyphens.");

    internal static IRuleBuilderOptions<T, string> Email<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(UserConstraints.EmailMaxLength)
            .EmailAddress();

    internal static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(UserConstraints.NameMaxLength);

    /// <summary>
    /// The password policy, driven by configuration rather than literals.
    /// </summary>
    /// <remarks>
    /// Length carries the weight; the composition requirements are light. Mandating symbols pushes people
    /// toward predictable substitutions, while length is what actually costs an attacker. The database and the
    /// hasher never see the plaintext beyond the hashing call.
    /// </remarks>
    internal static IRuleBuilderOptions<T, string> Password<T>(
        this IRuleBuilder<T, string> rule,
        PasswordPolicyOptions policy)
    {
        var builder = rule.NotEmpty()
            .MinimumLength(policy.MinimumLength)
            .MaximumLength(policy.MaximumLength);

        if (policy.RequireUppercase)
        {
            builder = builder.Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.");
        }

        if (policy.RequireLowercase)
        {
            builder = builder.Matches("[a-z]").WithMessage("Password must contain a lowercase letter.");
        }

        if (policy.RequireDigit)
        {
            builder = builder.Matches("[0-9]").WithMessage("Password must contain a digit.");
        }

        return builder;
    }

    internal static IRuleBuilderOptions<T, string> ConcurrencyTokenRule<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Must(value => ConcurrencyToken.IsValid(value))
            .WithMessage("The concurrency token is malformed. Reload the record and try again.");
}

public sealed class UserQueryParametersValidator : AbstractValidator<UserQueryParameters>
{
    public UserQueryParametersValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1);

        // Rejected rather than clamped: silently serving 10 rows when 5000 were asked for makes a client
        // believe it has the whole set. An unbounded page size is also a denial-of-service knob.
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, PagingDefaults.MaxPageSize);

        RuleFor(query => query.Search)
            .MaximumLength(PagingDefaults.MaxSearchLength);

        RuleFor(query => query.RoleId)
            .GreaterThan(0)
            .When(query => query.RoleId.HasValue);

        // sortBy is deliberately not validated here: the whitelist lives with the query that applies it, and
        // an unknown field must come back as INVALID_SORT_FIELD rather than a generic validation error.
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator(IOptions<PasswordPolicyOptions> passwordPolicy)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);

        RuleFor(request => request.Username).Username();
        RuleFor(request => request.Email).Email();
        RuleFor(request => request.FirstName).PersonName();
        RuleFor(request => request.LastName).PersonName();
        RuleFor(request => request.Password).Password(passwordPolicy.Value);
        RuleFor(request => request.RoleId).GreaterThan(0);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(request => request.Email).Email();
        RuleFor(request => request.FirstName).PersonName();
        RuleFor(request => request.LastName).PersonName();
        RuleFor(request => request.RoleId).GreaterThan(0);
        RuleFor(request => request.RowVersion).ConcurrencyTokenRule();
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(request => request.FirstName).PersonName();
        RuleFor(request => request.LastName).PersonName();
        RuleFor(request => request.Email).Email();
        RuleFor(request => request.RowVersion).ConcurrencyTokenRule();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(IOptions<PasswordPolicyOptions> passwordPolicy)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);

        RuleFor(request => request.CurrentPassword).NotEmpty();

        // The new password must meet the policy; the current one is only checked for presence, because
        // enforcing today's policy on an existing credential would lock out anyone whose password predates it.
        RuleFor(request => request.NewPassword).Password(passwordPolicy.Value);

        RuleFor(request => request.NewPassword)
            .NotEqual(request => request.CurrentPassword)
            .WithMessage("The new password must be different from the current password.");
    }
}

public sealed class RestoreUserRequestValidator : AbstractValidator<RestoreUserRequest>
{
    public RestoreUserRequestValidator() => RuleFor(request => request.RowVersion).ConcurrencyTokenRule();
}

public sealed class AvailabilityQueryParametersValidator : AbstractValidator<AvailabilityQueryParameters>
{
    public AvailabilityQueryParametersValidator()
    {
        RuleFor(query => query)
            .Must(query => !string.IsNullOrWhiteSpace(query.Username) || !string.IsNullOrWhiteSpace(query.Email))
            .WithMessage("Provide a username, an email, or both.");

        RuleFor(query => query.Username)
            .MaximumLength(UserConstraints.UsernameMaxLength);

        RuleFor(query => query.Email)
            .MaximumLength(UserConstraints.EmailMaxLength);
    }
}
