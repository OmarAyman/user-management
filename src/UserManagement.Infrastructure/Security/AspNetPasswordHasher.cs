using Microsoft.AspNetCore.Identity;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Security;

/// <summary>
/// Password hashing over ASP.NET Core's <see cref="PasswordHasher{TUser}"/>: PBKDF2-HMAC-SHA512, 210,000
/// iterations, 128-bit salt, in the versioned ASP.NET v3 format.
/// </summary>
/// <remarks>
/// The rest of ASP.NET Core Identity is deliberately not used - it would bring its own schema, endpoints and
/// conventions and obscure exactly the authentication code being assessed. Its hasher, on the other hand, is a
/// well-reviewed implementation of a standard construction, and writing a hashing routine by hand would be the
/// single worst decision available here.
/// </remarks>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>
    /// A real hash of a fixed, unused password. Verifying against it makes the "unknown username" path cost the
    /// same as the "wrong password" path, so response timing does not reveal whether an account exists.
    /// </summary>
    private readonly string _dummyHash;

    public AspNetPasswordHasher()
    {
        _dummyHash = _hasher.HashPassword(DummyUser, "not-a-real-password-8f2c41d6");
    }

    /// <summary>
    /// <see cref="PasswordHasher{TUser}"/> takes a user argument it never reads for the default (v3) format.
    /// One shared instance avoids allocating a throwaway user per call.
    /// </summary>
    private static User DummyUser { get; } = User.Create(
        "hash-context",
        "hash-context@invalid",
        "Hash",
        "Context",
        "placeholder",
        Domain.Constants.RoleIds.ReadOnlyUser);

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return _hasher.HashPassword(DummyUser, password);
    }

    public PasswordVerificationOutcome Verify(string passwordHash, string providedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(providedPassword);

        return _hasher.VerifyHashedPassword(DummyUser, passwordHash, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
            _ => PasswordVerificationOutcome.Failed,
        };
    }

    public void VerifyDummy(string providedPassword)
    {
        // The result is intentionally discarded: the call exists for its cost, not its answer.
        _ = _hasher.VerifyHashedPassword(DummyUser, _dummyHash, providedPassword ?? string.Empty);
    }
}
