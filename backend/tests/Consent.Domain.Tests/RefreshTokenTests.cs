using Consent.Domain.Common;
using Consent.Domain.Identity;

namespace Consent.Domain.Tests;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', RefreshToken.TokenHashLength);

    [Fact]
    public void Issue_CreatesActiveTokenWithExpiry()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Hash, Guid.NewGuid(), Now, TimeSpan.FromHours(12), "10.0.0.1");

        Assert.True(token.IsActive(Now));
        Assert.Equal(Now.AddHours(12), token.ExpiresAtUtc);
        Assert.False(token.IsActive(Now.AddHours(12)));
    }

    [Fact]
    public void Revoke_WithReplacement_MarksTokenAsRotated()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Hash, Guid.NewGuid(), Now, TimeSpan.FromHours(12), null);
        var replacement = Guid.NewGuid();

        token.Revoke(Now, RefreshTokenRevocationReasons.Rotated, replacement);

        Assert.True(token.IsRevoked);
        Assert.True(token.WasRotated);
        Assert.False(token.IsActive(Now));
        Assert.Equal(replacement, token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_IsIdempotent_KeepsFirstReason()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), Hash, Guid.NewGuid(), Now, TimeSpan.FromHours(12), null);

        token.Revoke(Now, RefreshTokenRevocationReasons.Logout);
        token.Revoke(Now.AddMinutes(1), RefreshTokenRevocationReasons.ReuseDetected);

        Assert.Equal(RefreshTokenRevocationReasons.Logout, token.RevokedReason);
        Assert.Equal(Now, token.RevokedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Issue_WithInvalidHash_Throws(string hash)
    {
        Assert.Throws<DomainException>(() =>
            RefreshToken.Issue(Guid.NewGuid(), hash, Guid.NewGuid(), Now, TimeSpan.FromHours(1), null));
    }
}
