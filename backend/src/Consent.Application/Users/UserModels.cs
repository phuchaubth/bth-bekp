using Consent.Application.Auth;

namespace Consent.Application.Users;

public sealed record UserQuery(string? Keyword = null, Guid? DepartmentId = null, bool? IsActive = null, int Page = 1, int PageSize = 20);

/// <param name="DepartmentId">Khoa/phòng chính.</param>
/// <param name="IsKioskLookupLocked">
/// Tài khoản kiosk đang tạm khóa tra cứu (người bệnh nhập sai ngày sinh nhiều lần, quyết định 24); quản trị mở khóa sớm bằng
/// thao tác Mở khóa.
/// </param>
/// <param name="AdditionalDepartmentNames">Tên các khoa/phòng khác của tài khoản (quyết định 41).</param>
public sealed record UserListItemDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Title,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> RoleNames,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset? LastLoginAtUtc,
    bool IsKioskLookupLocked = false,
    IReadOnlyList<string>? AdditionalDepartmentNames = null);

public sealed record UserDetailDto(
    Guid Id,
    string UserName,
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? Title,
    Guid? DepartmentId,
    string? DepartmentName,
    string? SmartCaUserId,
    IReadOnlyList<RoleRefDto> Roles,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset? LockoutEndUtc,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<DepartmentRefDto> AdditionalDepartments);

/// <summary>Thông tin hồ sơ dùng chung cho tạo và cập nhật người dùng.</summary>
public interface IUserProfileRequest
{
    string FullName { get; }

    string? Email { get; }

    string? PhoneNumber { get; }

    string? Title { get; }

    /// <summary>Khoa/phòng chính.</summary>
    Guid? DepartmentId { get; }

    string? SmartCaUserId { get; }

    IReadOnlyList<Guid> RoleIds { get; }

    /// <summary>Khoa/phòng khác (quyết định 41); bỏ trống là không có. Cập nhật thay toàn bộ danh sách.</summary>
    IReadOnlyList<Guid>? AdditionalDepartmentIds { get; }
}

/// <summary>Tạo người dùng. Mật khẩu ban đầu do quản trị viên đặt; người dùng phải đổi ở lần đăng nhập đầu.</summary>
public sealed record CreateUserRequest(
    string UserName,
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? Title,
    Guid? DepartmentId,
    string? SmartCaUserId,
    IReadOnlyList<Guid> RoleIds,
    string Password,
    IReadOnlyList<Guid>? AdditionalDepartmentIds = null) : IUserProfileRequest;

public sealed record UpdateUserRequest(
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? Title,
    Guid? DepartmentId,
    string? SmartCaUserId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<Guid>? AdditionalDepartmentIds = null) : IUserProfileRequest;

public sealed record ResetPasswordRequest(string NewPassword);
