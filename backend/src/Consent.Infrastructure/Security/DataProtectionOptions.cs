namespace Consent.Infrastructure.Security;

/// <summary>
/// Khóa bảo vệ dữ liệu nhạy cảm (mục <c>DataProtection</c>). Cả hai khóa là bí mật, dạng Base64 của 32 byte ngẫu nhiên,
/// chỉ cấu hình qua biến môi trường. Mất khóa mã hóa = mất dữ liệu đã mã hóa: phải sao lưu khóa an toàn.
/// </summary>
public sealed class DataProtectionOptions
{
    public const string SectionName = "DataProtection";
    public const int KeyBytes = 32;

    /// <summary>Khóa AES-256-GCM mã hóa cột (số CCCD, số điện thoại, mã BHYT…).</summary>
    public string FieldEncryptionKey { get; set; } = string.Empty;

    /// <summary>Khóa HMAC-SHA256 cho chỉ mục mù (tra cứu/phát hiện trùng số CCCD). Phải khác khóa mã hóa.</summary>
    public string BlindIndexKey { get; set; } = string.Empty;

    public static bool IsValidKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var buffer = new byte[KeyBytes + 1];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == KeyBytes;
    }
}
