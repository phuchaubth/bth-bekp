using Consent.Domain.Common;
using Consent.Domain.NursingReports;

namespace Consent.Domain.Tests;

/// <summary>Báo cáo điều dưỡng hằng ngày (quyết định 31): nháp → gửi (khóa) → Phòng Điều dưỡng mở lại.</summary>
public sealed class NursingReportTests
{
    private static readonly Guid Department = Guid.NewGuid();
    private static readonly Guid Nurse = Guid.NewGuid();
    private static readonly Guid NursingOffice = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 16);

    [Fact]
    public void NewReport_IsADraft_WithTrimmedNotes()
    {
        var report = NursingDailyReport.Create(Department, Day, """{"values":{"nurses":18}}""", "  Không có bất thường (giả lập)  ", Nurse, Now);

        Assert.Equal(NursingReportStatus.Draft, report.Status);
        Assert.False(report.IsSubmitted);
        Assert.Equal("Không có bất thường (giả lập)", report.Notes);
        Assert.Equal(Nurse, report.CreatedByUserId);
        Assert.Equal(Nurse, report.UpdatedByUserId);
        Assert.Null(report.SubmittedAtUtc);
    }

    [Fact]
    public void BlankNotes_AreStoredAsNull()
    {
        var report = NursingDailyReport.Create(Department, Day, "{}", "   ", Nurse, Now);

        Assert.Null(report.Notes);
    }

    [Fact]
    public void SubmittedReport_IsLocked_UntilTheNursingOfficeReopensItWithAReason()
    {
        var report = NursingDailyReport.Create(Department, Day, "{}", null, Nurse, Now);
        report.Submit(Nurse, Now.AddMinutes(5));

        Assert.True(report.IsSubmitted);
        Assert.Equal(Now.AddMinutes(5), report.SubmittedAtUtc);
        Assert.Throws<DomainException>(() => report.Update("{}", "sửa sau khi gửi", Nurse, Now.AddMinutes(6)));
        Assert.Throws<DomainException>(() => report.Submit(Nurse, Now.AddMinutes(6)));
        Assert.Throws<DomainException>(() => report.Reopen("  ", NursingOffice, Now.AddMinutes(7)));
        Assert.Throws<DomainException>(() => report.Reopen(new string('x', NursingDailyReport.ReopenReasonMaxLength + 1), NursingOffice, Now));

        report.Reopen(" Sai số NB hiện tại ", NursingOffice, Now.AddMinutes(8));

        Assert.Equal(NursingReportStatus.Draft, report.Status);
        Assert.Equal("Sai số NB hiện tại", report.ReopenReason);
        Assert.Equal(NursingOffice, report.ReopenedByUserId);
        report.Update("""{"values":{"nurses":17}}""", null, Nurse, Now.AddMinutes(9));
        report.Submit(Nurse, Now.AddMinutes(10));
        Assert.Equal(Now.AddMinutes(10), report.SubmittedAtUtc);
    }

    [Fact]
    public void Draft_CannotBeReopened()
    {
        var report = NursingDailyReport.Create(Department, Day, "{}", null, Nurse, Now);

        Assert.Throws<DomainException>(() => report.Reopen("lý do", NursingOffice, Now));
    }

    [Fact]
    public void InvalidContent_IsRejected()
    {
        Assert.Throws<DomainException>(() => NursingDailyReport.Create(Guid.Empty, Day, "{}", null, Nurse, Now));
        Assert.Throws<DomainException>(() => NursingDailyReport.Create(Department, Day, " ", null, Nurse, Now));
        Assert.Throws<DomainException>(() => NursingDailyReport.Create(
            Department, Day, "{}", new string('x', NursingDailyReport.NotesMaxLength + 1), Nurse, Now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(NursingReportDepartment.MaxBedCount + 1)]
    public void BedCount_MustBeInRange(int beds)
    {
        Assert.Throws<DomainException>(() => NursingReportDepartment.Create(Department, beds, Now));
        var department = NursingReportDepartment.Create(Department, 48, Now);
        Assert.Throws<DomainException>(() => department.SetBedCount(beds, Now));
        Assert.Equal(48, department.BedCount);
    }

    [Fact]
    public void Statistics_KeepTheirSourceAndWhoUpdatedThem()
    {
        var statistics = NursingDailyStatistics.Create(Day, """{"hospital":{"outpatientVisits":12}}""", NursingStatisticsSource.His, NursingOffice, Now);
        statistics.Set("""{"hospital":{"outpatientVisits":15}}""", NursingStatisticsSource.Manual, Nurse, Now.AddHours(1));

        Assert.Equal(NursingStatisticsSource.Manual, statistics.Source);
        Assert.Equal(Nurse, statistics.UpdatedByUserId);
        Assert.Equal(Now.AddHours(1), statistics.UpdatedAtUtc);
        Assert.Throws<DomainException>(() => statistics.Set(" ", NursingStatisticsSource.His, Nurse, Now));
    }
}
