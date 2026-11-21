using Consent.Domain.Common;
using Consent.Domain.NursingReports;

namespace Consent.Domain.Tests;

/// <summary>Ủy quyền báo cáo điều dưỡng khi người báo cáo vắng (quyết định 48). Dữ liệu giả lập.</summary>
public sealed class NursingReportDelegationTests
{
    private static readonly Guid Department = Guid.NewGuid();
    private static readonly Guid HeadNurse = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 11, 21);
    private static readonly DateOnly End = new(2026, 11, 23);

    private static NursingReportDelegation Delegation(
        NursingDelegationScope scope = NursingDelegationScope.Report, string? reason = null, DateOnly? start = null, DateOnly? end = null) =>
        NursingReportDelegation.Create(Department, HeadNurse, Colleague, start ?? Start, end ?? End, scope, reason, Now);

    [Fact]
    public void Create_KeepsTheChoices_AndTrimsTheReason()
    {
        var delegation = Delegation(NursingDelegationScope.Report | NursingDelegationScope.HisEntry, "  Nghỉ phép (giả lập)  ");

        Assert.Equal(Department, delegation.DepartmentId);
        Assert.Equal(HeadNurse, delegation.DelegatorUserId);
        Assert.Equal(Colleague, delegation.DelegateUserId);
        Assert.Equal(Start, delegation.StartDate);
        Assert.Equal(End, delegation.EndDate);
        Assert.Equal(NursingDelegationScope.Report | NursingDelegationScope.HisEntry, delegation.Scope);
        Assert.Equal("Nghỉ phép (giả lập)", delegation.Reason);
        Assert.Equal(Now, delegation.CreatedAtUtc);
        Assert.False(delegation.IsRevoked);
        Assert.Null(Delegation(reason: "   ").Reason);
    }

    [Fact]
    public void Covers_OnlyTheChosenDays_AndContents()
    {
        var reportOnly = Delegation();

        Assert.False(reportOnly.Covers(Start.AddDays(-1), NursingDelegationScope.Report));
        Assert.True(reportOnly.Covers(Start, NursingDelegationScope.Report));
        Assert.True(reportOnly.Covers(End, NursingDelegationScope.Report));
        Assert.False(reportOnly.Covers(End.AddDays(1), NursingDelegationScope.Report));
        Assert.False(reportOnly.Covers(Start, NursingDelegationScope.HisEntry));
        Assert.False(reportOnly.Covers(Start, NursingDelegationScope.None));

        var both = Delegation(NursingDelegationScope.Report | NursingDelegationScope.HisEntry);
        Assert.True(both.Covers(Start, NursingDelegationScope.HisEntry));
        Assert.True(both.Covers(Start, NursingDelegationScope.Report | NursingDelegationScope.HisEntry));
    }

    [Fact]
    public void Revoke_EndsTheDelegation_Once()
    {
        var delegation = Delegation();
        var later = Now.AddHours(3);

        delegation.Revoke(HeadNurse, later);

        Assert.True(delegation.IsRevoked);
        Assert.Equal(HeadNurse, delegation.RevokedByUserId);
        Assert.Equal(later, delegation.RevokedAtUtc);
        Assert.False(delegation.Covers(Start, NursingDelegationScope.Report));
        Assert.Throws<DomainException>(() => delegation.Revoke(HeadNurse, later));
        Assert.Throws<DomainException>(() => Delegation().Revoke(Guid.Empty, later));
    }

    [Fact]
    public void Create_RejectsSelfDelegation_WrongDates_NoContent_AndLongReasons()
    {
        Assert.Throws<DomainException>(() =>
            NursingReportDelegation.Create(Department, HeadNurse, HeadNurse, Start, End, NursingDelegationScope.Report, null, Now));
        Assert.Throws<DomainException>(() =>
            NursingReportDelegation.Create(Guid.Empty, HeadNurse, Colleague, Start, End, NursingDelegationScope.Report, null, Now));
        Assert.Throws<DomainException>(() =>
            NursingReportDelegation.Create(Department, Guid.Empty, Colleague, Start, End, NursingDelegationScope.Report, null, Now));
        Assert.Throws<DomainException>(() => Delegation(start: End, end: Start));
        Assert.Throws<DomainException>(() => Delegation(start: Start, end: Start.AddDays(NursingReportDelegation.MaxDays)));
        Assert.Equal(Start.AddDays(NursingReportDelegation.MaxDays - 1), Delegation(end: Start.AddDays(NursingReportDelegation.MaxDays - 1)).EndDate);
        Assert.Throws<DomainException>(() => Delegation(NursingDelegationScope.None));
        Assert.Throws<DomainException>(() => Delegation((NursingDelegationScope)4));
        Assert.Throws<DomainException>(() => Delegation(reason: new string('x', NursingReportDelegation.ReasonMaxLength + 1)));
        Assert.Equal(NursingReportDelegation.ReasonMaxLength, Delegation(reason: new string('x', NursingReportDelegation.ReasonMaxLength)).Reason!.Length);
    }

    [Fact]
    public void Reports_AndHisEntries_RememberTheDelegationUsed()
    {
        var delegation = Delegation(NursingDelegationScope.Report | NursingDelegationScope.HisEntry);
        var report = NursingDailyReport.Create(Department, Start, """{"values":{"nurses":18}}""", null, Colleague, Now);

        report.Submit(Colleague, Now, delegation.Id);
        Assert.Equal(delegation.Id, report.SubmittedDelegationId);

        // Bị trả lại, người của khoa tự gửi lại: không còn ghi ủy quyền.
        report.Reopen("Sai số (giả lập)", Guid.NewGuid(), Now);
        report.Submit(HeadNurse, Now);
        Assert.Null(report.SubmittedDelegationId);

        var entry = NursingDepartmentStatistics.Create(Department, Start, """{"waiting":{}}""", Colleague, Now, delegation.Id);
        Assert.Equal(delegation.Id, entry.DelegationId);
        entry.Set("""{"waiting":{}}""", HeadNurse, Now);
        Assert.Null(entry.DelegationId);
    }
}
