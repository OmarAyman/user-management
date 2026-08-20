namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Password hashing and verification. The implementation wraps ASP.NET Core's <c>PasswordHasher&lt;T&gt;</c>
/// (PBKDF2-HMAC-SHA512); no hashing algorithm is written by hand anywhere in this solution.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationOutcome Verify(string passwordHash, string providedPassword);

    /// <summary>
    /// Verifies against a fixed dummy hash and always fails. Called when the username does not exist, so the
    /// response time of "no such user" matches "wrong password" and the endpoint does not become a user
    /// enumeration oracle.
    /// </summary>
    void VerifyDummy(string providedPassword);
}

/// <summary>
/// The three outcomes of verification. <see cref="SuccessRehashNeeded"/> exists so a hash produced with older
/// parameters can be upgraded transparently on the next successful sign-in.
/// </summary>
public enum PasswordVerificationOutcome
{
    Failed = 0,

    Success = 1,

    SuccessRehashNeeded = 2,
}
