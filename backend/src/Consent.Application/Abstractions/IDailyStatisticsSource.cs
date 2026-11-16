namespace Consent.Application.Abstractions;

/// <summary>
/// Một số liệu trong ngày lấy từ HIS (quyết định 31). <paramref name="DepartmentCode"/>: mã khoa với chỉ số tính theo khoa
/// (ví dụ người bệnh phòng khám chờ nhập viện), <c>null</c> với chỉ số toàn viện.
/// </summary>
public sealed record ExternalDailyStatistic(string Indicator, string? DepartmentCode, int Value);

public enum DailyStatisticsSourceStatus
{
    Found,

    /// <summary>Không lấy được (lỗi mạng, quá thời gian, CSDL bệnh viện lỗi, chưa cấu hình câu truy vấn ở API thông tin người bệnh).</summary>
    Unavailable,

    /// <summary>Chưa cấu hình API thông tin người bệnh (<c>PatientSource:Provider</c> khác <c>Api</c>).</summary>
    Disabled,
}

public sealed record DailyStatisticsResult(DailyStatisticsSourceStatus Status, IReadOnlyList<ExternalDailyStatistic> Items)
{
    public static DailyStatisticsResult Unavailable { get; } = new(DailyStatisticsSourceStatus.Unavailable, []);

    public static DailyStatisticsResult Disabled { get; } = new(DailyStatisticsSourceStatus.Disabled, []);
}

/// <summary>
/// Số liệu trong ngày của bảng tổng hợp điều dưỡng mà khoa không báo cáo (người bệnh đến khám, chẩn đoán hình ảnh, chờ nhập
/// viện), lấy qua API thông tin người bệnh; câu truy vấn do phòng CNTT bệnh viện viết.
/// </summary>
public interface IDailyStatisticsSource
{
    /// <summary>Đã cấu hình API thông tin người bệnh.</summary>
    bool IsEnabled { get; }

    Task<DailyStatisticsResult> GetAsync(DateOnly date, CancellationToken cancellationToken);
}
