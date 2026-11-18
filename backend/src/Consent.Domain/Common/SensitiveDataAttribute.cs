namespace Consent.Domain.Common;

/// <summary>
/// Đánh dấu dữ liệu nhạy cảm (số CCCD, số điện thoại, mã BHYT, mật khẩu đã băm…): luôn được che khi ghi nhật ký audit.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveDataAttribute : Attribute
{
}
