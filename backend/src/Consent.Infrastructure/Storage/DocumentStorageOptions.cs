namespace Consent.Infrastructure.Storage;

/// <summary>Nơi lưu PDF phiếu đã ký (mục <c>Storage</c>): thư mục trên ổ đĩa hoặc thư mục mạng của máy chủ.</summary>
public sealed class DocumentStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Thư mục gốc; đường dẫn tương đối được tính từ thư mục làm việc của tiến trình.</summary>
    public string DocumentsPath { get; set; } = string.Empty;
}
