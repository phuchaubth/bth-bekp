using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.QualityControl;
using Consent.Domain.Authorization;
using Consent.Domain.CriticalResults;
using Consent.Shared;

namespace Consent.Application.CriticalResults;

/// <summary>
/// Cách tính dashboard kết quả nguy cấp (quyết định 60, Bảng 3.7 đề cương sáng kiến). Hồ sơ đã hủy đếm riêng, không vào tỷ lệ.
/// Một hồ sơ vào mẫu số khi đã có kết quả của bước đó hoặc đã quá hạn bước đó (hồ sơ còn trong hạn chưa tính). Tháng theo giờ có
/// kết quả (giờ Việt Nam).
/// </summary>
public static class CriticalDashboardBuilder
{
    public const int TrendMonths = 12;

    public const int LateLimit = 50;

    public static CriticalDashboardDto Build(
        DateOnly month,
        bool quarter,
        DateOnly today,
        DateTimeOffset nowUtc,
        IReadOnlyList<CriticalResult> results,
        IReadOnlyDictionary<Guid, string> departmentNames,
        CriticalSettings settings)
    {
        var (from, toExclusive) = quarter
            ? (new DateOnly(month.Year, ((month.Month - 1) / 3 * 3) + 1, 1), new DateOnly(month.Year, ((month.Month - 1) / 3 * 3) + 1, 1).AddMonths(3))
            : (month, month.AddMonths(1));
        var lastDay = toExclusive.AddDays(-1);
        var to = lastDay < today ? lastDay : today;
        var inPeriod = results.Where(r => Day(r) >= from && Day(r) < toExclusive).ToList();
        var active = inPeriod.Where(r => !r.IsCancelled).ToList();

        var trendStart = month.AddMonths(1 - TrendMonths);
        var trend = Enumerable.Range(0, TrendMonths)
            .Select(i => trendStart.AddMonths(i))
            .Select(m =>
            {
                var items = results.Where(r => !r.IsCancelled && Day(r) >= m && Day(r) < m.AddMonths(1)).ToList();
                return new CriticalDashboardMonthDto(
                    m.ToString("yyyy-MM", CultureInfo.InvariantCulture), items.Count, NotifyRate(items, nowUtc).Percent, ActionRate(items, nowUtc).Percent);
            })
            .ToList();

        string Name(Guid id) => departmentNames.TryGetValue(id, out var name) ? name : "Không rõ";
        var senders = active
            .GroupBy(r => r.SendingDepartmentId)
            .Select(g => new CriticalDashboardDepartmentDto(
                g.Key, Name(g.Key), g.Count(), NotifyRate(g, nowUtc), Median(g.Select(NotifyMinutes)),
                g.Count(r => r.Status == CriticalResultStatus.AwaitingNotification)))
            .OrderByDescending(d => d.Total)
            .ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .ToList();
        var receivers = active
            .Where(r => r.ReceivingDepartmentId is not null)
            .GroupBy(r => r.ReceivingDepartmentId!.Value)
            .Select(g => new CriticalDashboardDepartmentDto(
                g.Key, Name(g.Key), g.Count(), ActionRate(g, nowUtc), Median(g.Select(ActionMinutes)),
                g.Count(r => r.Status is CriticalResultStatus.AwaitingAcknowledgement or CriticalResultStatus.AwaitingAction)))
            .OrderByDescending(d => d.Total)
            .ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .ToList();

        var notifyMinutes = active.Select(NotifyMinutes).OfType<double>().OrderBy(v => v).ToList();
        var actionMinutes = active.Select(ActionMinutes).OfType<double>().OrderBy(v => v).ToList();
        // Đọc lại chính xác lần đầu: chỉ tính lần báo đã có người nhận đọc lại (báo trên phần mềm chưa gọi xác nhận thì chưa tính).
        var notified = active.Where(r => r.CurrentNotification is { CallPending: false }).ToList();
        var completeBase = active
            .Where(r => r.Status == CriticalResultStatus.Completed || nowUtc > (r.ActionDueAtUtc ?? r.NotifyDueAtUtc))
            .ToList();
        var kpis = new CriticalDashboardKpisDto(
            active.Count,
            inPeriod.Count - active.Count,
            NotifyRate(active, nowUtc),
            ActionRate(active, nowUtc),
            new CriticalRateDto(notified.Count(r => r.CurrentNotification!.IncorrectReadBacks == 0), notified.Count),
            new CriticalRateDto(completeBase.Count(r => r.Status == CriticalResultStatus.Completed), completeBase.Count),
            Percentile(notifyMinutes, 0.5),
            Percentile(notifyMinutes, 0.25),
            Percentile(notifyMinutes, 0.75),
            Percentile(actionMinutes, 0.5),
            Percentile(actionMinutes, 0.25),
            Percentile(actionMinutes, 0.75));

        var late = active
            .Select(r => (Result: r, Miss: Miss(r, nowUtc)))
            .Where(x => x.Miss is not null)
            .OrderByDescending(x => x.Result.ResultAtUtc)
            .Take(LateLimit)
            .Select(x => new CriticalDashboardLateDto(
                x.Result.Id,
                x.Result.Number,
                x.Result.ResultAtUtc,
                CriticalResultService.Initials(x.Result.PatientName),
                x.Result.PatientCode,
                x.Result.TestName,
                x.Result.Value,
                x.Result.Unit,
                Name(x.Result.SendingDepartmentId),
                x.Result.ReceivingDepartmentId is { } d ? Name(d) : null,
                x.Miss!))
            .ToList();

        var label = quarter
            ? $"Quý {Roman((month.Month - 1) / 3 + 1)}/{month.Year}"
            : $"Tháng {month.Month:00}/{month.Year}";
        return new CriticalDashboardDto(
            quarter ? "quarter" : "month",
            month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            label,
            from,
            to,
            nowUtc,
            new CriticalDashboardTargetsDto(
                settings.TargetNotifyPercent, settings.TargetActionPercent, settings.TargetReadBackPercent, settings.TargetCompletePercent,
                settings.NotifyMinutes, settings.ActionMinutes),
            kpis,
            trend,
            senders,
            receivers,
            late);
    }

