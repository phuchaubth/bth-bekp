using Consent.Application.Common;
using Consent.Domain.NursingReports;

namespace Consent.Application.NursingReports;

/// <summary>Một khoa trên bảng tổng hợp: có trong danh sách phải báo cáo không, số giường Phòng Điều dưỡng cài.</summary>
public sealed record NursingSummaryDepartment(Guid Id, string Name, bool IsReporting, int? BedCount);

/// <summary>
/// Dựng bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" từ báo cáo các khoa và số liệu HIS của ngày (quyết định 31).
/// Chỉ báo cáo <b>đã gửi</b> được tính; khoa chưa gửi để trống các ô do khoa báo cáo (như bản giấy), còn số giường và số liệu
/// HIS vẫn hiện. Khoa đã gửi mà bỏ trống ô nào thì ô đó tính là 0. <c>enteredBy</c>: chỉ số mục B, D lấy từ số khoa nhập tay
/// khi HIS lỗi, kèm tên khoa (quyết định 35).
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
        IReadOnlyDictionary<string, string>? enteredBy = null)
    {
        ArgumentNullException.ThrowIfNull(departments);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(userNames);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(statisticsInfo);

        var reportByDepartment = reports.Where(r => r.ReportDate == reportDate).ToDictionary(r => r.DepartmentId);
        var submitted = reportByDepartment.Values.Where(r => r.IsSubmitted).ToDictionary(r => r.DepartmentId);

        // Khoa phải báo cáo, cộng khoa đã gửi dù đã bị bỏ khỏi danh sách (không giấu báo cáo đã gửi).
        var shown = departments
            .Where(d => d.IsReporting || submitted.ContainsKey(d.Id))
            .OrderBy(d => d.Name, VietnameseComparer.Instance)
            .ToList();
        var names = departments.ToDictionary(d => d.Id, d => d.Name);

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
            .OrderBy(r => names.GetValueOrDefault(r.Report.DepartmentId) ?? string.Empty, VietnameseComparer.Instance)
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
                    .Select(r => names.GetValueOrDefault(r.Report.DepartmentId) ?? string.Empty)
                    .ToList();
                var text = submittedFigures.Count == 0 ? null
                    : issues.Count == 0 ? OkText
                    : $"{IssueText}: {string.Join(", ", issues)}";
                return new NursingSummaryCheckDto(c.Key, c.Label, text, issues);
            })
            .ToList();

        var incidents = submittedFigures
            .Where(r => !string.IsNullOrWhiteSpace(r.Report.Notes))
            .Select(r => new NursingSummaryNoteDto(
                r.Report.DepartmentId, names.GetValueOrDefault(r.Report.DepartmentId) ?? string.Empty, r.Report.Notes!))
            .ToList();

        return new NursingSummaryDto(
            reportDate,
            NursingReportForm.SummaryColumns,
            rows,
            totals,
            [.. NursingReportForm.OutpatientLines.Select(l => HospitalLine(l, statistics, enteredBy))],
            procedures,
            [.. NursingReportForm.ImagingLines.Select(l => HospitalLine(l, statistics, enteredBy))],
            situation,
            incidents,
            statisticsInfo,
            rows.Count(r => r.Status == NursingReportState.Submitted),
            departments.Count(d => d.IsReporting));
    }

    private static NursingSummaryLineDto HospitalLine(
        NursingSummaryLine line, NursingStatisticsValues statistics, IReadOnlyDictionary<string, string>? enteredBy) =>
        new(line.Key, line.Label, statistics.HospitalValue(line.Key), enteredBy?.GetValueOrDefault(line.Key));

    private static int? Cell(string column, NursingSummaryDepartment department, NursingReportFigures? figures, NursingStatisticsValues statistics) =>
        column switch
        {
            NursingReportForm.BedsColumn => department.BedCount,
            NursingHisIndicators.WaitingAdmission => statistics.Waiting(department.Id),
            _ when figures is null => null,
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
