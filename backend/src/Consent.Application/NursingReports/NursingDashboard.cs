using Consent.Application.Common;

namespace Consent.Application.NursingReports;

/// <summary>
/// Báo cáo đã gửi của một khoa trong một ngày, chỉ phần dashboard cần (quyết định 47): số liệu, phần tick và có phần nhập thêm
/// (sự cố, bất thường) không. Không đọc nội dung phần nhập thêm (đã mã hóa, có thể có thông tin người bệnh).
/// </summary>
public sealed record NursingSubmittedFigures(Guid DepartmentId, DateOnly ReportDate, string FiguresJson, bool HasNotes);

/// <summary>
/// Dashboard điều dưỡng của Phòng Điều dưỡng (quyết định 47), các ngày <see cref="From"/> đến <see cref="To"/>: số liệu theo
/// ngày, theo khoa (ngày cuối kỳ) và số lượt "Không ổn" theo từng dòng tick. Như bảng tổng hợp, chỉ báo cáo <b>đã gửi</b> được
/// tính. <see cref="ReportingCount"/>: số khoa phải báo cáo (danh sách hiện tại).
/// </summary>
public sealed record NursingDashboardDto(
    DateOnly From,
    DateOnly To,
    int ReportingCount,
    IReadOnlyList<NursingDashboardDayDto> Days,
    IReadOnlyList<NursingDashboardDepartmentDto> Departments,
    IReadOnlyList<NursingDashboardCheckDto> Checks);

/// <summary>
/// Một ngày báo cáo. <see cref="Values"/>: tổng từng chỉ số (<see cref="NursingDashboardBuilder.ValueKeys"/>) của các báo cáo đã
/// gửi; <c>null</c> khi chưa khoa nào gửi hoặc không báo cáo nào có dòng đó (mẫu cũ). <see cref="PatientChange"/>: Số NB hiện
/// tại trừ Số NB ngày hôm trước, cộng trên các báo cáo có cả hai số. <see cref="Beds"/>, <see cref="BedPatients"/>: số giường và
/// Số NB hiện tại của các khoa đã gửi có cài số giường (công suất giường = BedPatients / Beds). <see cref="IssueDepartments"/>:
/// số khoa báo "Không ổn" ít nhất một dòng; <see cref="Incidents"/>: số báo cáo có phần nhập thêm.
/// </summary>
public sealed record NursingDashboardDayDto(
    DateOnly Date,
    int Submitted,
    IReadOnlyDictionary<string, int?> Values,
    int? PatientChange,
    int? Beds,
    int? BedPatients,
    int IssueDepartments,
    int Incidents);

/// <summary>
/// Một khoa (khoa phải báo cáo và khoa đã gửi báo cáo trong kỳ, theo thứ tự bảng tổng hợp), ghi tên viết tắt như bảng tổng hợp.
/// <see cref="SubmittedDays"/>: số ngày đã gửi báo cáo trong kỳ. Số liệu của ngày cuối kỳ: <see cref="Submitted"/> là khoa đã
/// gửi báo cáo ngày đó (chưa gửi thì <see cref="Values"/> rỗng), <see cref="Issues"/> là khóa các dòng tick báo "Không ổn",
/// <see cref="HasIncident"/> là báo cáo có phần nhập thêm.
/// </summary>
public sealed record NursingDashboardDepartmentDto(
    Guid DepartmentId,
    string Code,
    string Name,
    bool IsReporting,
    int? BedCount,
    int SubmittedDays,
    bool Submitted,
    IReadOnlyDictionary<string, int?> Values,
    IReadOnlyList<string> Issues,
    bool HasIncident);

/// <summary>
/// Một dòng tick: số lượt khoa báo "Không ổn" trong kỳ (mỗi báo cáo đã gửi một lượt) và các khoa báo "Không ổn" ngày cuối kỳ (tên
/// viết tắt, theo thứ tự bảng tổng hợp).
/// </summary>
public sealed record NursingDashboardCheckDto(string Key, string Label, int IssueCount, IReadOnlyList<string> LastDayDepartments);

