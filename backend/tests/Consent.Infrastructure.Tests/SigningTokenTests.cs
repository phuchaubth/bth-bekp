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
        var token = service.CreateSigningToken(new SigningTokenClaims(Guid.NewGuid(), "tiepdon.demo", [formId], sessionId));
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
    public async Task BundleSigningToken_CarriesEveryFormOfTheBundle()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock());
        Guid[] formIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var token = service.CreateSigningToken(new SigningTokenClaims(Guid.NewGuid(), "tiepdon.demo", formIds, Guid.NewGuid()));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, ValidationFor(Options.SigningAudience));

        Assert.True(result.IsValid);
        Assert.Equal(
            formIds.Select(id => id.ToString()),
            result.ClaimsIdentity.FindAll(AppClaimTypes.SigningForm).Select(c => c.Value));
    }

    [Fact]
    public void SigningToken_WithoutForms_IsRefused()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock());

        Assert.Throws<ArgumentException>(() =>
            service.CreateSigningToken(new SigningTokenClaims(Guid.NewGuid(), "tiepdon.demo", [], Guid.NewGuid())));
    }

    // Quyết định 41: khoa chính ở claim "dept", mỗi khoa khác một claim "dept_other".
    [Fact]
    public async Task AccessToken_CarriesPrimaryDepartment_AndEachOtherDepartmentAsItsOwnClaim()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock());
        Guid primary = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();

        var several = await new JsonWebTokenHandler().ValidateTokenAsync(
            service.CreateAccessToken(new AccessTokenClaims(
                Guid.NewGuid(), "dieuduong.demo", "Điều dưỡng Demo", primary, ["NURSE"], ["consents.read"], false, [second, third])).Value,
            ValidationFor(Options.Audience));
        var one = await new JsonWebTokenHandler().ValidateTokenAsync(
            service.CreateAccessToken(new AccessTokenClaims(
                Guid.NewGuid(), "dieuduong.demo", "Điều dưỡng Demo", primary, ["NURSE"], ["consents.read"], false, [second])).Value,
            ValidationFor(Options.Audience));
        var none = await new JsonWebTokenHandler().ValidateTokenAsync(
            service.CreateAccessToken(new AccessTokenClaims(
                Guid.NewGuid(), "dieuduong.demo", "Điều dưỡng Demo", primary, ["NURSE"], ["consents.read"], false)).Value,
            ValidationFor(Options.Audience));

        Assert.True(several.IsValid);
        Assert.Equal(primary.ToString(), several.ClaimsIdentity.FindFirst(AppClaimTypes.Department)?.Value);
        Assert.Equal(
            [second.ToString(), third.ToString()],
            several.ClaimsIdentity.FindAll(AppClaimTypes.AdditionalDepartment).Select(c => c.Value));
        Assert.Equal([second.ToString()], one.ClaimsIdentity.FindAll(AppClaimTypes.AdditionalDepartment).Select(c => c.Value));
        Assert.Empty(none.ClaimsIdentity.FindAll(AppClaimTypes.AdditionalDepartment));
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
