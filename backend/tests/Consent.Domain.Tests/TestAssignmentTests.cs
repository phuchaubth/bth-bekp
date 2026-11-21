using Consent.Domain.Common;
using Consent.Domain.Testing;

namespace Consent.Domain.Tests;

/// <summary>Phân công kiểm thử (quyết định 49): quy tắc của thực thể. Dữ liệu giả lập.</summary>
public sealed class TestAssignmentTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tester = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();

    private static TestAssignment Assignment(DateOnly? start = null, DateOnly? due = null) =>
        TestAssignment.Create(" Gói 71 ", " Ủy quyền ", "  ", Tester, start ?? Today, due ?? Today.AddDays(2), Manager, Now);

    [Fact]
    public void Create_TrimsText_AndChecksDatesAndPeople()
    {
        var assignment = Assignment();
        Assert.Equal(("Gói 71", "Ủy quyền", (string?)null), (assignment.Version, assignment.Feature, assignment.Instructions));
        Assert.False(assignment.IsReported);

        Assert.Throws<DomainException>(() => TestAssignment.Create(" ", "F", null, Tester, Today, Today, Manager, Now));
        Assert.Throws<DomainException>(() => TestAssignment.Create("V", "", null, Tester, Today, Today, Manager, Now));
        Assert.Throws<DomainException>(() => TestAssignment.Create("V", "F", null, Guid.Empty, Today, Today, Manager, Now));
        Assert.Throws<DomainException>(() => Assignment(Today, Today.AddDays(-1)));
        Assert.Throws<DomainException>(() => Assignment(Today, Today.AddDays(366)));
        Assert.Throws<DomainException>(() => TestAssignment.Create(new string('v', 101), "F", null, Tester, Today, Today, Manager, Now));
        Assert.Equal(Today.AddDays(365), Assignment(Today, Today.AddDays(365)).DueDate);
    }

    [Fact]
    public void Report_ByTheTester_FromTheStartDate_WithFindingsWhenNotPassed()
    {
        var assignment = Assignment(Today.AddDays(1), Today.AddDays(2));

        Assert.Throws<DomainException>(() => assignment.Report(Tester, TestResult.Passed, null, null, Today, Now));
        Assert.Throws<DomainException>(() => assignment.Report(Manager, TestResult.Passed, null, null, Today.AddDays(1), Now));
        Assert.Throws<DomainException>(() => assignment.Report(Tester, TestResult.Failed, " ", null, Today.AddDays(1), Now));
        Assert.Throws<DomainException>(() => assignment.Report(Tester, TestResult.PartiallyPassed, null, null, Today.AddDays(1), Now));
        Assert.Throws<DomainException>(() => assignment.Report(Tester, (TestResult)7, "x", null, Today.AddDays(1), Now));

        assignment.Report(Tester, TestResult.PartiallyPassed, " Lỗi A ", " Đề xuất B ", Today.AddDays(3), Now);
        Assert.Equal((TestResult.PartiallyPassed, "Lỗi A", "Đề xuất B", true), (assignment.Result, assignment.Findings, assignment.Suggestions, assignment.IsLate));
        Assert.Throws<DomainException>(() => assignment.Update("V", "F", null, Today, Today));
        Assert.Throws<DomainException>(() => assignment.Cancel(Manager, Now));

        assignment.Report(Tester, TestResult.Passed, null, null, Today.AddDays(2), Now);
        Assert.False(assignment.IsLate);
    }

    [Fact]
    public void Review_NeedsAReport_AndLocksIt()
    {
        var assignment = Assignment();
        Assert.Throws<DomainException>(() => assignment.Review(Manager, "x", Now));

        assignment.Report(Tester, TestResult.Passed, null, null, Today, Now);
        Assert.Throws<DomainException>(() => assignment.Review(Manager, " ", Now));
        Assert.Throws<DomainException>(() => assignment.Review(Manager, new string('x', 2001), Now));
        assignment.Review(Manager, " Đã chuyển nhóm phát triển ", Now);
        Assert.Equal(("Đã chuyển nhóm phát triển", (Guid?)Manager), (assignment.ReviewNote, assignment.ReviewedByUserId));
        Assert.Throws<DomainException>(() => assignment.Report(Tester, TestResult.Passed, null, null, Today, Now));
    }

    [Fact]
    public void Cancel_BlocksReportsAndUpdates()
    {
        var assignment = Assignment();
        assignment.Cancel(Manager, Now);

        Assert.True(assignment.IsCancelled);
        Assert.Throws<DomainException>(() => assignment.Cancel(Manager, Now));
        Assert.Throws<DomainException>(() => assignment.Update("V", "F", null, Today, Today));
        Assert.Throws<DomainException>(() => assignment.Report(Tester, TestResult.Passed, null, null, Today, Now));
    }
}
