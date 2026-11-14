using System.Security.Claims;
using Consent.Application.Abstractions;
using Consent.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Consent.Infrastructure.Tests;

public sealed class SigningTokenTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "khoa-ky-thu-nghiem-chi-dung-trong-test-0123456789",
        SigningTokenMinutes = 30,
    };

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private static TokenValidationParameters ValidationFor(string audience) => new()
    {
        ValidIssuer = Options.Issuer,
        ValidAudience = audience,
        IssuerSigningKey = Options.CreateSigningKey(),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };

    [Fact]
    public async Task SigningToken_IsOnlyValidForTheSigningAudience_AndCarriesFormAndSession()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock());
        var formId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var token = service.CreateSigningToken(new SigningTokenClaims(Guid.NewGuid(), "tiepdon.demo", formId, sessionId));
        var handler = new JsonWebTokenHandler();

        var asSigning = await handler.ValidateTokenAsync(token.Value, ValidationFor(Options.SigningAudience));
        var asLogin = await handler.ValidateTokenAsync(token.Value, ValidationFor(Options.Audience));

        Assert.True(asSigning.IsValid);
        Assert.False(asLogin.IsValid);
        var claims = asSigning.ClaimsIdentity;
        Assert.Equal(formId.ToString(), claims.FindFirst(AppClaimTypes.SigningForm)?.Value);
        Assert.Equal(sessionId.ToString(), claims.FindFirst(AppClaimTypes.SigningSession)?.Value);
        Assert.Null(claims.FindFirst(AppClaimTypes.Permission));
        Assert.Null(claims.FindFirst(ClaimTypes.Role));
        Assert.InRange(token.ExpiresAtUtc - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
    }

    [Fact]
    public async Task LoginToken_IsNotAcceptedAsASigningToken()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock());
        var token = service.CreateAccessToken(new AccessTokenClaims(
            Guid.NewGuid(), "tiepdon.demo", "Tiếp đón Demo", null, ["RECEPTIONIST"], ["consents.read"], false));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, ValidationFor(Options.SigningAudience));

        Assert.False(result.IsValid);
    }
}
