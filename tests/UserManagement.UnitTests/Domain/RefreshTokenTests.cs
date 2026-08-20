using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;

namespace UserManagement.UnitTests.Domain;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid FamilyId = Guid.CreateVersion7();

    [Fact]
    public void An_issued_token_is_active_and_unrotated()
    {
        var token = Issue();

        Assert.True(token.IsActive(Now));
        Assert.False(token.IsRotated);
        Assert.False(token.IsRevoked);
        Assert.Null(token.RevocationReason);
    }

    [Fact]
    public void Issue_rejects_an_expiry_that_is_not_in_the_future()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RefreshToken.Issue(UserId, FamilyId, new string('a', 64), Now, Now, "127.0.0.1"));
    }

    [Fact]
    public void A_token_is_inactive_once_it_expires()
    {
        var token = Issue();

        Assert.True(token.IsActive(Now.AddDays(6)));
        Assert.False(token.IsActive(Now.AddDays(7)));
        Assert.True(token.IsExpired(Now.AddDays(8)));
    }

    [Fact]
    public void MarkRotated_records_the_successor_and_the_reason()
    {
        var token = Issue();
        var successorId = Guid.CreateVersion7();

        token.MarkRotated(successorId, Now, "10.0.0.1");

        // The successor pointer is what makes reuse detectable: a token presented after it has one means two
        // clients hold tokens from the same lineage.
        Assert.True(token.IsRotated);
        Assert.Equal(successorId, token.ReplacedByTokenId);
        Assert.Equal(RevocationReason.Rotated, token.RevocationReason);
        Assert.Equal("10.0.0.1", token.RevokedByIp);
        Assert.False(token.IsActive(Now));
    }

    [Fact]
    public void Revoking_twice_preserves_the_first_reason()
    {
        var token = Issue();

        token.Revoke(RevocationReason.Logout, Now, "10.0.0.1");
        token.Revoke(RevocationReason.ReuseDetected, Now.AddMinutes(5), "10.0.0.2");

        // The first cause of revocation is the one worth keeping for an investigation.
        Assert.Equal(RevocationReason.Logout, token.RevocationReason);
        Assert.Equal(Now, token.RevokedAt);
        Assert.Equal("10.0.0.1", token.RevokedByIp);
    }

    [Fact]
    public void Every_token_in_a_lineage_shares_its_family()
    {
        var first = Issue();
        var second = RefreshToken.Issue(UserId, first.FamilyId, new string('b', 64), Now, Now.AddDays(7), "127.0.0.1");

        Assert.Equal(first.FamilyId, second.FamilyId);
    }

    private static RefreshToken Issue() =>
        RefreshToken.Issue(UserId, FamilyId, new string('a', 64), Now, Now.AddDays(7), "127.0.0.1");
}
