using Consent.Application.Abstractions;
using Consent.Application.NursingReports;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Authorization;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.NursingReports;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Báo cáo điều dưỡng hằng ngày (quyết định 31): khoa lập, gửi, Phòng Điều dưỡng mở lại và tổng hợp.</summary>
public sealed class NursingReportServiceTests
{
    /// <summary>Ngày hiện tại theo giờ Việt Nam của đồng hồ giả lập (08:00 UTC ngày 11/11/2026).</summary>
    private static readonly DateOnly Today = new(2026, 11, 11);

    private readonly AppTestContext _ctx = new();
    private readonly Department _ward;
    private readonly Department _surgery;
    private readonly User _nurse;
    private readonly User _office;

    public NursingReportServiceTests()
    {
        _ward = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _surgery = _ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
        _ctx.AddDepartment("KKB", "Khoa Khám bệnh (giả lập)");
        _nurse = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _ward.Id);
        _office = _ctx.AddUser("phong.dieuduong", SystemRoles.NursingOffice);
        _ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(_ward.Id, 48, _ctx.Clock.UtcNow));
        _ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(_surgery.Id, 25, _ctx.Clock.UtcNow));
        ActAs(_nurse, Permissions.NursingReportsSubmit);
    }

    private static Dictionary<string, string?> AllOk() =>
        NursingReportForm.Checks.ToDictionary(c => c.Key, _ => (string?)NursingReportForm.CheckOk);

    private static SaveNursingReportRequest Request(
        Dictionary<string, int?>? values = null,
        Dictionary<string, string?>? checks = null,
        string? notes = null,
        bool submit = false,
        DateOnly? date = null,
        Guid? departmentId = null) =>
        new(departmentId, date ?? Today, values ?? new() { ["nurses"] = 18, ["currentPatients"] = 47 }, checks, notes, submit);

    private void ActAs(User user, params string[] permissions)
    {
        _ctx.CurrentUser.UserId = user.Id;
        _ctx.CurrentUser.UserName = user.UserName;
        _ctx.CurrentUser.DepartmentId = user.DepartmentId;
        _ctx.CurrentUser.AdditionalDepartmentIds.Clear();
        _ctx.CurrentUser.AdditionalDepartmentIds.AddRange(user.AdditionalDepartmentIds);
        _ctx.CurrentUser.Permissions.Clear();
        foreach (var permission in permissions)
        {
            _ctx.CurrentUser.Permissions.Add(permission);
        }
    }

    private async Task<NursingReportDto> SubmitAsync(Department department, User nurse, Dictionary<string, int?> values, string? notes = null,
        Dictionary<string, string?>? checks = null, DateOnly? date = null)
    {
        ActAs(nurse, Permissions.NursingReportsSubmit);
        var result = await _ctx.CreateNursingReportService().SaveAsync(
            Request(values, checks ?? AllOk(), notes, submit: true, date: date, departmentId: department.Id), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public async Task Nurse_SavesADraft_ThenSubmits_AndTheReportIsLocked()
    {
        var service = _ctx.CreateNursingReportService();

        var draft = await service.SaveAsync(
            Request(new() { ["nurses"] = 18, ["orderlies"] = 1, ["technicians"] = null }, new() { ["professional"] = "issue" }, " Ghi chú (giả lập) "),
            CancellationToken.None);

        Assert.True(draft.IsSuccess, draft.Error?.Message);
        Assert.Equal(NursingReportState.Draft, draft.Value.Status);
        Assert.Equal(new Dictionary<string, int> { ["nurses"] = 18, ["orderlies"] = 1 }, draft.Value.Values);
        Assert.Equal("issue", draft.Value.Checks["professional"]);
        Assert.Equal("Ghi chú (giả lập)", draft.Value.Notes);
        Assert.True(draft.Value.CanEdit);
        Assert.False(draft.Value.CanReopen);

        var sent = await service.SaveAsync(Request(checks: AllOk(), submit: true), CancellationToken.None);

        Assert.True(sent.IsSuccess, sent.Error?.Message);
        Assert.Equal(NursingReportState.Submitted, sent.Value.Status);
        Assert.Equal(draft.Value.Id, sent.Value.Id);
        Assert.Equal(_nurse.FullName, sent.Value.SubmittedBy);
        Assert.Equal(_ctx.Clock.UtcNow, sent.Value.SubmittedAtUtc);
        Assert.False(sent.Value.CanEdit);
        Assert.Single(_ctx.Store.NursingReports);

        var again = await service.SaveAsync(Request(checks: AllOk()), CancellationToken.None);
        Assert.Equal("nursing_reports.submitted", again.Error?.Code);
    }

    [Fact]
    public async Task Submit_RequiresEveryTickLineAnswered()
    {
        var checks = AllOk();
        checks["speakers"] = null;

        var result = await _ctx.CreateNursingReportService().SaveAsync(Request(checks: checks, submit: true), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error?.Type);
        Assert.True(result.Error!.FieldErrors!.ContainsKey("checks"));
        Assert.Empty(_ctx.Store.NursingReports);
    }

    [Fact]
    public async Task InvalidValues_AreRejectedLineByLine()
    {
        var result = await _ctx.CreateNursingReportService().SaveAsync(
            Request(
                new() { ["khongCo"] = 1, ["nurses"] = -1, ["currentPatients"] = NursingReportForm.MaxValue + 1 },
                new() { ["professional"] = "maybe", ["khongCo"] = "ok" },
                new string('x', NursingDailyReport.NotesMaxLength + 1)),
            CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error?.Type);
        Assert.Equal(
            ["checks.khongCo", "checks.professional", "notes", "values.currentPatients", "values.khongCo", "values.nurses"],
            result.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Nurse_CannotWriteAnotherDepartmentsReport_UnlessTheyHaveHospitalWideScope()
    {
        var service = _ctx.CreateNursingReportService();

        var denied = await service.SaveAsync(Request(departmentId: _surgery.Id), CancellationToken.None);
        Assert.Equal(ErrorType.Forbidden, denied.Error?.Type);

        ActAs(_nurse, Permissions.NursingReportsSubmit, Permissions.ScopeAllDepartments);
        var allowed = await service.SaveAsync(Request(departmentId: _surgery.Id), CancellationToken.None);
        Assert.True(allowed.IsSuccess, allowed.Error?.Message);
        Assert.Equal(_surgery.Id, allowed.Value.DepartmentId);

        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var office = await service.SaveAsync(Request(departmentId: _ward.Id), CancellationToken.None);
        Assert.Equal(ErrorType.Forbidden, office.Error?.Type);
    }

    // Quyết định 41: điều dưỡng thuộc nhiều khoa lập báo cáo cho từng khoa của mình; mặc định là khoa chính.
    [Fact]
    public async Task Nurse_WithSeveralDepartments_ReportsForEachOfThem_DefaultingToThePrimary_ButNotForOthers()
    {
        var third = _ctx.AddDepartment("SAN", "Khoa Sản (giả lập)");
        _ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(third.Id, 20, _ctx.Clock.UtcNow));
        _nurse.SetDepartments(_ward.Id, [_surgery.Id]);
        ActAs(_nurse, Permissions.NursingReportsSubmit);
        var service = _ctx.CreateNursingReportService();

        var primary = await service.GetAsync(null, Today, CancellationToken.None);
        var other = await service.GetAsync(_surgery.Id, Today, CancellationToken.None);
        var saved = await service.SaveAsync(Request(departmentId: _surgery.Id), CancellationToken.None);
        var denied = await service.SaveAsync(Request(departmentId: third.Id), CancellationToken.None);
        var deniedView = await service.GetAsync(third.Id, Today, CancellationToken.None);

        Assert.Equal(_ward.Id, primary.Value.DepartmentId);
        Assert.True(primary.Value.CanEdit);
        Assert.Equal(_surgery.Id, other.Value.DepartmentId);
        Assert.True(other.Value.CanEdit);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(_surgery.Id, saved.Value.DepartmentId);
        Assert.Equal(ErrorType.Forbidden, denied.Error?.Type);
        Assert.Equal(ErrorType.Forbidden, deniedView.Error?.Type);
    }

    [Fact]
    public async Task DepartmentOutsideTheReportingList_CannotReport()
    {
        _ctx.Store.NursingDepartments.RemoveAll(d => d.DepartmentId == _ward.Id);
        var service = _ctx.CreateNursingReportService();

        var view = await service.GetAsync(null, Today, CancellationToken.None);
        var save = await service.SaveAsync(Request(), CancellationToken.None);

        Assert.True(view.IsSuccess);
        Assert.False(view.Value.IsReportingDepartment);
        Assert.False(view.Value.CanEdit);
        Assert.Equal("nursing_reports.department_not_reporting", save.Error?.Code);
    }

    [Fact]
    public async Task FutureAndVeryOldDates_AreRejected()
    {
        var service = _ctx.CreateNursingReportService();

        var future = await service.SaveAsync(Request(date: Today.AddDays(1)), CancellationToken.None);
        var old = await service.SaveAsync(Request(date: Today.AddDays(-(NursingReportService.MaxDaysBack + 1))), CancellationToken.None);
        var yesterday = await service.SaveAsync(Request(date: Today.AddDays(-1)), CancellationToken.None);

        Assert.True(future.Error!.FieldErrors!.ContainsKey("reportDate"));
        Assert.True(old.Error!.FieldErrors!.ContainsKey("reportDate"));
        Assert.True(yesterday.IsSuccess, yesterday.Error?.Message);
    }

    [Fact]
    public async Task NewReport_CarriesOverStaffAndQuota_FromTheLatestSubmittedReport()
    {
        await SubmitAsync(_ward, _nurse, new() { ["nurses"] = 18, ["orderlies"] = 1, ["quotaPatients"] = 49, ["currentPatients"] = 47 },
            date: Today.AddDays(-2));
        ActAs(_nurse, Permissions.NursingReportsSubmit);

        var today = await _ctx.CreateNursingReportService().GetAsync(null, Today, CancellationToken.None);

        Assert.True(today.IsSuccess);
        Assert.Null(today.Value.Id);
        Assert.Equal(NursingReportState.New, today.Value.Status);
        Assert.Equal(new Dictionary<string, int> { ["nurses"] = 18, ["orderlies"] = 1, ["quotaPatients"] = 49 }, today.Value.Values);
        Assert.Empty(today.Value.Checks);
        Assert.Equal(["nurses", "orderlies", "quotaPatients"], today.Value.CarriedOverKeys.Order(StringComparer.Ordinal));
        Assert.True(today.Value.CanEdit);
    }

    [Fact]
    public async Task NursingOffice_SeesEveryDepartment_OtherNursesOnlyTheirOwn()
    {
        await SubmitAsync(_ward, _nurse, new() { ["nurses"] = 18 }, notes: "Người bệnh A (giả lập) sốt về đêm");
        var surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);

        ActAs(surgeryNurse, Permissions.NursingReportsSubmit);
        var denied = await _ctx.CreateNursingReportService().GetAsync(_ward.Id, Today, CancellationToken.None);
        Assert.Equal(ErrorType.Forbidden, denied.Error?.Type);

        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var seen = await _ctx.CreateNursingReportService().GetAsync(_ward.Id, Today, CancellationToken.None);
        Assert.True(seen.IsSuccess);
        Assert.Equal("Người bệnh A (giả lập) sốt về đêm", seen.Value.Notes);
        Assert.False(seen.Value.CanEdit);
        Assert.True(seen.Value.CanReopen);
    }

    [Fact]
    public async Task Reopen_NeedsTheNursingOfficeAndAReason_AndUnlocksTheReport()
    {
        var sent = await SubmitAsync(_ward, _nurse, new() { ["currentPatients"] = 47 });
        var service = _ctx.CreateNursingReportService();

        var byNurse = await service.ReopenAsync(sent.Id!.Value, new ReopenNursingReportRequest("Sai số"), CancellationToken.None);
        Assert.Equal(ErrorType.Forbidden, byNurse.Error?.Type);

        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var noReason = await service.ReopenAsync(sent.Id.Value, new ReopenNursingReportRequest("  "), CancellationToken.None);
        Assert.True(noReason.Error!.FieldErrors!.ContainsKey("reason"));

        var reopened = await service.ReopenAsync(sent.Id.Value, new ReopenNursingReportRequest("Sai số NB hiện tại"), CancellationToken.None);
        Assert.True(reopened.IsSuccess, reopened.Error?.Message);
        Assert.Equal(NursingReportState.Draft, reopened.Value.Status);
        Assert.Equal("Sai số NB hiện tại", reopened.Value.ReopenReason);
        Assert.Equal(_office.FullName, reopened.Value.ReopenedBy);

        var twice = await service.ReopenAsync(sent.Id.Value, new ReopenNursingReportRequest("lần nữa"), CancellationToken.None);
        Assert.Equal("nursing_reports.not_submitted", twice.Error?.Code);
        // Bệnh viện gọi việc mở lại là "trả lại khoa": thông báo dùng đúng chữ trên nút.
        Assert.Equal("Báo cáo chưa gửi nên không cần trả lại khoa.", twice.Error?.Message);
        Assert.Equal(["Nhập lý do trả lại (tối đa 500 ký tự)."], noReason.Error.FieldErrors["reason"]);

        ActAs(_nurse, Permissions.NursingReportsSubmit);
        var fixedReport = await service.SaveAsync(Request(new() { ["currentPatients"] = 46 }, AllOk(), submit: true), CancellationToken.None);
        Assert.True(fixedReport.IsSuccess, fixedReport.Error?.Message);
        Assert.Equal(46, fixedReport.Value.Values["currentPatients"]);
    }

    [Fact]
    public async Task Pdf_UsesTheHospitalName_AndSaysWhetherTheReportWasSent()
    {
        var service = _ctx.CreateNursingReportService();
        var draft = (await service.SaveAsync(Request(notes: "Ghi chú (giả lập)"), CancellationToken.None)).Value;

        var draftPdf = await service.RenderPdfAsync(draft.Id!.Value, CancellationToken.None);

        Assert.True(draftPdf.IsSuccess);
        Assert.Equal("application/pdf", draftPdf.Value.ContentType);
        Assert.Equal("bao-cao-dieu-duong-NOIA-2026-11-11.pdf", draftPdf.Value.FileName);
        var document = _ctx.NursingDocuments.Report!;
        Assert.Equal(new Consent.Application.ConsentForms.HospitalOptions().Name, document.HospitalName);
        Assert.Equal(_ward.Name, document.DepartmentName);
        Assert.Equal("Ghi chú (giả lập)", document.Notes);
        Assert.StartsWith("Bản nháp", document.StatusLine, StringComparison.Ordinal);

        await service.SaveAsync(Request(checks: AllOk(), submit: true), CancellationToken.None);
        await service.RenderPdfAsync(draft.Id.Value, CancellationToken.None);
        Assert.Equal($"Đã gửi lúc 15:00 11/11/2026 bởi {_nurse.FullName}", _ctx.NursingDocuments.Report!.StatusLine);

        var surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);
        ActAs(surgeryNurse, Permissions.NursingReportsSubmit);
        var hidden = await service.RenderPdfAsync(draft.Id.Value, CancellationToken.None);
        Assert.Equal(ErrorType.NotFound, hidden.Error?.Type);
    }

    [Fact]
    public async Task Summary_FollowsThePaperForm()
    {
        var surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);
        await SubmitAsync(_surgery, surgeryNurse, new()
        {
            ["currentPatients"] = 20, ["admissionsFromClinic"] = 3, ["admissionsByTransfer"] = 1, ["dischargesYesterday"] = 3,
            ["expectedDischarges"] = 2, ["foreignPatients"] = 2, ["transfersOutYesterday"] = 1,
            ["newAdmissions"] = 1, ["adr"] = 1, ["outsideExams"] = 1, ["transfers"] = 1,
            ["boneMarrow"] = 2, ["paracentesis"] = 5, ["cvp"] = 1,
        });
        // Khoa Nội A chỉ lưu nháp: bảng tổng hợp để trống như khoa chưa gửi.
        ActAs(_nurse, Permissions.NursingReportsSubmit);
        await _ctx.CreateNursingReportService().SaveAsync(Request(new() { ["currentPatients"] = 99 }), CancellationToken.None);
        _ctx.Store.NursingStatistics.Add(NursingDailyStatistics.Create(
            Today,
            new NursingStatisticsValues(
                new Dictionary<string, int> { [NursingHisIndicators.OutpatientVisits] = 686, [NursingHisIndicators.XRay] = 65 },
                new Dictionary<Guid, int> { [_ward.Id] = 3 }).ToJson(),
            NursingStatisticsSource.His,
            _office.Id,
            _ctx.Clock.UtcNow));

        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);

        // Khoa sắp theo mã khoa (tên viết tắt, in ở cột khoa phòng); khoa không phải báo cáo (Khoa Khám bệnh) không có dòng.
        Assert.Equal(["NGOAIB", "NOIA"], summary.Rows.Select(r => r.DepartmentCode));
        Assert.Equal(["Khoa Ngoại B (giả lập)", "Khoa Nội A (giả lập)"], summary.Rows.Select(r => r.DepartmentName));
        var surgery = summary.Rows[0];
        Assert.Equal(NursingReportState.Submitted, surgery.Status);
        Assert.Equal(surgeryNurse.FullName, surgery.SubmittedBy);
        Assert.Equal(25, surgery.Cells["beds"]);
        Assert.Equal(20, surgery.Cells["currentPatients"]);
        Assert.Equal(3, surgery.Cells["admissionsFromClinic"]);
        Assert.Equal(1, surgery.Cells["admissionsByTransfer"]);
        Assert.Equal(2, surgery.Cells["foreignPatients"]);
        Assert.Equal(1, surgery.Cells["transfersOutYesterday"]);
        Assert.Equal(0, surgery.Cells["scheduledAdmissions"]);
        Assert.Null(surgery.Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.Equal(1, surgery.Cells["adr"]);
        Assert.Equal(2, surgery.Cells["outsideVisits"]);

        var ward = summary.Rows[1];
        Assert.Equal(NursingReportState.Draft, ward.Status);
        Assert.Equal(48, ward.Cells["beds"]);
        Assert.Equal(3, ward.Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.Null(ward.Cells["currentPatients"]);
        Assert.Null(ward.Cells["outsideVisits"]);

        Assert.Equal(73, summary.Totals["beds"]);
        Assert.Equal(20, summary.Totals["currentPatients"]);
        Assert.Equal(3, summary.Totals[NursingHisIndicators.WaitingAdmission]);
        Assert.Equal(1, summary.SubmittedCount);
        Assert.Equal(2, summary.ReportingCount);

        Assert.Equal(686, Assert.Single(summary.Outpatient).Value);
        Assert.Equal(65, summary.Imaging.Single(l => l.Key == NursingHisIndicators.XRay).Value);
        Assert.Null(summary.Imaging.Single(l => l.Key == NursingHisIndicators.Mri).Value);
        Assert.Equal(2, summary.Procedures.Single(l => l.Key == "boneMarrow").Value);
        Assert.Equal(0, summary.Procedures.Single(l => l.Key == "hickman").Value);
        // Bản giấy không có "Chọc dịch màng bụng" ở mục C và "ĐD thực hiện thuốc" ở mục E.
        Assert.DoesNotContain(summary.Procedures, l => l.Key == "paracentesis");
        Assert.DoesNotContain(summary.Situation, c => c.Key == "drugAdministration");
        Assert.Equal(7, summary.Situation.Count);
        Assert.All(summary.Situation, c => Assert.Equal("Ổn", c.Text));
        Assert.Empty(summary.Incidents);
        Assert.True(summary.Statistics.HasValues);
        Assert.Equal(NursingStatisticsSource.His, summary.Statistics.Source);
        Assert.Equal(_office.FullName, summary.Statistics.UpdatedBy);

        // Người báo cáo từng mục (quyết định 44): B, D lấy từ HIS; C là khoa có số PTTT; E, F không khoa nào báo "Không ổn" hay
        // sự cố nên là mọi khoa đã gửi (cột người báo cáo của mục A). Khoa Nội A chỉ lưu nháp: không có người báo cáo.
        Assert.Equal(surgeryNurse.FullName, surgery.SubmittedBy);
        Assert.Null(ward.SubmittedBy);
        Assert.Equal(
            new NursingSummaryReportersDto(
                $"{_office.FullName} (lấy từ HIS)",
                $"{surgeryNurse.FullName} (NGOAIB)",
                $"{_office.FullName} (lấy từ HIS)",
                NursingReportForm.AllSubmittedReporters,
                NursingReportForm.AllSubmittedReporters),
            summary.Reporters);
    }

    [Fact]
    public async Task Summary_NamesTheDepartmentsReportingAnIssue_AndListsTheirNotes()
    {
        var checks = AllOk();
        checks["drugSupply"] = NursingReportForm.CheckIssue;
        await SubmitAsync(_ward, _nurse, new() { ["currentPatients"] = 40 }, notes: "Thiếu thuốc X (giả lập)\nĐã báo khoa Dược", checks);
        var surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);
        await SubmitAsync(_surgery, surgeryNurse, new() { ["currentPatients"] = 20 }, notes: "  ");

        ActAs(_office, Permissions.NursingReportsRead);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);

        // Khoa ghi bằng mã khoa (quyết định 44).
        var supply = summary.Situation.Single(c => c.Key == "drugSupply");
        Assert.Equal("Không ổn: NOIA", supply.Text);
        Assert.Equal(["NOIA"], supply.IssueDepartments);
        Assert.Equal("Ổn", summary.Situation.Single(c => c.Key == "professional").Text);
        var note = Assert.Single(summary.Incidents);
        Assert.Equal("NOIA", note.DepartmentCode);
        Assert.Equal("Khoa Nội A (giả lập)", note.DepartmentName);
        Assert.Equal("Thiếu thuốc X (giả lập)\nĐã báo khoa Dược", note.Text);
        // Người báo cáo mục E, F: khoa báo "Không ổn", khoa báo cáo sự cố; mục C không khoa nào có số PTTT: mọi khoa đã gửi.
        Assert.Equal($"{_nurse.FullName} (NOIA)", summary.Reporters.Situation);
        Assert.Equal($"{_nurse.FullName} (NOIA)", summary.Reporters.Incidents);
        Assert.Equal(NursingReportForm.AllSubmittedReporters, summary.Reporters.Procedures);
        Assert.Null(summary.Reporters.Outpatient);
        Assert.Null(summary.Reporters.Imaging);
    }

    [Fact]
    public async Task Summary_WithNoReportSent_LeavesTheDepartmentFiguresBlank()
    {
        ActAs(_office, Permissions.NursingReportsRead);

        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);

        Assert.All(summary.Rows, r => Assert.Equal(NursingReportState.New, r.Status));
        Assert.Null(summary.Totals["currentPatients"]);
        Assert.Equal(73, summary.Totals["beds"]);
        Assert.All(summary.Procedures, l => Assert.Null(l.Value));
        Assert.All(summary.Situation, c => Assert.Null(c.Text));
        Assert.False(summary.Statistics.HasValues);
        Assert.Equal(new NursingSummaryReportersDto(null, null, null, null, null), summary.Reporters);
    }

    [Fact]
    public async Task Summary_KeepsASubmittedReport_OfADepartmentRemovedFromTheList()
    {
        await SubmitAsync(_ward, _nurse, new() { ["currentPatients"] = 40 });
        _ctx.Store.NursingDepartments.RemoveAll(d => d.DepartmentId == _ward.Id);

        ActAs(_office, Permissions.NursingReportsRead);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);

        var ward = summary.Rows.Single(r => r.DepartmentId == _ward.Id);
        Assert.False(ward.IsReporting);
        Assert.Null(ward.Cells["beds"]);
        Assert.Equal(40, ward.Cells["currentPatients"]);
        Assert.Equal(1, summary.ReportingCount);
    }

    [Fact]
    public async Task RefreshStatistics_WhenHisIsNotConfiguredOrFails_SaysTheNumbersCanBeTypedIn()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var service = _ctx.CreateNursingSummaryService();

        var disabled = await service.RefreshStatisticsAsync(new RefreshNursingStatisticsRequest(Today), CancellationToken.None);
        Assert.Equal("nursing_reports.his_disabled", disabled.Error?.Code);
        Assert.Empty(_ctx.DailyStatistics.Calls);

        _ctx.DailyStatistics.IsEnabled = true;
        var failed = await service.RefreshStatisticsAsync(new RefreshNursingStatisticsRequest(Today), CancellationToken.None);
        Assert.Equal("nursing_reports.his_unavailable", failed.Error?.Code);
        Assert.Empty(_ctx.Store.NursingStatistics);

        var future = await service.RefreshStatisticsAsync(new RefreshNursingStatisticsRequest(Today.AddDays(1)), CancellationToken.None);
        Assert.True(future.Error!.FieldErrors!.ContainsKey("reportDate"));
    }

    [Fact]
    public async Task RefreshStatistics_AddsUpHospitalFigures_MatchesDepartmentsByCode_AndReportsUnknownCodes()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        _ctx.DailyStatistics.IsEnabled = true;
        _ctx.DailyStatistics.Result = new DailyStatisticsResult(DailyStatisticsSourceStatus.Found,
        [
            new ExternalDailyStatistic(NursingHisIndicators.OutpatientVisits, null, 686),
            new ExternalDailyStatistic(NursingHisIndicators.Ultrasound, "KKB", 50),
            new ExternalDailyStatistic(NursingHisIndicators.Ultrasound, "NOIA", 32),
            new ExternalDailyStatistic(NursingHisIndicators.WaitingAdmission, "noia", 2),
            new ExternalDailyStatistic(NursingHisIndicators.WaitingAdmission, "NOIA", 1),
            new ExternalDailyStatistic(NursingHisIndicators.WaitingAdmission, "KHONGCO", 4),
            new ExternalDailyStatistic("chiSoKhac", null, 9),
        ]);
        var service = _ctx.CreateNursingSummaryService();

        var refreshed = await service.RefreshStatisticsAsync(new RefreshNursingStatisticsRequest(Today), CancellationToken.None);

        Assert.True(refreshed.IsSuccess, refreshed.Error?.Message);
        Assert.Equal([Today], _ctx.DailyStatistics.Calls);
        Assert.Equal(["KHONGCO"], refreshed.Value.UnmatchedDepartmentCodes);
        var summary = refreshed.Value.Summary;
        Assert.Equal(686, summary.Outpatient.Single().Value);
        Assert.Equal(82, summary.Imaging.Single(l => l.Key == NursingHisIndicators.Ultrasound).Value);
        Assert.Equal(3, summary.Rows.Single(r => r.DepartmentId == _ward.Id).Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.Equal(NursingStatisticsSource.His, summary.Statistics.Source);

        // Lấy lại thì thay số cũ của ngày, không tạo thêm dòng.
        _ctx.DailyStatistics.Result = new DailyStatisticsResult(DailyStatisticsSourceStatus.Found, []);
        await service.RefreshStatisticsAsync(new RefreshNursingStatisticsRequest(Today), CancellationToken.None);
        Assert.Single(_ctx.Store.NursingStatistics);
        Assert.Equal("""{"hospital":{},"waitingAdmission":{}}""", _ctx.Store.NursingStatistics[0].ValuesJson);
    }

    private (Department Clinic, User ClinicNurse) Clinic()
    {
        var clinic = _ctx.Store.Departments.Single(d => d.Code == "KKB");
        return (clinic, _ctx.AddUser("dieuduong.kkb", SystemRoles.Nurse, departmentId: clinic.Id));
    }

    /// <summary>Phòng Điều dưỡng giao khoa nhập từng chỉ số toàn viện khi HIS lỗi (giữ nguyên hai khoa phải báo cáo).</summary>
    private async Task AssignAsync(Dictionary<string, Guid?> hisEntry)
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var result = await _ctx.CreateNursingSummaryService().UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest([new(_ward.Id, 48), new(_surgery.Id, 25)], hisEntry), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private async Task<Result<NursingReportDto>> SaveHisEntryAsync(User user, Dictionary<string, int?> values, Guid? departmentId = null, DateOnly? date = null)
    {
        ActAs(user, Permissions.NursingReportsSubmit);
        return await _ctx.CreateNursingReportService().SaveHisEntryAsync(
            new SaveNursingHisEntryRequest(departmentId, date ?? Today, values), CancellationToken.None);
    }

    [Fact]
    public async Task HisEntry_EachDepartmentEntersItsOwnFigures_AndTheNursingOfficeOnlySumsThemUp()
    {
        var (clinic, clinicNurse) = Clinic();
        await AssignAsync(new() { [NursingHisIndicators.OutpatientVisits] = clinic.Id, [NursingHisIndicators.XRay] = clinic.Id });

        // Khoa Khám bệnh không phải gửi báo cáo điều dưỡng: chỉ nhập các chỉ số được giao.
        ActAs(clinicNurse, Permissions.NursingReportsSubmit);
        var clinicPage = (await _ctx.CreateNursingReportService().GetAsync(null, Today, CancellationToken.None)).Value;
        Assert.False(clinicPage.IsReportingDepartment);
        Assert.False(clinicPage.CanEdit);
        Assert.Equal([NursingHisIndicators.OutpatientVisits, NursingHisIndicators.XRay], clinicPage.HisEntry!.Lines.Select(l => l.Key));
        Assert.Equal(["TSNB đến khám", "TS X-quang"], clinicPage.HisEntry.Lines.Select(l => l.Label));
        Assert.True(clinicPage.HisEntry.CanEdit);
        Assert.False(clinicPage.HisEntry.HisHasValues);

        var notAssigned = await SaveHisEntryAsync(
            clinicNurse, new() { [NursingHisIndicators.WaitingAdmission] = 1, [NursingHisIndicators.Mri] = 2 });
        Assert.Equal(["values.mri", "values.waitingAdmission"], notAssigned.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));

        var clinicSaved = await SaveHisEntryAsync(
            clinicNurse, new() { [NursingHisIndicators.OutpatientVisits] = 412, [NursingHisIndicators.XRay] = null });
        Assert.True(clinicSaved.IsSuccess, clinicSaved.Error?.Message);
        Assert.Equal([412, null], clinicSaved.Value.HisEntry!.Lines.Select(l => l.Value));
        Assert.Equal(clinicNurse.FullName, clinicSaved.Value.HisEntry.UpdatedBy);

        // Khoa phải báo cáo: nhập "NB PK chờ nhập viện" của khoa mình, không nhập chỉ số giao cho khoa khác.
        ActAs(_nurse, Permissions.NursingReportsSubmit);
        var wardPage = (await _ctx.CreateNursingReportService().GetAsync(null, Today, CancellationToken.None)).Value;
        Assert.Equal([NursingHisIndicators.WaitingAdmission], wardPage.HisEntry!.Lines.Select(l => l.Key));
        var wrong = await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.OutpatientVisits] = 1 });
        Assert.True(wrong.Error!.FieldErrors!.ContainsKey("values.outpatientVisits"));
        Assert.True((await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.WaitingAdmission] = 3 })).IsSuccess);

        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);
        Assert.Equal(412, summary.Outpatient.Single().Value);
        Assert.Equal("KKB", summary.Outpatient.Single().EnteredBy);
        Assert.Null(summary.Imaging.Single(l => l.Key == NursingHisIndicators.XRay).Value);
        // Người báo cáo mục B là người khoa đã nhập tay; mục D chưa có số.
        Assert.Equal($"{clinicNurse.FullName} (KKB)", summary.Reporters.Outpatient);
        Assert.Null(summary.Reporters.Imaging);
        Assert.Equal(3, summary.Rows.Single(r => r.DepartmentId == _ward.Id).Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.Null(summary.Rows.Single(r => r.DepartmentId == _surgery.Id).Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.False(summary.Statistics.HasValues);
        Assert.True(summary.Statistics.UsesDepartmentEntries);

        // Khoa phải nhập và đã nhập chưa, để Phòng Điều dưỡng nhắc.
        var entries = summary.Statistics.Entries;
        Assert.Equal([clinic.Id, _surgery.Id, _ward.Id], entries.Select(e => e.DepartmentId));
        Assert.Equal([NursingHisIndicators.OutpatientVisits, NursingHisIndicators.XRay], entries[0].Keys);
        Assert.Equal(clinicNurse.FullName, entries[0].EnteredBy);
        Assert.Null(entries[1].EnteredAtUtc);
        Assert.Equal([NursingHisIndicators.WaitingAdmission], entries[2].Keys);
        Assert.Equal(_nurse.FullName, entries[2].EnteredBy);
    }

    [Fact]
    public async Task HisEntry_NumbersFromHisComeFirst_DepartmentNumbersOnlyFillTheGaps()
    {
        var (clinic, clinicNurse) = Clinic();
        await AssignAsync(new() { [NursingHisIndicators.OutpatientVisits] = clinic.Id });
        Assert.True((await SaveHisEntryAsync(clinicNurse, new() { [NursingHisIndicators.OutpatientVisits] = 412 })).IsSuccess);
        Assert.True((await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.WaitingAdmission] = 3 })).IsSuccess);

        // HIS hoạt động lại: có tổng số khám và số chờ nhập viện của khoa Ngoại, không có số của khoa Nội.
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        _ctx.DailyStatistics.IsEnabled = true;
        _ctx.DailyStatistics.Result = new DailyStatisticsResult(DailyStatisticsSourceStatus.Found,
        [
            new ExternalDailyStatistic(NursingHisIndicators.OutpatientVisits, null, 686),
            new ExternalDailyStatistic(NursingHisIndicators.WaitingAdmission, "NGOAIB", 2),
        ]);
        var service = _ctx.CreateNursingSummaryService();
        var summary = (await service.RefreshStatisticsAsync(new RefreshNursingStatisticsRequest(Today), CancellationToken.None)).Value.Summary;

        Assert.Equal(686, summary.Outpatient.Single().Value);
        Assert.Null(summary.Outpatient.Single().EnteredBy);
        Assert.Equal(2, summary.Rows.Single(r => r.DepartmentId == _surgery.Id).Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.Equal(3, summary.Rows.Single(r => r.DepartmentId == _ward.Id).Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.True(summary.Statistics.HasValues);
        Assert.True(summary.Statistics.UsesDepartmentEntries);
        await service.RenderPdfAsync(Today, CancellationToken.None);
        Assert.EndsWith(
            "số liệu HIS lấy lúc 15:00 11/11/2026, phần HIS không có do khoa nhập tay", _ctx.NursingDocuments.Summary!.PrintedLine, StringComparison.Ordinal);

        // Khoa thấy số HIS bên cạnh số đã nhập.
        ActAs(clinicNurse, Permissions.NursingReportsSubmit);
        var page = (await _ctx.CreateNursingReportService().GetAsync(null, Today, CancellationToken.None)).Value.HisEntry!;
        Assert.True(page.HisHasValues);
        Assert.Equal((412, 686), (page.Lines[0].Value, page.Lines[0].HisValue));
    }

    [Fact]
    public async Task HisEntry_IsLimitedToTheDepartmentsOwnLines_RecentDates_AndDepartmentStaff()
    {
        var waiting = new Dictionary<string, int?> { [NursingHisIndicators.WaitingAdmission] = 1 };

        // Phòng Điều dưỡng không nhập thay khoa; điều dưỡng không nhập cho khoa khác.
        Assert.Equal("nursing_reports.forbidden", (await SaveHisEntryAsync(_office, waiting, _ward.Id)).Error?.Code);
        Assert.Equal("nursing_reports.forbidden", (await SaveHisEntryAsync(_nurse, waiting, _surgery.Id)).Error?.Code);

        Assert.True((await SaveHisEntryAsync(_nurse, waiting, date: Today.AddDays(1))).Error!.FieldErrors!.ContainsKey("reportDate"));
        Assert.True((await SaveHisEntryAsync(_nurse, waiting, date: Today.AddDays(-NursingReportService.MaxDaysBack - 1)))
            .Error!.FieldErrors!.ContainsKey("reportDate"));
        Assert.True((await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.WaitingAdmission] = -1 }))
            .Error!.FieldErrors!.ContainsKey("values.waitingAdmission"));

        // Khoa không phải báo cáo và chưa được giao chỉ số nào: không có gì để nhập.
        var (_, clinicNurse) = Clinic();
        ActAs(clinicNurse, Permissions.NursingReportsSubmit);
        Assert.Null((await _ctx.CreateNursingReportService().GetAsync(null, Today, CancellationToken.None)).Value.HisEntry);
        Assert.Equal("nursing_reports.no_his_entry", (await SaveHisEntryAsync(clinicNurse, [])).Error?.Code);

        // Để trống là xóa số đã nhập; vẫn một dòng cho mỗi khoa mỗi ngày.
        Assert.True((await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.WaitingAdmission] = 3 })).IsSuccess);
        var cleared = await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.WaitingAdmission] = null });
        Assert.Null(cleared.Value.HisEntry!.Lines.Single().Value);
        Assert.Single(_ctx.Store.NursingDepartmentStatistics);
        Assert.Empty(_ctx.Store.NursingStatistics);
    }

    [Fact]
    public async Task HisEntryAssignments_AreValidated_ListedPerDepartment_AndCanBeCleared()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var (clinic, _) = Clinic();
        var closed = _ctx.AddDepartment("DONG", "Khoa đã đóng (giả lập)", active: false);
        var service = _ctx.CreateNursingSummaryService();

        var invalid = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_ward.Id, 48)],
                new Dictionary<string, Guid?> { ["khongCo"] = clinic.Id, [NursingHisIndicators.Mri] = closed.Id, [NursingHisIndicators.CtScan] = Guid.NewGuid() }),
            CancellationToken.None);
        Assert.Equal(["hisEntry.ct", "hisEntry.khongCo", "hisEntry.mri"], invalid.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(_ctx.Store.SystemSettings);
        Assert.Equal(2, _ctx.Store.NursingDepartments.Count);

        var saved = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_ward.Id, 48), new(_surgery.Id, 25)],
                new Dictionary<string, Guid?>
                {
                    [NursingHisIndicators.Mri] = clinic.Id, [NursingHisIndicators.OutpatientVisits] = clinic.Id, [NursingHisIndicators.Ultrasound] = null,
                }),
            CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal([NursingHisIndicators.OutpatientVisits, NursingHisIndicators.Mri], saved.Value.Single(d => d.DepartmentId == clinic.Id).HisIndicators);
        Assert.Empty(saved.Value.Single(d => d.DepartmentId == _ward.Id).HisIndicators);
        Assert.Equal($"outpatientVisits:{clinic.Id},mri:{clinic.Id}", _ctx.Store.SystemSettings.Single().Value);

        // Không gửi phần giao chỉ số thì giữ nguyên; gửi rỗng thì bỏ giao hết.
        await service.UpdateDepartmentsAsync(new UpdateNursingReportDepartmentsRequest([new(_ward.Id, 50), new(_surgery.Id, 25)]), CancellationToken.None);
        Assert.Single(_ctx.Store.SystemSettings);
        await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest([new(_ward.Id, 50), new(_surgery.Id, 25)], new Dictionary<string, Guid?>()), CancellationToken.None);
        Assert.Empty(_ctx.Store.SystemSettings);
    }

    [Fact]
    public void HisEntryAssignments_IgnoreBrokenParts_AndAreWrittenInSummaryOrder()
    {
        var clinic = Guid.NewGuid();
        var imaging = Guid.NewGuid();

        var parsed = NursingHisEntry.ParseAssignments($" mri:{imaging} ,outpatientVisits:{clinic},khongCo:{clinic},xray:khong-phai-guid,,ct");

        Assert.Equal(new Dictionary<string, Guid> { ["mri"] = imaging, ["outpatientVisits"] = clinic }, parsed);
        Assert.Equal($"outpatientVisits:{clinic},mri:{imaging}", NursingHisEntry.FormatAssignments(parsed));
        Assert.Null(NursingHisEntry.FormatAssignments(new Dictionary<string, Guid>()));
        Assert.Empty(NursingHisEntry.ParseAssignments(null));
    }

    [Fact]
    public void Merge_KeepsHisNumbers_TakesTheLatestDepartmentNumberForGaps_AndOnlyEachDepartmentsOwnWaitingNumber()
    {
        var ward = Guid.NewGuid();
        var surgery = Guid.NewGuid();
        var clinic = Guid.NewGuid();
        var user = Guid.NewGuid();
        var at = _ctx.Clock.UtcNow;
        static string Json(Dictionary<string, int> hospital, Dictionary<Guid, int> waiting) => new NursingStatisticsValues(hospital, waiting).ToJson();
        var his = new NursingStatisticsValues(new Dictionary<string, int> { ["outpatientVisits"] = 686 }, new Dictionary<Guid, int> { [surgery] = 2 });
        // Số "NB PK chờ nhập viện" của khoa khác nằm trong số khoa này nhập thì bỏ qua.
        var older = NursingDepartmentStatistics.Create(clinic, Today, Json(new() { ["outpatientVisits"] = 400, ["xray"] = 10 }, new() { [ward] = 7 }), user, at);
        var newer = NursingDepartmentStatistics.Create(surgery, Today, Json(new() { ["xray"] = 12 }, new() { [surgery] = 5, [ward] = 9 }), user, at.AddMinutes(5));
        var wardEntry = NursingDepartmentStatistics.Create(ward, Today, Json([], new() { [ward] = 3 }), user, at);

        var merged = NursingHisEntry.Merge(his, [older, newer, wardEntry]);

        Assert.Equal(686, merged.Values.HospitalValue("outpatientVisits"));
        Assert.Equal(12, merged.Values.HospitalValue("xray"));
        Assert.Equal(2, merged.Values.Waiting(surgery));
        Assert.Equal(3, merged.Values.Waiting(ward));
        Assert.Null(merged.Values.Waiting(clinic));
        Assert.Equal(new Dictionary<string, Guid> { ["xray"] = surgery }, merged.HospitalEnteredBy);
        Assert.Equal([ward], merged.WaitingEnteredBy);
        Assert.False(NursingHisEntry.Merge(his, []).UsesDepartmentEntries);
    }

    [Fact]
    public async Task ReportingDepartments_CanBeAddedChangedAndRemoved()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var clinic = _ctx.Store.Departments.Single(d => d.Code == "KKB");
        var service = _ctx.CreateNursingSummaryService();

        var result = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest([new(_ward.Id, 50), new(clinic.Id, 0)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(
            [("KKB", true, (int?)0), ("NGOAIB", false, null), ("NOIA", true, 50)],
            result.Value.OrderBy(d => d.Code, StringComparer.Ordinal).Select(d => (d.Code, d.IsReporting, d.BedCount)));
        Assert.Equal(2, _ctx.Store.NursingDepartments.Count);
        Assert.Equal(50, _ctx.Store.NursingDepartments.Single(d => d.DepartmentId == _ward.Id).BedCount);
    }

    [Fact]
    public async Task ReportingDepartments_RejectUnknownInactiveDuplicateOrOutOfRangeEntries()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var closed = _ctx.AddDepartment("DONG", "Khoa đã đóng (giả lập)", active: false);

        var result = await _ctx.CreateNursingSummaryService().UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
            [
                new(Guid.NewGuid(), 10),
                new(closed.Id, 10),
                new(_ward.Id, 10),
                new(_ward.Id, 12),
                new(_surgery.Id, NursingReportDepartment.MaxBedCount + 1),
            ]),
            CancellationToken.None);

        Assert.Equal(
            ["items[0].departmentId", "items[1].departmentId", "items[3].departmentId", "items[4].bedCount"],
            result.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, _ctx.Store.NursingDepartments.Count);
    }

    /// <summary>
    /// Khoa nhập tay trước đây đã cài báo cáo và được giao nhập số liệu HIS, rồi ngừng sử dụng khi lấy danh mục khoa từ HIS
    /// (quyết định 42); khoa trên HIS là một khoa mới. Danh sách ghi rõ khoa ngừng sử dụng; gửi lại khoa đó (như giao diện cũ, bệnh
    /// viện báo "Dữ liệu không hợp lệ" 20/11) thì lỗi ghi tên khoa; bỏ khoa đó thì lưu được.
    /// </summary>
    [Fact]
    public async Task ReportingDepartments_ADepartmentStoppedByTheHisList_IsFlagged_AndSavingWithoutItWorks()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var oldIcu = _ctx.AddDepartment("HSTC", "Khoa Hồi sức tích cực (giả lập)");
        var hisIcu = _ctx.AddDepartment("HHTCCD", "Hồi Sức Tích Cực - Chống Độc (giả lập)");
        var service = _ctx.CreateNursingSummaryService();
        var before = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_ward.Id, 48), new(_surgery.Id, 25), new(oldIcu.Id, 20)],
                new Dictionary<string, Guid?> { [NursingHisIndicators.Ultrasound] = oldIcu.Id }),
            CancellationToken.None);
        Assert.True(before.IsSuccess, before.Error?.Message);
        oldIcu.Deactivate();

        var listed = await service.ListDepartmentsAsync(CancellationToken.None);
        var stopped = listed.Single(d => d.DepartmentId == oldIcu.Id);
        Assert.False(stopped.IsActive);
        Assert.True(stopped.IsReporting);
        Assert.Equal([NursingHisIndicators.Ultrasound], stopped.HisIndicators);
        Assert.True(listed.Single(d => d.DepartmentId == hisIcu.Id).IsActive);
        Assert.True(listed.Single(d => d.DepartmentId == _ward.Id).IsActive);

        // Gửi lại khoa ngừng sử dụng: lỗi ghi rõ khoa nào và cách sửa, không lưu gì.
        var rejected = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_ward.Id, 48), new(_surgery.Id, 25), new(oldIcu.Id, 20), new(hisIcu.Id, 22)],
                new Dictionary<string, Guid?> { [NursingHisIndicators.Ultrasound] = oldIcu.Id }),
            CancellationToken.None);
        Assert.Equal(["hisEntry.ultrasound", "items[2].departmentId"], rejected.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            "Khoa Hồi sức tích cực (giả lập) (HSTC) đã ngừng sử dụng (không còn trong danh mục khoa/phòng), bỏ chọn khoa này.",
            Assert.Single(rejected.Error.FieldErrors["items[2].departmentId"]));
        Assert.Equal(
            "Khoa Hồi sức tích cực (giả lập) (HSTC) đã ngừng sử dụng (không còn trong danh mục khoa/phòng), chọn khoa khác.",
            Assert.Single(rejected.Error.FieldErrors["hisEntry.ultrasound"]));
        Assert.DoesNotContain(_ctx.Store.NursingDepartments, d => d.DepartmentId == hisIcu.Id);

        // Bỏ khoa ngừng sử dụng và bỏ giao chỉ số của khoa đó (như giao diện mới gửi): lưu được, khoa cũ ra khỏi danh sách.
        var saved = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_ward.Id, 48), new(_surgery.Id, 25), new(hisIcu.Id, 22)],
                new Dictionary<string, Guid?> { [NursingHisIndicators.Ultrasound] = null }),
            CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.DoesNotContain(saved.Value, d => d.DepartmentId == oldIcu.Id);
        var icu = saved.Value.Single(d => d.DepartmentId == hisIcu.Id);
        Assert.Equal((true, true, (int?)22), (icu.IsActive, icu.IsReporting, icu.BedCount));
        Assert.DoesNotContain(_ctx.Store.NursingDepartments, d => d.DepartmentId == oldIcu.Id);
        Assert.Empty(_ctx.Store.SystemSettings);
    }

    /// <summary>
    /// Phòng Điều dưỡng xếp thứ tự khoa và đặt tên viết tắt (quyết định 45): bảng tổng hợp (mục A, E, F, người báo cáo) theo thứ
    /// tự và tên viết tắt; danh sách cài đặt trả khoa báo cáo theo thứ tự trước; tên viết tắt để trống thì dùng lại mã; không cho
    /// trùng, quá dài, đặt cho khoa ngừng sử dụng; mã HIS giữ nguyên.
    /// </summary>
    [Fact]
    public async Task ReportingDepartments_FollowTheOfficeOrder_AndShortNames()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var (clinic, _) = Clinic();

        // Nội A trước Ngoại B (ngược thứ tự mã); Nội A viết tắt "Nội A", Khoa Khám bệnh (không phải báo cáo) "KB".
        var saved = await _ctx.CreateNursingSummaryService().UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_ward.Id, 48), new(_surgery.Id, 25)],
                null,
                new Dictionary<Guid, string?> { [_ward.Id] = "  Nội A ", [clinic.Id] = "KB" }),
            CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(["NOIA", "NGOAIB", "KKB"], saved.Value.Select(d => d.Code));
        Assert.Equal(["Nội A", null, "KB"], saved.Value.Select(d => d.ShortName));
        Assert.Equal(
            [(_ward.Id, 1), (_surgery.Id, 2)],
            _ctx.Store.NursingDepartments.OrderBy(d => d.SortOrder).Select(d => (d.DepartmentId, d.SortOrder)));
        Assert.Equal("NOIA", _ward.Code);

        var checks = AllOk();
        checks["drugSupply"] = NursingReportForm.CheckIssue;
        await SubmitAsync(_ward, _nurse, new() { ["currentPatients"] = 40 }, notes: "Ghi chú khoa Nội A (giả lập)", checks);
        var surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);
        await SubmitAsync(_surgery, surgeryNurse, new() { ["currentPatients"] = 20 }, notes: "Ghi chú khoa Ngoại B (giả lập)", checks);
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);
        Assert.Equal(["Nội A", "NGOAIB"], summary.Rows.Select(r => r.DepartmentCode));
        Assert.Equal("Không ổn: Nội A, NGOAIB", summary.Situation.Single(c => c.Key == "drugSupply").Text);
        Assert.Equal(["Nội A", "NGOAIB"], summary.Incidents.Select(i => i.DepartmentCode));
        Assert.Equal($"{_nurse.FullName} (Nội A); {surgeryNurse.FullName} (NGOAIB)", summary.Reporters.Incidents);

        // Đổi thứ tự: Ngoại B lên đầu; Khoa Khám bệnh mới phải báo cáo, xếp cuối.
        var service = _ctx.CreateNursingSummaryService();
        Assert.True((await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest([new(_surgery.Id, 25), new(_ward.Id, 48), new(clinic.Id, 0)]),
            CancellationToken.None)).IsSuccess);
        Assert.Equal(
            [(_surgery.Id, 1), (_ward.Id, 2), (clinic.Id, 3)],
            _ctx.Store.NursingDepartments.OrderBy(d => d.SortOrder).Select(d => (d.DepartmentId, d.SortOrder)));
        Assert.Equal(["NGOAIB", "Nội A", "KB"], (await service.GetAsync(Today, CancellationToken.None)).Rows.Select(r => r.DepartmentCode));
        Assert.Equal(["NGOAIB", "NOIA", "KKB"], (await service.ListDepartmentsAsync(CancellationToken.None)).Select(d => d.Code));

        // Trùng tên viết tắt (không phân biệt hoa thường) hoặc trùng mã khoa khác, quá dài, khoa ngừng sử dụng: báo lỗi, không lưu gì.
        var closed = _ctx.AddDepartment("DONG", "Khoa đã đóng (giả lập)", active: false);
        var invalid = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_surgery.Id, 25), new(_ward.Id, 48)],
                null,
                new Dictionary<Guid, string?> { [_surgery.Id] = "kb", [_ward.Id] = new string('X', 21), [closed.Id] = "DONG2" }),
            CancellationToken.None);
        Assert.Equal(
            new[] { $"shortNames.{closed.Id}", $"shortNames.{_surgery.Id}", $"shortNames.{_ward.Id}" }.Order(StringComparer.Ordinal),
            invalid.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            "Tên viết tắt \"kb\" trùng với Khoa Khám bệnh (giả lập) (KKB).",
            Assert.Single(invalid.Error.FieldErrors[$"shortNames.{_surgery.Id}"]));
        Assert.Equal("Tên viết tắt tối đa 20 ký tự.", Assert.Single(invalid.Error.FieldErrors[$"shortNames.{_ward.Id}"]));
        Assert.Equal("Nội A", _ward.ShortName);
        Assert.Null(_surgery.ShortName);

        // Gõ kiểu tổ hợp (một số bộ gõ tiếng Việt) vẫn nhận ra trùng: tên viết tắt chuẩn hóa như tên khoa lấy từ HIS.
        var decomposed = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_surgery.Id, 25), new(_ward.Id, 48)], null, new Dictionary<Guid, string?> { [_surgery.Id] = " Nội a " }),
            CancellationToken.None);
        Assert.Equal(
            "Tên viết tắt \"Nội a\" trùng với Khoa Nội A (giả lập) (NOIA).",
            Assert.Single(decomposed.Error!.FieldErrors![$"shortNames.{_surgery.Id}"]));
        Assert.Null(_surgery.ShortName);

        // Để trống thì bỏ tên viết tắt, dùng lại mã khoa.
        Assert.True((await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_surgery.Id, 25), new(_ward.Id, 48)], null, new Dictionary<Guid, string?> { [_ward.Id] = "  " }),
            CancellationToken.None)).IsSuccess);
        Assert.Null(_ward.ShortName);
        Assert.Equal(["NGOAIB", "NOIA"], (await service.GetAsync(Today, CancellationToken.None)).Rows.Select(r => r.DepartmentCode));

        // Khoa Nội A nay lại hiện mã NOIA: không đặt "noia" làm tên viết tắt cho khoa khác.
        var takenCode = await service.UpdateDepartmentsAsync(
            new UpdateNursingReportDepartmentsRequest(
                [new(_surgery.Id, 25), new(_ward.Id, 48)], null, new Dictionary<Guid, string?> { [_surgery.Id] = "noia" }),
            CancellationToken.None);
        Assert.Equal(
            "Tên viết tắt \"noia\" trùng với Khoa Nội A (giả lập) (NOIA).",
            Assert.Single(takenCode.Error!.FieldErrors![$"shortNames.{_surgery.Id}"]));
        Assert.Null(_surgery.ShortName);
    }

    [Fact]
    public async Task SummaryFiles_ShareOneDocument_WithThePrintTimeAndSources()
    {
        await SubmitAsync(_ward, _nurse, new() { ["currentPatients"] = 40 });
        Assert.True((await SaveHisEntryAsync(_nurse, new() { [NursingHisIndicators.WaitingAdmission] = 3 })).IsSuccess);
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var service = _ctx.CreateNursingSummaryService();

        var pdf = await service.RenderPdfAsync(Today, CancellationToken.None);
        var workbook = await service.RenderWorkbookAsync(Today, CancellationToken.None);

        Assert.Equal("tong-hop-dieu-duong-2026-11-11.pdf", pdf.FileName);
        Assert.Equal("tong-hop-dieu-duong-2026-11-11.xlsx", workbook.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", workbook.ContentType);
        Assert.Equal(
            "In lúc 15:00 11/11/2026 · Khoa đã gửi: 1/2 · số liệu HIS do khoa nhập tay",
            _ctx.NursingDocuments.Summary!.PrintedLine);
        Assert.Equal(_ctx.NursingDocuments.Summary.PrintedLine, _ctx.NursingDocuments.Workbook!.PrintedLine);
    }

    [Fact]
    public void Figures_IgnoreUnknownLinesAndBrokenJson_AndAreWrittenInKeyOrder()
    {
        var figures = NursingReportFigures.Parse(
            """{"values":{"nurses":18,"khongCo":2,"orderlies":"mot"},"checks":{"professional":"ok","security":"co le","khongCo":"ok"}}""");

        Assert.Equal(new Dictionary<string, int> { ["nurses"] = 18 }, figures.Values);
        Assert.Equal(new Dictionary<string, string> { ["professional"] = "ok" }, figures.Checks);
        Assert.Same(NursingReportFigures.Empty, NursingReportFigures.Parse("{không phải json"));
        // Ghi kèm phiên bản mẫu; số liệu lưu trước khi có phiên bản là phiên bản 1.
        Assert.Equal(1, figures.Version);
        Assert.Equal(
            """{"values":{"currentPatients":3,"nurses":1},"checks":{"security":"issue"},"form":2}""",
            new NursingReportFigures(
                new Dictionary<string, int> { ["nurses"] = 1, ["currentPatients"] = 3 },
                new Dictionary<string, string> { ["security"] = "issue" }).ToJson());
        Assert.Equal(2, NursingReportFigures.Parse("""{"values":{},"checks":{},"form":2}""").Version);
        Assert.Equal(1, NursingReportFigures.Parse("""{"values":{},"checks":{},"form":"hai"}""").Version);
        Assert.Equal(1, NursingReportFigures.Parse("""{"values":{},"checks":{},"form":0}""").Version);
    }

    [Fact]
    public async Task Summary_LeavesTheSplitAdmissionColumnsBlank_ForReportsSavedBeforeTheSplit()
    {
        // Báo cáo đã gửi trước khi tách cột (quyết định 38): chỉ có số tổng "Số ca nhập viện ngày hôm trước".
        var legacy = NursingDailyReport.Create(
            _ward.Id, Today, """{"values":{"admissionsYesterday":4,"currentPatients":40},"checks":{}}""", null, _nurse.Id, _ctx.Clock.UtcNow);
        legacy.Submit(_nurse.Id, _ctx.Clock.UtcNow);
        _ctx.Store.NursingReports.Add(legacy);
        var surgeryNurse = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _surgery.Id);
        await SubmitAsync(_surgery, surgeryNurse, new() { ["admissionsFromClinic"] = 2 });

        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var summary = await _ctx.CreateNursingSummaryService().GetAsync(Today, CancellationToken.None);

        var ward = summary.Rows.Single(r => r.DepartmentId == _ward.Id);
        Assert.Null(ward.Cells["admissionsFromClinic"]);
        Assert.Null(ward.Cells["admissionsByTransfer"]);
        Assert.Equal(40, ward.Cells["currentPatients"]);
        Assert.Equal(0, ward.Cells["noBed"]);
        // Báo cáo lưu trước khi có hai dòng mới (quyết định 44): để trống, không ghi 0.
        Assert.Null(ward.Cells["foreignPatients"]);
        Assert.Null(ward.Cells["transfersOutYesterday"]);
        // Báo cáo mới bỏ trống một dòng thì tính 0 như mọi ô khác.
        var surgery = summary.Rows.Single(r => r.DepartmentId == _surgery.Id);
        Assert.Equal(2, surgery.Cells["admissionsFromClinic"]);
        Assert.Equal(0, surgery.Cells["admissionsByTransfer"]);
        Assert.Equal(0, surgery.Cells["foreignPatients"]);
        Assert.Equal(0, surgery.Cells["transfersOutYesterday"]);
        Assert.Equal(2, summary.Totals["admissionsFromClinic"]);
        Assert.Equal(0, summary.Totals["admissionsByTransfer"]);
        Assert.Equal(0, summary.Totals["foreignPatients"]);

        // Khoa không nhập mới được số tổng cũ.
        ActAs(_nurse, Permissions.NursingReportsSubmit);
        var old = await _ctx.CreateNursingReportService().SaveAsync(
            Request(new() { ["admissionsYesterday"] = 4 }, date: Today.AddDays(-1)), CancellationToken.None);
        Assert.True(old.Error!.FieldErrors!.ContainsKey("values.admissionsYesterday"));
    }

    /// <summary>
    /// Người báo cáo từng mục (quyết định 44): mỗi người một lần, theo thứ tự mã khoa (C) hoặc thứ tự dòng (B, D); số Phòng Điều
    /// dưỡng nhập tay trước quyết định 35 ghi Phòng Điều dưỡng; tài khoản không còn thì "Không rõ". Khoa xếp theo mã, không theo
    /// tên (mã HHNL1 trước NGOAIB dù tên "Trung tâm…" sau "Khoa…").
    /// </summary>
    [Fact]
    public void Summary_Reporters_NameEachPersonOnce_WithTheirDepartmentCode()
    {
        var hematology = Guid.NewGuid();
        var surgery = Guid.NewGuid();
        var imaging = Guid.NewGuid();
        var departments = new List<NursingSummaryDepartment>
        {
            new(surgery, "NGOAIB", "Khoa Ngoại B (giả lập)", IsReporting: true, BedCount: 20),
            new(hematology, "HHNL1", "Trung tâm Huyết học người lớn 1 (giả lập)", IsReporting: true, BedCount: 30),
            new(imaging, "CDHA", "Khoa Chẩn đoán hình ảnh (giả lập)", IsReporting: false, BedCount: null),
        };
        var hematologyNurse = Guid.NewGuid();
        var surgeryNurse = Guid.NewGuid();
        var office = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        NursingDailyReport Submitted(Guid department, Guid nurse, Dictionary<string, int> values, string? issue = null)
        {
            var checks = NursingReportForm.Checks.ToDictionary(
                c => c.Key, c => c.Key == issue ? NursingReportForm.CheckIssue : NursingReportForm.CheckOk, StringComparer.Ordinal);
            var report = NursingDailyReport.Create(
                department,
                Today,
                new NursingReportFigures(values, checks).ToJson(),
                null,
                nurse,
                now);
            report.Submit(nurse, now);
            return report;
        }

        var summary = NursingSummaryBuilder.Build(
            Today,
            departments,
            [
                // "ĐD thực hiện thuốc" không có trên bảng tổng hợp (mục E): báo không ổn dòng này không tính người báo cáo mục E.
                Submitted(surgery, surgeryNurse, new() { ["cvp"] = 1 }, issue: "drugAdministration"),
                Submitted(hematology, hematologyNurse, new() { ["boneMarrow"] = 4, ["marrowBiopsy"] = 1 }),
            ],
            // Người gửi báo cáo khoa Ngoại B không còn tài khoản.
            new Dictionary<Guid, string> { [hematologyNurse] = "Điều dưỡng HHNL1 (giả lập)", [office] = "Phòng Điều dưỡng Demo" },
            new NursingStatisticsValues(
                new Dictionary<string, int>
                {
                    [NursingHisIndicators.OutpatientVisits] = 686, [NursingHisIndicators.Ultrasound] = 40, [NursingHisIndicators.XRay] = 65,
                    [NursingHisIndicators.Mri] = 3,
                },
                new Dictionary<Guid, int>()),
            new NursingStatisticsInfoDto(
                true, true, NursingStatisticsSource.Manual, now, "Phòng Điều dưỡng Demo",
                [new NursingHisEntryStatusDto(imaging, "Khoa Chẩn đoán hình ảnh (giả lập)", [NursingHisIndicators.Ultrasound], now, "KTV CĐHA (giả lập)")],
                true),
            new Dictionary<string, Guid> { [NursingHisIndicators.Ultrasound] = imaging });

        Assert.Equal(["HHNL1", "NGOAIB"], summary.Rows.Select(r => r.DepartmentCode));
        Assert.Equal(["Điều dưỡng HHNL1 (giả lập)", null], summary.Rows.Select(r => r.SubmittedBy));
        Assert.Equal("Phòng Điều dưỡng Demo (Phòng Điều dưỡng)", summary.Reporters.Outpatient);
        // X-quang và MRI cùng do Phòng Điều dưỡng nhập: ghi một lần.
        Assert.Equal("KTV CĐHA (giả lập) (CDHA); Phòng Điều dưỡng Demo (Phòng Điều dưỡng)", summary.Reporters.Imaging);
        Assert.Equal("CDHA", summary.Imaging.Single(l => l.Key == NursingHisIndicators.Ultrasound).EnteredBy);
        Assert.Equal("Điều dưỡng HHNL1 (giả lập) (HHNL1); Không rõ (NGOAIB)", summary.Reporters.Procedures);
        Assert.Equal(NursingReportForm.AllSubmittedReporters, summary.Reporters.Situation);
    }

    [Fact]
    public void Form_FollowsThePaperForm()
    {
        var form = NursingReportService.Form;

        Assert.Equal(
            ["I. NHÂN SỰ", "II. NHÂN VIÊN TRONG NGÀY", "III. TÌNH HÌNH NGƯỜI BỆNH", "IV. PHẢN ỨNG CÓ HẠI THUỐC (ADR)",
             "V. SỐ CA ĐI NGOẠI VIỆN", "VI. SỐ LƯỢNG NB"],
            form.Sections.Select(s => s.Title));
        Assert.Equal([4, 8, 30, 0, 4, 11], form.Sections.Select(s => s.Items.Count));
        Assert.Equal("adr", form.Sections[3].ValueKey);
        Assert.Equal(8, form.Checks.Count);
        Assert.Equal(58, NursingReportForm.ValueKeys.Count);
        // Bảng A: cột "Số ca nhập viện ngày hôm trước" của bản giấy tách thành nhập khoa từ PK và do CK (quyết định 38); thêm
        // "Người nước ngoài" sau số NB hiện tại và "Chuyển khoa" sau cột xuất viện (quyết định 44).
        Assert.Equal(
            ["beds", "currentPatients", "foreignPatients", "admissionsFromClinic", "admissionsByTransfer", "dischargesYesterday",
             "transfersOutYesterday", "expectedDischarges", "scheduledAdmissions", "newAdmissions", "waitingAdmission", "noBed", "adr",
             "outsideVisits"],
            NursingReportForm.SummaryColumns.Select(c => c.Key));
        Assert.Equal(
            ["Người nước ngoài", "Nhập khoa từ PK", "Nhập khoa do CK", "Số ca Xuất viện ngày hôm trước", "Chuyển khoa"],
            NursingReportForm.SummaryColumns.Skip(2).Take(5).Select(c => c.Header));
        // Báo cáo khoa nhập hai dòng mới ở mục VI, ngay sau dòng tương ứng; báo cáo lưu bằng mẫu cũ để trống hai cột này.
        var counts = form.Sections[5].Items.Select(i => i.Key).ToList();
        Assert.Equal("foreignPatients", counts[counts.IndexOf("currentPatients") + 1]);
        Assert.Equal("transfersOutYesterday", counts[counts.IndexOf("dischargesYesterday") + 1]);
        Assert.Equal(
            ["Số NB người nước ngoài", "Số ca chuyển khoa ngày hôm trước"],
            form.Sections[5].Items.Where(i => i.Since == 2).Select(i => i.Label));
        Assert.Equal(2, NursingReportForm.Since("foreignPatients"));
        Assert.Equal(1, NursingReportForm.Since("currentPatients"));
        Assert.Equal(1, NursingReportForm.Since(NursingReportForm.BedsColumn));
        Assert.Equal(
            ["Số ca nhập khoa từ PK ngày hôm trước", "Số ca nhập khoa do CK ngày hôm trước"],
            form.Sections[5].Items.Where(i => NursingReportForm.AdmissionKeys.Contains(i.Key)).Select(i => i.Label));
        Assert.DoesNotContain(NursingReportForm.LegacyAdmissionsKey, NursingReportForm.ValueKeys);
        Assert.All(NursingReportForm.ProcedureLines, l => Assert.Contains(l.Key, NursingReportForm.ValueKeys));
        Assert.All(NursingReportForm.OutsideVisitKeys, k => Assert.Contains(k, NursingReportForm.ValueKeys));
    }
}