/// <summary>
/// Dựng dashboard điều dưỡng (quyết định 47) từ các báo cáo đã gửi. Cách tính giống bảng tổng hợp: báo cáo đã gửi mà bỏ trống ô
/// nào thì ô đó tính 0, trừ dòng mẫu thêm sau khi khoa gửi (không tính); khoa xếp theo thứ tự Phòng Điều dưỡng đặt. Phần tick
/// tính cả 8 dòng của báo cáo khoa (kể cả "ĐD thực hiện thuốc", bảng tổng hợp giấy không có dòng này). Số giường là số đang cài.
/// </summary>
public static class NursingDashboardBuilder
{
    /// <summary>
    /// Tổng số ca nhập khoa ngày hôm trước (từ PK và do CK); báo cáo lưu trước khi tách hai dòng này (quyết định 38) dùng số
    /// tổng cũ.
    /// </summary>
    public const string AdmissionsKey = "admissions";

    /// <summary>Các chỉ số cộng theo ngày và trả theo khoa (khóa dòng của báo cáo khoa, thêm <see cref="AdmissionsKey"/>).</summary>
    public static IReadOnlyList<string> ValueKeys { get; } =
    [
        NursingReportForm.PreviousDayPatientsKey,
        NursingReportForm.CurrentPatientsKey,
        AdmissionsKey,
        NursingReportForm.AdmissionsFromClinicKey,
        NursingReportForm.AdmissionsByTransferKey,
        NursingReportForm.DischargesKey,
        NursingReportForm.TransfersOutKey,
        "careLevel1",
        "ventilated",
        "monitored",
        "oxygen",
        "noBed",
        NursingReportForm.AdrKey,
    ];

    public static NursingDashboardDto Build(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<NursingSummaryDepartment> departments,
        IReadOnlyList<NursingSubmittedFigures> reports)
    {
        ArgumentNullException.ThrowIfNull(departments);
        ArgumentNullException.ThrowIfNull(reports);

        var parsed = reports
            .Where(r => r.ReportDate >= from && r.ReportDate <= to)
            .Select(r => new Parsed(r, NursingReportFigures.Parse(r.FiguresJson)))
            .ToList();
        var byId = departments.ToDictionary(d => d.Id);
        var submittedDays = parsed.GroupBy(p => p.Report.DepartmentId).ToDictionary(g => g.Key, g => g.Count());

        // Như mục A của bảng tổng hợp: khoa phải báo cáo, cộng khoa đã gửi báo cáo trong kỳ dù không còn trong danh sách.
        var shown = departments
            .Where(d => d.IsReporting || submittedDays.ContainsKey(d.Id))
            .OrderBy(d => d.SortOrder ?? int.MaxValue)
            .ThenBy(d => d.Code, VietnameseComparer.Instance)
            .ThenBy(d => d.Name, VietnameseComparer.Instance)
            .ToList();

        var days = new List<NursingDashboardDayDto>(to.DayNumber - from.DayNumber + 1);
        var byDate = parsed.ToLookup(p => p.Report.ReportDate);
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add(Day(date, [.. byDate[date]], byId));
        }

        var lastDay = byDate[to].ToDictionary(p => p.Report.DepartmentId);
        var rows = shown
            .Select(d => lastDay.TryGetValue(d.Id, out var report)
                ? new NursingDashboardDepartmentDto(
                    d.Id,
                    d.Code,
                    d.Name,
                    d.IsReporting,
                    d.BedCount,
                    submittedDays.GetValueOrDefault(d.Id),
                    true,
                    ValueKeys.ToDictionary(k => k, k => Value(k, report.Figures), StringComparer.Ordinal),
                    [.. NursingReportForm.Checks.Where(c => report.HasIssue(c.Key)).Select(c => c.Key)],
                    report.Report.HasNotes)
                : new NursingDashboardDepartmentDto(
                    d.Id,
                    d.Code,
                    d.Name,
                    d.IsReporting,
                    d.BedCount,
                    submittedDays.GetValueOrDefault(d.Id),
                    false,
                    new Dictionary<string, int?>(StringComparer.Ordinal),
                    [],
                    false))
            .ToList();

