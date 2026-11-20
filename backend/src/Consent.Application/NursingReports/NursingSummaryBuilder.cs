using Consent.Application.Common;
using Consent.Domain.NursingReports;

namespace Consent.Application.NursingReports;

/// <summary>
/// Một khoa trên bảng tổng hợp: tên viết tắt (Phòng Điều dưỡng đặt, không có thì mã khoa HIS; in thay tên khoa, quyết định 44,
/// 45), có trong danh sách phải báo cáo không, số giường và thứ tự Phòng Điều dưỡng cài (<c>null</c>: không trong danh sách, đứng
/// cuối).
/// </summary>
public sealed record NursingSummaryDepartment(Guid Id, string Code, string Name, bool IsReporting, int? BedCount, int? SortOrder = null);

/// <summary>
/// Dựng bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" từ báo cáo các khoa và số liệu HIS của ngày (quyết định 31).
/// Chỉ báo cáo <b>đã gửi</b> được tính; khoa chưa gửi để trống các ô do khoa báo cáo (như bản giấy), còn số giường và số liệu
/// HIS vẫn hiện. Khoa đã gửi mà bỏ trống ô nào thì ô đó tính là 0, trừ dòng mẫu thêm sau khi khoa gửi (để trống).
/// <c>hospitalEnteredBy</c>: chỉ số mục B, D lấy từ số khoa nhập tay khi HIS lỗi, kèm khoa đã nhập (quyết định 35).
/// Khoa ghi bằng mã khoa (tên viết tắt), mỗi mục có người báo cáo (quyết định 44).
/// </summary>
public static class NursingSummaryBuilder
{
    public const string OkText = "Ổn";

    public const string IssueText = "Không ổn";

