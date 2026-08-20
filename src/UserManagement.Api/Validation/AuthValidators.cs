using FluentValidation;
using UserManagement.Api.Contracts.Auth;
using UserManagement.Domain.Constants;

namespace UserManagement.Api.Validation;

/// <summary>
/// Shape validation for sign-in.
/// </summary>
/// <remarks>
/// <para>
/// Validators live in the API layer because that is where the validated object exists: the action filter sees
/// the request record the client posted, so a validator written against the internal command would never run -
/// a silent gap that looks like validation while providing none.
/// </para>
/// <para>
/// The division of labour is therefore explicit: field shape (required, length, format) is a transport
/// concern and is enforced here; semantics (uniqueness, the last-administrator rule, role restrictions) are
/// business rules and are enforced by the handlers, which cannot be bypassed by any other caller.
/// </para>
/// <para>
/// This validator deliberately does not apply the password policy. Rejecting a short password at sign-in
/// would reveal that the policy changed, and a user whose existing password predates a stricter policy must
/// still be able to sign in and change it.
/// </para>
/// </remarks>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Username)
            .NotEmpty()
            .MaximumLength(UserConstraints.UsernameMaxLength);

        RuleFor(request => request.Password)
            .NotEmpty()
            .MaximumLength(UserConstraints.PasswordMaxLength);
    }
}