        var checks = NursingReportForm.Checks
            .Select(c => new NursingDashboardCheckDto(
                c.Key,
                c.Label,
                parsed.Count(p => p.HasIssue(c.Key)),
                [.. shown.Where(d => lastDay.TryGetValue(d.Id, out var report) && report.HasIssue(c.Key)).Select(d => d.Code)]))
            .ToList();

        return new NursingDashboardDto(from, to, departments.Count(d => d.IsReporting), days, rows, checks);
    }

    private static NursingDashboardDayDto Day(
        DateOnly date, IReadOnlyList<Parsed> reports, IReadOnlyDictionary<Guid, NursingSummaryDepartment> departments)
    {
        // Công suất giường: chỉ các khoa đã gửi có cài số giường, để khoa chưa gửi không kéo công suất xuống.
        var withBeds = reports
            .Select(p => (
                Beds: departments.TryGetValue(p.Report.DepartmentId, out var department) ? department.BedCount : null,
                Patients: Value(NursingReportForm.CurrentPatientsKey, p.Figures)))
            .Where(x => x.Beds is > 0 && x.Patients is not null)
            .ToList();
        return new NursingDashboardDayDto(
            date,
            reports.Count,
            ValueKeys.ToDictionary(k => k, k => NursingSummaryBuilder.Sum(reports.Select(p => Value(k, p.Figures))), StringComparer.Ordinal),
            NursingSummaryBuilder.Sum(reports.Select(p => Change(p.Figures))),
            withBeds.Count == 0 ? null : withBeds.Sum(x => x.Beds!.Value),
            withBeds.Count == 0 ? null : withBeds.Sum(x => x.Patients!.Value),
            reports.Count(p => NursingReportForm.Checks.Any(c => p.HasIssue(c.Key))),
            reports.Count(p => p.Report.HasNotes));
    }

    /// <summary>
    /// Số của một chỉ số trong báo cáo đã gửi, như ô của bảng tổng hợp: ô trống tính 0; dòng mẫu thêm sau khi khoa gửi và hai
    /// dòng nhập khoa của báo cáo trước khi tách (quyết định 38) không tính (<c>null</c>).
    /// </summary>
    private static int? Value(string key, NursingReportFigures figures) =>
        key switch
        {
            AdmissionsKey when figures.HasOnlyLegacyAdmissions => figures.Value(NursingReportForm.LegacyAdmissionsKey) ?? 0,
            AdmissionsKey => NursingReportForm.AdmissionKeys.Sum(k => figures.Value(k) ?? 0),
            _ when figures.Version < NursingReportForm.Since(key) => null,
            _ when figures.HasOnlyLegacyAdmissions && NursingReportForm.AdmissionKeys.Contains(key) => null,
            _ => figures.Value(key) ?? 0,
        };

    /// <summary>Số NB tăng (giảm) so với ngày hôm trước, chỉ khi báo cáo có cả Số NB ngày hôm trước (từ quyết định 46).</summary>
    private static int? Change(NursingReportFigures figures) =>
        Value(NursingReportForm.PreviousDayPatientsKey, figures) is { } previous && Value(NursingReportForm.CurrentPatientsKey, figures) is { } current
            ? current - previous
            : null;

    private sealed record Parsed(NursingSubmittedFigures Report, NursingReportFigures Figures)
    {
        public bool HasIssue(string checkKey) => Figures.Check(checkKey) == NursingReportForm.CheckIssue;
    }
}