    public static NursingSummaryDto Build(
        DateOnly reportDate,
        IReadOnlyList<NursingSummaryDepartment> departments,
        IReadOnlyList<NursingDailyReport> reports,
        IReadOnlyDictionary<Guid, string> userNames,
        NursingStatisticsValues statistics,
        NursingStatisticsInfoDto statisticsInfo,
        IReadOnlyDictionary<string, Guid>? hospitalEnteredBy = null)
    {
        ArgumentNullException.ThrowIfNull(departments);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(userNames);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(statisticsInfo);

        var reportByDepartment = reports.Where(r => r.ReportDate == reportDate).ToDictionary(r => r.DepartmentId);
        var submitted = reportByDepartment.Values.Where(r => r.IsSubmitted).ToDictionary(r => r.DepartmentId);

        // Khoa phải báo cáo, cộng khoa đã gửi dù đã bị bỏ khỏi danh sách (không giấu báo cáo đã gửi). Theo thứ tự Phòng Điều dưỡng
        // xếp (quyết định 45); chưa xếp hoặc cùng thứ tự thì theo tên viết tắt; khoa không trong danh sách đứng cuối.
        var shown = departments
            .Where(d => d.IsReporting || submitted.ContainsKey(d.Id))
            .OrderBy(d => d.SortOrder ?? int.MaxValue)
            .ThenBy(d => d.Code, VietnameseComparer.Instance)
            .ThenBy(d => d.Name, VietnameseComparer.Instance)
            .ToList();
        var rank = shown.Select((d, index) => (d.Id, index)).ToDictionary(p => p.Id, p => p.index);
        var byId = departments.ToDictionary(d => d.Id);
        string Code(Guid departmentId) => byId.TryGetValue(departmentId, out var d) ? d.Code : string.Empty;
        string Reporter(NursingDailyReport report) =>
            Person(report.SubmittedByUserId is { } id ? userNames.GetValueOrDefault(id) : null, Code(report.DepartmentId));

        var rows = new List<NursingSummaryRowDto>(shown.Count);
        foreach (var department in shown)
        {
            reportByDepartment.TryGetValue(department.Id, out var report);
            var figures = report is { IsSubmitted: true } ? NursingReportFigures.Parse(report.FiguresJson) : null;
            var cells = NursingReportForm.SummaryColumns.ToDictionary(
                c => c.Key,
                c => Cell(c.Key, department, figures, statistics),
                StringComparer.Ordinal);
            rows.Add(new NursingSummaryRowDto(
                department.Id,
                department.Code,
                department.Name,
                department.IsReporting,
                report?.Id,
                report is null ? NursingReportState.New : report.IsSubmitted ? NursingReportState.Submitted : NursingReportState.Draft,
                report?.SubmittedByUserId is { } submittedBy && report.IsSubmitted ? userNames.GetValueOrDefault(submittedBy) : null,
                report is { IsSubmitted: true } ? report.SubmittedAtUtc : null,
                cells));
        }

        var totals = NursingReportForm.SummaryColumns.ToDictionary(
            c => c.Key,
            c => Sum(rows.Select(r => r.Cells[c.Key])),
            StringComparer.Ordinal);

        var submittedFigures = submitted.Values
            .Select(r => (Report: r, Figures: NursingReportFigures.Parse(r.FiguresJson)))
            // Mục C, E, F theo đúng thứ tự khoa của mục A.
            .OrderBy(r => rank.GetValueOrDefault(r.Report.DepartmentId, int.MaxValue))
            .ToList();

        var procedures = NursingReportForm.ProcedureLines
            .Select(l => new NursingSummaryLineDto(
                l.Key,
                l.Label,
                submittedFigures.Count == 0 ? null : submittedFigures.Sum(r => r.Figures.Value(l.Key) ?? 0)))
            .ToList();

        var situation = NursingReportForm.Checks
            .Where(c => c.InSummary)
            .Select(c =>
            {
                var issues = submittedFigures
                    .Where(r => r.Figures.Check(c.Key) == NursingReportForm.CheckIssue)
                    .Select(r => Code(r.Report.DepartmentId))
                    .ToList();
                var text = submittedFigures.Count == 0 ? null
                    : issues.Count == 0 ? OkText
                    : $"{IssueText}: {string.Join(", ", issues)}";
                return new NursingSummaryCheckDto(c.Key, c.Label, text, issues);
            })
            .ToList();

        var withNotes = submittedFigures.Where(r => !string.IsNullOrWhiteSpace(r.Report.Notes)).ToList();
        var incidents = withNotes
            .Select(r => new NursingSummaryNoteDto(
                r.Report.DepartmentId,
                Code(r.Report.DepartmentId),
                byId.TryGetValue(r.Report.DepartmentId, out var d) ? d.Name : string.Empty,
                r.Report.Notes!))
            .ToList();

        // Người báo cáo mục C, E, F: người gửi báo cáo của các khoa có số liệu PTTT, báo "Không ổn", báo cáo sự cố (mỗi khoa một
        // báo cáo mỗi ngày nên không trùng); không khoa nào có thì mục đó lấy từ báo cáo của mọi khoa đã gửi (cột "Người báo cáo"
        // của mục A).
        string? FromReports(IEnumerable<NursingDailyReport> contributing)
        {
            var people = contributing.Select(Reporter).ToList();
            return people.Count > 0 ? string.Join("; ", people)
                : submittedFigures.Count > 0 ? NursingReportForm.AllSubmittedReporters
                : null;
        }

        var reporters = new NursingSummaryReportersDto(
            HospitalReporters(NursingReportForm.OutpatientLines, statistics, statisticsInfo, hospitalEnteredBy, Code),
            FromReports(submittedFigures
                .Where(r => NursingReportForm.ProcedureLines.Any(l => r.Figures.Value(l.Key) is > 0))
                .Select(r => r.Report)),
            HospitalReporters(NursingReportForm.ImagingLines, statistics, statisticsInfo, hospitalEnteredBy, Code),
            FromReports(submittedFigures
                .Where(r => NursingReportForm.Checks.Any(c => c.InSummary && r.Figures.Check(c.Key) == NursingReportForm.CheckIssue))
                .Select(r => r.Report)),
            FromReports(withNotes.Select(r => r.Report)));

        return new NursingSummaryDto(
            reportDate,
            NursingReportForm.SummaryColumns,
            rows,
            totals,
            [.. NursingReportForm.OutpatientLines.Select(l => HospitalLine(l, statistics, hospitalEnteredBy, Code))],
            procedures,
            [.. NursingReportForm.ImagingLines.Select(l => HospitalLine(l, statistics, hospitalEnteredBy, Code))],
            situation,
            incidents,
            statisticsInfo,
            rows.Count(r => r.Status == NursingReportState.Submitted),
            departments.Count(d => d.IsReporting),
            reporters);
    }

