namespace Consent.Application.Abstractions;

/// <summary>
/// Lưu trữ tài liệu (PDF phiếu đã ký) trên ổ đĩa/thư mục mạng của máy chủ. Nội dung được mã hóa khi lưu; đọc lại sẽ
/// báo lỗi nếu file bị sửa hoặc bị tráo sang khóa lưu trữ khác.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Lưu nội dung, trả về khóa lưu trữ (đường dẫn tương đối do hệ thống đặt).</summary>
    Task<string> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken);

    /// <summary>Đọc và giải mã. Ném <see cref="DocumentUnavailableException"/> khi file mất, hỏng hoặc bị sửa.</summary>
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Không đọc được tài liệu đã lưu (mất file, file hỏng hoặc bị sửa).</summary>
public sealed class DocumentUnavailableException : Exception
{
    public DocumentUnavailableException()
    {
    }

    public DocumentUnavailableException(string message)
        : base(message)
    {
    }

    public DocumentUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
