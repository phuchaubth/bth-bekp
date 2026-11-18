using Consent.Domain.Identity;

namespace Consent.Application.Auth;

/// <summary>Cấu hình đăng nhập (mục <c>Auth</c> trong appsettings/biến môi trường).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Số lần đăng nhập sai liên tiếp trước khi khóa tạm thời.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Thời hạn refresh token (giờ). Mỗi lần làm mới sẽ cấp token mới với thời hạn mới.</summary>
    public int RefreshTokenLifetimeHours { get; set; } = 12;

    /// <summary>
    /// Khoảng thời gian (giây) mà việc dùng lại refresh token vừa bị thay thế được coi là xung đột đồng thời
    /// (ví dụ hai tab cùng làm mới) thay vì token bị đánh cắp.
    /// </summary>
    public int RefreshReuseGraceSeconds { get; set; } = 30;

    public LockoutPolicy ToLockoutPolicy() => new(MaxFailedAttempts, TimeSpan.FromMinutes(LockoutMinutes));
}
