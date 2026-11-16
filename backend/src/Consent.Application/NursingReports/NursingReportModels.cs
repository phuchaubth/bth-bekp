namespace Consent.Application.NursingReports;

/// <summary>Trạng thái báo cáo khoa trên màn hình: <see cref="New"/> là chưa lưu lần nào (đang hiện mẫu trống/điền sẵn).</summary>
public enum NursingReportState
{
    New,
    Draft,
    Submitted,
}

/// <summary>
/// Báo cáo điều dưỡng của một khoa trong một ngày. <paramref name="CarriedOverKeys"/>: các dòng điền sẵn theo báo cáo đã gửi
/// gần nhất (chỉ khi báo cáo còn là <see cref="NursingReportState.New"/>). <paramref name="IsReportingDepartment"/>: khoa có
/// trong danh sách khoa báo cáo do Phòng Điều dưỡng cài (chưa có thì chưa lập được).
/// </summary>
public sealed record NursingReportDto(
    Guid? Id,
    Guid DepartmentId,
    string DepartmentName,
    DateOnly ReportDate,
    NursingReportState Status,
    IReadOnlyDictionary<string, int> Values,
    IReadOnlyDictionary<string, string> Checks,
    string? Notes,
    bool CanEdit,
    bool CanReopen,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAtUtc,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? ReopenedBy,
    DateTimeOffset? ReopenedAtUtc,
    string? ReopenReason,
    IReadOnlyList<string> CarriedOverKeys,
    bool IsReportingDepartment);

/// <summary>
/// Lưu báo cáo của khoa trong ngày (tạo mới nếu chưa có). <paramref name="DepartmentId"/> bỏ trống là khoa của người dùng.
/// Ô để trống gửi <c>null</c> hoặc bỏ khỏi <paramref name="Values"/>. <paramref name="Submit"/>: lưu rồi gửi luôn (khóa lại).
/// </summary>
public sealed record SaveNursingReportRequest(
    Guid? DepartmentId,
    DateOnly ReportDate,
    IReadOnlyDictionary<string, int?>? Values,
    IReadOnlyDictionary<string, string?>? Checks,
    string? Notes,
    bool Submit);

public sealed record ReopenNursingReportRequest(string? Reason);

/// <summary>Một dòng (khoa) của bảng A. <paramref name="Cells"/>: theo khóa cột, <c>null</c> là ô trống.</summary>
public sealed record NursingSummaryRowDto(
    Guid DepartmentId,
    string DepartmentName,
    bool IsReporting,
    Guid? ReportId,
    NursingReportState Status,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    IReadOnlyDictionary<string, int?> Cells);

public sealed record NursingSummaryLineDto(string Key, string Label, int? Value);

/// <summary>Một dòng mục E: "Ổn", "Không ổn: khoa…" hoặc trống khi chưa khoa nào gửi.</summary>
public sealed record NursingSummaryCheckDto(string Key, string Label, string? Text, IReadOnlyList<string> IssueDepartments);

/// <summary>Một dòng mục F: phần báo cáo nhập thêm của khoa.</summary>
public sealed record NursingSummaryNoteDto(Guid DepartmentId, string DepartmentName, string Text);

/// <summary>Số liệu HIS của ngày: nguồn (HIS hay nhập tay), lúc lấy/sửa, người sửa; <paramref name="HisAvailable"/>: đã cấu hình HIS.</summary>
public sealed record NursingStatisticsInfoDto(
    bool HisAvailable,
    bool HasValues,
    Consent.Domain.NursingReports.NursingStatisticsSource? Source,
    DateTimeOffset? UpdatedAtUtc,
    string? UpdatedBy);

/// <summary>Bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" của một ngày (mục A–F như bản giấy).</summary>
public sealed record NursingSummaryDto(
    DateOnly ReportDate,
    IReadOnlyList<NursingSummaryColumn> Columns,
    IReadOnlyList<NursingSummaryRowDto> Rows,
    IReadOnlyDictionary<string, int?> Totals,
    IReadOnlyList<NursingSummaryLineDto> Outpatient,
    IReadOnlyList<NursingSummaryLineDto> Procedures,
    IReadOnlyList<NursingSummaryLineDto> Imaging,
    IReadOnlyList<NursingSummaryCheckDto> Situation,
    IReadOnlyList<NursingSummaryNoteDto> Incidents,
    NursingStatisticsInfoDto Statistics,
    int SubmittedCount,
    int ReportingCount);

/// <summary>Một khoa trên màn hình cài khoa báo cáo: có phải báo cáo không, số giường thực kê.</summary>
public sealed record NursingReportDepartmentDto(Guid DepartmentId, string Code, string Name, bool IsReporting, int? BedCount);

public sealed record NursingReportDepartmentItem(Guid DepartmentId, int BedCount);

/// <summary>Đặt danh sách khoa phải báo cáo (khoa không có trong danh sách thì bỏ ra) và số giường từng khoa.</summary>
public sealed record UpdateNursingReportDepartmentsRequest(IReadOnlyList<NursingReportDepartmentItem>? Items);

/// <summary>
/// Phòng Điều dưỡng nhập tay số liệu HIS của ngày (HIS lỗi hoặc chưa có câu truy vấn). Ô để trống gửi <c>null</c>.
/// <paramref name="WaitingAdmission"/>: người bệnh phòng khám chờ nhập viện theo khoa.
/// </summary>
public sealed record UpdateNursingStatisticsRequest(
    DateOnly ReportDate,
    IReadOnlyDictionary<string, int?>? Hospital,
    IReadOnlyDictionary<Guid, int?>? WaitingAdmission);

public sealed record RefreshNursingStatisticsRequest(DateOnly ReportDate);

/// <summary>Kết quả lấy số liệu HIS: bảng tổng hợp mới, và mã khoa HIS trả về mà không khớp khoa nào (để CNTT sửa câu truy vấn).</summary>
public sealed record NursingStatisticsRefreshDto(NursingSummaryDto Summary, IReadOnlyList<string> UnmatchedDepartmentCodes);

/// <summary>File tải về (PDF, Excel).</summary>
public sealed record NursingReportFile(byte[] Content, string ContentType, string FileName);
