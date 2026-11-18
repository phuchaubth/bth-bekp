using System.Globalization;
using Consent.Shared;

namespace Consent.Application.Auth;

public static class AuthErrors
{
    public static Error InvalidCredentials { get; } =
        Error.Unauthorized("auth.invalid_credentials", "Tên đăng nhập hoặc mật khẩu không đúng.");

    public static Error Inactive { get; } =
        Error.Unauthorized("auth.inactive", "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");

    public static Error RefreshInvalid { get; } =
        Error.Unauthorized("auth.refresh_invalid", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");

    /// <summary>Refresh token vừa được thay thế bởi một yêu cầu khác (ví dụ tab khác). Client có thể thử lại.</summary>
    public static Error RefreshSuperseded { get; } =
        Error.Unauthorized("auth.refresh_superseded", "Phiên đăng nhập vừa được làm mới ở nơi khác. Vui lòng thử lại.");

    public static Error SessionInvalid { get; } =
        Error.Unauthorized("auth.session_invalid", "Phiên đăng nhập không còn hợp lệ. Vui lòng đăng nhập lại.");

    public static Error WrongCurrentPassword { get; } =
        Error.ValidationField("currentPassword", "Mật khẩu hiện tại không đúng.");

    public static Error LockedOut(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return Error.Unauthorized(
            "auth.locked_out",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {minutes} phút."));
    }
}
