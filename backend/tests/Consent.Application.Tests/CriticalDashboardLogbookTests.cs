using Consent.Application.ConsentForms;
using Consent.Application.CriticalResults;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.CriticalResults;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>
/// Dashboard QLCL và sổ điện tử kết quả nguy cấp (quyết định 60). Hồ sơ dựng thẳng từ domain với giờ sự việc cụ thể (ghi đúng
/// lúc sự việc, trừ hồ sơ ghi bù); bây giờ là 10:30 ngày 27/11/2026, giờ Việt Nam. Thời hạn báo và xử trí 10 phút. Dữ liệu giả lập.
/// </summary>
public sealed class CriticalDashboardLogbookTests
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private static readonly DateOnly BirthDate = new(1960, 1, 2);
    private static readonly CriticalDeadlines Deadlines = new(10, 10, 30, RequireAddress: true);

    private readonly AppTestContext _ctx = new();
    private readonly Department _lab;
    private readonly Department _micro;
    private readonly Department _wardA;
    private readonly Department _wardB;
    private readonly User _technician;
    private readonly User _microTechnician;
    private readonly User _nurseA;
    private readonly User _doctorA;
    private readonly User _doctorB;
    private readonly User _quality;
    private readonly User _screen;
    private readonly CriticalTest _potassium;
    private readonly CriticalTest _culture;
    private int _number;

    public CriticalDashboardLogbookTests()
    {
        _lab = _ctx.AddDepartment("HSH", "Khoa Huyết học – Sinh hóa (giả lập)");
        _micro = _ctx.AddDepartment("VS", "Khoa Vi sinh (giả lập)");
        _wardA = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _wardB = _ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
        var qa = _ctx.AddDepartment("QLCL", "Phòng Quản lý chất lượng (giả lập)");
        _technician = _ctx.AddUser("ktv.hsh", SystemRoles.CriticalLab, departmentId: _lab.Id);
        _microTechnician = _ctx.AddUser("ktv.vs", SystemRoles.CriticalLab, departmentId: _micro.Id);
        _nurseA = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _wardA.Id);
        _doctorA = _ctx.AddUser("bacsi.noia", SystemRoles.Doctor, departmentId: _wardA.Id);
        _doctorB = _ctx.AddUser("bacsi.ngoaib", SystemRoles.Doctor, departmentId: _wardB.Id);
        _quality = _ctx.AddUser("qlcl.kqnc", SystemRoles.CriticalQuality, departmentId: qa.Id);
        _screen = _ctx.AddUser("manhinh.noia", SystemRoles.CriticalDisplay, departmentId: _wardA.Id);
        _potassium = CriticalTest.Create(
            new("Kali (K+) (giả lập)", "Ví dụ: ≤ 2,5 hoặc ≥ 6,0", null, "mmol/L", "Sinh hóa", CriticalLogbook.Laboratory, false, 1), At(7, 0));
        _culture = CriticalTest.Create(
            new("Cấy máu (giả lập)", "Ví dụ: dương tính", null, null, "Vi sinh", CriticalLogbook.Microbiology, true, 2), At(7, 0));
        _ctx.Store.CriticalTests.AddRange([_potassium, _culture]);
        _ctx.Clock.UtcNow = At(10, 30);
    }

    private CriticalDashboardService Dashboard => _ctx.CreateCriticalDashboardService();

    private CriticalLogbookService Logbook => _ctx.CreateCriticalLogbookService();

    /// <summary>Giờ Việt Nam (mặc định ngày 27/11/2026), đổi sang UTC.</summary>
    private static DateTimeOffset At(int hour, int minute, int month = 11, int day = 27) =>
        new DateTimeOffset(2026, month, day, hour, minute, 0, Vietnam).ToUniversalTime();

    private static CriticalActor Actor(User user) => new(user.Id, user.FullName);

    private static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static Error Fails<T>(Result<T> result, ErrorType type)
    {
        Assert.True(result.IsFailure, "Lẽ ra phải lỗi.");
        Assert.Equal(type, result.Error!.Type);
        return result.Error;
    }

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

    /// <summary>Hồ sơ khoa <paramref name="sending"/> ghi lúc có kết quả (hoặc lúc <paramref name="recordedAt"/> khi ghi bù).</summary>
    private CriticalResult Seed(
        Department sending,
        CriticalTest test,
        string patient,
        DateTimeOffset resultAt,
        string value = "6,8",
        DateTimeOffset? recordedAt = null,
        string? sampleId = null,
        bool urgent = false)
    {
        _number++;
        var technician = sending == _micro ? _microTechnician : _technician;
        var result = CriticalResult.Create(
            $"KQNC-2026-{_number:000000}",
            sending.Id,
            new CriticalPatient($"NB-GL-{_number:0000}", patient, BirthDate, Gender.Female, "Ấp Mẫu (giả lập)", CriticalPatientSource.His),
            test,
            new CriticalResultDetails(
                value, test.TextResult ? null : "mmol/L", DateOnly.FromDateTime(resultAt.ToOffset(Vietnam).DateTime), resultAt, sampleId, urgent),
            Deadlines,
            Actor(technician),
            recordedAt ?? resultAt,
            Vietnam);
        _ctx.Store.CriticalResults.Add(result);
        return result;
    }

    private void Call(CriticalResult result, Department ward, DateTimeOffset at, int incorrect = 0, bool twice = false)
    {
        var technician = result.SendingDepartmentId == _micro.Id ? _microTechnician : _technician;
        result.RecordNotification(
            new(ward.Id, CriticalNotificationMethod.Phone, at, "ĐD Nghe Máy (giả lập)", ReadBackCorrect: true, incorrect, twice),
            Actor(technician),
            at);
    }

    /// <summary>
    /// Tháng 11: (1) báo 5 phút, BS 7 phút; (2) báo 15 phút, đọc lại sai 1 lần, BS 25 phút; (3) chưa báo, quá hạn; (4) chưa báo,
    /// còn hạn; (5) đã hủy; (6) Vi sinh báo 7 phút, chờ xác nhận, còn hạn xử trí. Tháng 10: một hồ sơ báo chậm, BS đúng hạn.
    /// </summary>
    private (CriticalResult First, CriticalResult Second, CriticalResult Overdue, CriticalResult Cancelled, CriticalResult Culture) Scenario()
    {
        var first = Seed(_lab, _potassium, "Người Bệnh Một", At(8, 0));
        Call(first, _wardA, At(8, 5));
        first.RecordAction("Ngưng thuốc giữ kali (giả lập)", null, true, Actor(_doctorA), At(8, 12));

        var second = Seed(_lab, _potassium, "Người Bệnh Hai", At(9, 0), "2,1");
        Call(second, _wardA, At(9, 15), incorrect: 1);
        second.RecordAction("Bù kali đường uống (giả lập)", null, true, Actor(_doctorA), At(9, 40));

        var overdue = Seed(_lab, _potassium, "Người Bệnh Ba", At(10, 0));
        Seed(_lab, _potassium, "Người Bệnh Bốn", At(10, 25));

        var cancelled = Seed(_lab, _potassium, "Người Bệnh Năm", At(9, 30));
        Call(cancelled, _wardA, At(9, 35));
        cancelled.Cancel("Nhập nhầm người bệnh", true, Actor(_technician), At(9, 50));

        var culture = Seed(_micro, _culture, "Người Bệnh Sáu", At(10, 18), "Dương tính, cầu khuẩn gram dương (giả lập)", sampleId: "VS-0001", urgent: true);
        Call(culture, _wardB, At(10, 25), twice: true);

        var october = Seed(_lab, _potassium, "Người Bệnh Tháng Mười", At(8, 0, month: 10, day: 15));
        Call(october, _wardA, At(8, 20, month: 10, day: 15));
        october.RecordAction("Xử trí (giả lập)", null, false, Actor(_doctorA), At(8, 25, month: 10, day: 15));
        return (first, second, overdue, cancelled, culture);
    }

    [Fact]
    public async Task Dashboard_RatesCountRecordsDueOrDone_CancelledCountedApart()
    {
        var (_, second, overdue, _, _) = Scenario();
        ActAs(_quality);

        var month = Ok(await Dashboard.GetAsync(null, null, CancellationToken.None));

        var k = month.Kpis;
        Assert.Equal(("month", "2026-11", "Tháng 11/2026"), (month.Period, month.Month, month.Label));
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 27)), (month.From, month.To));
        Assert.Equal((5, 1), (k.Total, k.Cancelled));
        Assert.Equal(new CriticalRateDto(2, 4), k.NotifyOnTime);
        Assert.Equal(50.0, k.NotifyOnTime.Percent);
        Assert.Equal(new CriticalRateDto(1, 2), k.ActionOnTime);
        Assert.Equal(new CriticalRateDto(2, 3), k.ReadBackFirstTime);
        Assert.Equal(66.7, k.ReadBackFirstTime.Percent);
        Assert.Equal(new CriticalRateDto(2, 3), k.Complete);
        Assert.Equal((7.0, 6.0, 11.0), (k.MedianNotifyMinutes, k.NotifyQ1Minutes, k.NotifyQ3Minutes));
        Assert.Equal((16.0, 11.5, 20.5), (k.MedianActionMinutes, k.ActionQ1Minutes, k.ActionQ3Minutes));
        Assert.Equal(new CriticalDashboardTargetsDto(95, 90, 98, 98, 10, 10), month.Targets);
        Assert.Equal(At(10, 30), month.ServerTimeUtc);

        Assert.Equal(
            [
                new CriticalDashboardDepartmentDto(_lab.Id, _lab.Name, 4, new(1, 3), 10.0, 2),
                new CriticalDashboardDepartmentDto(_micro.Id, _micro.Name, 1, new(1, 1), 7.0, 0),
            ],
            month.Senders);
        Assert.Equal(
            [
                new CriticalDashboardDepartmentDto(_wardA.Id, _wardA.Name, 2, new(1, 2), 16.0, 0),
                new CriticalDashboardDepartmentDto(_wardB.Id, _wardB.Name, 1, new(0, 0), null, 1),
            ],
            month.Receivers);

        Assert.Equal([overdue.Id, second.Id], month.Late.Select(l => l.Id));
        Assert.Equal("Chưa báo, quá hạn 20 phút", month.Late[0].Miss);
        Assert.Equal("N.B.B", month.Late[0].PatientInitials);
        Assert.Null(month.Late[0].ReceivingDepartmentName);
        Assert.Equal("Báo sau 15 phút · BS xử trí sau 25 phút", month.Late[1].Miss);
        Assert.Equal(_wardA.Name, month.Late[1].ReceivingDepartmentName);

        Assert.Equal(12, month.Trend.Count);
        Assert.Equal("2025-12", month.Trend[0].Month);
        Assert.Equal(new CriticalDashboardMonthDto("2026-11", 5, 50.0, 50.0), month.Trend[^1]);
        Assert.Equal(new CriticalDashboardMonthDto("2026-10", 1, 0.0, 100.0), month.Trend[^2]);
        Assert.Equal(new CriticalDashboardMonthDto("2026-09", 0, null, null), month.Trend[^3]);
    }

    [Fact]
    public async Task Dashboard_QuarterCoversThreeMonthsUpToToday()
    {
        Scenario();
        ActAs(_quality);

        var quarter = Ok(await Dashboard.GetAsync("2026-11", "QUARTER", CancellationToken.None));

        Assert.Equal(("quarter", "Quý IV/2026"), (quarter.Period, quarter.Label));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 27)), (quarter.From, quarter.To));
        Assert.Equal((6, 1), (quarter.Kpis.Total, quarter.Kpis.Cancelled));
        Assert.Equal(new CriticalRateDto(2, 5), quarter.Kpis.NotifyOnTime);
        Assert.Equal(new CriticalRateDto(2, 3), quarter.Kpis.ActionOnTime);
    }

    [Theory]
    [InlineData("2026-12", null, "month")]
    [InlineData("2024-10", null, "month")]
    [InlineData("11/2026", null, "month")]
    [InlineData("2026-11", "week", "period")]
    public async Task Dashboard_RejectsMonthsOutsideTheWindowAndUnknownPeriod(string month, string? period, string field)
    {
        ActAs(_quality);

        var result = await Dashboard.GetAsync(month, period, CancellationToken.None);

        Assert.Contains(field, Fails(result, ErrorType.Validation).FieldErrors!.Keys);
    }

    [Fact]
    public async Task Dashboard_IsForQualityOnly_OldestMonthStillOpens()
    {
        ActAs(_technician);
        var technician = await Dashboard.GetAsync(null, null, CancellationToken.None);
        ActAs(_doctorA);
        var doctor = await Dashboard.GetAsync(null, null, CancellationToken.None);
        ActAs(_quality);
        var oldest = Ok(await Dashboard.GetAsync("2024-11", "month", CancellationToken.None));

        Fails(technician, ErrorType.Forbidden);
        Fails(doctor, ErrorType.Forbidden);
        Assert.Equal(0, oldest.Kpis.Total);
        Assert.Null(oldest.Kpis.NotifyOnTime.Percent);
        Assert.Null(oldest.Kpis.MedianNotifyMinutes);
    }

    [Fact]
    public void Percentile_InterpolatesLinearly()
    {
        Assert.Null(CriticalDashboardBuilder.Percentile([], 0.5));
        Assert.Equal(4.0, CriticalDashboardBuilder.Percentile([4], 0.75));
        Assert.Equal(2.5, CriticalDashboardBuilder.Percentile([1, 2, 3, 4], 0.5));
        Assert.Equal(3.3, CriticalDashboardBuilder.Percentile([1, 2, 3, 4], 0.75));
    }

    [Fact]
    public async Task LaboratoryLogbook_RowsInResultOrder_CancelledStruck_RejectedCallsAndLateEntryInNotes()
    {
        var (_, second, _, cancelled, _) = Scenario();
        var rerouted = Seed(_lab, _potassium, "Người Bệnh Chuyển Khoa", At(10, 5));
        Call(rerouted, _wardB, At(10, 8));
        rerouted.RejectNotification("Không phải khoa tôi", Actor(_doctorB), At(10, 9));
        Call(rerouted, _wardA, At(10, 10));
        Seed(_lab, _potassium, "Người Bệnh Ghi Bù", At(7, 0), recordedAt: At(7, 45));
        ActAs(_technician);

        var book = Ok(await Logbook.GetAsync(CriticalLogbookForm.Laboratory, _lab.Id, "2026-11", CancellationToken.None));

        Assert.Equal(("QT-KQNC/ST01", _lab.Name, "2026-11"), (book.Code, book.DepartmentName, book.Month));
        Assert.Equal(CriticalLogbookService.ColumnsOf(CriticalLogbookForm.Laboratory), book.Columns);
        Assert.Equal(
            ["Người Bệnh Ghi Bù", "Người Bệnh Một", "Người Bệnh Hai", "Người Bệnh Năm", "Người Bệnh Ba", "Người Bệnh Chuyển Khoa", "Người Bệnh Bốn"],
            book.Rows.Select(r => r.Cells[1].Split(',')[0]));
        Assert.Equal(
            [
                "2", "Người Bệnh Một, 02/01/1960", "Kali (K+) (giả lập)", "6,8", "mmol/L", "08:00 27/11/2026", "08:05 27/11/2026",
                "ktv.hsh Demo", "ĐD Nghe Máy (giả lập) – Khoa Nội A (giả lập)", "☒ Chính xác",
            ],
            book.Rows[1].Cells);
        Assert.Equal("☒ Chính xác (đọc lại lần 2)", book.Rows[2].Cells[9]);
        Assert.Equal(second.Value, book.Rows[2].Cells[3]);
        Assert.Equal((true, $"Đã hủy 09:50 27/11/2026 (ktv.hsh Demo): {cancelled.CancelReason}"), (book.Rows[3].Cancelled, book.Rows[3].Note));
        Assert.Equal(["Chưa báo", "", "", ""], book.Rows[4].Cells.Skip(6));
        Assert.Equal("Lần báo 1 cho Khoa Ngoại B (giả lập) bị trả lại: Không phải khoa tôi", book.Rows[5].Note);
        Assert.Equal("ĐD Nghe Máy (giả lập) – Khoa Nội A (giả lập)", book.Rows[5].Cells[8]);
        Assert.Equal("Ghi bù sau sự việc", book.Rows[0].Note);
        Assert.Equal([false, false, false, true, false, false, false], book.Rows.Select(r => r.Cancelled));
    }

    [Fact]
    public async Task ClinicalLogbook_HasCallsReceivedByTheDepartment_WithDoctorConfirmation()
    {
        Scenario();
        ActAs(_nurseA);

        var book = Ok(await Logbook.GetAsync(CriticalLogbookForm.Clinical, _wardA.Id, "2026-11", CancellationToken.None));

        Assert.Equal("QT-KQNC/ST02", book.Code);
        Assert.Equal(["Người Bệnh Một", "Người Bệnh Hai", "Người Bệnh Năm"], book.Rows.Select(r => r.Cells[1].Split(',')[0]));
        Assert.Equal(
            [
                "27/11/2026", "Người Bệnh Một, 02/01/1960", "Kali (K+) (giả lập)", "6,8 mmol/L", "08:05 27/11/2026",
                $"ktv.hsh Demo ({_lab.Name})", "ĐD Nghe Máy (giả lập)", "08:12 27/11/2026", "bacsi.noia Demo · xác nhận 08:12 27/11/2026",
                "Ngưng thuốc giữ kali (giả lập)",
            ],
            book.Rows[0].Cells);
        Assert.Equal(("", "Chưa xử trí", ""), (book.Rows[2].Cells[7], book.Rows[2].Cells[8], book.Rows[2].Cells[9]));
        Assert.True(book.Rows[2].Cancelled);
    }

    [Fact]
    public async Task MicrobiologyLogbook_HasSampleIdUrgentFlagAndRepeatTwice()
    {
        Scenario();
        ActAs(_microTechnician);

        var book = Ok(await Logbook.GetAsync(CriticalLogbookForm.Microbiology, _micro.Id, null, CancellationToken.None));
        var labBook = Ok(await Logbook.GetAsync(CriticalLogbookForm.Laboratory, _micro.Id, null, CancellationToken.None));

        Assert.Equal("QT-VS-21/ST02", book.Code);
        Assert.Equal(13, book.Columns.Count);
        Assert.Equal(
            [
                "VS-0001", "27/11/2026", "Người Bệnh Sáu", "02/01/1960", _wardB.Name, "Cấy máu (giả lập)", "☒", "10:18 27/11/2026",
                "10:25 27/11/2026", "ktv.vs Demo", "ĐD Nghe Máy (giả lập)", "Dương tính, cầu khuẩn gram dương (giả lập)", "Đã nhắc lại 2 lần",
            ],
            Assert.Single(book.Rows).Cells);
        Assert.Empty(labBook.Rows);
    }

    [Fact]
    public async Task Logbook_DepartmentSeesOwnBook_QualitySeesEveryBook()
    {
        Scenario();

        ActAs(_nurseA);
        var nurseOtherWard = await Logbook.GetAsync(CriticalLogbookForm.Clinical, _wardB.Id, null, CancellationToken.None);
        var nurseLabBook = await Logbook.GetAsync(CriticalLogbookForm.Laboratory, _wardA.Id, null, CancellationToken.None);
        ActAs(_technician);
        var otherLab = await Logbook.GetAsync(CriticalLogbookForm.Laboratory, _micro.Id, null, CancellationToken.None);
        var clinical = await Logbook.GetAsync(CriticalLogbookForm.Clinical, _lab.Id, null, CancellationToken.None);
        ActAs(_screen);
        var screen = await Logbook.GetAsync(CriticalLogbookForm.Clinical, _wardA.Id, null, CancellationToken.None);
        ActAs(_quality);
        var quality = Ok(await Logbook.GetAsync(CriticalLogbookForm.Clinical, _wardB.Id, null, CancellationToken.None));
        var unknown = await Logbook.GetAsync(CriticalLogbookForm.Clinical, Guid.NewGuid(), null, CancellationToken.None);
        var future = await Logbook.GetAsync(CriticalLogbookForm.Clinical, _wardA.Id, "2026-12", CancellationToken.None);
        var tooOld = await Logbook.GetAsync(CriticalLogbookForm.Clinical, _wardA.Id, "2024-10", CancellationToken.None);
        var form = await Logbook.GetAsync((CriticalLogbookForm)9, _wardA.Id, null, CancellationToken.None);

        Fails(nurseOtherWard, ErrorType.Forbidden);
        Fails(nurseLabBook, ErrorType.Forbidden);
        Fails(otherLab, ErrorType.Forbidden);
        Fails(clinical, ErrorType.Forbidden);
        Fails(screen, ErrorType.Forbidden);
        Assert.Equal("Người Bệnh Sáu, 02/01/1960", Assert.Single(quality.Rows).Cells[1]);
        Assert.Contains("departmentId", Fails(unknown, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Contains("month", Fails(future, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Contains("month", Fails(tooOld, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Contains("form", Fails(form, ErrorType.Validation).FieldErrors!.Keys);
    }

    [Fact]
    public async Task LogbookPdf_UsesConfiguredCodeHospitalAndLogo_AndIsAudited()
    {
        Scenario();
        ActAs(_quality);
        var settings = _ctx.CreateCriticalSettingsService();
        Ok(await settings.UpdateAsync(CriticalSettings.Default with { CodeClinical = "QT-KQNC/ST02-B" }, CancellationToken.None));
        Assert.True((await _ctx.CreateSettingsService().UploadLogoAsync(new(TestLogos.Png64), CancellationToken.None)).IsSuccess);
        ActAs(_doctorA);

        var file = Ok(await Logbook.RenderPdfAsync(CriticalLogbookForm.Clinical, _wardA.Id, "2026-11", CancellationToken.None));
        ActAs(_technician);
        var labFile = Ok(await Logbook.RenderPdfAsync(CriticalLogbookForm.Laboratory, _lab.Id, "2026-11", CancellationToken.None));
        ActAs(_microTechnician);
        var microFile = Ok(await Logbook.RenderPdfAsync(CriticalLogbookForm.Microbiology, _micro.Id, "2026-11", CancellationToken.None));
        ActAs(_doctorA);
        var forbidden = await Logbook.RenderPdfAsync(CriticalLogbookForm.Clinical, _wardB.Id, "2026-11", CancellationToken.None);

        Assert.Equal("ket-qua-nguy-cap-so-ST02-2026-11.pdf", file.FileName);
        Assert.Equal("ket-qua-nguy-cap-so-ST01-2026-11.pdf", labFile.FileName);
        Assert.Equal("ket-qua-nguy-cap-so-vi-sinh-2026-11.pdf", microFile.FileName);
        Assert.Equal("%PDF-giả"u8.ToArray(), file.Content);
        Fails(forbidden, ErrorType.Forbidden);
        var printed = _ctx.Store.AuditLogs.Where(a => a.Action == AuditActions.CriticalLogbookPrinted).ToList();
        Assert.Equal(3, printed.Count);
        Assert.Equal($"{{\"form\":\"Clinical\",\"departmentId\":\"{_wardA.Id}\",\"month\":\"2026-11\",\"rows\":3}}", printed[0].Details);
        Assert.Equal(_doctorA.Id, printed[0].UserId);

        var document = _ctx.CriticalPdf.Document!;
        Assert.Equal(CriticalLogbookForm.Microbiology, document.Form);
        Assert.Equal("QT-VS-21/ST02", document.Code);
        Assert.Equal((new HospitalOptions().ParentAgency, new HospitalOptions().Name), (document.ParentAgency, document.HospitalName));
        Assert.NotNull(document.Logo);
        Assert.Equal(_micro.Name, document.DepartmentName);
        Assert.Equal("Từ 01/11/2026 đến 30/11/2026", document.PeriodText);
        Assert.Equal("In lúc 10:30 27/11/2026 · ktv.vs Demo", document.PrintedText);
        Assert.Single(document.Rows);
    }

    [Fact]
    public async Task LogbookPdf_ClinicalCodeComesFromSettings()
    {
        ActAs(_quality);
        Ok(await _ctx.CreateCriticalSettingsService().UpdateAsync(CriticalSettings.Default with { CodeClinical = "QT-KQNC/ST02-B" }, CancellationToken.None));

        Ok(await Logbook.RenderPdfAsync(CriticalLogbookForm.Clinical, _wardA.Id, null, CancellationToken.None));

        var document = _ctx.CriticalPdf.Document!;
        Assert.Equal("QT-KQNC/ST02-B", document.Code);
        Assert.Null(document.Logo);
        Assert.Empty(document.Rows);
        Assert.Equal("In lúc 10:30 27/11/2026 · qlcl.kqnc Demo", document.PrintedText);
    }
}
