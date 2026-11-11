namespace Consent.Application.Abstractions;

/// <summary>Thông tin đưa vào access token.</summary>
public sealed record AccessTokenClaims(
    Guid UserId,
    string UserName,
    string FullName,
    Guid? DepartmentId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    bool MustChangePassword);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(AccessTokenClaims claims);

    /// <summary>Sinh refresh token ngẫu nhiên (dạng chuỗi gửi cho client).</summary>
    string GenerateRefreshToken();

    /// <summary>Băm refresh token (SHA-256, hex) để lưu và tra cứu.</summary>
    string HashRefreshToken(string refreshToken);
}
