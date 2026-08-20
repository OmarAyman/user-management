using UserManagement.Application.Common.Abstractions;
using UserManagement.Infrastructure.Security;

namespace UserManagement.UnitTests.Security;

public sealed class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new AspNetPasswordHasher();

    [Fact]
    public void A_hash_never_contains_the_password()
    {
        const string password = "Admin@123456";

        var hash = _hasher.Hash(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
        Assert.NotEqual(password, hash);
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        // Per-password salt: two users with the same password must not be visibly identical in the database.
        var first = _hasher.Hash("Admin@123456");
        var second = _hasher.Hash("Admin@123456");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_hash_is_the_versioned_aspnet_v3_format()
    {
        var hash = _hasher.Hash("Admin@123456");

        // 0x01 marks the versioned format; the v3 payload is PBKDF2-HMAC-SHA512 with an embedded iteration
        // count, which is what makes the parameters upgradeable later without breaking existing hashes.
        var bytes = Convert.FromBase64String(hash);
        Assert.Equal(0x01, bytes[0]);
    }

    [Fact]
    public void Verification_succeeds_for_the_correct_password()
    {
        var hash = _hasher.Hash("Admin@123456");

        Assert.Equal(PasswordVerificationOutcome.Success, _hasher.Verify(hash, "Admin@123456"));
    }

    [Theory]
    [InlineData("admin@123456")]
    [InlineData("Admin@12345")]
    [InlineData("Admin@1234567")]
    [InlineData(" Admin@123456")]
    public void Verification_fails_for_anything_else(string attempt)
    {
        var hash = _hasher.Hash("Admin@123456");

        Assert.Equal(PasswordVerificationOutcome.Failed, _hasher.Verify(hash, attempt));
    }

    [Fact]
    public void VerifyDummy_does_not_throw_for_any_input()
    {
        // Called on the unknown-username path so its cost matches a real verification. It must never surface an
        // error, or the timing defence would itself become an oracle.
        _hasher.VerifyDummy("anything");
        _hasher.VerifyDummy(string.Empty);
    }
}
