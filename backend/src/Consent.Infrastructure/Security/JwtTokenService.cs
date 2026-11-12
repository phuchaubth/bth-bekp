using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Consent.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Consent.Infrastructure.Security;

internal sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(AccessTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var payload = new Dictionary<string, object>
        {
            [AppClaimTypes.Subject] = claims.UserId.ToString(),
            [AppClaimTypes.UserName] = claims.UserName,
            [AppClaimTypes.FullName] = claims.FullName,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };
        if (claims.DepartmentId is { } departmentId)
        {
            payload[AppClaimTypes.Department] = departmentId.ToString();
        }

        if (claims.Roles.Count > 0)
        {
            payload[AppClaimTypes.Role] = claims.Roles.ToArray();
        }

        if (claims.Permissions.Count > 0)
        {
            payload[AppClaimTypes.Permission] = claims.Permissions.ToArray();
        }

        if (claims.MustChangePassword)
        {
            payload[AppClaimTypes.MustChangePassword] = "true";
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = payload,
            SigningCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    public AccessToken CreateSigningToken(SigningTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.SigningTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.SigningAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [AppClaimTypes.Subject] = claims.StaffUserId.ToString(),
                [AppClaimTypes.UserName] = claims.StaffUserName,
                [AppClaimTypes.SigningForm] = claims.FormId.ToString(),
                [AppClaimTypes.SigningSession] = claims.SessionId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    public string GenerateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
