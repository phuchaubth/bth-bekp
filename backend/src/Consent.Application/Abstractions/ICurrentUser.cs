namespace Consent.Application.Abstractions;

/// <summary>Người dùng đang thực hiện yêu cầu (đọc từ access token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? UserName { get; }

    Guid? DepartmentId { get; }

    bool HasPermission(string permission);
}
