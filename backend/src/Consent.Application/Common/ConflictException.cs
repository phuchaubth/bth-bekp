namespace Consent.Application.Common;

/// <summary>Xung đột khi lưu dữ liệu: trùng giá trị duy nhất hoặc bị cập nhật đồng thời.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException()
        : base("Dữ liệu đã bị thay đổi hoặc bị trùng. Vui lòng tải lại và thử lại.")
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
