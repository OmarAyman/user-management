using FluentValidation;
using Microsoft.Extensions.Localization;
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
    // Custom messages resolve through the same resource file as the error titles, so an Arabic request gets
    // Arabic field errors. FluentValidation's own built-in messages (NotEmpty, MaximumLength, EmailAddress)
    // are already translated by its language manager, which reads CultureInfo.CurrentUICulture - set for the
    // request by the localization middleware. So neither kind of message needs a language check anywhere.

    internal static IRuleBuilderOptions<T, string> Username<T>(
        this IRuleBuilder<T, string> rule,
        IStringLocalizer localizer) =>
        rule.NotEmpty()
            .MinimumLength(3)
            .MaximumLength(UserConstraints.UsernameMaxLength)
            // Letters, digits, dot, underscore and hyphen. Restrictive on purpose: a username appears in audit
            // history and in URLs, so exotic characters buy nothing and cost display and comparison bugs.
            .Matches("^[a-zA-Z0-9._-]+$")
            .WithMessage(_ => localizer[MessageKeys.UsernamePattern].Value);

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
        PasswordPolicyOptions policy,
        IStringLocalizer localizer)
    {
        var builder = rule.NotEmpty()
            .MinimumLength(policy.MinimumLength)
            .MaximumLength(policy.MaximumLength);

        if (policy.RequireUppercase)
        {
            builder = builder.Matches("[A-Z]").WithMessage(_ => localizer[MessageKeys.PasswordUppercase].Value);
        }

        if (policy.RequireLowercase)
        {
            builder = builder.Matches("[a-z]").WithMessage(_ => localizer[MessageKeys.PasswordLowercase].Value);
        }

        if (policy.RequireDigit)
        {
            builder = builder.Matches("[0-9]").WithMessage(_ => localizer[MessageKeys.PasswordDigit].Value);
        }

        return builder;
    }

    internal static IRuleBuilderOptions<T, string> ConcurrencyTokenRule<T>(
        this IRuleBuilder<T, string> rule,
        IStringLocalizer localizer) =>
        rule.NotEmpty()
            .Must(value => ConcurrencyToken.IsValid(value))
            .WithMessage(_ => localizer[MessageKeys.RowVersionMalformed].Value);
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
    public CreateUserRequestValidator(
        IOptions<PasswordPolicyOptions> passwordPolicy,
        IStringLocalizer<ErrorMessages> localizer)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);

        RuleFor(request => request.Username).Username(localizer);
        RuleFor(request => request.Email).Email();
        RuleFor(request => request.FirstName).PersonName();
        RuleFor(request => request.LastName).PersonName();
        RuleFor(request => request.Password).Password(passwordPolicy.Value, localizer);
        RuleFor(request => request.RoleId).GreaterThan(0);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator(IStringLocalizer<ErrorMessages> localizer)
    {
        RuleFor(request => request.Email).Email();
        RuleFor(request => request.FirstName).PersonName();
        RuleFor(request => request.LastName).PersonName();
        RuleFor(request => request.RoleId).GreaterThan(0);
        RuleFor(request => request.RowVersion).ConcurrencyTokenRule(localizer);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator(IStringLocalizer<ErrorMessages> localizer)
    {
        RuleFor(request => request.FirstName).PersonName();
        RuleFor(request => request.LastName).PersonName();
        RuleFor(request => request.Email).Email();
        RuleFor(request => request.RowVersion).ConcurrencyTokenRule(localizer);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator(
        IOptions<PasswordPolicyOptions> passwordPolicy,
        IStringLocalizer<ErrorMessages> localizer)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);

        RuleFor(request => request.CurrentPassword).NotEmpty();

        // The new password must meet the policy; the current one is only checked for presence, because
        // enforcing today's policy on an existing credential would lock out anyone whose password predates it.
        RuleFor(request => request.NewPassword).Password(passwordPolicy.Value, localizer);

        RuleFor(request => request.NewPassword)
            .NotEqual(request => request.CurrentPassword)
            .WithMessage(_ => localizer[MessageKeys.PasswordNotSameAsCurrent].Value);
    }
}

public sealed class RestoreUserRequestValidator : AbstractValidator<RestoreUserRequest>
{
    public RestoreUserRequestValidator(IStringLocalizer<ErrorMessages> localizer) =>
        RuleFor(request => request.RowVersion).ConcurrencyTokenRule(localizer);
}

public sealed class AvailabilityQueryParametersValidator : AbstractValidator<AvailabilityQueryParameters>
{
    public AvailabilityQueryParametersValidator(IStringLocalizer<ErrorMessages> localizer)
    {
        RuleFor(query => query)
            .Must(query => !string.IsNullOrWhiteSpace(query.Username) || !string.IsNullOrWhiteSpace(query.Email))
            .WithMessage(_ => localizer[MessageKeys.AvailabilityAtLeastOne].Value);

        RuleFor(query => query.Username)
            .MaximumLength(UserConstraints.UsernameMaxLength);

        RuleFor(query => query.Email)
            .MaximumLength(UserConstraints.EmailMaxLength);
    }
}