    /// <summary>"Họ tên (mã khoa)"; tài khoản không còn thì ghi "Không rõ".</summary>
    private static string Person(string? name, string where) => $"{name ?? UnknownPerson} ({where})";

    private const string UnknownPerson = "Không rõ";

    private static NursingSummaryLineDto HospitalLine(
        NursingSummaryLine line, NursingStatisticsValues statistics, IReadOnlyDictionary<string, Guid>? hospitalEnteredBy, Func<Guid, string> code) =>
        new(
            line.Key,
            line.Label,
            statistics.HospitalValue(line.Key),
            hospitalEnteredBy is not null && hospitalEnteredBy.TryGetValue(line.Key, out var departmentId) ? code(departmentId) : null);

    /// <summary>
    /// Người báo cáo mục B, D: người khoa nhập tay số liệu khi HIS lỗi (quyết định 35), hoặc người lấy số liệu từ HIS (số do
    /// Phòng Điều dưỡng nhập tay trước quyết định 35 thì ghi Phòng Điều dưỡng). Dòng chưa có số không tính.
    /// </summary>
    private static string? HospitalReporters(
        IReadOnlyList<NursingSummaryLine> lines,
        NursingStatisticsValues statistics,
        NursingStatisticsInfoDto info,
        IReadOnlyDictionary<string, Guid>? hospitalEnteredBy,
        Func<Guid, string> code)
    {
        var people = new List<string>();
        foreach (var line in lines.Where(l => statistics.HospitalValue(l.Key) is not null))
        {
            people.Add(hospitalEnteredBy is not null && hospitalEnteredBy.TryGetValue(line.Key, out var departmentId)
                ? Person(info.Entries.FirstOrDefault(e => e.DepartmentId == departmentId)?.EnteredBy, code(departmentId))
                : Person(info.UpdatedBy, info.Source == NursingStatisticsSource.Manual ? NursingReportForm.SummaryUnit : "lấy từ HIS"));
        }

        var distinct = people.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count == 0 ? null : string.Join("; ", distinct);
    }

    private static int? Cell(string column, NursingSummaryDepartment department, NursingReportFigures? figures, NursingStatisticsValues statistics) =>
        column switch
        {
            NursingReportForm.BedsColumn => department.BedCount,
            NursingHisIndicators.WaitingAdmission => statistics.Waiting(department.Id),
            _ when figures is null => null,
            // Dòng mẫu thêm sau khi khoa gửi báo cáo (quyết định 44): khoa chưa có để nhập, để trống thay vì ghi 0.
            _ when figures.Version < NursingReportForm.Since(column) => null,
            // Báo cáo gửi trước khi tách cột chỉ có số tổng: để trống hai cột mới, không ghi 0 (quyết định 38).
            _ when figures.HasOnlyLegacyAdmissions && NursingReportForm.AdmissionKeys.Contains(column) => null,
            NursingReportForm.OutsideVisitsColumn => NursingReportForm.OutsideVisitKeys.Sum(k => figures.Value(k) ?? 0),
            _ => figures.Value(column) ?? 0,
        };

    private static int? Sum(IEnumerable<int?> values)
    {
        int? total = null;
        foreach (var value in values)
        {
            if (value is { } number)
            {
                total = (total ?? 0) + number;
            }
        }

        return total;
    }
}
