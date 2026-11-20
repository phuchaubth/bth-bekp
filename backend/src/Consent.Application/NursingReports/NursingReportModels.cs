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
/// trong danh sách khoa báo cáo do Phòng Điều dưỡng cài (chưa có thì chưa lập được). <paramref name="HisEntry"/>: số liệu HIS
/// khoa nhập khi HIS lỗi (quyết định 35); <c>null</c> khi khoa không có dòng nào phải nhập.
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
    bool IsReportingDepartment,
    NursingHisEntryDto? HisEntry);

/// <summary>
/// Một dòng số liệu HIS khoa nhập. <paramref name="Value"/>: số khoa đã nhập. <paramref name="HisValue"/>: số lấy từ HIS của
/// ngày (nếu có, bảng tổng hợp dùng số này thay số khoa nhập).
/// </summary>
public sealed record NursingHisEntryLineDto(string Key, string Label, int? Value, int? HisValue);

/// <summary>
/// Số liệu HIS khoa nhập tay khi HIS lỗi (quyết định 35): "NB PK chờ nhập viện" của khoa (khoa phải báo cáo) và chỉ số toàn
/// viện Phòng Điều dưỡng giao cho khoa. Không khóa theo báo cáo của khoa. <paramref name="HisHasValues"/>: ngày này đã có số
/// liệu lấy từ HIS.
/// </summary>
public sealed record NursingHisEntryDto(
    IReadOnlyList<NursingHisEntryLineDto> Lines,
    bool CanEdit,
    bool HisHasValues,
    DateTimeOffset? HisUpdatedAtUtc,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Khoa lưu số liệu HIS của khoa trong ngày (<paramref name="DepartmentId"/> bỏ trống là khoa của người dùng). Khóa là mã
/// dòng (<c>waitingAdmission</c>, <c>outpatientVisits</c>, <c>ultrasound</c>…); ô để trống gửi <c>null</c> (xóa số đã nhập).
/// </summary>
public sealed record SaveNursingHisEntryRequest(Guid? DepartmentId, DateOnly ReportDate, IReadOnlyDictionary<string, int?>? Values);

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

/// <summary>
/// Một dòng (khoa) của bảng A. <paramref name="DepartmentCode"/>: mã khoa (tên viết tắt) in ở cột khoa phòng (quyết định 44).
/// <paramref name="SubmittedBy"/>: người gửi báo cáo, in ở cột "Người báo cáo". <paramref name="Cells"/>: theo khóa cột,
/// <c>null</c> là ô trống.
/// </summary>
public sealed record NursingSummaryRowDto(
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    bool IsReporting,
    Guid? ReportId,
    NursingReportState Status,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    IReadOnlyDictionary<string, int?> Cells);

/// <summary>Một dòng mục B, C, D. <paramref name="EnteredBy"/>: mã khoa đã nhập tay số này khi HIS lỗi (mục B, D).</summary>
public sealed record NursingSummaryLineDto(string Key, string Label, int? Value, string? EnteredBy = null);

/// <summary>Một dòng mục E: "Ổn", "Không ổn: mã khoa…" hoặc trống khi chưa khoa nào gửi.</summary>
public sealed record NursingSummaryCheckDto(string Key, string Label, string? Text, IReadOnlyList<string> IssueDepartments);

/// <summary>Một dòng mục F: phần báo cáo nhập thêm của khoa (mã khoa in ở cột khoa).</summary>
public sealed record NursingSummaryNoteDto(Guid DepartmentId, string DepartmentCode, string DepartmentName, string Text);

/// <summary>
/// Người báo cáo từng mục B–F (quyết định 44), dạng "Họ tên (mã khoa)" cách nhau bởi dấu chấm phẩy; <c>null</c> khi mục chưa
/// có số liệu. B, D: người khoa nhập tay khi HIS lỗi, hoặc người lấy số liệu từ HIS. C, E, F: người gửi báo cáo của các khoa có
/// số liệu PTTT, báo "Không ổn", báo cáo sự cố; không khoa nào có thì là <see cref="NursingReportForm.AllSubmittedReporters"/>.
/// </summary>
public sealed record NursingSummaryReportersDto(string? Outpatient, string? Procedures, string? Imaging, string? Situation, string? Incidents);

/// <summary>
/// Số liệu HIS của ngày: số lấy từ HIS (lúc lấy, người bấm; <paramref name="Source"/> <c>Manual</c> là số Phòng Điều dưỡng
/// nhập trước quyết định 35); <paramref name="HisAvailable"/>: đã cấu hình HIS. <paramref name="Entries"/>: các khoa phải
/// nhập tay khi HIS lỗi và đã nhập chưa. <paramref name="UsesDepartmentEntries"/>: bảng có số do khoa nhập.
/// </summary>
public sealed record NursingStatisticsInfoDto(
    bool HisAvailable,
    bool HasValues,
    Consent.Domain.NursingReports.NursingStatisticsSource? Source,
    DateTimeOffset? UpdatedAtUtc,
    string? UpdatedBy,
    IReadOnlyList<NursingHisEntryStatusDto> Entries,
    bool UsesDepartmentEntries);

/// <summary>Một khoa phải nhập số liệu HIS khi HIS lỗi: các dòng (mã chỉ số) của khoa, đã nhập lúc nào, bởi ai.</summary>
public sealed record NursingHisEntryStatusDto(
    Guid DepartmentId,
    string DepartmentName,
    IReadOnlyList<string> Keys,
    DateTimeOffset? EnteredAtUtc,
    string? EnteredBy);

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
    int ReportingCount,
    NursingSummaryReportersDto Reporters);

/// <summary>
/// Một khoa trên màn hình cài khoa báo cáo: có phải báo cáo không, số giường thực kê, các chỉ số toàn viện khoa nhập tay khi
/// HIS lỗi (quyết định 35).
/// </summary>
public sealed record NursingReportDepartmentDto(
    Guid DepartmentId, string Code, string Name, bool IsReporting, int? BedCount, IReadOnlyList<string> HisIndicators);

public sealed record NursingReportDepartmentItem(Guid DepartmentId, int BedCount);

/// <summary>
/// Đặt danh sách khoa phải báo cáo (khoa không có trong danh sách thì bỏ ra) và số giường từng khoa. <paramref name="HisEntry"/>:
/// khoa nhập tay từng chỉ số toàn viện khi HIS lỗi (quyết định 35), đủ cả bảng (chỉ số không có hoặc <c>null</c> là chưa giao);
/// bỏ trống thì giữ nguyên.
/// </summary>
public sealed record UpdateNursingReportDepartmentsRequest(
    IReadOnlyList<NursingReportDepartmentItem>? Items,
    IReadOnlyDictionary<string, Guid?>? HisEntry = null);

public sealed record RefreshNursingStatisticsRequest(DateOnly ReportDate);

/// <summary>Kết quả lấy số liệu HIS: bảng tổng hợp mới, và mã khoa HIS trả về mà không khớp khoa nào (để CNTT sửa câu truy vấn).</summary>
public sealed record NursingStatisticsRefreshDto(NursingSummaryDto Summary, IReadOnlyList<string> UnmatchedDepartmentCodes);

/// <summary>File tải về (PDF, Excel).</summary>
public sealed record NursingReportFile(byte[] Content, string ContentType, string FileName);
