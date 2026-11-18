using BEKP.Application.TimeStandards;
using BEKP.Domain.Beds;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Tests;

public sealed class TimeStandardsServiceTests
{
    [Fact]
    public async Task Report_ComputesComplianceMedianAndP90()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 2);
        foreach (var minutes in new[] { 10, 20, 25, 28, 29, 31, 40, 45, 50, 90 })
        {
            host.Repository.Timings.Add(new ProcessTiming(Guid.NewGuid(), TimeStandardType.TransfusionStartAfterIssue, department.Id, TestHost.Now.AddMinutes(-minutes - 5), TestHost.Now.AddMinutes(-5), null));
        }

        var report = await host.TimeStandards.GetReportAsync(ReportPeriod.Today, null, CancellationToken.None);
        var stat = report.Standards.Single(s => s.Standard == TimeStandardType.TransfusionStartAfterIssue);

        Assert.Equal(10, stat.Completed);
        Assert.Equal(5, stat.WithinTarget);
        Assert.Equal(50.0m, stat.CompliancePercent);
        Assert.Equal(29, stat.MedianMinutes);
        Assert.Equal(50, stat.P90Minutes);
        Assert.False(stat.ReferenceConfirmed);
        Assert.Equal(Enum.GetValues<TimeStandardType>().Length, report.Standards.Count);
    }

    [Fact]
    public async Task Report_ExcludesTimingsOutsidePeriod_AndCountsOverdueInProgressItems()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 2);
        host.Repository.Timings.Add(new ProcessTiming(Guid.NewGuid(), TimeStandardType.BedTurnaround, department.Id, TestHost.Now.AddDays(-3).AddMinutes(-30), TestHost.Now.AddDays(-3), null));
        host.Repository.Beds.First(b => !b.IsExtra).SetHousekeepingStatus(BedStatus.Cleaning, TestHost.Now.AddMinutes(-75));
        var stay = host.Ward.Admit(department);
        var request = new BloodRequest(Guid.NewGuid(), stay.Id, department.Id, BloodProductType.RedCells, AboGroup.O, true, 2, BloodRequestUrgency.Urgent, BloodProductModifiers.None, TestHost.Now.AddMinutes(-90));
        host.Repository.Requests.Add(request);

        var today = await host.TimeStandards.GetReportAsync(ReportPeriod.Today, "DEP-0001", CancellationToken.None);
        var week = await host.TimeStandards.GetReportAsync(ReportPeriod.Last7Days, "DEP-0001", CancellationToken.None);

        var bedToday = today.Standards.Single(s => s.Standard == TimeStandardType.BedTurnaround);
        Assert.Equal(0, bedToday.Completed);
        Assert.Null(bedToday.CompliancePercent);
        Assert.Equal(1, bedToday.InProgressOverdue);
        Assert.Equal(1, week.Standards.Single(s => s.Standard == TimeStandardType.BedTurnaround).Completed);
        Assert.Equal(1, today.Standards.Single(s => s.Standard == TimeStandardType.BloodIssueUrgent).InProgressOverdue);
    }

    [Fact]
    public void Catalog_UsesConfiguredTargetAndReference()
    {
        var host = new TestHost(new Dictionary<TimeStandardType, TimeStandardSetting>
        {
            [TimeStandardType.BloodIssueEmergency] = new() { TargetMinutes = 20, Reference = "Quy định nội bộ QĐ-01", ReferenceConfirmed = true },
        });

        var definition = host.Catalog.Get(TimeStandardType.BloodIssueEmergency);
        Assert.Equal(20, definition.TargetMinutes);
        Assert.True(definition.ReferenceConfirmed);
        Assert.False(host.Catalog.Get(TimeStandardType.BedTurnaround).ReferenceConfirmed);
    }

    [Theory]
    [InlineData(new int[0], 50, null)]
    [InlineData(new[] { 7 }, 90, 7)]
    [InlineData(new[] { 1, 2, 3, 4 }, 50, 2)]
    public void Percentile_NearestRank(int[] values, int percentile, int? expected) =>
        Assert.Equal(expected, TimeStandardsService.Percentile(values, percentile));
}
