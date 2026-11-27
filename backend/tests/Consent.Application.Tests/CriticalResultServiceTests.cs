using Consent.Application.Abstractions;
using Consent.Application.CriticalResults;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.CriticalResults;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>
/// Kết quả nguy cấp (quyết định 60): danh mục, thông số vận hành, khoa cận lâm sàng ghi và báo, khoa điều trị xác nhận hoặc trả
/// lại, bác sĩ xử trí bằng tài khoản của mình, hủy hồ sơ báo nhầm, bàn làm việc hai khoa, cảnh báo (tên viết tắt trên màn hình
/// theo dõi tại khoa), tra người bệnh. Ngày 27/11/2026, giờ Việt Nam. Dữ liệu giả lập.
/// </summary>
public sealed class CriticalResultServiceTests
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    private readonly AppTestContext _ctx = new();
    private readonly Department _lab;
    private readonly Department _micro;
    private readonly Department _wardA;
    private readonly Department _wardB;
    private readonly Department _closed;
    private readonly User _technician;
    private readonly User _microTechnician;
    private readonly User _nurseA;
    private readonly User _doctorA;
    private readonly User _doctorB;
    private readonly User _quality;
    private readonly User _screen;
    private CriticalTestDto _potassium = null!;
    private CriticalTestDto _culture = null!;

    public CriticalResultServiceTests()
    {
        _lab = _ctx.AddDepartment("HSH", "Khoa Huyết học – Sinh hóa (giả lập)");
        _micro = _ctx.AddDepartment("VS", "Khoa Vi sinh (giả lập)");
        _wardA = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _wardB = _ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
        _closed = _ctx.AddDepartment("CU", "Khoa đã ngưng (giả lập)", active: false);
        var qa = _ctx.AddDepartment("QLCL", "Phòng Quản lý chất lượng (giả lập)");
        _technician = _ctx.AddUser("ktv.hsh", SystemRoles.CriticalLab, departmentId: _lab.Id);
        _microTechnician = _ctx.AddUser("ktv.vs", SystemRoles.CriticalLab, departmentId: _micro.Id);
        _nurseA = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _wardA.Id);
        _doctorA = _ctx.AddUser("bacsi.noia", SystemRoles.Doctor, departmentId: _wardA.Id);
        _doctorB = _ctx.AddUser("bacsi.ngoaib", SystemRoles.Doctor, departmentId: _wardB.Id);
        _quality = _ctx.AddUser("qlcl.kqnc", SystemRoles.CriticalQuality, departmentId: qa.Id);
        _screen = _ctx.AddUser("manhinh.noia", SystemRoles.CriticalDisplay, departmentId: _wardA.Id);
        _ctx.Clock.UtcNow = At(10, 6);
    }

    private CriticalResultService Results => _ctx.CreateCriticalResultService();

    private CriticalCatalogService Catalog => _ctx.CreateCriticalCatalogService();

    private CriticalSettingsService Settings => _ctx.CreateCriticalSettingsService();

    /// <summary>Giờ Việt Nam ngày 27/11/2026, đổi sang UTC.</summary>
    private static DateTimeOffset At(int hour, int minute) => new DateTimeOffset(2026, 11, 27, hour, minute, 0, Vietnam).ToUniversalTime();

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

    private static CriticalPatientRequest Patient(string name = "Người Bệnh Giả Lập", string code = "NB-GL-0001") =>
        new(code, name, new DateOnly(1960, 1, 2), Gender.Female, "Ấp Mẫu, xã Mẫu (giả lập)", CriticalPatientSource.His);

    private static CriticalNotificationRequest Call(Guid ward, DateTimeOffset? at = null, int incorrect = 0, bool twice = false) =>
        new(ward, CriticalNotificationMethod.Phone, at ?? At(10, 5), "ĐD Nghe Máy (giả lập)", ReadBackCorrect: true, incorrect, twice);

    private void ActAs(User user, params Guid[] otherDepartments)
    {
        _ctx.CurrentUser.UserId = user.Id;
        _ctx.CurrentUser.UserName = user.UserName;
        _ctx.CurrentUser.DepartmentId = user.DepartmentId;
        _ctx.CurrentUser.AdditionalDepartmentIds.Clear();
        _ctx.CurrentUser.AdditionalDepartmentIds.AddRange(otherDepartments);
        _ctx.CurrentUser.Permissions.Clear();
        foreach (var permission in _ctx.Store.Roles.Where(r => user.RoleIds.Contains(r.Id)).SelectMany(r => r.Permissions).Select(p => p.PermissionCode))
        {
            _ctx.CurrentUser.Permissions.Add(permission);
        }
    }

    /// <summary>QLCL khai báo danh mục: Kali (sổ xét nghiệm) và Cấy máu (sổ Vi sinh, kết quả dạng chữ).</summary>
    private async Task CatalogAsync()
    {
        ActAs(_quality);
        _potassium = Ok(await Catalog.CreateAsync(
            new("Kali (K+) (giả lập)", "Ví dụ: ≤ 2,5 hoặc ≥ 6,0", null, "mmol/L", "Sinh hóa", CriticalLogbook.Laboratory, false, 1),
            CancellationToken.None));
        _culture = Ok(await Catalog.CreateAsync(
            new("Cấy máu (giả lập)", "Ví dụ: dương tính", null, null, "Vi sinh", CriticalLogbook.Microbiology, true, 2), CancellationToken.None));
    }

    /// <summary>KTV khoa HSH ghi kết quả Kali lúc 10:01 (có lần báo nếu đã gọi điện).</summary>
    private async Task<CriticalResultDto> RecordAsync(CriticalNotificationRequest? call = null, string? name = null, DateTimeOffset? resultAt = null)
    {
        ActAs(_technician);
        return Ok(await Results.CreateAsync(
            new(_lab.Id, _potassium.Id, name is null ? Patient() : Patient(name), "6,8", "mmol/L", new DateOnly(2026, 11, 27),
                resultAt ?? At(10, 1), null, false, call),
            CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_ManagerSeesInactiveItems_LabSeesActiveItemsInOrder_OthersCannotEdit()
    {
        await CatalogAsync();
        var stopped = Ok(await Catalog.CreateAsync(
            new("Natri (giả lập)", "Ví dụ: ≤ 120", null, "mmol/L", null, CriticalLogbook.Laboratory, false, 0, IsActive: false), CancellationToken.None));

        var managed = Ok(await Catalog.ListAsync(CancellationToken.None));
        ActAs(_technician);
        var forLab = Ok(await Catalog.ListAsync(CancellationToken.None));
        var labEdit = await Catalog.UpdateAsync(
            _potassium.Id, new("Kali", "≥ 6", null, "mmol/L", null, CriticalLogbook.Laboratory, false, 1), CancellationToken.None);
        ActAs(_nurseA);
        var forNurse = await Catalog.ListAsync(CancellationToken.None);

        Assert.Equal(["Natri (giả lập)", "Kali (K+) (giả lập)", "Cấy máu (giả lập)"], managed.Select(t => t.Name));
        Assert.False(stopped.IsActive);
        Assert.Equal([_potassium.Id, _culture.Id], forLab.Select(t => t.Id));
        Assert.Null(_culture.Unit);
        Fails(labEdit, ErrorType.Forbidden);
        Fails(forNurse, ErrorType.Forbidden);
    }

    [Fact]
    public async Task Catalog_UpdateStopsItem_UnknownItemIsNotFound_InvalidItemIsRejected()
    {
        await CatalogAsync();

        var updated = Ok(await Catalog.UpdateAsync(
            _potassium.Id, new("Kali (K+) (giả lập)", "Ví dụ: ≥ 6,5", null, "mmol/L", "Sinh hóa", CriticalLogbook.Laboratory, false, 5, IsActive: false),
            CancellationToken.None));
        var missing = await Catalog.UpdateAsync(
            Guid.NewGuid(), new("Kali", "≥ 6", null, "mmol/L", null, CriticalLogbook.Laboratory, false, 1), CancellationToken.None);
        var invalid = await Catalog.CreateAsync(new("  ", "≥ 6", null, "mmol/L", null, CriticalLogbook.Laboratory, false, 1), CancellationToken.None);

        Assert.False(updated.IsActive);
        Assert.Equal(5, updated.SortOrder);
        Assert.Equal("Ví dụ: ≥ 6,5", updated.AdultRange);
        Fails(missing, ErrorType.NotFound);
        Assert.Equal("critical.rule", Fails(invalid, ErrorType.BusinessRule).Code);
    }

    [Fact]
    public async Task Create_WithCall_WaitsForAcknowledgement_AndKeepsDeadlinesOfThatMoment()
    {
        await CatalogAsync();
        ActAs(_quality);
        var settings = Ok(await Settings.GetAsync(CancellationToken.None)).Values;
        Ok(await Settings.UpdateAsync(settings with { NotifyMinutes = 15, ActionMinutes = 20 }, CancellationToken.None));
        var saves = _ctx.Store.SaveCount;

        var created = await RecordAsync(Call(_wardA.Id));
        var savesAfterCreate = _ctx.Store.SaveCount;
        ActAs(_quality);
        Ok(await Settings.UpdateAsync(settings with { NotifyMinutes = 5, ActionMinutes = 5, WarnMinutes = 2 }, CancellationToken.None));
        var later = Ok(await Results.GetAsync(created.Id, CancellationToken.None));

        Assert.Equal("KQNC-2026-000001", created.Number);
        Assert.Equal(CriticalResultStatus.AwaitingAcknowledgement, created.Status);
        Assert.Equal(_lab.Name, created.SendingDepartmentName);
        Assert.Equal(_wardA.Name, created.ReceivingDepartmentName);
        Assert.Equal("ktv.hsh Demo", created.RecordedByName);
        Assert.Equal("N.B.G.L", created.PatientInitials);
        Assert.Equal(15, later.NotifyDeadlineMinutes);
        Assert.Equal(20, later.ActionDeadlineMinutes);
        Assert.Equal(At(10, 16), later.NotifyDueAtUtc);
        Assert.Equal(At(10, 25), later.ActionDueAtUtc);
        Assert.True(later.NotifiedOnTime);
        Assert.Equal("ktv.hsh Demo", Assert.Single(later.Notifications).NotifiedByName);
        Assert.Equal(saves + 1, savesAfterCreate);
        Assert.Equal(new CriticalResultPermissionsDto(false, false, false, false, Cancel: true), created.Can);
    }

    [Fact]
    public async Task Create_IsLimitedToOwnActiveDepartment_KnownItem_ActiveReceivingDepartment()
    {
        await CatalogAsync();
        ActAs(_technician);
        CreateCriticalResultRequest Request(Guid sending, Guid test, CriticalNotificationRequest? call = null, string? value = "6,8") =>
            new(sending, test, Patient(), value, "mmol/L", new DateOnly(2026, 11, 27), At(10, 1), null, false, call);

        var otherDepartment = await Results.CreateAsync(Request(_wardA.Id, _potassium.Id), CancellationToken.None);
        var unknownTest = await Results.CreateAsync(Request(_lab.Id, Guid.NewGuid()), CancellationToken.None);
        var closedReceiver = await Results.CreateAsync(Request(_lab.Id, _potassium.Id, Call(_closed.Id)), CancellationToken.None);
        var noValue = await Results.CreateAsync(Request(_lab.Id, _potassium.Id, value: " "), CancellationToken.None);
        ActAs(_technician, _closed.Id);
        var closedSender = await Results.CreateAsync(Request(_closed.Id, _potassium.Id), CancellationToken.None);
        ActAs(_nurseA);
        var nurse = await Results.CreateAsync(Request(_wardA.Id, _potassium.Id), CancellationToken.None);

        Fails(otherDepartment, ErrorType.Forbidden);
        Assert.Contains("testId", Fails(unknownTest, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Contains("notification.receivingDepartmentId", Fails(closedReceiver, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Equal("critical.rule", Fails(noValue, ErrorType.BusinessRule).Code);
        Assert.Contains("sendingDepartmentId", Fails(closedSender, ErrorType.Validation).FieldErrors!.Keys);
        Fails(nurse, ErrorType.Forbidden);
        Assert.Empty(_ctx.Store.CriticalResults);
    }

    [Fact]
    public async Task ReceivingNurseAcknowledges_ThenDoctorRecordsActionWithOwnAccount()
    {
        await CatalogAsync();
        var created = await RecordAsync(Call(_wardA.Id));

        ActAs(_doctorB);
        var otherWard = await Results.AcknowledgeAsync(created.Id, CancellationToken.None);
        ActAs(_nurseA);
        var seenByNurse = Ok(await Results.GetAsync(created.Id, CancellationToken.None));
        _ctx.Clock.UtcNow = At(10, 7);
        var acknowledged = Ok(await Results.AcknowledgeAsync(created.Id, CancellationToken.None));
        var nurseActs = await Results.ActAsync(created.Id, new("Thở oxy", null, true), CancellationToken.None);
        ActAs(_doctorA);
        var canAct = Ok(await Results.GetAsync(created.Id, CancellationToken.None)).Can;
        _ctx.Clock.UtcNow = At(10, 12);
        var completed = Ok(await Results.ActAsync(created.Id, new("Ngưng thuốc giữ kali, làm lại điện giải đồ", null, true), CancellationToken.None));
        var twice = await Results.ActAsync(created.Id, new("Lần nữa", null, true), CancellationToken.None);

        Fails(otherWard, ErrorType.NotFound);
        Assert.True(seenByNurse.Can.Acknowledge);
        Assert.True(seenByNurse.Can.Reject);
        Assert.False(seenByNurse.Can.Act);
        Assert.False(seenByNurse.Can.Cancel);
        Assert.Equal(CriticalResultStatus.AwaitingAction, acknowledged.Status);
        Assert.Equal("dieuduong.noia Demo", acknowledged.AcknowledgedByName);
        Assert.Equal(At(10, 7), acknowledged.AcknowledgedAtUtc);
        Fails(nurseActs, ErrorType.Forbidden);
        Assert.True(canAct.Act);
        Assert.Equal(CriticalResultStatus.Completed, completed.Status);
        Assert.Equal("bacsi.noia Demo", completed.ActionByName);
        Assert.Equal(At(10, 12), completed.ActionAtUtc);
        Assert.True(completed.ActedOnTime);
        Assert.True(completed.RecordedInMedicalRecord);
        Assert.Equal("dieuduong.noia Demo", completed.AcknowledgedByName);
        Assert.Equal("critical.rule", Fails(twice, ErrorType.BusinessRule).Code);
    }

    [Fact]
    public async Task DoctorAnsweringThePhone_RecordsActionDirectly_AndBecomesAcknowledger()
    {
        await CatalogAsync();
        var created = await RecordAsync(Call(_wardA.Id));
        ActAs(_doctorA);
        _ctx.Clock.UtcNow = At(10, 20);

        var early = await Results.ActAsync(created.Id, new("Xử trí theo phác đồ", At(10, 4), false), CancellationToken.None);
        var completed = Ok(await Results.ActAsync(created.Id, new("Xử trí theo phác đồ", At(10, 18), false), CancellationToken.None));

        Fails(early, ErrorType.BusinessRule);
        Assert.Equal(CriticalResultStatus.Completed, completed.Status);
        Assert.Equal("bacsi.noia Demo", completed.AcknowledgedByName);
        Assert.Equal(At(10, 20), completed.AcknowledgedAtUtc);
        Assert.Equal(At(10, 18), completed.ActionAtUtc);
        Assert.Equal(At(10, 20), completed.ActionRecordedAtUtc);
        Assert.False(completed.ActedOnTime);
        Assert.False(completed.LateEntry);
    }

    [Fact]
    public async Task WrongDepartmentReturnsTheCall_LabCallsTheRightDepartment()
    {
        await CatalogAsync();
        var created = await RecordAsync(Call(_wardB.Id));

        ActAs(_doctorB);
        var noReason = await Results.RejectAsync(created.Id, new(" "), CancellationToken.None);
        var rejected = Ok(await Results.RejectAsync(created.Id, new("Người bệnh đã chuyển khoa Nội A"), CancellationToken.None));
        var afterReject = await Results.GetAsync(created.Id, CancellationToken.None);
        ActAs(_technician);
        var closedWard = await Results.NotifyAsync(created.Id, Call(_closed.Id, At(10, 6)), CancellationToken.None);
        var called = Ok(await Results.NotifyAsync(created.Id, Call(_wardA.Id, At(10, 6)), CancellationToken.None));
        var again = await Results.NotifyAsync(created.Id, Call(_wardA.Id, At(10, 6)), CancellationToken.None);
        ActAs(_microTechnician);
        var otherLab = await Results.NotifyAsync(created.Id, Call(_wardA.Id, At(10, 6)), CancellationToken.None);

        Fails(noReason, ErrorType.BusinessRule);
        Assert.Equal(CriticalResultStatus.AwaitingNotification, rejected.Status);
        Assert.Null(rejected.ReceivingDepartmentId);
        Assert.Equal("Người bệnh đã chuyển khoa Nội A", Assert.Single(rejected.Notifications).RejectReason);
        Fails(afterReject, ErrorType.NotFound);
        Assert.Contains("receivingDepartmentId", Fails(closedWard, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Equal(CriticalResultStatus.AwaitingAcknowledgement, called.Status);
        Assert.Equal([1, 2], called.Notifications.Select(n => n.AttemptNo));
        Assert.Equal(_wardA.Name, called.ReceivingDepartmentName);
        Assert.Equal("bacsi.ngoaib Demo", called.Notifications[0].RejectedByName);
        Fails(again, ErrorType.BusinessRule);
        Fails(otherLab, ErrorType.NotFound);
    }

    [Fact]
    public async Task LabCancelsMisreportedRecord_OnlyAfterInformingTheReceivingDepartment()
    {
        await CatalogAsync();
        var created = await RecordAsync(Call(_wardA.Id));

        ActAs(_nurseA);
        var nurseCancels = await Results.CancelAsync(created.Id, new("Nhầm", true), CancellationToken.None);
        ActAs(_technician);
        var notInformed = await Results.CancelAsync(created.Id, new("Nhập nhầm người bệnh", false), CancellationToken.None);
        var cancelled = Ok(await Results.CancelAsync(created.Id, new("Nhập nhầm người bệnh", true), CancellationToken.None));
        var twice = await Results.CancelAsync(created.Id, new("Nhập nhầm người bệnh", true), CancellationToken.None);
        ActAs(_nurseA);
        var acknowledge = await Results.AcknowledgeAsync(created.Id, CancellationToken.None);

        Fails(nurseCancels, ErrorType.Forbidden);
        Fails(notInformed, ErrorType.BusinessRule);
        Assert.Equal(CriticalResultStatus.Cancelled, cancelled.Status);
        Assert.True(cancelled.ReceivingDepartmentInformed);
        Assert.Equal("ktv.hsh Demo", cancelled.CancelledByName);
        Assert.Equal(new CriticalResultPermissionsDto(false, false, false, false, false), cancelled.Can);
        Fails(twice, ErrorType.BusinessRule);
        Fails(acknowledge, ErrorType.BusinessRule);
    }

    [Fact]
    public async Task SentList_OpenRecordsByNearestDeadline_ThenClosedRecords_ForConfiguredHours()
    {
        await CatalogAsync();
        var completed = await RecordAsync(Call(_wardA.Id), "Người Bệnh Một");
        var waiting = await RecordAsync(null, "Người Bệnh Hai", At(10, 4));
        var notified = await RecordAsync(Call(_wardA.Id, At(10, 2)), "Người Bệnh Ba", At(10, 0));
        var urgent = await RecordAsync(null, "Người Bệnh Bốn", At(9, 58));
        ActAs(_doctorA);
        Ok(await Results.ActAsync(completed.Id, new("Xử trí", null, true), CancellationToken.None));

        ActAs(_technician);
        var sent = Ok(await Results.ListSentAsync(null, CancellationToken.None));
        ActAs(_microTechnician);
        var otherLab = Ok(await Results.ListSentAsync(null, CancellationToken.None));
        var otherLabAsks = await Results.ListSentAsync(_lab.Id, CancellationToken.None);
        ActAs(_quality);
        var everyLab = Ok(await Results.ListSentAsync(null, CancellationToken.None));
        _ctx.Clock.UtcNow = At(10, 6).AddHours(24).AddMinutes(1);
        ActAs(_technician);
        var nextDay = Ok(await Results.ListSentAsync(_lab.Id, CancellationToken.None));

        // Hạn báo 10:08 (Bốn), 10:14 (Hai); hạn bác sĩ 10:12 (Ba); hồ sơ hoàn tất ở cuối.
        Assert.Equal([urgent.Id, notified.Id, waiting.Id, completed.Id], sent.Items.Select(r => r.Id));
        Assert.Equal(At(10, 6), sent.ServerTimeUtc);
        Assert.True(sent.Items[0].Can.Notify);
        Assert.Empty(otherLab.Items);
        Fails(otherLabAsks, ErrorType.Forbidden);
        Assert.Equal(4, everyLab.Items.Count);
        Assert.Equal([urgent.Id, notified.Id, waiting.Id], nextDay.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task ReceivedList_ShowsRecordsOfOwnReceivingDepartment_QualitySeesAnyDepartment()
    {
        await CatalogAsync();
        var toA = await RecordAsync(Call(_wardA.Id));
        var toB = await RecordAsync(Call(_wardB.Id), "Người Bệnh Khác");
        await RecordAsync(null, "Người Bệnh Chưa Báo");

        ActAs(_nurseA);
        var nurse = Ok(await Results.ListReceivedAsync(null, CancellationToken.None));
        var nurseAsksB = await Results.ListReceivedAsync(_wardB.Id, CancellationToken.None);
        ActAs(_quality);
        var quality = Ok(await Results.ListReceivedAsync(_wardB.Id, CancellationToken.None));
        ActAs(_screen);
        var screen = await Results.ListReceivedAsync(null, CancellationToken.None);
        ActAs(_technician);
        var technician = await Results.ListReceivedAsync(null, CancellationToken.None);

        Assert.Equal([toA.Id], nurse.Items.Select(r => r.Id));
        Fails(nurseAsksB, ErrorType.Forbidden);
        Assert.Equal([toB.Id], quality.Items.Select(r => r.Id));
        Assert.All(quality.Items, r => Assert.Equal(new CriticalResultPermissionsDto(false, false, false, false, false), r.Can));
        Fails(screen, ErrorType.Forbidden);
        Fails(technician, ErrorType.Forbidden);
    }

    [Fact]
    public async Task Alerts_DepartmentScreenSeesInitials_StaffSeeFullNames_LabSeesUnnotifiedCount()
    {
        await CatalogAsync();
        var waiting = await RecordAsync(Call(_wardA.Id), "Người Bệnh Giả Lập");
        var acknowledged = await RecordAsync(Call(_wardA.Id, At(10, 3)), "Người Bệnh Thứ Hai", At(10, 0));
        var done = await RecordAsync(Call(_wardA.Id, At(10, 4)), "Người Bệnh Thứ Ba", At(10, 2));
        await RecordAsync(null, "Người Bệnh Chưa Báo", At(9, 50));
        await RecordAsync(Call(_wardB.Id), "Người Bệnh Khoa Khác");
        ActAs(_nurseA);
        Ok(await Results.AcknowledgeAsync(acknowledged.Id, CancellationToken.None));
        ActAs(_doctorA);
        Ok(await Results.ActAsync(done.Id, new("Xử trí", null, true), CancellationToken.None));

        ActAs(_screen);
        var screen = Ok(await Results.AlertsAsync(CancellationToken.None));
        var detail = await Results.GetAsync(waiting.Id, CancellationToken.None);
        ActAs(_nurseA);
        var nurse = Ok(await Results.AlertsAsync(CancellationToken.None));
        ActAs(_technician);
        var lab = Ok(await Results.AlertsAsync(CancellationToken.None));
        ActAs(_quality);
        var values = Ok(await Settings.GetAsync(CancellationToken.None)).Values;
        Ok(await Settings.UpdateAsync(values with { DisplayFullName = true, PollSeconds = 10, SoundEnabled = false }, CancellationToken.None));
        var noAlerts = await Results.AlertsAsync(CancellationToken.None);
        ActAs(_screen);
        var fullNames = Ok(await Results.AlertsAsync(CancellationToken.None));

        var first = Assert.Single(screen.AwaitingAcknowledgement);
        Assert.Equal(waiting.Id, first.Id);
        Assert.Equal("N.B.G.L", first.PatientName);
        Assert.Equal("…0001", first.PatientCodeTail);
        Assert.Equal(1960, first.PatientBirthYear);
        Assert.Equal(At(10, 15), first.ActionDueAtUtc);
        Assert.Equal(_lab.Name, first.SendingDepartmentName);
        Assert.Equal("N.B.T.H", Assert.Single(screen.AwaitingAction).PatientName);
        Assert.Equal(done.Id, Assert.Single(screen.RecentlyCompleted).Id);
        Assert.Equal([_wardA.Name], screen.ReceivingDepartmentNames);
        Assert.Equal((3, 1, 1), (screen.TodayReceived, screen.TodayActedOnTime, screen.TodayActed));
        Assert.Equal(new CriticalAlertSettingsDto(3, 15, true, 60, true, 1200), screen.Settings);
        Assert.Equal(0, screen.AwaitingNotification);
        Fails(detail, ErrorType.NotFound);
        Assert.Equal("Người Bệnh Giả Lập", Assert.Single(nurse.AwaitingAcknowledgement).PatientName);
        Assert.Empty(lab.AwaitingAcknowledgement);
        Assert.Equal((1, 1), (lab.AwaitingNotification, lab.OverdueNotification));
        Fails(noAlerts, ErrorType.Forbidden);
        Assert.Equal("Người Bệnh Giả Lập", fullNames.AwaitingAcknowledgement[0].PatientName);
        Assert.Equal(10, fullNames.Settings.PollSeconds);
        Assert.False(fullNames.Settings.SoundEnabled);
    }

    [Fact]
    public async Task LookupPatient_FillsFromHis_FallsBackToManualEntry()
    {
        ActAs(_technician);
        _ctx.PatientSource.Result = new PatientSourceResult(
            PatientSourceStatus.Found,
            new ExternalPatient("NB-GL-0001", "Người Bệnh Giả Lập", new DateOnly(1960, 1, 2), Gender.Female, null, null,
                "Thường trú (giả lập)", "Hiện tại (giả lập)", null, null, null, null));

        var found = Ok(await Results.LookupPatientAsync(" NB-GL-0001 ", CancellationToken.None));
        _ctx.PatientSource.Throws = true;
        var unavailable = Ok(await Results.LookupPatientAsync("NB-GL-0002", CancellationToken.None));
        _ctx.PatientSource.IsEnabled = false;
        var disabled = Ok(await Results.LookupPatientAsync(" NB-GL-0003 ", CancellationToken.None));
        var empty = await Results.LookupPatientAsync(" ", CancellationToken.None);
        ActAs(_nurseA);
        var nurse = await Results.LookupPatientAsync("NB-GL-0001", CancellationToken.None);

        Assert.Equal(
            new CriticalPatientLookupDto(PatientSourceStatus.Found, "NB-GL-0001", "Người Bệnh Giả Lập", new DateOnly(1960, 1, 2), Gender.Female, "Hiện tại (giả lập)"),
            found);
        Assert.Equal(PatientSourceStatus.Unavailable, unavailable.Status);
        Assert.Equal("NB-GL-0002", unavailable.Code);
        Assert.Equal(new CriticalPatientLookupDto(PatientSourceStatus.Disabled, "NB-GL-0003", null, null, null, null), disabled);
        Fails(empty, ErrorType.Validation);
        Fails(nurse, ErrorType.Forbidden);
    }

    [Fact]
    public async Task Settings_EveryCriticalAccountReads_OnlyManagerUpdates_ChangesAreAudited()
    {
        ActAs(_screen);
        var screen = Ok(await Settings.GetAsync(CancellationToken.None));
        var screenUpdates = await Settings.UpdateAsync(screen.Values with { NotifyMinutes = 15 }, CancellationToken.None);
        ActAs(_quality);
        var saves = _ctx.Store.SaveCount;
        var unchanged = Ok(await Settings.UpdateAsync(CriticalSettings.Default, CancellationToken.None));
        var savesAfterUnchanged = _ctx.Store.SaveCount;
        var updated = Ok(await Settings.UpdateAsync(
            CriticalSettings.Default with { NotifyMinutes = 15, CodeLaboratory = "  QT-KQNC/ST01-B " }, CancellationToken.None));
        _ctx.Clock.Advance(TimeSpan.FromMinutes(5));
        var again = Ok(await Settings.UpdateAsync(updated.Values with { FlashEnabled = false }, CancellationToken.None));
        var anonymous = new AppTestContext();
        var noPermission = await anonymous.CreateCriticalSettingsService().GetAsync(CancellationToken.None);

        Assert.Equal(CriticalSettings.Default, screen.Values);
        Assert.Null(screen.UpdatedAtUtc);
        Fails(screenUpdates, ErrorType.Forbidden);
        Assert.Null(unchanged.UpdatedBy);
        Assert.Equal(saves, savesAfterUnchanged);
        Assert.Equal(15, updated.Values.NotifyMinutes);
        Assert.Equal("QT-KQNC/ST01-B", updated.Values.CodeLaboratory);
        Assert.Equal("qlcl.kqnc Demo (qlcl.kqnc)", updated.UpdatedBy);
        Assert.Equal(At(10, 11), again.UpdatedAtUtc);
        var audits = _ctx.Store.AuditLogs.Where(a => a.Action == AuditActions.CriticalSettingsUpdated).ToList();
        Assert.Equal(2, audits.Count);
        Assert.Equal("{\"NotifyMinutes\":[\"10\",\"15\"],\"CodeLaboratory\":[\"QT-KQNC/ST01\",\"QT-KQNC/ST01-B\"]}", audits[0].Details);
        Assert.Equal("{\"FlashEnabled\":[\"true\",\"false\"]}", audits[1].Details);
        Assert.Equal(
            SettingKeys.CriticalNames.Select(name => SettingKeys.CriticalPrefix + name).Order(StringComparer.Ordinal),
            _ctx.Store.SystemSettings.Select(s => s.Key).Order(StringComparer.Ordinal));
        Fails(noPermission, ErrorType.Forbidden);
    }

    [Fact]
    public async Task Settings_RejectsValuesOutOfRange_WarningMustBeShorterThanBothDeadlines()
    {
        ActAs(_quality);

        var invalid = await Settings.UpdateAsync(
            CriticalSettings.Default with
            {
                NotifyMinutes = 0,
                FlashPeriodMs = 500,
                KeepHours = 200,
                TargetCompletePercent = 101,
                CodeMicrobiology = " ",
                CodeClinical = new string('X', CriticalSettings.CodeMaxLength + 1),
            },
            CancellationToken.None);
        var warning = await Settings.UpdateAsync(CriticalSettings.Default with { ActionMinutes = 3, WarnMinutes = 3 }, CancellationToken.None);
        var shortest = Ok(await Settings.UpdateAsync(
            CriticalSettings.Default with { NotifyMinutes = 1, ActionMinutes = 1, WarnMinutes = 0, SoundRepeatSeconds = 0 }, CancellationToken.None));

        Assert.Equal(
            ["notifyMinutes", "flashPeriodMs", "keepHours", "targetCompletePercent", "codeClinical", "codeMicrobiology"],
            Fails(invalid, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Equal(["warnMinutes"], Fails(warning, ErrorType.Validation).FieldErrors!.Keys);
        Assert.Equal(1, shortest.Values.NotifyMinutes);
        Assert.DoesNotContain(_ctx.Store.SystemSettings, s => s.Key == "Critical.FlashPeriodMs" && s.Value == "500");
    }

    [Fact]
    public void Settings_StoredValueOutOfRangeOrUnreadable_FallsBackToDefault()
    {
        var now = DateTimeOffset.UnixEpoch;
        var stored = new[]
        {
            SystemSetting.Create("Critical.NotifyMinutes", "999", now),
            SystemSetting.Create("Critical.ActionMinutes", "12", now),
            SystemSetting.Create("Critical.SoundEnabled", "có", now),
            SystemSetting.Create("Critical.DisplayFullName", "true", now),
            SystemSetting.Create("Critical.CodeClinical", new string('X', CriticalSettings.CodeMaxLength + 1), now),
            SystemSetting.Create("Hospital.Name", "Không liên quan", now),
        };

        var read = CriticalSettings.Read(stored);

        Assert.Equal(CriticalSettings.Default with { ActionMinutes = 12, DisplayFullName = true }, read);
        Assert.Equal("QT-VS-21/ST02", read.CodeOf(CriticalLogbookForm.Microbiology));
        Assert.Equal(new CriticalDeadlines(10, 12, 30, true), read.Deadlines);
    }

    [Theory]
    [InlineData("Nguyễn Thị Hoa", "N.T.H")]
    [InlineData("  trần   văn  a ", "T.V.A")]
    [InlineData("Ánh", "Á")]
    [InlineData("", "")]
    public void Initials_TakeFirstLetterOfEachWord(string name, string expected) =>
        Assert.Equal(expected, CriticalResultService.Initials(name));
}
