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
            ["currentPatients"] = 20, ["admissionsYesterday"] = 4, ["dischargesYesterday"] = 3, ["expectedDischarges"] = 2,
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

        // Khoa sắp theo tên; khoa không phải báo cáo (Khoa Khám bệnh) không có dòng.
        Assert.Equal(["Khoa Ngoại B (giả lập)", "Khoa Nội A (giả lập)"], summary.Rows.Select(r => r.DepartmentName));
        var surgery = summary.Rows[0];
        Assert.Equal(NursingReportState.Submitted, surgery.Status);
        Assert.Equal(surgeryNurse.FullName, surgery.SubmittedBy);
        Assert.Equal(25, surgery.Cells["beds"]);
        Assert.Equal(20, surgery.Cells["currentPatients"]);
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

        var supply = summary.Situation.Single(c => c.Key == "drugSupply");
        Assert.Equal("Không ổn: Khoa Nội A (giả lập)", supply.Text);
        Assert.Equal(["Khoa Nội A (giả lập)"], supply.IssueDepartments);
        Assert.Equal("Ổn", summary.Situation.Single(c => c.Key == "professional").Text);
        var note = Assert.Single(summary.Incidents);
        Assert.Equal("Khoa Nội A (giả lập)", note.DepartmentName);
        Assert.Equal("Thiếu thuốc X (giả lập)\nĐã báo khoa Dược", note.Text);
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

    [Fact]
    public async Task ManualStatistics_ReplaceTheDayValues_AndAreValidated()
    {
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var service = _ctx.CreateNursingSummaryService();

        var invalid = await service.UpdateStatisticsAsync(
            new UpdateNursingStatisticsRequest(
                Today,
                new Dictionary<string, int?> { ["khongCo"] = 1, [NursingHisIndicators.Mri] = -1 },
                new Dictionary<Guid, int?> { [Guid.NewGuid()] = 1 }),
            CancellationToken.None);
        Assert.Equal(3, invalid.Error!.FieldErrors!.Count);

        var saved = await service.UpdateStatisticsAsync(
            new UpdateNursingStatisticsRequest(
                Today,
                new Dictionary<string, int?> { [NursingHisIndicators.OutpatientVisits] = 700, [NursingHisIndicators.Mri] = null },
                new Dictionary<Guid, int?> { [_surgery.Id] = 2 }),
            CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(700, saved.Value.Outpatient.Single().Value);
        Assert.Null(saved.Value.Imaging.Single(l => l.Key == NursingHisIndicators.Mri).Value);
        Assert.Equal(2, saved.Value.Rows.Single(r => r.DepartmentId == _surgery.Id).Cells[NursingHisIndicators.WaitingAdmission]);
        Assert.Equal(NursingStatisticsSource.Manual, saved.Value.Statistics.Source);
        Assert.Equal(_office.FullName, saved.Value.Statistics.UpdatedBy);
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

    [Fact]
    public async Task SummaryFiles_ShareOneDocument_WithThePrintTimeAndSources()
    {
        await SubmitAsync(_ward, _nurse, new() { ["currentPatients"] = 40 });
        ActAs(_office, Permissions.NursingReportsRead, Permissions.NursingReportsManage);
        var service = _ctx.CreateNursingSummaryService();
        await service.UpdateStatisticsAsync(
            new UpdateNursingStatisticsRequest(Today, new Dictionary<string, int?> { [NursingHisIndicators.OutpatientVisits] = 10 }, null),
            CancellationToken.None);

        var pdf = await service.RenderPdfAsync(Today, CancellationToken.None);
        var workbook = await service.RenderWorkbookAsync(Today, CancellationToken.None);

        Assert.Equal("tong-hop-dieu-duong-2026-11-11.pdf", pdf.FileName);
        Assert.Equal("tong-hop-dieu-duong-2026-11-11.xlsx", workbook.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", workbook.ContentType);
        Assert.Equal(
            "In lúc 15:00 11/11/2026 · Khoa đã gửi: 1/2 · số liệu HIS nhập tay lúc 15:00 11/11/2026",
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
        Assert.Equal(
            """{"values":{"currentPatients":3,"nurses":1},"checks":{"security":"issue"}}""",
            new NursingReportFigures(
                new Dictionary<string, int> { ["nurses"] = 1, ["currentPatients"] = 3 },
                new Dictionary<string, string> { ["security"] = "issue" }).ToJson());
    }

    [Fact]
    public void Form_FollowsThePaperForm()
    {
        var form = NursingReportService.Form;

        Assert.Equal(
            ["I. NHÂN SỰ", "II. NHÂN VIÊN TRONG NGÀY", "III. TÌNH HÌNH NGƯỜI BỆNH", "IV. PHẢN ỨNG CÓ HẠI THUỐC (ADR)",
             "V. SỐ CA ĐI NGOẠI VIỆN", "VI. SỐ LƯỢNG NB"],
            form.Sections.Select(s => s.Title));
        Assert.Equal([4, 8, 30, 0, 4, 8], form.Sections.Select(s => s.Items.Count));
        Assert.Equal("adr", form.Sections[3].ValueKey);
        Assert.Equal(8, form.Checks.Count);
        Assert.Equal(55, NursingReportForm.ValueKeys.Count);
        Assert.Equal(11, NursingReportForm.SummaryColumns.Count);
        Assert.All(NursingReportForm.ProcedureLines, l => Assert.Contains(l.Key, NursingReportForm.ValueKeys));
        Assert.All(NursingReportForm.OutsideVisitKeys, k => Assert.Contains(k, NursingReportForm.ValueKeys));
    }
}
