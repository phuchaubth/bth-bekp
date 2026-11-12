namespace Consent.Application.Abstractions;

/// <summary>Người dùng đang thực hiện yêu cầu (đọc từ access token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? UserName { get; }

    Guid? DepartmentId { get; }

    bool HasPermission(string permission);

    /// <summary>Người dùng có vai trò (theo mã vai trò, ví dụ <c>NURSE</c>).</summary>
    bool IsInRole(string roleCode);

    /// <summary>Phiếu được phép ký khi yêu cầu dùng token phiên ký (chế độ ký trên máy tính bảng); null với token thường.</summary>
    Guid? SigningFormId { get; }

    /// <summary>Mã phiên ký trong token phiên ký.</summary>
    Guid? SigningSessionId { get; }
}
