using Consent.Domain.Common;
using Consent.Domain.CriticalResults;
using Consent.Domain.Patients;

namespace Consent.Domain.Tests;

/// <summary>Kết quả nguy cấp (quyết định 60): quy tắc danh mục và hồ sơ theo QT-KQNC lần 04. Dữ liệu giả lập.</summary>
public sealed class CriticalResultTests
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    // 10:10 giờ Việt Nam ngày 27/11/2026.
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 3, 10, 0, TimeSpan.Zero);
    private static readonly Guid Lab = Guid.NewGuid();
    private static readonly Guid Ward = Guid.NewGuid();
    private static readonly Guid OtherWard = Guid.NewGuid();
    private static readonly CriticalActor Technician = new(Guid.NewGuid(), "KTV Giả Lập A");
    private static readonly CriticalActor Nurse = new(Guid.NewGuid(), "ĐD Giả Lập B");
    private static readonly CriticalActor Doctor = new(Guid.NewGuid(), "BS Giả Lập C");
    private static readonly CriticalDeadlines Deadlines = new(10, 10, 30, RequireAddress: true);

    private readonly CriticalTest _potassium = CriticalTest.Create(
        new(" Kali (K+) ", "≤ 2,5 hoặc ≥ 6,0 mmol/L", null, "mmol/L", "Xét nghiệm", CriticalLogbook.Laboratory, false, 1), Now);

    private readonly CriticalTest _culture = CriticalTest.Create(
        new("Cấy máu", "Dương tính", null, "bỏ qua", "Vi sinh", CriticalLogbook.Microbiology, true, 2), Now);

    /// <summary>Giờ Việt Nam ngày 27/11/2026, đổi sang UTC.</summary>
    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new DateTimeOffset(2026, 11, 27, hour, minute, second, Vietnam).ToUniversalTime();

    private static CriticalPatient Patient(string? address = "Ấp Mẫu, xã Mẫu") =>
        new("NB-0001", "Người Bệnh Giả Lập", new DateOnly(1960, 1, 2), Gender.Female, address, CriticalPatientSource.His);

    private static CriticalResultDetails Value(DateTimeOffset? resultAt = null, string? value = "6,8", string? unit = "mmol/L", string? sid = null) =>
        new(value, unit, new DateOnly(2026, 11, 27), resultAt ?? At(10, 1), sid, Urgent: false);

    private static CriticalNotificationDetails Call(
        DateTimeOffset? at = null, Guid? ward = null, bool correct = true, int incorrect = 0, bool twice = false, string? receiver = "ĐD Giả Lập B") =>
        new(ward ?? Ward, CriticalNotificationMethod.Phone, at ?? At(10, 5), receiver, correct, incorrect, twice);

    private CriticalResult Result(CriticalDeadlines? deadlines = null, DateTimeOffset? resultAt = null, DateTimeOffset? now = null) =>
        CriticalResult.Create("KQNC-2026-000001", Lab, Patient(), _potassium, Value(resultAt), deadlines ?? Deadlines, Technician, now ?? Now, Vietnam);

    [Fact]
    public void Catalog_TrimsText_ClearsUnitOfTextResults_AndCanBeDeactivated()
    {
        Assert.Equal(("Kali (K+)", "mmol/L", 1, true), (_potassium.Name, _potassium.Unit, _potassium.SortOrder, _potassium.IsActive));
        Assert.Null(_culture.Unit);
        Assert.True(_culture.TextResult);
        Assert.Throws<DomainException>(() => CriticalTest.Create(new(" ", "x", null, null, null, CriticalLogbook.Laboratory, false, 0), Now));
        Assert.Throws<DomainException>(() => CriticalTest.Create(new("Kali", " ", null, null, null, CriticalLogbook.Laboratory, false, 0), Now));
        Assert.Throws<DomainException>(() => CriticalTest.Create(new("Kali", "x", null, null, null, (CriticalLogbook)9, false, 0), Now));
        Assert.Throws<DomainException>(() => CriticalTest.Create(new("Kali", "x", null, null, null, CriticalLogbook.Laboratory, false, -1), Now));
        Assert.Throws<DomainException>(() => CriticalTest.Create(
            new(new string('K', CriticalTest.NameMaxLength + 1), "x", null, null, null, CriticalLogbook.Laboratory, false, 0), Now));

        _potassium.Update(new("Kali", "≥ 6,5 mmol/L", "≥ 6,0 mmol/L", "mmol/L", null, CriticalLogbook.Laboratory, false, 3), isActive: false, Now.AddDays(1));
        Assert.Equal(("≥ 6,5 mmol/L", "≥ 6,0 mmol/L", false, Now.AddDays(1)), (_potassium.AdultRange, _potassium.ChildRange, _potassium.IsActive, _potassium.UpdatedAtUtc));
        var error = Assert.Throws<DomainException>(() => Result());
        Assert.Contains("ngưng dùng", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_CopiesTestAndDeadlines_AndChecksPatientAndResult()
    {
        var result = Result(new CriticalDeadlines(15, 20, 45, true));

        Assert.Equal(CriticalResultStatus.AwaitingNotification, result.Status);
        Assert.Equal(("Kali (K+)", "≤ 2,5 hoặc ≥ 6,0 mmol/L", CriticalLogbook.Laboratory, false), (result.TestName, result.TestAdultRange, result.Logbook, result.TextResult));
        Assert.Equal((15, 20, 45), (result.NotifyDeadlineMinutes, result.ActionDeadlineMinutes, result.LateEntryMinutes));
        Assert.Equal(At(10, 16), result.NotifyDueAtUtc);
        Assert.Null(result.ActionDueAtUtc);
        Assert.Equal(("NB-0001", "Người Bệnh Giả Lập", "6,8", "mmol/L"), (result.PatientCode, result.PatientName, result.Value, result.Unit));
        Assert.Equal((Technician.UserId, Technician.Name, Now), (result.RecordedByUserId, result.RecordedByName, result.RecordedAtUtc));

        CriticalResult Make(CriticalPatient patient, CriticalResultDetails details, CriticalDeadlines? deadlines = null, CriticalTest? test = null) =>
            CriticalResult.Create("KQNC-2026-000002", Lab, patient, test ?? _potassium, details, deadlines ?? Deadlines, Technician, Now, Vietnam);

        Assert.Throws<DomainException>(() => Make(Patient() with { Code = " " }, Value()));
        Assert.Throws<DomainException>(() => Make(Patient() with { FullName = null }, Value()));
        Assert.Throws<DomainException>(() => Make(Patient() with { DateOfBirth = null }, Value()));
        Assert.Throws<DomainException>(() => Make(Patient() with { DateOfBirth = new DateOnly(2026, 11, 28) }, Value()));
        Assert.Throws<DomainException>(() => Make(Patient(address: null), Value()));
        Assert.Null(Make(Patient(address: null), Value(), Deadlines with { RequireAddress = false }).PatientAddress);
        Assert.Throws<DomainException>(() => Make(Patient(), Value(unit: " ")));
        Assert.Throws<DomainException>(() => Make(Patient(), Value(value: null)));
        Assert.Throws<DomainException>(() => Make(Patient(), Value(value: new string('9', CriticalResult.NumberValueMaxLength + 1))));
        Assert.Throws<DomainException>(() => Make(Patient(), Value(resultAt: Now.AddMinutes(CriticalResult.FutureToleranceMinutes + 1))));
        Assert.Equal(Now.AddMinutes(CriticalResult.FutureToleranceMinutes), Make(Patient(), Value(resultAt: Now.AddMinutes(CriticalResult.FutureToleranceMinutes))).ResultAtUtc);
        Assert.Throws<DomainException>(() => Make(Patient(), Value(resultAt: Now.AddDays(-CriticalResult.MaxDaysBack).AddMinutes(-1))));
        Assert.Throws<DomainException>(() => Make(Patient(), Value() with { OrderDate = new DateOnly(2026, 11, 28) }));
        Assert.Throws<DomainException>(() => Make(Patient(), Value() with { OrderDate = null }));
        Assert.Throws<DomainException>(() => Make(Patient(), Value() with { ResultAtUtc = null }));
        Assert.Throws<DomainException>(() => Make(Patient(), Value(), Deadlines with { NotifyMinutes = 0 }));
        Assert.Throws<DomainException>(() => CriticalResult.Create("KQNC-2026-000003", Guid.Empty, Patient(), _potassium, Value(), Deadlines, Technician, Now, Vietnam));
        Assert.Throws<DomainException>(() => CriticalResult.Create("KQNC-2026-000003", Lab, Patient(), _potassium, Value(), Deadlines, Technician with { UserId = Guid.Empty }, Now, Vietnam));

        // Ngày chỉ định so với ngày có kết quả theo giờ Việt Nam: 06:30 ngày 27 (23:30 UTC ngày 26) vẫn là ngày 27.
        var early = Make(Patient(), Value(resultAt: At(6, 30)));
        Assert.Equal(new DateOnly(2026, 11, 27), early.OrderDate);

        // Vi sinh: kết quả ghi bằng chữ, không có đơn vị, bắt buộc SID.
        Assert.Throws<DomainException>(() => Make(Patient(), Value(value: "Dương tính"), test: _culture));
        var culture = Make(Patient(), Value(value: "Dương tính, Gram âm", unit: "bỏ qua", sid: " VS-0001 "), test: _culture);
        Assert.Equal(("Dương tính, Gram âm", (string?)null, "VS-0001"), (culture.Value, culture.Unit, culture.SampleId));
    }

    [Fact]
    public void Notification_RequiresCorrectReadBack_AnotherWard_AndTimesInOrder()
    {
        var result = Result();
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(correct: false), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(ward: Lab), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(ward: Guid.Empty), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(at: At(10, 0)), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(at: Now.AddMinutes(CriticalResult.FutureToleranceMinutes + 1)), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(receiver: " "), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(incorrect: CriticalNotification.MaxIncorrectReadBacks + 1), Technician, Now));
        Assert.Throws<DomainException>(() => result.RecordNotification(Call() with { NotifiedAtUtc = null }, Technician, Now));
        Assert.Equal(CriticalResultStatus.AwaitingNotification, result.Status);
        Assert.Empty(result.Notifications);

        result.RecordNotification(Call(at: At(10, 11), incorrect: 1), Technician, Now.AddMinutes(1));
        Assert.Equal(CriticalResultStatus.AwaitingAcknowledgement, result.Status);
        Assert.Equal((Ward, At(10, 11), At(10, 21)), (result.ReceivingDepartmentId, result.NotifiedAtUtc, result.ActionDueAtUtc));
        var notification = Assert.Single(result.Notifications);
        Assert.Equal((1, 1, "ĐD Giả Lập B", Technician.Name), (notification.AttemptNo, notification.IncorrectReadBacks, notification.ReceiverName, notification.NotifiedByName));
        Assert.Same(notification, result.CurrentNotification);

        // Báo đúng mốc 10 phút vẫn là đúng hạn; sau mốc là quá hạn.
        Assert.True(result.NotifiedOnTime);
        var late = Result();
        late.RecordNotification(Call(at: At(10, 11, 1)), Technician, Now.AddMinutes(2));
        Assert.False(late.NotifiedOnTime);

        // Đã có lần báo thì không báo thêm khi khoa nhận chưa trả lại.
        Assert.Throws<DomainException>(() => result.RecordNotification(Call(ward: OtherWard), Technician, Now.AddMinutes(2)));
    }

    [Fact]
    public void Microbiology_RequiresRecipientToRepeatTwice()
    {
        var culture = CriticalResult.Create(
            "KQNC-2026-000004", Lab, Patient(), _culture, Value(value: "Dương tính", sid: "VS-1"), Deadlines, Technician, Now, Vietnam);
        Assert.Throws<DomainException>(() => culture.RecordNotification(Call(twice: false), Technician, Now));
        culture.RecordNotification(Call(twice: true), Technician, Now);
        Assert.True(culture.CurrentNotification!.RepeatedTwice);
    }

    [Fact]
    public void Reject_ReturnsToNotify_KeepsHistory_AndIsClosedOnceAcknowledged()
    {
        var result = Result();
        Assert.Throws<DomainException>(() => result.RejectNotification("Không có người bệnh", Nurse, Now));
        result.RecordNotification(Call(ward: OtherWard, at: At(10, 3)), Technician, Now);
        Assert.Throws<DomainException>(() => result.RejectNotification(" ", Nurse, Now));
        result.RejectNotification("Người bệnh đã chuyển khoa", Nurse, Now.AddMinutes(1));

        Assert.Equal(CriticalResultStatus.AwaitingNotification, result.Status);
        Assert.Null(result.ReceivingDepartmentId);
        Assert.Null(result.NotifiedAtUtc);
        Assert.Null(result.CurrentNotification);
        var rejected = Assert.Single(result.Notifications);
        Assert.Equal((true, "Người bệnh đã chuyển khoa", Nurse.Name), (rejected.IsRejected, rejected.RejectReason, rejected.RejectedByName));

        result.RecordNotification(Call(at: At(10, 5)), Technician, Now.AddMinutes(2));
        Assert.Equal(2, result.CurrentNotification!.AttemptNo);
        Assert.Equal(Ward, result.ReceivingDepartmentId);
        Assert.Equal(2, result.Notifications.Count);

        result.Acknowledge(Nurse, Now.AddMinutes(3));
        Assert.Throws<DomainException>(() => result.RejectNotification("Sai khoa", Nurse, Now.AddMinutes(4)));
    }

    [Fact]
    public void Acknowledge_ThenDoctorAction_CompletesWithOwnAccount()
    {
        var result = Result();
        Assert.Throws<DomainException>(() => result.Acknowledge(Nurse, Now));
        Assert.Throws<DomainException>(() => result.RecordAction("Xử trí", null, true, Doctor, Now));
        result.RecordNotification(Call(), Technician, Now);
        result.Acknowledge(Nurse, Now.AddMinutes(1));
        Assert.Equal((CriticalResultStatus.AwaitingAction, Nurse.UserId, Now.AddMinutes(1)), (result.Status, result.AcknowledgedByUserId, result.AcknowledgedAtUtc));
        Assert.Throws<DomainException>(() => result.Acknowledge(Nurse, Now.AddMinutes(1)));

        Assert.Throws<DomainException>(() => result.RecordAction(" ", null, true, Doctor, Now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => result.RecordAction("Xử trí", At(10, 4), true, Doctor, Now.AddMinutes(2)));
        Assert.Throws<DomainException>(() => result.RecordAction("Xử trí", Now.AddMinutes(5), true, Doctor, Now.AddMinutes(2)));
        result.RecordAction(" Đã đánh giá, cho y lệnh ", At(10, 15), true, Doctor, Now.AddMinutes(8));

        Assert.Equal(CriticalResultStatus.Completed, result.Status);
        Assert.Equal((Doctor.UserId, "Đã đánh giá, cho y lệnh", At(10, 15), true), (result.ActionByUserId, result.ActionSummary, result.ActionAtUtc, result.RecordedInMedicalRecord));
        Assert.Equal(Nurse.UserId, result.AcknowledgedByUserId);
        Assert.True(result.ActedOnTime);
        Assert.Throws<DomainException>(() => result.RecordAction("Lần hai", null, true, Doctor, Now.AddMinutes(9)));

        // Bác sĩ nghe máy, ghi ngay: bác sĩ là người xác nhận đã nhận; giờ xử trí mặc định là bây giờ; quá 10 phút là quá hạn.
        var direct = Result();
        direct.RecordNotification(Call(), Technician, Now);
        direct.RecordAction("Đã xử trí", null, false, Doctor, At(10, 16));
        Assert.Equal((Doctor.UserId, At(10, 16), At(10, 16)), (direct.AcknowledgedByUserId, direct.AcknowledgedAtUtc, direct.ActionAtUtc));
        Assert.False(direct.ActedOnTime);
    }

    [Fact]
    public void Cancel_RequiresReason_AndCallingTheWardOnceNotified()
    {
        var notYet = Result();
        Assert.Throws<DomainException>(() => notYet.Cancel(" ", false, Technician, Now));
        notYet.Cancel("Ghi nhầm người bệnh", receivingDepartmentInformed: false, Technician, Now);
        Assert.Equal((CriticalResultStatus.Cancelled, "Ghi nhầm người bệnh", Technician.Name), (notYet.Status, notYet.CancelReason, notYet.CancelledByName));
        Assert.Throws<DomainException>(() => notYet.Cancel("Lần hai", false, Technician, Now));
        Assert.Throws<DomainException>(() => notYet.RecordNotification(Call(), Technician, Now));

        var notified = Result();
        notified.RecordNotification(Call(), Technician, Now);
        notified.Acknowledge(Nurse, Now);
        Assert.Throws<DomainException>(() => notified.Cancel("Báo nhầm kết quả", receivingDepartmentInformed: false, Technician, Now));
        notified.Cancel("Báo nhầm kết quả", receivingDepartmentInformed: true, Technician, Now);
        Assert.True(notified.ReceivingDepartmentInformed);
        Assert.Throws<DomainException>(() => notified.RecordAction("Xử trí", null, true, Doctor, Now));
    }

    [Fact]
    public void LateEntry_FlagsStepsRecordedLongAfterTheEvent()
    {
        // Kết quả 09:39, ghi 10:10: sau 31 phút, quá ngưỡng 30.
        Assert.True(Result(resultAt: At(9, 39)).LateEntry);
        Assert.False(Result(resultAt: At(9, 40)).LateEntry);

        var notification = Result(resultAt: At(9, 40));
        notification.RecordNotification(Call(at: At(9, 45)), Technician, At(10, 16));
        Assert.True(notification.LateEntry);

        var action = Result();
        action.RecordNotification(Call(at: At(10, 5)), Technician, Now);
        action.RecordAction("Xử trí", At(10, 6), true, Doctor, At(10, 36));
        Assert.False(action.LateEntry);
        var lateAction = Result();
        lateAction.RecordNotification(Call(at: At(10, 5)), Technician, Now);
        lateAction.RecordAction("Xử trí", At(10, 6), true, Doctor, At(10, 36, 1));
        Assert.True(lateAction.LateEntry);
    }
}
