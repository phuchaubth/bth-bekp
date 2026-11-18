namespace Consent.Application.Abstractions;

/// <summary>Người dùng đang thực hiện yêu cầu (đọc từ access token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? UserName { get; }

    /// <summary>Khoa/phòng chính: khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng.</summary>
    Guid? DepartmentId { get; }

    /// <summary>
    /// Mọi khoa/phòng của người dùng (khoa chính và các khoa khác, quyết định 41): người dùng thấy dữ liệu của các khoa này.
    /// Rỗng khi không có khoa chính.
    /// </summary>
    IReadOnlyList<Guid> DepartmentIds { get; }

    bool HasPermission(string permission);

    /// <summary>Người dùng có vai trò (theo mã vai trò, ví dụ <c>NURSE</c>).</summary>
    bool IsInRole(string roleCode);

    /// <summary>
    /// Các phiếu được phép ký khi yêu cầu dùng token phiên ký (chế độ ký trên điện thoại): một phiếu, hoặc các phiếu của
    /// một bộ phiếu; rỗng với token thường.
    /// </summary>
    IReadOnlyList<Guid> SigningFormIds { get; }

    /// <summary>Mã phiên ký trong token phiên ký.</summary>
    Guid? SigningSessionId { get; }
}
