using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Shared;

namespace Consent.Application.Reports;

/// <summary>
/// Báo cáo thống kê (màn hình 14) trên các phiếu lập trong khoảng thời gian chọn, trong phạm vi khoa của người xem
/// (trừ quyền xem toàn viện). Số theo trạng thái là trạng thái hiện tại; ý kiến, người ký và thời gian hoàn tất tính trên
/// các phiếu đang hoàn tất (phiếu đã thu hồi không tính, vì phiếu thay thế được tính riêng).
/// </summary>
public sealed class ReportService(
    IConsentStatisticsRepository statistics,
    IDepartmentRepository departments,
    IConsentTemplateRepository templates,
    IClock clock,
    ICurrentUser currentUser)
{
    /// <summary>Khoảng thời gian tối đa của một báo cáo.</summary>
    public const int MaxDays = 366;

    private bool ScopeAll => currentUser.HasPermission(Permissions.ScopeAllDepartments);

    public async Task<Result<ReportSummaryDto>> SummaryAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var to = query.To ?? ReportDates.Today(clock.UtcNow);
        var from = query.From ?? new DateOnly(to.Year, to.Month, 1);
        if (from > to)
        {
            return Error.ValidationField("to", "Đến ngày phải sau hoặc bằng từ ngày.");
        }

        if (to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Error.ValidationField("to", $"Báo cáo tối đa {MaxDays} ngày; hãy thu hẹp khoảng thời gian.");
        }

        if (!ScopeAll && query.DepartmentId is { } departmentId && departmentId != currentUser.DepartmentId)
        {
            return Error.Forbidden("reports.department_forbidden", "Bạn chỉ xem được báo cáo của khoa mình.");
        }

        var fromUtc = ConsentFormService.StartOfDayUtc(from)!.Value;
        var search = new ConsentFormSearch(
            Status: null, query.TemplateId, PatientId: null, query.DepartmentId, fromUtc, ConsentFormService.StartOfDayUtc(to.AddDays(1)),
            Keyword: null, Page: 1, PageSize: 1, ScopeAll, currentUser.DepartmentId, currentUser.UserId);

        var groups = await statistics.CountByGroupAsync(search, cancellationToken).ConfigureAwait(false);
        var daily = await statistics.CountByCreatedDayAsync(search, fromUtc, cancellationToken).ConfigureAwait(false);
        var seconds = await statistics.CompletionSecondsAsync(search, cancellationToken).ConfigureAwait(false);
        var departmentById = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.Id);
        var templateById = (await templates.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(t => t.Id);

        var completed = groups.Where(g => g.Status == ConsentFormStatus.Completed).ToList();
        var byDepartment = groups.GroupBy(g => g.DepartmentId)
            .Select(g => new GroupStats(
                g.Key,
                departmentById.TryGetValue(g.Key, out var department) ? department.Code : string.Empty,
                department?.Name ?? string.Empty,
                Counts(g),
                Decisions(g.Where(x => x.Status == ConsentFormStatus.Completed))))
            .OrderByDescending(g => g.Counts.Total).ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        var byTemplate = groups.GroupBy(g => g.TemplateId)
            .Select(g => new GroupStats(
                g.Key,
                templateById.TryGetValue(g.Key, out var template) ? template.Code : string.Empty,
                template?.Name ?? string.Empty,
                Counts(g),
                Decisions(g.Where(x => x.Status == ConsentFormStatus.Completed))))
            .OrderByDescending(g => g.Counts.Total).ThenBy(g => g.Code, StringComparer.Ordinal)
            .ToList();
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => new DailyStats(from.AddDays(i), Counts(daily.Where(d => d.DayIndex == i).Select(d => (d.Status, d.Count)))))
            .ToList();

        return new ReportSummaryDto(
            from,
            to,
            Counts(groups),
            Decisions(completed),
            new SignerCounts(
                completed.Where(g => g.SignerKind == ConsentSignerKind.Patient).Sum(g => g.Count),
                completed.Where(g => g.SignerKind == ConsentSignerKind.Representative).Sum(g => g.Count)),
            CompletionTime(seconds),
            byDepartment,
            byTemplate,
            days);
    }

    internal static StatusCounts Counts(IEnumerable<ConsentFormGroupCount> groups) => Counts(groups.Select(g => (g.Status, g.Count)));

    internal static StatusCounts Counts(IEnumerable<(ConsentFormStatus Status, int Count)> counts)
    {
        var byStatus = counts.GroupBy(c => c.Status).ToDictionary(g => g.Key, g => g.Sum(c => c.Count));
        int Of(ConsentFormStatus status) => byStatus.GetValueOrDefault(status);
        return new StatusCounts(
            byStatus.Values.Sum(),
            Of(ConsentFormStatus.Draft),
            Of(ConsentFormStatus.PendingSignature),
            Of(ConsentFormStatus.Completed),
            Of(ConsentFormStatus.Cancelled),
            Of(ConsentFormStatus.Revoked));
    }

    internal static DecisionCounts Decisions(IEnumerable<ConsentFormGroupCount> completed)
    {
        var list = completed.ToList();
        var agreed = list.Where(g => g.Decision == ConsentDecision.Agreed).Sum(g => g.Count);
        var refused = list.Where(g => g.Decision == ConsentDecision.Refused).Sum(g => g.Count);
        return new DecisionCounts(agreed, refused, agreed + refused == 0 ? null : Math.Round((double)refused / (agreed + refused), 4));
    }

    internal static CompletionTimeStats CompletionTime(IReadOnlyList<int> seconds)
    {
        if (seconds.Count == 0)
        {
            return new CompletionTimeStats(0, null, null, null, 0, 0, 0, 0);
        }

        var sorted = seconds.Select(s => Math.Max(0, s)).Order().ToArray();
        static double Minutes(double value) => Math.Round(value / 60d, 1);
        return new CompletionTimeStats(
            sorted.Length,
            Minutes(sorted.Average()),
            Minutes(Percentile(sorted, 0.5)),
            Minutes(Percentile(sorted, 0.9)),
            sorted.Count(s => s <= 15 * 60),
            sorted.Count(s => s is > 15 * 60 and <= 60 * 60),
            sorted.Count(s => s is > 60 * 60 and <= 24 * 60 * 60),
            sorted.Count(s => s > 24 * 60 * 60));
    }

    /// <summary>Phân vị theo nội suy tuyến tính giữa hai hạng gần nhất (như PERCENTILE.INC của Excel).</summary>
    private static double Percentile(int[] sorted, double p)
    {
        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((rank - lower) * (sorted[upper] - sorted[lower]));
    }
}