    /// <summary>Báo đúng hạn: mẫu số là hồ sơ đã báo hoặc đã quá hạn báo.</summary>
    internal static CriticalRateDto NotifyRate(IEnumerable<CriticalResult> results, DateTimeOffset nowUtc)
    {
        var due = results.Where(r => r.NotifiedAtUtc is not null || nowUtc > r.NotifyDueAtUtc).ToList();
        return new CriticalRateDto(due.Count(r => r.NotifiedOnTime == true), due.Count);
    }

    /// <summary>Bác sĩ xử trí đúng hạn: mẫu số là hồ sơ đã báo và đã xử trí hoặc đã quá hạn xử trí.</summary>
    internal static CriticalRateDto ActionRate(IEnumerable<CriticalResult> results, DateTimeOffset nowUtc)
    {
        var due = results
            .Where(r => r.ActionDueAtUtc is { } deadline && (r.ActionAtUtc is not null || nowUtc > deadline))
            .ToList();
        return new CriticalRateDto(due.Count(r => r.ActedOnTime == true), due.Count);
    }

    private static DateOnly Day(CriticalResult r) => CriticalResultService.LocalDate(r.ResultAtUtc);

    private static double? NotifyMinutes(CriticalResult r) =>
        r.NotifiedAtUtc is { } at ? (at - r.ResultAtUtc).TotalMinutes : null;

    private static double? ActionMinutes(CriticalResult r) =>
        r.ActionAtUtc is { } at && r.NotifiedAtUtc is { } notified ? (at - notified).TotalMinutes : null;

    private static double? Median(IEnumerable<double?> values) =>
        Percentile([.. values.OfType<double>().OrderBy(v => v)], 0.5);

