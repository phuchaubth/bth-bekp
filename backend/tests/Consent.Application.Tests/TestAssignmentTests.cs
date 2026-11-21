using Consent.Application.Testing;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Testing;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Phân công kiểm thử tính năng mới (quyết định 49). Dữ liệu giả lập.</summary>
public sealed class TestAssignmentTests
{
    /// <summary>Ngày hôm nay theo giờ Việt Nam của đồng hồ giả lập (08:00 UTC ngày 11/11/2026).</summary>
    private static readonly DateOnly Today = new(2026, 11, 11);

    private const string Version = "Gói 71 – 21/11/2026 (giả lập)";

    private readonly AppTestContext _ctx = new();
    private readonly Department _ward;
    private readonly User _manager;
    private readonly User _nurse;
    private readonly User _doctor;
    private readonly User _kiosk;
    private readonly User _inactive;

    public TestAssignmentTests()
    {
        _ward = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _manager = _ctx.AddUser("quantri", SystemRoles.Administrator);
        _nurse = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _ward.Id);
        _doctor = _ctx.AddUser("bacsi.noia", SystemRoles.Doctor, departmentId: _ward.Id);
        _kiosk = _ctx.AddUser("kiosk.sanh", SystemRoles.Kiosk);
        _inactive = _ctx.AddUser("dieuduong.nghi", SystemRoles.Nurse, active: false, departmentId: _ward.Id);
    }

    private TestAssignmentService Service => _ctx.CreateTestAssignmentService();

    private void ActAs(User user)
    {
        _ctx.CurrentUser.UserId = user.Id;
        _ctx.CurrentUser.UserName = user.UserName;
        _ctx.CurrentUser.DepartmentId = user.DepartmentId;
        _ctx.CurrentUser.Permissions.Clear();
        foreach (var permission in _ctx.Store.Roles.Where(r => user.RoleIds.Contains(r.Id)).SelectMany(r => r.Permissions).Select(p => p.PermissionCode))
        {
            _ctx.CurrentUser.Permissions.Add(permission);
        }
    }

    private static CreateTestAssignmentsRequest Request(params TestAssigneeRequest[] assignees) =>
        new(Version, "Ủy quyền báo cáo điều dưỡng", "Ủy quyền cho người khác, đăng nhập người được ủy quyền, gửi báo cáo.", assignees);

    private async Task<IReadOnlyList<TestAssignmentDto>> AssignAsync(params TestAssigneeRequest[] assignees)
    {
        ActAs(_manager);
        var result = await Service.CreateAsync(Request(assignees), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public async Task Create_AssignsEachPerson_TheirOwnDates()
    {
        var created = await AssignAsync(
            new TestAssigneeRequest(_nurse.Id, Today, Today.AddDays(2)),
            new TestAssigneeRequest(_doctor.Id, Today.AddDays(3), Today.AddDays(5)));

        Assert.Equal(2, created.Count);
        var nurse = created.Single(a => a.AssigneeUserId == _nurse.Id);
        Assert.Equal((Today, Today.AddDays(2), TestAssignmentStatus.InProgress), (nurse.StartDate, nurse.DueDate, nurse.Status));
        Assert.Equal("dieuduong.noia Demo", nurse.AssigneeName);
        Assert.Equal("Khoa Nội A (giả lập)", nurse.AssigneeDepartment);
        Assert.Equal("quantri Demo", nurse.CreatedBy);
        Assert.True(nurse.CanEdit);
        Assert.False(nurse.CanReport);
        var doctor = created.Single(a => a.AssigneeUserId == _doctor.Id);
        Assert.Equal(TestAssignmentStatus.Upcoming, doctor.Status);
        Assert.Equal(2, _ctx.Store.TestAssignments.Count);
    }

    [Fact]
    public async Task Create_ChecksTheFields_TheTesters_AndTheDates()
    {
        ActAs(_manager);
        async Task<IReadOnlyDictionary<string, string[]>> Errors(CreateTestAssignmentsRequest request)
        {
            var result = await Service.CreateAsync(request, CancellationToken.None);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
            return result.Error.FieldErrors!;
        }

        var empty = await Errors(new CreateTestAssignmentsRequest(" ", null, null, []));
        Assert.Equal(["assignees", "feature", "version"], empty.Keys.Order(StringComparer.Ordinal));
        Assert.True((await Errors(Request(new TestAssigneeRequest(_kiosk.Id, Today, Today)))).ContainsKey("assignees[0].userId"));
        Assert.True((await Errors(Request(new TestAssigneeRequest(_inactive.Id, Today, Today)))).ContainsKey("assignees[0].userId"));
        Assert.Equal(
            ["Mỗi người chỉ phân công một lần cho tính năng này."],
            (await Errors(Request(new TestAssigneeRequest(_nurse.Id, Today, Today), new TestAssigneeRequest(_nurse.Id, Today, Today))))["assignees[1].userId"]);
        Assert.Equal(["Hạn báo cáo phải sau hoặc bằng ngày bắt đầu."], (await Errors(Request(new TestAssigneeRequest(_nurse.Id, Today, Today.AddDays(-1)))))["assignees[0].dueDate"]);
        Assert.Equal(["Hạn báo cáo từ hôm nay trở đi."], (await Errors(Request(new TestAssigneeRequest(_nurse.Id, Today.AddDays(-3), Today.AddDays(-1)))))["assignees[0].dueDate"]);
        Assert.Equal(["Phân công tối đa 366 ngày."], (await Errors(Request(new TestAssigneeRequest(_nurse.Id, Today, Today.AddDays(366)))))["assignees[0].dueDate"]);
        Assert.True((await Errors(Request(new TestAssigneeRequest(_nurse.Id, Today, Today)) with { Feature = new string('x', 201) })).ContainsKey("feature"));
        Assert.True((await Errors(Request([.. Enumerable.Range(0, 51).Select(_ => new TestAssigneeRequest(_nurse.Id, Today, Today))]))).ContainsKey("assignees"));
        Assert.Empty(_ctx.Store.TestAssignments);

        // Bắt đầu từ hôm qua (đợt kiểm thử đang chạy) và đủ 366 ngày được nhận; quản trị tự phân công cho mình được.
        Assert.True((await Service.CreateAsync(Request(new TestAssigneeRequest(_nurse.Id, Today.AddDays(-1), Today)), CancellationToken.None)).IsSuccess);
        Assert.True((await Service.CreateAsync(Request(new TestAssigneeRequest(_manager.Id, Today, Today.AddDays(365))), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task OnlyTestingManagers_AssignListExportAndReview()
    {
        var assignment = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];

        ActAs(_nurse);
        Assert.Equal(ErrorType.Forbidden, (await Service.ListAsync(null, CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await Service.CandidatesAsync(CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await Service.CreateAsync(Request(new TestAssigneeRequest(_doctor.Id, Today, Today)), CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await Service.UpdateAsync(assignment.Id, new UpdateTestAssignmentRequest("v", "f", null, Today, Today), CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await Service.CancelAsync(assignment.Id, CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await Service.ReviewAsync(assignment.Id, new ReviewTestReportRequest("x"), CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await Service.ExportAsync(null, CancellationToken.None)).Error!.Type);
        var mine = Assert.Single((await Service.MineAsync(CancellationToken.None)).Value);
        Assert.False(mine.CanEdit);
        // Hôm nay đúng ngày hạn: vẫn đang kiểm thử, chưa quá hạn.
        Assert.Equal(TestAssignmentStatus.InProgress, mine.Status);
        _ctx.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(TestAssignmentStatus.Overdue, Assert.Single((await Service.MineAsync(CancellationToken.None)).Value).Status);
    }

    [Fact]
    public async Task Candidates_AreActiveAccountsWithARole_ExceptKiosk()
    {
        ActAs(_manager);
        var candidates = (await Service.CandidatesAsync(CancellationToken.None)).Value;

        Assert.Equal(["bacsi.noia Demo", "dieuduong.noia Demo", "quantri Demo"], candidates.Select(c => c.FullName));
        Assert.Equal("Khoa Nội A (giả lập)", candidates[0].DepartmentName);
        Assert.Null(candidates[2].DepartmentName);
    }

    [Fact]
    public async Task Tester_SeesOnlyOwnWork_AndReportsFromTheStartDate()
    {
        var upcoming = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today.AddDays(1), Today.AddDays(3))))[0];
        await AssignAsync(new TestAssigneeRequest(_doctor.Id, Today, Today));

        ActAs(_nurse);
        var mine = Assert.Single((await Service.MineAsync(CancellationToken.None)).Value);
        Assert.Equal((upcoming.Id, TestAssignmentStatus.Upcoming, false), (mine.Id, mine.Status, mine.CanReport));
        var early = await Service.ReportAsync(upcoming.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None);
        Assert.Equal("testing.not_started", early.Error!.Code);
        Assert.Contains("12/11/2026", early.Error.Message, StringComparison.Ordinal);

        // Người khác không thấy, không báo cáo được việc của người khác.
        var doctorWork = _ctx.Store.TestAssignments.Single(a => a.AssigneeUserId == _doctor.Id);
        Assert.Equal(ErrorType.NotFound, (await Service.ReportAsync(doctorWork.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None)).Error!.Type);

        _ctx.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True(Assert.Single((await Service.MineAsync(CancellationToken.None)).Value).CanReport);
    }

    [Fact]
    public async Task Report_RequiresAResult_AndFindingsWhenNotPassed_AndIsLateAfterTheDueDate()
    {
        var assignment = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today.AddDays(1))))[0];
        ActAs(_nurse);

        Assert.True((await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(null, null, null), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("result"));
        Assert.True((await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest((TestResult)9, null, null), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("result"));
        Assert.True((await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.Failed, " ", null), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("findings"));
        Assert.True((await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.PartiallyPassed, null, null), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("findings"));
        Assert.True((await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.Passed, null, new string('x', 4001)), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("suggestions"));

        // Quá hạn mới gửi: vẫn nhận, ghi trễ hạn.
        _ctx.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(TestAssignmentStatus.Overdue, Assert.Single((await Service.MineAsync(CancellationToken.None)).Value).Status);
        var report = await Service.ReportAsync(
            assignment.Id,
            new SubmitTestReportRequest(TestResult.Failed, "  Người được ủy quyền không thấy khoa trong ô chọn khoa.  ", "Thêm nhắc việc trên trang Tổng quan."),
            CancellationToken.None);
        Assert.True(report.IsSuccess, report.Error?.Message);
        Assert.Equal((TestAssignmentStatus.Reported, TestResult.Failed, true), (report.Value.Status, report.Value.Result, report.Value.Late));
        Assert.Equal("Người được ủy quyền không thấy khoa trong ô chọn khoa.", report.Value.Findings);
        Assert.Equal("Thêm nhắc việc trên trang Tổng quan.", report.Value.Suggestions);
        Assert.True(report.Value.CanReport);

        // Sửa báo cáo trước khi có hướng xử lý.
        var updated = await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None);
        Assert.Equal((TestResult.Passed, (string?)null), (updated.Value.Result, updated.Value.Findings));
    }

    [Fact]
    public async Task Mine_ListsOpenWorkFirst_ByDueDate_ThenReported_ThenCancelled()
    {
        var reported = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];
        var cancelled = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];
        var later = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today.AddDays(5))))[0];
        var sooner = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today.AddDays(1))))[0];
        Assert.True((await Service.CancelAsync(cancelled.Id, CancellationToken.None)).IsSuccess);
        ActAs(_nurse);
        Assert.True((await Service.ReportAsync(reported.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None)).IsSuccess);

        var mine = (await Service.MineAsync(CancellationToken.None)).Value;

        Assert.Equal([sooner.Id, later.Id, reported.Id, cancelled.Id], mine.Select(a => a.Id));
    }

    [Fact]
    public async Task OnTimeReport_IsNotLate()
    {
        var assignment = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];
        ActAs(_nurse);

        var report = await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None);

        Assert.False(report.Value.Late);
        Assert.Equal(Today, _ctx.Store.TestAssignments.Single().ReportedOn);
    }

    [Fact]
    public async Task Review_LocksTheReport_AndNeedsAReport()
    {
        var assignment = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];
        ActAs(_manager);
        Assert.Equal("testing.not_reported", (await Service.ReviewAsync(assignment.Id, new ReviewTestReportRequest("x"), CancellationToken.None)).Error!.Code);

        ActAs(_nurse);
        await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.PartiallyPassed, "Chậm khi mở trang (giả lập).", null), CancellationToken.None);

        ActAs(_manager);
        Assert.True((await Service.ReviewAsync(assignment.Id, new ReviewTestReportRequest(" "), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("note"));
        Assert.True((await Service.ReviewAsync(assignment.Id, new ReviewTestReportRequest(new string('x', 2001)), CancellationToken.None)).Error!.FieldErrors!.ContainsKey("note"));
        var reviewed = await Service.ReviewAsync(assignment.Id, new ReviewTestReportRequest(" Sửa trong gói sau. "), CancellationToken.None);
        Assert.True(reviewed.IsSuccess, reviewed.Error?.Message);
        Assert.Equal(("Sửa trong gói sau.", "quantri Demo"), (reviewed.Value.ReviewNote, reviewed.Value.ReviewedBy));
        Assert.False(reviewed.Value.CanEdit);

        ActAs(_nurse);
        var mine = Assert.Single((await Service.MineAsync(CancellationToken.None)).Value);
        Assert.False(mine.CanReport);
        Assert.Equal("Sửa trong gói sau.", mine.ReviewNote);
        Assert.Equal("testing.reviewed", (await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task UpdateAndCancel_OnlyBeforeTheReport()
    {
        var assignment = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];
        ActAs(_manager);

        var badDates = await Service.UpdateAsync(assignment.Id, new UpdateTestAssignmentRequest(Version, "Dashboard", null, Today.AddDays(2), Today.AddDays(1)), CancellationToken.None);
        Assert.True(badDates.Error!.FieldErrors!.ContainsKey("dueDate"));
        var updated = await Service.UpdateAsync(assignment.Id, new UpdateTestAssignmentRequest(" Gói 72 ", " Dashboard ", " Xem 90 ngày ", Today, Today.AddDays(4)), CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);
        Assert.Equal(("Gói 72", "Dashboard", "Xem 90 ngày", Today.AddDays(4)), (updated.Value.Version, updated.Value.Feature, updated.Value.Instructions, updated.Value.DueDate));
        Assert.Equal(ErrorType.NotFound, (await Service.UpdateAsync(Guid.NewGuid(), new UpdateTestAssignmentRequest("v", "f", null, Today, Today), CancellationToken.None)).Error!.Type);

        ActAs(_nurse);
        await Service.ReportAsync(assignment.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None);
        ActAs(_manager);
        Assert.Equal("testing.reported", (await Service.UpdateAsync(assignment.Id, new UpdateTestAssignmentRequest("v", "f", null, Today, Today), CancellationToken.None)).Error!.Code);
        Assert.Equal("testing.reported", (await Service.CancelAsync(assignment.Id, CancellationToken.None)).Error!.Code);

        var other = (await AssignAsync(new TestAssigneeRequest(_doctor.Id, Today, Today)))[0];
        var cancelled = await Service.CancelAsync(other.Id, CancellationToken.None);
        Assert.Equal((TestAssignmentStatus.Cancelled, "quantri Demo"), (cancelled.Value.Status, cancelled.Value.CancelledBy));
        Assert.Equal("testing.cancelled", (await Service.CancelAsync(other.Id, CancellationToken.None)).Error!.Code);
        ActAs(_doctor);
        Assert.Equal("testing.cancelled", (await Service.ReportAsync(other.Id, new SubmitTestReportRequest(TestResult.Passed, null, null), CancellationToken.None)).Error!.Code);
        Assert.Equal(TestAssignmentStatus.Cancelled, Assert.Single((await Service.MineAsync(CancellationToken.None)).Value).Status);
    }

    [Fact]
    public async Task List_FiltersByVersion_AndExportsTheReportsToExcel()
    {
        var first = (await AssignAsync(new TestAssigneeRequest(_nurse.Id, Today, Today)))[0];
        _ctx.Clock.Advance(TimeSpan.FromMinutes(5));
        ActAs(_manager);
        await Service.CreateAsync(Request(new TestAssigneeRequest(_doctor.Id, Today, Today.AddDays(1))) with { Version = "Gói 72 (giả lập)" }, CancellationToken.None);
        ActAs(_nurse);
        await Service.ReportAsync(first.Id, new SubmitTestReportRequest(TestResult.Failed, "Lỗi lưu (giả lập).", "Thêm nút lưu nháp."), CancellationToken.None);

        ActAs(_manager);
        var all = (await Service.ListAsync(null, CancellationToken.None)).Value;
        Assert.Equal(["Gói 72 (giả lập)", Version], all.Versions);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(_nurse.Id, Assert.Single((await Service.ListAsync($" {Version} ", CancellationToken.None)).Value.Items).AssigneeUserId);

        var file = await Service.ExportAsync(Version, CancellationToken.None);
        Assert.True(file.IsSuccess, file.Error?.Message);
        Assert.EndsWith(".xlsx", file.Value.FileName, StringComparison.Ordinal);
        var writer = _ctx.XlsxWriter;
        var row = Assert.Single(writer.Rows);
        Assert.Equal("dieuduong.noia Demo", writer.Cell(0, "Người kiểm thử"));
        Assert.Equal(Today, writer.Cell(0, "Hạn báo cáo"));
        Assert.Equal("Đã báo cáo", writer.Cell(0, "Trạng thái"));
        Assert.Equal("Không đạt", writer.Cell(0, "Kết quả"));
        Assert.Equal("Lỗi lưu (giả lập).", writer.Cell(0, "Lỗi, vấn đề phát hiện"));
        Assert.Equal(new DateTime(2026, 11, 11, 15, 5, 0, DateTimeKind.Unspecified), writer.Cell(0, "Gửi báo cáo lúc"));
        Assert.Null(writer.Cell(0, "Trễ hạn"));
        Assert.Contains(writer.Properties, p => p.Key == "Đợt kiểm thử" && p.Value == Version);
        Assert.Equal(15, row.Count);
    }
}
