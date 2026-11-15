namespace Consent.Domain.Identity;

/// <summary>
/// Giới hạn tra cứu người bệnh trên kiosk (quyết định 24): nhập mã y tế/số CCCD mà ngày sinh không khớp (hoặc không có
/// hồ sơ) 5 lần liên tiếp thì tạm khóa tra cứu 5 phút, chống dò thông tin người khác.
/// </summary>
public static class KioskLookupPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public static LockoutPolicy Default { get; } = new(MaxFailedAttempts, LockoutDuration);
}
