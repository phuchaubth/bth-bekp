namespace Consent.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record RoleRefDto(Guid Id, string Code, string Name);

/// <summary>Khoa/phòng gắn với tài khoản (tên để hiển thị).</summary>
public sealed record DepartmentRefDto(Guid Id, string Name);

/// <summary>Thông tin người dùng đang đăng nhập, dùng để hiển thị và phân quyền giao diện.</summary>
/// <param name="DepartmentId">Khoa/phòng chính: khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng.</param>
/// <param name="Departments">
/// Mọi khoa/phòng của tài khoản (quyết định 41), khoa chính đứng đầu: tài khoản thấy dữ liệu của các khoa này.
/// </param>
public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Title,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<DepartmentRefDto> Departments,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword,
    bool HasSignatureSpecimen = false);

/// <summary>Chữ ký mẫu của chính mình: ảnh PNG Base64 vẽ trong khung ký.</summary>
public sealed record SignatureSpecimenRequest(string? Image);

/// <summary>Kết quả đăng nhập/làm mới: access token, refresh token (đặt vào cookie) và thông tin người dùng.</summary>
public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    CurrentUserDto User);
