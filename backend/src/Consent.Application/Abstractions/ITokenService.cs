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

/// <summary>
/// Thông tin đưa vào token phiên ký: chỉ dùng để người bệnh ký đúng một phiếu trên điện thoại, không dùng được cho API
/// khác (đối tượng nhận token riêng).
/// </summary>
public sealed record SigningTokenClaims(Guid StaffUserId, string StaffUserName, Guid FormId, Guid SessionId);

public interface ITokenService
{
    AccessToken CreateAccessToken(AccessTokenClaims claims);

    /// <summary>Tạo token phiên ký (thời hạn ngắn) cho chế độ ký trên điện thoại.</summary>
    AccessToken CreateSigningToken(SigningTokenClaims claims);

    /// <summary>Sinh refresh token ngẫu nhiên (dạng chuỗi gửi cho client).</summary>
    string GenerateRefreshToken();

    /// <summary>Băm refresh token (SHA-256, hex) để lưu và tra cứu.</summary>
    string HashRefreshToken(string refreshToken);
}
