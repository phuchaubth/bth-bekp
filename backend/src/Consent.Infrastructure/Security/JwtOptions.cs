using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Consent.Infrastructure.Security;

/// <summary>Cấu hình JWT (mục <c>Jwt</c>). <see cref="SigningKey"/> là bí mật, chỉ cấu hình qua biến môi trường.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Khóa ký HMAC-SHA256 phải dài ít nhất 32 byte (UTF-8).</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = "consent-management";

    public string Audience { get; set; } = "consent-management-web";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Thời hạn token phiên ký (chế độ ký trên điện thoại).</summary>
    public int SigningTokenMinutes { get; set; } = 30;

    /// <summary>
    /// Đối tượng nhận riêng của token phiên ký: token này không được chấp nhận ở các API thường (và ngược lại), nên
    /// người bệnh cầm máy không dùng được nó để làm việc khác ngoài ký đúng phiếu.
    /// </summary>
    public string SigningAudience => $"{Audience}:signing";

    public bool HasValidSigningKey => Encoding.UTF8.GetByteCount(SigningKey ?? string.Empty) >= MinimumSigningKeyBytes;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}
