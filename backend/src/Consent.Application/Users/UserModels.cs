using Consent.Application.Auth;

namespace Consent.Application.Users;

public sealed record UserQuery(string? Keyword = null, Guid? DepartmentId = null, bool? IsActive = null, int Page = 1, int PageSize = 20);

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
    DateTimeOffset? LastLoginAtUtc);

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
    DateTimeOffset CreatedAtUtc);

/// <summary>Thông tin hồ sơ dùng chung cho tạo và cập nhật người dùng.</summary>
public interface IUserProfileRequest
{
    string FullName { get; }

    string? Email { get; }

    string? PhoneNumber { get; }

    string? Title { get; }

    Guid? DepartmentId { get; }

    string? SmartCaUserId { get; }

    IReadOnlyList<Guid> RoleIds { get; }
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
    string Password) : IUserProfileRequest;

public sealed record UpdateUserRequest(
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? Title,
    Guid? DepartmentId,
    string? SmartCaUserId,
    IReadOnlyList<Guid> RoleIds) : IUserProfileRequest;

public sealed record ResetPasswordRequest(string NewPassword);