    /// <summary>Phân vị nội suy tuyến tính trên dãy đã sắp xếp; làm tròn 1 chữ số thập phân (0,05 làm tròn lên).</summary>
    public static double? Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var position = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var value = sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
        return Math.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private static string? Miss(CriticalResult r, DateTimeOffset nowUtc)
    {
        var parts = new List<string>();
        if (r.NotifiedAtUtc is { } notified)
        {
            if (notified > r.NotifyDueAtUtc)
            {
                parts.Add($"Báo sau {Minutes(notified - r.ResultAtUtc)} phút");
            }
        }
        else if (nowUtc > r.NotifyDueAtUtc)
        {
            parts.Add($"Chưa báo, quá hạn {Minutes(nowUtc - r.NotifyDueAtUtc)} phút");
        }

        if (r.ActionDueAtUtc is { } due && r.NotifiedAtUtc is { } from)
        {
            if (r.ActionAtUtc is { } acted)
            {
                if (acted > due)
                {
                    parts.Add($"BS xử trí sau {Minutes(acted - from)} phút");
                }
            }
            else if (nowUtc > due)
            {
                parts.Add($"BS chưa xử trí, quá hạn {Minutes(nowUtc - due)} phút");
            }
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static int Minutes(TimeSpan span) => (int)Math.Ceiling(span.TotalMinutes);

    private static string Roman(int quarter) => quarter switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        _ => "IV",
    };
}

/// <summary>
/// Dashboard kết quả nguy cấp, chỉ đọc: toàn viện, hoặc một khoa (quyết định 64: hồ sơ khoa đó báo đi và hồ sơ gửi đến khoa
/// đó). QLCL, KHTH (<see cref="Permissions.CriticalMonitor"/>) xem mọi khoa; tài khoản của khoa (ghi, nhận, xử trí kết quả nguy
/// cấp) chỉ xem dashboard của các khoa mình thuộc về, không xem toàn viện (quyết định 65), trừ khi có quyền xem mọi khoa.
/// </summary>
public sealed class CriticalDashboardService(
    ICriticalResultRepository critical,
    IDepartmentRepository departments,
    ICurrentUser currentUser,
    IClock clock,
    CriticalSettingsService settings)
{
    /// <summary>Xem được bấy nhiêu tháng gần đây.</summary>
    public const int MaxMonthsBack = 24;

    public async Task<Result<CriticalDashboardDto>> GetAsync(string? month, string? period, Guid? departmentId, CancellationToken cancellationToken)
    {
        var staff = currentUser.HasPermission(Permissions.CriticalRecord)
            || currentUser.HasPermission(Permissions.CriticalReceive)
            || currentUser.HasPermission(Permissions.CriticalTreat);
        var everyDepartment = currentUser.HasPermission(Permissions.CriticalMonitor)
            || (staff && currentUser.HasPermission(Permissions.ScopeAllDepartments));
        if (!everyDepartment)
        {
            if (!staff)
            {
                return Error.Forbidden("critical.forbidden", "Không có quyền xem dashboard kết quả nguy cấp.");
            }

            // Tài khoản của khoa: chỉ dashboard của khoa mình (khoa báo hoặc khoa nhận), không có toàn viện.
            if (departmentId is not { } own || !currentUser.DepartmentIds.Contains(own))
            {
                return Error.Forbidden("critical.forbidden", "Tài khoản của khoa chỉ xem dashboard kết quả nguy cấp của khoa mình.");
            }
        }

        var now = clock.UtcNow;
        var today = CriticalResultService.LocalDate(now);
        var current = new DateOnly(today.Year, today.Month, 1);
        if (QcBookService.ParseMonth(month ?? current.ToString("yyyy-MM", CultureInfo.InvariantCulture)) is not { } first)
        {
            return Error.ValidationField("month", "Tháng dạng yyyy-MM.");
        }

        if (first > current)
        {
            return Error.ValidationField("month", "Chưa tới tháng này.");
        }

        if (first < current.AddMonths(-MaxMonthsBack))
        {
            return Error.ValidationField("month", $"Chỉ xem được {MaxMonthsBack} tháng gần đây.");
        }

        var quarter = string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase);
        if (!quarter && period is not null && !string.Equals(period, "month", StringComparison.OrdinalIgnoreCase))
        {
            return Error.ValidationField("period", "Kỳ xem là month hoặc quarter.");
        }

        var quarterEnd = new DateOnly(first.Year, ((first.Month - 1) / 3 * 3) + 1, 1).AddMonths(3);
        var start = first.AddMonths(1 - CriticalDashboardBuilder.TrendMonths);
        var end = quarter ? quarterEnd : first.AddMonths(1);
        var results = await critical.ListByResultTimeAsync(Utc(start), Utc(end), cancellationToken).ConfigureAwait(false);
        var names = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.Id, d => d.Name);
        if (departmentId is { } unknown && !names.ContainsKey(unknown))
        {
            return Error.ValidationField("departmentId", "Không có khoa/phòng này.");
        }

        // Một khoa: hồ sơ khoa đó báo đi (khoa cận lâm sàng) và hồ sơ gửi đến khoa đó (khoa điều trị).
        IReadOnlyList<CriticalResult> scoped = departmentId is { } department
            ? [.. results.Where(r => r.SendingDepartmentId == department || r.ReceivingDepartmentId == department)]
            : results;
        var values = await settings.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var dashboard = CriticalDashboardBuilder.Build(first, quarter, today, now, scoped, names, values);
        return departmentId is { } id ? dashboard with { DepartmentId = id, DepartmentName = names[id] } : dashboard;
    }

    internal static DateTimeOffset Utc(DateOnly localDate) =>
        new DateTimeOffset(localDate.ToDateTime(TimeOnly.MinValue), ConsentFormService.VietnamOffset).ToUniversalTime();
}
