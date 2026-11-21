using Consent.Application.NursingReports;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.NursingReports;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Ủy quyền báo cáo điều dưỡng khi người báo cáo vắng (quyết định 48). Dữ liệu giả lập.</summary>
public sealed class NursingDelegationTests
{
    /// <summary>Ngày hôm nay theo giờ Việt Nam của đồng hồ giả lập (08:00 UTC ngày 11/11/2026).</summary>
    private static readonly DateOnly Today = new(2026, 11, 11);

    private readonly AppTestContext _ctx = new();
    private readonly Department _ward;
    private readonly Department _surgery;
    private readonly Department _clinic;
    private readonly User _headNurse;
    private readonly User _wardNurse;
    private readonly User _surgeryNurse;
    private readonly User _clinicNurse;
    private readonly User _doctor;
    private readonly User _office;

    public NursingDelegationTests()
    {
        _ward = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _surgery = _ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
        _clinic = _ctx.AddDepartment("KKB", "Khoa Khám bệnh (giả lập)");
        _headNurse = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _ward.Id);
        _wardNurse = _ctx.AddUser("dieuduong.noia2", SystemRoles.Nurse, departmentId: _ward.Id);
        _surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);
        _clinicNurse = _ctx.AddUser("dieuduong.kkb", SystemRoles.Nurse, departmentId: _clinic.Id);
        _doctor = _ctx.AddUser("bacsi.noia", SystemRoles.Doctor, departmentId: _ward.Id);
        _office = _ctx.AddUser("phong.dieuduong", SystemRoles.NursingOffice);
        _ctx.AddUser("dieuduong.nghi", SystemRoles.Nurse, active: false, departmentId: _ward.Id);
        _ctx.AddUser("quantri", SystemRoles.Administrator);
        _ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(_ward.Id, 48, _ctx.Clock.UtcNow, sortOrder: 1));
        _ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(_surgery.Id, 25, _ctx.Clock.UtcNow, sortOrder: 2));
    }

    /// <summary>Đăng nhập như <paramref name="user"/>: quyền theo vai trò, khoa theo tài khoản (như token).</summary>
    private void ActAs(User user)
    {
        _ctx.CurrentUser.UserId = user.Id;
        _ctx.CurrentUser.UserName = user.UserName;
        _ctx.CurrentUser.DepartmentId = user.DepartmentId;
        _ctx.CurrentUser.AdditionalDepartmentIds.Clear();
        _ctx.CurrentUser.Permissions.Clear();
        foreach (var permission in _ctx.Store.Roles.Where(r => user.RoleIds.Contains(r.Id)).SelectMany(r => r.Permissions).Select(p => p.PermissionCode))
        {
            _ctx.CurrentUser.Permissions.Add(permission);
        }
    }

    private CreateNursingDelegationRequest Request(
        User delegateUser, DateOnly? start = null, DateOnly? end = null, bool report = true, bool his = false, string? reason = null, Guid? departmentId = null) =>
        new(departmentId ?? _ward.Id, delegateUser.Id, start ?? Today, end ?? Today.AddDays(2), report, his, reason);

    private async Task<NursingDelegationDto> DelegateAsync(
        User delegateUser, DateOnly? start = null, DateOnly? end = null, bool report = true, bool his = false)
    {
        ActAs(_headNurse);
        var result = await _ctx.CreateNursingDelegationService().CreateAsync(Request(delegateUser, start, end, report, his), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static Dictionary<string, string?> Checks(string? issue = null) =>
        NursingReportForm.Checks.ToDictionary(c => c.Key, c => (string?)(c.Key == issue ? NursingReportForm.CheckIssue : NursingReportForm.CheckOk));

    private static SaveNursingReportRequest Report(Department department, DateOnly date, string? notes = null, string? issue = null) =>
        new(department.Id, date, new Dictionary<string, int?> { ["nurses"] = 18, ["previousDayPatients"] = 40, ["admissionsFromClinic"] = 2 },
            Checks(issue), notes, Submit: true);

    [Fact]
    public async Task Create_ChecksTheDepartment_TheDelegate_TheDates_AndTheContents()
    {
        ActAs(_headNurse);
        var service = _ctx.CreateNursingDelegationService();
        async Task<IReadOnlyDictionary<string, string[]>> Errors(CreateNursingDelegationRequest request)
        {
            var result = await service.CreateAsync(request, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
            return result.Error.FieldErrors!;
        }

        Assert.Equal(["Không ủy quyền cho chính mình."], (await Errors(Request(_headNurse)))["delegateUserId"]);
        // Bác sĩ không có quyền lập báo cáo điều dưỡng; tài khoản đã khóa; tài khoản quản trị không thuộc khoa nào.
        Assert.True((await Errors(Request(_doctor))).ContainsKey("delegateUserId"));
        Assert.True((await Errors(Request(_ctx.Store.Users.Single(u => u.UserName == "dieuduong.nghi")))).ContainsKey("delegateUserId"));
        Assert.True((await Errors(Request(_ctx.Store.Users.Single(u => u.UserName == "quantri")))).ContainsKey("delegateUserId"));
        Assert.Equal(["Ngày bắt đầu từ hôm nay trở đi."], (await Errors(Request(_surgeryNurse, start: Today.AddDays(-1))))["startDate"]);
        Assert.Equal(["Ngày kết thúc phải sau hoặc bằng ngày bắt đầu."], (await Errors(Request(_surgeryNurse, end: Today.AddDays(-1))))["endDate"]);
        Assert.Equal(["Ủy quyền tối đa 366 ngày."], (await Errors(Request(_surgeryNurse, end: Today.AddDays(366))))["endDate"]);
        Assert.Equal(["Chọn ít nhất một nội dung ủy quyền."], (await Errors(Request(_surgeryNurse, report: false)))["contents"]);
        // Chỉ ủy quyền được khoa của mình.
        Assert.True((await Errors(Request(_surgeryNurse, departmentId: _surgery.Id))).ContainsKey("departmentId"));
        Assert.True((await Errors(Request(_surgeryNurse, reason: new string('x', 201)))).ContainsKey("reason"));

        // Khoa Khám bệnh: không phải gửi báo cáo, không được giao số liệu HIS.
        ActAs(_clinicNurse);
        Assert.StartsWith("Khoa chưa có trong danh sách", (await Errors(Request(_surgeryNurse, departmentId: _clinic.Id)))["contents"][0]);
        Assert.StartsWith("Khoa không có số liệu HIS", (await Errors(Request(_surgeryNurse, report: false, his: true, departmentId: _clinic.Id)))["contents"][0]);
        Assert.Empty(_ctx.Store.NursingDelegations);

        ActAs(_headNurse);
        var created = await service.CreateAsync(Request(_surgeryNurse, his: true, reason: "  Nghỉ phép (giả lập)  "), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal("NOIA", created.Value.DepartmentCode);
        Assert.Equal("Khoa Nội A (giả lập)", created.Value.DepartmentName);
        Assert.Equal("dieuduong.noia Demo", created.Value.DelegatorName);
        Assert.Equal("dieuduong.ngoaib Demo", created.Value.DelegateName);
        Assert.Equal((Today, Today.AddDays(2)), (created.Value.StartDate, created.Value.EndDate));
        Assert.Equal(["report", "hisEntry"], created.Value.Contents);
        Assert.Equal("Nghỉ phép (giả lập)", created.Value.Reason);
        Assert.Equal(NursingDelegationStatus.Active, created.Value.Status);
        Assert.True(created.Value.CanRevoke);
        // Khoa bỏ trống là khoa chính; ủy quyền từ ngày mai là "sắp tới".
        var upcoming = await service.CreateAsync(Request(_wardNurse, start: Today.AddDays(1)) with { DepartmentId = null }, CancellationToken.None);
        Assert.True(upcoming.IsSuccess, upcoming.Error?.Message);
        Assert.Equal(_ward.Id, upcoming.Value.DepartmentId);
        Assert.Equal(NursingDelegationStatus.Upcoming, upcoming.Value.Status);
        // Một ngày (hôm nay) và đủ 366 ngày được nhận.
        Assert.True((await service.CreateAsync(Request(_clinicNurse, end: Today), CancellationToken.None)).IsSuccess);
        Assert.True((await service.CreateAsync(Request(_clinicNurse, end: Today.AddDays(365)), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Candidates_AreActiveUsersOfADepartment_WhoMayReport()
    {
        ActAs(_headNurse);
        var result = await _ctx.CreateNursingDelegationService().CandidatesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(
            [("dieuduong.kkb Demo", "Khoa Khám bệnh (giả lập)"), ("dieuduong.ngoaib Demo", "Khoa Ngoại B (giả lập)"), ("dieuduong.noia2 Demo", "Khoa Nội A (giả lập)")],
            result.Value.Select(c => (c.FullName, c.DepartmentName)));

        ActAs(_doctor);
        Assert.Equal(ErrorType.Forbidden, (await _ctx.CreateNursingDelegationService().CandidatesAsync(CancellationToken.None)).Error!.Type);
    }

    [Fact]
    public async Task Delegate_Reports_OnlyWithinTheDates_AndOnlyTheDelegatedContents()
    {
        await DelegateAsync(_surgeryNurse, end: Today.AddDays(1));
        ActAs(_surgeryNurse);
        var service = _ctx.CreateNursingReportService();

        var view = await service.GetAsync(_ward.Id, Today, CancellationToken.None);
        Assert.True(view.IsSuccess, view.Error?.Message);
        Assert.True(view.Value.CanEdit);
        Assert.True(view.Value.ReportVisible);
        Assert.False(view.Value.HisEntry!.CanEdit);

        var sent = await service.SaveAsync(Report(_ward, Today), CancellationToken.None);
        Assert.True(sent.IsSuccess, sent.Error?.Message);
        Assert.Equal("dieuduong.ngoaib Demo (được dieuduong.noia Demo ủy quyền)", sent.Value.SubmittedBy);
        Assert.Equal(_ctx.Store.NursingDelegations.Single().Id, _ctx.Store.NursingReports.Single().SubmittedDelegationId);

        // Số liệu HIS không được ủy quyền; ngày báo cáo ngoài thời gian ủy quyền.
        Assert.Equal(ErrorType.Forbidden, (await service.SaveHisEntryAsync(
            new SaveNursingHisEntryRequest(_ward.Id, Today, new Dictionary<string, int?> { ["waitingAdmission"] = 2 }), CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await service.GetAsync(_ward.Id, Today.AddDays(-1), CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await service.SaveAsync(Report(_ward, Today.AddDays(-1)), CancellationToken.None)).Error!.Type);

        // Hết thời gian ủy quyền: ngày báo cáo nằm trong thời gian ủy quyền nhưng hôm nay đã qua.
        _ctx.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(ErrorType.Forbidden, (await service.GetAsync(_ward.Id, Today.AddDays(1), CancellationToken.None)).Error!.Type);
    }

    [Fact]
    public async Task UpcomingDelegation_DoesNotWorkBeforeItStarts()
    {
        await DelegateAsync(_surgeryNurse, start: Today.AddDays(1));
        ActAs(_surgeryNurse);

        Assert.Equal(ErrorType.Forbidden, (await _ctx.CreateNursingReportService().GetAsync(_ward.Id, Today, CancellationToken.None)).Error!.Type);
        Assert.Equal(ErrorType.Forbidden, (await _ctx.CreateNursingReportService().GetAsync(_ward.Id, Today.AddDays(1), CancellationToken.None)).Error!.Type);
        _ctx.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True((await _ctx.CreateNursingReportService().GetAsync(_ward.Id, Today.AddDays(1), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Summary_NamesTheDelegate_AndShowsTheReportWasDelegated()
    {
        await DelegateAsync(_surgeryNurse);
        ActAs(_surgeryNurse);
        var reports = _ctx.CreateNursingReportService();
        Assert.True((await reports.SaveAsync(Report(_ward, Today, notes: "Máy hút đàm hỏng (giả lập)", issue: "facilities"), CancellationToken.None)).IsSuccess);
        // Khoa mình: gửi không theo ủy quyền.
        Assert.True((await reports.SaveAsync(Report(_surgery, Today), CancellationToken.None)).IsSuccess);

        ActAs(_office);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);
        var rows = summary.Rows.ToDictionary(r => r.DepartmentCode);
        Assert.Equal("dieuduong.ngoaib Demo (được dieuduong.noia Demo ủy quyền)", rows["NOIA"].SubmittedBy);
        Assert.Equal("dieuduong.ngoaib Demo", rows["NGOAIB"].SubmittedBy);
        Assert.Equal("dieuduong.ngoaib Demo (NOIA, được dieuduong.noia Demo ủy quyền)", summary.Reporters.Situation);
        Assert.Equal("dieuduong.ngoaib Demo (NOIA, được dieuduong.noia Demo ủy quyền)", summary.Reporters.Incidents);
        var active = Assert.Single(summary.Delegations!);
        Assert.Equal(("NOIA", "dieuduong.ngoaib Demo", "dieuduong.noia Demo"), (active.DepartmentCode, active.DelegateName, active.DelegatorName));
        Assert.Equal((Today, Today.AddDays(2)), (active.StartDate, active.EndDate));
        Assert.Equal(["report"], active.Contents);

        // Hủy ủy quyền sau khi đã gửi: báo cáo vẫn ghi ủy quyền, ngày đó không còn ủy quyền có hiệu lực.
        ActAs(_headNurse);
        Assert.True((await _ctx.CreateNursingDelegationService().RevokeAsync(_ctx.Store.NursingDelegations.Single().Id, CancellationToken.None)).IsSuccess);
        ActAs(_office);
        var after = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);
        Assert.Equal("dieuduong.ngoaib Demo (được dieuduong.noia Demo ủy quyền)", after.Rows.Single(r => r.DepartmentCode == "NOIA").SubmittedBy);
        Assert.Empty(after.Delegations!);
    }

    [Fact]
    public async Task ColleagueWithADelegation_IsShownAsDelegated_TheDelegatorIsNot()
    {
        await DelegateAsync(_wardNurse);
        // Sang ngày hôm sau: hôm qua và hôm nay đều trong thời gian ủy quyền.
        _ctx.Clock.Advance(TimeSpan.FromDays(1));
        ActAs(_wardNurse);
        var sent = await _ctx.CreateNursingReportService().SaveAsync(Report(_ward, Today), CancellationToken.None);
        Assert.Equal("dieuduong.noia2 Demo (được dieuduong.noia Demo ủy quyền)", sent.Value.SubmittedBy);

        // Người ủy quyền tự gửi báo cáo của khoa trong thời gian ủy quyền: không ghi ủy quyền.
        ActAs(_headNurse);
        var own = await _ctx.CreateNursingReportService().SaveAsync(Report(_ward, Today.AddDays(1)), CancellationToken.None);
        Assert.Equal("dieuduong.noia Demo", own.Value.SubmittedBy);
        Assert.Null(_ctx.Store.NursingReports.Single(r => r.ReportDate == Today.AddDays(1)).SubmittedDelegationId);
    }

    [Fact]
    public async Task HisEntryDelegation_EntersHisFigures_WithoutSeeingTheReport()
    {
        // Khoa đã lưu nháp báo cáo có phần nhập thêm: người chỉ được ủy quyền số liệu HIS không được xem.
        ActAs(_headNurse);
        Assert.True((await _ctx.CreateNursingReportService().SaveAsync(
            Report(_ward, Today, notes: "Người bệnh Trần Văn Bình (giả lập)") with { Submit = false }, CancellationToken.None)).IsSuccess);
        await DelegateAsync(_surgeryNurse, report: false, his: true);
        ActAs(_surgeryNurse);
        var service = _ctx.CreateNursingReportService();

        var view = await service.GetAsync(_ward.Id, Today, CancellationToken.None);
        Assert.True(view.IsSuccess, view.Error?.Message);
        Assert.False(view.Value.ReportVisible);
        Assert.Null(view.Value.Id);
        Assert.Null(view.Value.Notes);
        Assert.Empty(view.Value.Values);
        Assert.False(view.Value.CanEdit);
        Assert.True(view.Value.HisEntry!.CanEdit);
        Assert.Equal(ErrorType.Forbidden, (await service.SaveAsync(Report(_ward, Today), CancellationToken.None)).Error!.Type);

        var saved = await service.SaveHisEntryAsync(
            new SaveNursingHisEntryRequest(_ward.Id, Today, new Dictionary<string, int?> { ["waitingAdmission"] = 3 }), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal("dieuduong.ngoaib Demo (được dieuduong.noia Demo ủy quyền)", saved.Value.HisEntry!.UpdatedBy);
        Assert.Equal(_ctx.Store.NursingDelegations.Single().Id, _ctx.Store.NursingDepartmentStatistics.Single().DelegationId);

        ActAs(_office);
        var entry = (await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None)).Statistics.Entries.Single(e => e.DepartmentId == _ward.Id);
        Assert.Equal(("dieuduong.ngoaib Demo", "dieuduong.noia Demo"), (entry.EnteredBy, entry.EnteredByDelegator));

        // Người của khoa sửa lại số đã nhập: không còn ghi ủy quyền; người được ủy quyền sửa tiếp thì ghi lại.
        ActAs(_headNurse);
        Assert.True((await _ctx.CreateNursingReportService().SaveHisEntryAsync(
            new SaveNursingHisEntryRequest(_ward.Id, Today, new Dictionary<string, int?> { ["waitingAdmission"] = 4 }), CancellationToken.None)).IsSuccess);
        Assert.Null(_ctx.Store.NursingDepartmentStatistics.Single().DelegationId);
        ActAs(_surgeryNurse);
        var again = await service.SaveHisEntryAsync(
            new SaveNursingHisEntryRequest(_ward.Id, Today, new Dictionary<string, int?> { ["waitingAdmission"] = 5 }), CancellationToken.None);
        Assert.Equal("dieuduong.ngoaib Demo (được dieuduong.noia Demo ủy quyền)", again.Value.HisEntry!.UpdatedBy);
        Assert.Equal(_ctx.Store.NursingDelegations.Single().Id, _ctx.Store.NursingDepartmentStatistics.Single().DelegationId);
    }

    [Fact]
    public async Task Revoke_ByTheDelegatorOrTheNursingOffice_Once_AndNotAfterItEnds()
    {
        var delegation = await DelegateAsync(_surgeryNurse);
        var service = _ctx.CreateNursingDelegationService();

        ActAs(_surgeryNurse);
        Assert.Equal(ErrorType.Forbidden, (await service.RevokeAsync(delegation.Id, CancellationToken.None)).Error!.Type);
        ActAs(_wardNurse);
        Assert.Equal(ErrorType.NotFound, (await service.RevokeAsync(delegation.Id, CancellationToken.None)).Error!.Type);

        ActAs(_office);
        var revoked = await service.RevokeAsync(delegation.Id, CancellationToken.None);
        Assert.True(revoked.IsSuccess, revoked.Error?.Message);
        Assert.Equal(NursingDelegationStatus.Revoked, revoked.Value.Status);
        Assert.Equal("phong.dieuduong Demo", revoked.Value.RevokedBy);
        Assert.False(revoked.Value.CanRevoke);
        Assert.Equal("nursing_delegations.revoked", (await service.RevokeAsync(delegation.Id, CancellationToken.None)).Error!.Code);

        ActAs(_surgeryNurse);
        Assert.Equal(ErrorType.Forbidden, (await _ctx.CreateNursingReportService().GetAsync(_ward.Id, Today, CancellationToken.None)).Error!.Type);

        var ended = await DelegateAsync(_surgeryNurse, end: Today);
        _ctx.Clock.Advance(TimeSpan.FromDays(1));
        ActAs(_headNurse);
        Assert.Equal("nursing_delegations.expired", (await service.RevokeAsync(ended.Id, CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task List_ShowsOwnDelegations_AndAllForTheNursingOffice()
    {
        await DelegateAsync(_surgeryNurse);
        await DelegateAsync(_wardNurse, start: Today.AddDays(1));
        var service = _ctx.CreateNursingDelegationService();

        ActAs(_headNurse);
        Assert.Equal(2, (await service.ListAsync(false, CancellationToken.None)).Value.Count);
        ActAs(_surgeryNurse);
        var mine = Assert.Single((await service.ListAsync(false, CancellationToken.None)).Value);
        Assert.Equal("dieuduong.ngoaib Demo", mine.DelegateName);
        Assert.False(mine.CanRevoke);
        ActAs(_clinicNurse);
        Assert.Empty((await service.ListAsync(false, CancellationToken.None)).Value);

        ActAs(_office);
        var all = (await service.ListAsync(true, CancellationToken.None)).Value;
        Assert.Equal(2, all.Count);
        Assert.All(all, d => Assert.True(d.CanRevoke));
        ActAs(_wardNurse);
        Assert.Equal(ErrorType.Forbidden, (await service.ListAsync(true, CancellationToken.None)).Error!.Type);
        ActAs(_doctor);
        Assert.Equal(ErrorType.Forbidden, (await service.ListAsync(false, CancellationToken.None)).Error!.Type);
    }
}
