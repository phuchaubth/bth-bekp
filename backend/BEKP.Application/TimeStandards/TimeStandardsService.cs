using BEKP.Application.Abstractions;
using BEKP.Application.Common;
using BEKP.Domain.Beds;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.TimeStandards;

public enum ReportPeriod
{
    Today,
    Last7Days,
    Last30Days,
}

public sealed record TimeStandardStatDto(
    TimeStandardType Standard,
    int TargetMinutes,
    string Reference,
    bool ReferenceConfirmed,
    int Completed,
    int WithinTarget,
    decimal? CompliancePercent,
    int? MedianMinutes,
    int? P90Minutes,
    int InProgress,
    int InProgressOverdue);

public sealed record TimeStandardsReportDto(
    DateTime GeneratedAtUtc,
    DateTime FromUtc,
    ReportPeriod Period,
    string? DepartmentCode,
    IReadOnlyList<TimeStandardStatDto> Standards);

public interface ITimeStandardsService
{
    Task<TimeStandardsReportDto> GetReportAsync(ReportPeriod period, string? departmentCode, CancellationToken cancellationToken);
}

/// <summary>Compliance statistics of regulated process times (thời gian thực hiện theo quy định).</summary>
internal sealed class TimeStandardsService(
    IInpatientRepository repository,
    IClock clock,
    HospitalCalendar calendar,
    TimeStandardCatalog catalog) : ITimeStandardsService
{
    public async Task<TimeStandardsReportDto> GetReportAsync(ReportPeriod period, string? departmentCode, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        Guid? departmentId = null;
        if (departmentCode is not null)
        {
            var department = await repository.GetDepartmentByCodeAsync(departmentCode, cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("Department", departmentCode);
            departmentId = department.Id;
        }

        var dayStart = calendar.LocalDayStartUtc(now);
        var from = period switch
        {
            ReportPeriod.Today => dayStart,
            ReportPeriod.Last7Days => dayStart.AddDays(-6),
            _ => dayStart.AddDays(-29),
        };

        var timings = await repository.GetProcessTimingsAsync(from, departmentId, cancellationToken).ConfigureAwait(false);
        var open = await CollectInProgressAsync(departmentId, now, cancellationToken).ConfigureAwait(false);

        var stats = catalog.All.Select(definition =>
        {
            var durations = timings.Where(t => t.Standard == definition.Type).Select(t => t.DurationMinutes).OrderBy(m => m).ToList();
            var within = durations.Count(m => m <= definition.TargetMinutes);
            var inProgress = open.Where(o => o.Standard == definition.Type).ToList();
            return new TimeStandardStatDto(
                definition.Type,
                definition.TargetMinutes,
                definition.Reference,
                definition.ReferenceConfirmed,
                durations.Count,
                within,
                durations.Count == 0 ? null : Math.Round(within * 100m / durations.Count, 1, MidpointRounding.AwayFromZero),
                Percentile(durations, 50),
                Percentile(durations, 90),
                inProgress.Count,
                inProgress.Count(o => o.ElapsedMinutes > definition.TargetMinutes));
        }).ToList();

        return new TimeStandardsReportDto(now, from, period, departmentCode, stats);
    }

    /// <summary>Nearest-rank percentile of an ascending list.</summary>
    internal static int? Percentile(IReadOnlyList<int> sortedAscending, int percentile)
    {
        if (sortedAscending.Count == 0)
        {
            return null;
        }

        var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count);
        return sortedAscending[Math.Clamp(rank, 1, sortedAscending.Count) - 1];
    }

    private static int Minutes(DateTime from, DateTime to) => Math.Max(0, (int)Math.Floor((to - from).TotalMinutes));

    private async Task<List<(TimeStandardType Standard, int ElapsedMinutes)>> CollectInProgressAsync(Guid? departmentId, DateTime now, CancellationToken cancellationToken)
    {
        var open = new List<(TimeStandardType, int)>();

        var requests = await repository.GetPendingBloodRequestsAsync(departmentId, cancellationToken).ConfigureAwait(false);
        foreach (var request in requests)
        {
            if (request.Status == BloodRequestStatus.Issued && request.IssuedAtUtc is { } issued)
            {
                open.Add((TimeStandardType.TransfusionStartAfterIssue, Minutes(issued, now)));
            }
            else if (request.Status != BloodRequestStatus.Issued)
            {
                open.Add((IssueStandardFor(request.Urgency), Minutes(request.RequestedAtUtc, now)));
            }
        }

        var episodes = await repository.GetActiveEpisodesAsync(departmentId, now, cancellationToken).ConfigureAwait(false);
        open.AddRange(episodes.Where(e => e.AwaitingAntibiotic).Select(e => (TimeStandardType.FebrileNeutropeniaAntibiotic, e.ElapsedMinutes(now))));

        var beds = await repository.GetBedsAsync(departmentId, cancellationToken).ConfigureAwait(false);
        open.AddRange(beds.Where(b => b.Status == BedStatus.Cleaning).Select(b => (TimeStandardType.BedTurnaround, Minutes(b.StatusChangedAtUtc, now))));

        return open;
    }

    internal static TimeStandardType IssueStandardFor(BloodRequestUrgency urgency) => urgency switch
    {
        BloodRequestUrgency.Emergency => TimeStandardType.BloodIssueEmergency,
        BloodRequestUrgency.Urgent => TimeStandardType.BloodIssueUrgent,
        _ => TimeStandardType.BloodIssueRoutine,
    };
}
