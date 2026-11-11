namespace Consent.Domain.Common;

/// <summary>
/// Đánh dấu thực thể mà mọi thao tác tạo/sửa/xóa phải được ghi nhật ký audit tự động
/// (trong cùng giao dịch với thay đổi dữ liệu).
/// </summary>
#pragma warning disable CA1040 // Interface đánh dấu (marker) là có chủ đích.
public interface IAuditableEntity
{
}
#pragma warning restore CA1040
