using Consent.Domain.Common;

namespace Consent.Domain.Exports;

/// <summary>Định dạng file xuất.</summary>
public enum ExportFormat
{
    /// <summary>Excel (Office Open XML).</summary>
    Xlsx,

    /// <summary>CSV (UTF-8, phân cách bằng dấu phẩy).</summary>
    Csv,
}

/// <summary>
/// Một lần xuất dữ liệu (lịch sử xuất, màn hình 13). File đã xuất không được lưu lại trên máy chủ; chỉ lưu người xuất,
/// bộ lọc, các cột, số dòng và mã băm SHA-256 của file để đối chiếu khi cần.
/// </summary>
public sealed class ExportJob : Entity
{
    public const string ConsentFormsKind = "ConsentForms";
    public const int KindMaxLength = 50;
    public const int FileNameMaxLength = 200;
    public const int FiltersMaxLength = 2000;
    public const int ColumnsMaxLength = 2000;
    public const int Sha256Length = 64;

    private ExportJob(Guid id, string kind, Guid requestedByUserId, DateTimeOffset requestedAtUtc, ExportFormat format)
        : base(id)
    {
        Kind = kind;
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = requestedAtUtc;
        Format = format;
        FiltersJson = "{}";
        Columns = string.Empty;
        FileName = string.Empty;
        FileSha256 = string.Empty;
    }

    /// <summary>Loại dữ liệu xuất, hiện chỉ có <see cref="ConsentFormsKind"/>.</summary>
    public string Kind { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public ExportFormat Format { get; private set; }

    /// <summary>Bộ lọc đã chuẩn hóa (JSON), để xem lại hoặc xuất lại với cùng điều kiện.</summary>
    public string FiltersJson { get; private set; }

    /// <summary>Mã các cột đã xuất theo thứ tự, cách nhau bằng dấu phẩy.</summary>
    public string Columns { get; private set; }

    /// <summary>Có kèm nội dung phiếu theo mẫu (các trường của mẫu phiếu).</summary>
    public bool IncludesContent { get; private set; }

    /// <summary>Có xuất dữ liệu nhạy cảm đầy đủ (số CCCD, số điện thoại, số thẻ BHYT không bị che).</summary>
    public bool IncludesSensitiveData { get; private set; }

    public int RowCount { get; private set; }

    public string FileName { get; private set; }

    public long FileSizeBytes { get; private set; }

    /// <summary>SHA-256 (hex chữ thường) của file đã trả cho người xuất.</summary>
    public string FileSha256 { get; private set; }

    public int DurationMs { get; private set; }

    /// <param name="id">Mã lần xuất (ghi cả vào file xuất để đối chiếu).</param>
    public static ExportJob Create(
        Guid id,
        string kind,
        Guid requestedByUserId,
        DateTimeOffset requestedAtUtc,
        ExportFormat format,
        string filtersJson,
        IReadOnlyCollection<string> columns,
        bool includesContent,
        bool includesSensitiveData,
        int rowCount,
        string fileName,
        long fileSizeBytes,
        string fileSha256,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > KindMaxLength)
        {
            throw new DomainException("Loại dữ liệu xuất không hợp lệ.");
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new DomainException("Thiếu người xuất dữ liệu.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new DomainException("Định dạng file xuất không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(filtersJson) || filtersJson.Length > FiltersMaxLength)
        {
            throw new DomainException("Bộ lọc xuất dữ liệu không hợp lệ.");
        }

        var joinedColumns = string.Join(',', columns);
        if (columns.Count == 0 || joinedColumns.Length > ColumnsMaxLength)
        {
            throw new DomainException("Danh sách cột xuất không hợp lệ.");
        }

        if (rowCount < 0 || fileSizeBytes <= 0 || duration < TimeSpan.Zero)
        {
            throw new DomainException("Thông tin file xuất không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > FileNameMaxLength)
        {
            throw new DomainException("Tên file xuất không hợp lệ.");
        }

        if (fileSha256 is not { Length: Sha256Length } || !fileSha256.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            throw new DomainException("Mã băm SHA-256 của file xuất không hợp lệ.");
        }

        return new ExportJob(id, kind, requestedByUserId, requestedAtUtc, format)
        {
            FiltersJson = filtersJson,
            Columns = joinedColumns,
            IncludesContent = includesContent,
            IncludesSensitiveData = includesSensitiveData,
            RowCount = rowCount,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            FileSha256 = fileSha256,
            DurationMs = (int)Math.Min(int.MaxValue, Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero)),
        };
    }

    /// <summary>Các cột đã xuất (tách từ <see cref="Columns"/>).</summary>
    public IReadOnlyList<string> ColumnKeys => Columns.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
