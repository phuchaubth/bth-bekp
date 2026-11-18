using Consent.Domain.Exports;

namespace Consent.Application.Abstractions;

/// <summary>Kiểu dữ liệu của một cột trong file xuất (để Excel định dạng ngày, số đúng kiểu).</summary>
public enum TabularCellKind
{
    Text,
    Number,
    Date,
    DateTime,
}

/// <summary>Cột của bảng xuất: tiêu đề tiếng Việt, kiểu ô và độ rộng gợi ý (số ký tự).</summary>
public sealed record TabularColumn(string Header, TabularCellKind Kind = TabularCellKind.Text, double Width = 16);

/// <summary>
/// Bảng cần ghi ra file. Giá trị mỗi ô: <c>string</c>, số nguyên, <c>DateOnly</c>, <c>DateTime</c> (giờ Việt Nam) hoặc
/// <c>null</c> (ô trống). Các dòng được đọc lần lượt (không cần nạp hết vào bộ nhớ).
/// <see cref="Properties"/> là thông tin về lần xuất (người xuất, bộ lọc…), ghi ở trang riêng nếu định dạng hỗ trợ.
/// </summary>
public sealed record TabularData(
    string SheetName,
    IReadOnlyList<TabularColumn> Columns,
    IAsyncEnumerable<IReadOnlyList<object?>> Rows,
    IReadOnlyList<KeyValuePair<string, string>> Properties);

/// <summary>Ghi bảng dữ liệu ra một định dạng file (Excel, CSV).</summary>
public interface ITabularFileWriter
{
    ExportFormat Format { get; }

    string ContentType { get; }

    /// <summary>Phần mở rộng của tên file, không có dấu chấm (ví dụ <c>xlsx</c>).</summary>
    string FileExtension { get; }

    /// <summary>Ghi toàn bộ bảng vào <paramref name="output"/>; trả về số dòng dữ liệu (không tính dòng tiêu đề).</summary>
    Task<int> WriteAsync(TabularData data, Stream output, CancellationToken cancellationToken);
}
