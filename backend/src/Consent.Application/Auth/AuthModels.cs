namespace Consent.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record RoleRefDto(Guid Id, string Code, string Name);

/// <summary>Thông tin người dùng đang đăng nhập, dùng để hiển thị và phân quyền giao diện.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Title,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

/// <summary>Kết quả đăng nhập/làm mới: access token, refresh token (đặt vào cookie) và thông tin người dùng.</summary>
public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    CurrentUserDto User);
