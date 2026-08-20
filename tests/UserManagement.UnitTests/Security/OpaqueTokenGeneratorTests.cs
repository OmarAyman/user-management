using System.Globalization;
using UserManagement.Domain.Constants;
using UserManagement.Infrastructure.Security;

namespace UserManagement.UnitTests.Security;

public sealed class OpaqueTokenGeneratorTests
{
    [Fact]
    public void Every_token_is_distinct()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => OpaqueTokenGenerator.CreateToken()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(500, tokens.Count);
    }

    [Fact]
    public void A_token_is_url_safe()
    {
        var token = OpaqueTokenGenerator.CreateToken();

        // It travels in a cookie, so the characters that would need escaping must not appear at all.
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void A_token_carries_256_bits_of_entropy()
    {
        var token = OpaqueTokenGenerator.CreateToken();

        // 32 bytes base64url-encoded, unpadded.
        Assert.Equal(43, token.Length);
    }

    [Fact]
    public void The_hash_is_64_lowercase_hex_characters()
    {
        var hash = OpaqueTokenGenerator.Hash(OpaqueTokenGenerator.CreateToken());

        Assert.Equal(UserConstraints.TokenHashLength, hash.Length);
        Assert.Equal(hash.ToLower(CultureInfo.InvariantCulture), hash);
        Assert.True(hash.All(Uri.IsHexDigit));
    }

    [Fact]
    public void Hashing_is_deterministic_so_a_presented_token_can_be_found()
    {
        var token = OpaqueTokenGenerator.CreateToken();

        Assert.Equal(OpaqueTokenGenerator.Hash(token), OpaqueTokenGenerator.Hash(token));
    }

    [Fact]
    public void The_hash_does_not_reveal_the_token()
    {
        var token = OpaqueTokenGenerator.CreateToken();

        var hash = OpaqueTokenGenerator.Hash(token);

        Assert.DoesNotContain(token, hash, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hashing_rejects_an_empty_token(string token)
    {
        Assert.Throws<ArgumentException>(() => OpaqueTokenGenerator.Hash(token));
    }
}
