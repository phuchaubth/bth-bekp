using Consent.Application.ConsentForms;
using Consent.Domain.ConsentForms;

namespace Consent.Application.Reports;

/// <summary>Bộ lọc báo cáo (ngày lập phiếu, giờ Việt Nam). Bỏ trống ngày: từ đầu tháng hiện tại đến hôm nay.</summary>
public sealed record ReportQuery(DateOnly? From = null, DateOnly? To = null, Guid? DepartmentId = null, Guid? TemplateId = null);

/// <summary>Số phiếu theo trạng thái hiện tại.</summary>
public sealed record StatusCounts(int Total, int Draft, int PendingSignature, int Completed, int Cancelled, int Revoked);

/// <summary>Ý kiến của người bệnh trên các phiếu hoàn tất (còn hiệu lực). <see cref="RefusalRate"/> từ 0 đến 1.</summary>
public sealed record DecisionCounts(int Agreed, int Refused, double? RefusalRate);

/// <summary>Người ký trên các phiếu hoàn tất: người bệnh tự ký hay người đại diện ký thay (hiện đều ký tay trên điện thoại).</summary>
public sealed record SignerCounts(int Patient, int Representative);

/// <summary>
/// Thời gian từ lúc lập phiếu đến lúc hoàn tất (phút), trên các phiếu hoàn tất; kèm số phiếu theo từng khoảng:
/// đến 15 phút, trên 15 đến 60 phút, trên 1 đến 24 giờ, trên 24 giờ.
/// </summary>
public sealed record CompletionTimeStats(
    int Count,
    double? AverageMinutes,
    double? MedianMinutes,
    double? Percentile90Minutes,
    int UpTo15Minutes,
    int UpTo1Hour,
    int UpTo1Day,
    int Over1Day);

public sealed record GroupStats(Guid Id, string Code, string Name, StatusCounts Counts, DecisionCounts Decisions);

/// <summary>Số phiếu lập trong một ngày, theo trạng thái hiện tại.</summary>
public sealed record DailyStats(DateOnly Date, StatusCounts Counts);

public sealed record ReportSummaryDto(
    DateOnly From,
    DateOnly To,
    StatusCounts Totals,
    DecisionCounts Decisions,
    SignerCounts Signers,
    CompletionTimeStats CompletionTime,
    IReadOnlyList<GroupStats> ByDepartment,
    IReadOnlyList<GroupStats> ByTemplate,
    IReadOnlyList<DailyStats> Daily);

/// <summary>Một nhóm đếm từ CSDL: khoa × mẫu × trạng thái × quyết định × người ký.</summary>
public sealed record ConsentFormGroupCount(
    Guid DepartmentId, Guid TemplateId, ConsentFormStatus Status, ConsentDecision? Decision, ConsentSignerKind SignerKind, int Count);

/// <summary>Số phiếu theo ngày (chỉ số ngày tính từ mốc 00:00 giờ Việt Nam) và trạng thái.</summary>
public sealed record DailyStatusCount(int DayIndex, ConsentFormStatus Status, int Count);

public sealed record DailyCount(int DayIndex, int Count);

/// <summary>Phiếu hiển thị trên trang tổng quan.</summary>
public sealed record DashboardFormItem(
    Guid Id,
    string FormNo,
    ConsentFormStatus Status,
    string PatientName,
    string TemplateCode,
    string TemplateName,
    string DepartmentName,
    DateTimeOffset AtUtc);

public sealed record DashboardFormList(int Total, IReadOnlyList<DashboardFormItem> Items);

public sealed record DashboardDay(DateOnly Date, int Created, int Completed);

/// <summary>Trang tổng quan (màn hình 2), trong phạm vi khoa của người dùng.</summary>
public sealed record DashboardDto(
    int Drafts,
    int PendingSignature,
    int CompletedToday,
    int RevokedLast30Days,
    DashboardFormList AwaitingMyConfirmation,
    DashboardFormList MyOpenForms,
    IReadOnlyList<DashboardDay> Last14Days);

internal static class ReportDates
{
    /// <summary>Ngày hiện tại theo giờ Việt Nam.</summary>
    public static DateOnly Today(DateTimeOffset nowUtc) =>
        DateOnly.FromDateTime(nowUtc.ToOffset(ConsentFormService.VietnamOffset).DateTime);
}
