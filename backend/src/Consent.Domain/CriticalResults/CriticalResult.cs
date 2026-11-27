using Consent.Domain.Common;
using Consent.Domain.Patients;

namespace Consent.Domain.CriticalResults;

/// <summary>Bước đang chờ của hồ sơ kết quả nguy cấp.</summary>
public enum CriticalResultStatus
{
    /// <summary>Khoa cận lâm sàng đã ghi kết quả, chưa ghi lần báo (hoặc khoa nhận đã trả lại lần báo).</summary>
    AwaitingNotification,

    /// <summary>Đã báo, khoa điều trị chưa xác nhận đã nhận trên phần mềm.</summary>
    AwaitingAcknowledgement,

    /// <summary>Khoa điều trị đã nhận, bác sĩ chưa ghi hướng xử trí.</summary>
    AwaitingAction,

    Completed,

    Cancelled,
}

/// <summary>Nguồn thông tin người bệnh: lấy từ HIS theo mã người bệnh, hoặc nhập tay (HIS lỗi, chưa kết nối).</summary>
public enum CriticalPatientSource
{
    His,
    Manual,
}

/// <summary>Định danh người bệnh chép vào hồ sơ (in vào sổ): mã, họ tên, ngày sinh, giới, địa chỉ.</summary>
public sealed record CriticalPatient(
    string? Code, string? FullName, DateOnly? DateOfBirth, Gender? Gender, string? Address, CriticalPatientSource Source);

/// <summary>Kết quả khi ghi: giá trị (hoặc chữ), đơn vị, ngày chỉ định, giờ có kết quả, SID và mẫu khẩn (Vi sinh).</summary>
public sealed record CriticalResultDetails(
    string? Value, string? Unit, DateOnly? OrderDate, DateTimeOffset? ResultAtUtc, string? SampleId, bool Urgent);

/// <summary>
/// Thông số cấu hình áp dụng cho hồ sơ lúc ghi (chép vào hồ sơ: đổi cấu hình sau này không đổi hạn của hồ sơ đã có).
/// </summary>
public sealed record CriticalDeadlines(int NotifyMinutes, int ActionMinutes, int LateEntryMinutes, bool RequireAddress);

/// <summary>
/// Hồ sơ một kết quả nguy cấp (quyết định 60, theo QT-KQNC lần 04): khoa cận lâm sàng gọi điện báo khoa điều trị rồi ghi một lần
/// (định danh người bệnh, kết quả, lần báo, đọc lại); khoa điều trị xác nhận đã nhận; bác sĩ ghi hướng xử trí bằng tài khoản của
/// mình (thay chữ ký cột 9 sổ ST02). Hai mốc: báo trong <see cref="NotifyDeadlineMinutes"/> phút từ lúc có kết quả, bác sĩ có
/// hướng xử trí trong <see cref="ActionDeadlineMinutes"/> phút từ lúc khoa nhận cuộc gọi. Không sửa sau khi ghi: báo nhầm thì hủy
/// có lý do (hồ sơ giữ lại, sổ in gạch ngang) rồi ghi hồ sơ đúng. Giờ sự việc do người dùng nhập, giờ hệ thống lưu riêng.
/// </summary>
public sealed class CriticalResult : Entity, IAuditableEntity
{
    public const int NumberMaxLength = 30;
    public const int PatientCodeMaxLength = 50;
    public const int PatientNameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int TextValueMaxLength = 500;
    public const int NumberValueMaxLength = 50;
    public const int SampleIdMaxLength = 50;
    public const int SummaryMaxLength = 1000;
    public const int ReasonMaxLength = 500;
    public const int ActorNameMaxLength = 200;

    /// <summary>Giờ nhập được sớm hơn giờ máy chủ tối đa bấy nhiêu phút (lệch đồng hồ máy trạm).</summary>
    public const int FutureToleranceMinutes = 2;

    /// <summary>Ghi bù được kết quả có trước tối đa bấy nhiêu ngày (mất mạng, ghi sổ giấy rồi nhập lại).</summary>
    public const int MaxDaysBack = 31;

    private readonly List<CriticalNotification> _notifications = [];

    private CriticalResult(Guid id)
        : base(id)
    {
    }

    /// <summary>Số hồ sơ, ví dụ <c>KQNC-2026-000184</c>.</summary>
    public string Number { get; private set; } = string.Empty;

    public Guid SendingDepartmentId { get; private set; }

    public string PatientCode { get; private set; } = string.Empty;

    public string PatientName { get; private set; } = string.Empty;

    public DateOnly PatientDateOfBirth { get; private set; }

    public Gender? PatientGender { get; private set; }

    public string? PatientAddress { get; private set; }

    public CriticalPatientSource PatientSource { get; private set; }

    public Guid TestId { get; private set; }

    /// <summary>Tên xét nghiệm, kỹ thuật lúc ghi (danh mục đổi sau này không đổi hồ sơ).</summary>
    public string TestName { get; private set; } = string.Empty;

    public string TestAdultRange { get; private set; } = string.Empty;

    public string? TestChildRange { get; private set; }

    public CriticalLogbook Logbook { get; private set; }

    public bool TextResult { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public string? Unit { get; private set; }

    public DateOnly OrderDate { get; private set; }

    /// <summary>Giờ có kết quả (giờ sự việc, do khoa nhập theo phần mềm xét nghiệm).</summary>
    public DateTimeOffset ResultAtUtc { get; private set; }

    /// <summary>SID (mã mẫu), bắt buộc với sổ Vi sinh.</summary>
    public string? SampleId { get; private set; }

    public bool Urgent { get; private set; }

    public int NotifyDeadlineMinutes { get; private set; }

    public int ActionDeadlineMinutes { get; private set; }

    public int LateEntryMinutes { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public string RecordedByName { get; private set; } = string.Empty;

    /// <summary>Giờ hệ thống lúc ghi hồ sơ.</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public CriticalResultStatus Status { get; private set; }

    /// <summary>Khoa đang nhận (lần báo chưa bị trả lại); trống khi chưa báo.</summary>
    public Guid? ReceivingDepartmentId { get; private set; }

    /// <summary>Giờ báo của lần báo đang có hiệu lực: mốc bắt đầu thời hạn bác sĩ xử trí.</summary>
    public DateTimeOffset? NotifiedAtUtc { get; private set; }

    public Guid? AcknowledgedByUserId { get; private set; }

    public string? AcknowledgedByName { get; private set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }

    public Guid? ActionByUserId { get; private set; }

    public string? ActionByName { get; private set; }

    /// <summary>Giờ bác sĩ có hướng xử trí (giờ sự việc).</summary>
    public DateTimeOffset? ActionAtUtc { get; private set; }

    public string? ActionSummary { get; private set; }

    public bool RecordedInMedicalRecord { get; private set; }

    /// <summary>Giờ hệ thống lúc bác sĩ lưu hướng xử trí.</summary>
    public DateTimeOffset? ActionRecordedAtUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public string? CancelledByName { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>Người hủy xác nhận đã gọi báo khoa nhận về việc hủy (bắt buộc khi đã báo).</summary>
    public bool ReceivingDepartmentInformed { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<CriticalNotification> Notifications => _notifications;

    public DateTimeOffset NotifyDueAtUtc => ResultAtUtc.AddMinutes(NotifyDeadlineMinutes);

    public DateTimeOffset? ActionDueAtUtc => NotifiedAtUtc?.AddMinutes(ActionDeadlineMinutes);

    /// <summary>Lần báo đang có hiệu lực (khoa nhận chưa trả lại).</summary>
    public CriticalNotification? CurrentNotification =>
        ReceivingDepartmentId is null ? null : _notifications.LastOrDefault(n => !n.IsRejected);

    public bool IsCancelled => Status == CriticalResultStatus.Cancelled;

    /// <summary>Đã báo đúng hạn; <c>null</c> khi chưa báo.</summary>
    public bool? NotifiedOnTime => NotifiedAtUtc is { } at ? at <= NotifyDueAtUtc : null;

    /// <summary>Bác sĩ xử trí đúng hạn; <c>null</c> khi chưa xử trí.</summary>
    public bool? ActedOnTime => ActionAtUtc is { } at && ActionDueAtUtc is { } due ? at <= due : null;

    /// <summary>Có bước ghi sau giờ sự việc quá <see cref="LateEntryMinutes"/> phút (ghi bù khi mất mạng, ghi sổ giấy trước).</summary>
    public bool LateEntry
    {
        get
        {
            var limit = TimeSpan.FromMinutes(LateEntryMinutes);
            var notification = CurrentNotification;
            return RecordedAtUtc - ResultAtUtc > limit
                || (notification is not null && notification.RecordedAtUtc - notification.NotifiedAtUtc > limit)
                || (ActionRecordedAtUtc is { } recorded && ActionAtUtc is { } at && recorded - at > limit);
        }
    }

    /// <summary>Khoa cận lâm sàng ghi kết quả nguy cấp (chưa báo hoặc báo ngay sau đó bằng <see cref="RecordNotification"/>).</summary>
    /// <param name="localOffset">Múi giờ bệnh viện, để so ngày chỉ định với ngày có kết quả.</param>
    public static CriticalResult Create(
        string number,
        Guid sendingDepartmentId,
        CriticalPatient patient,
        CriticalTest test,
        CriticalResultDetails details,
        CriticalDeadlines deadlines,
        CriticalActor recordedBy,
        DateTimeOffset nowUtc,
        TimeSpan localOffset)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(deadlines);
        CheckActor(recordedBy);
        if (sendingDepartmentId == Guid.Empty)
        {
            throw new DomainException("Chọn khoa báo.");
        }

        if (!test.IsActive)
        {
            throw new DomainException($"{test.Name} đã ngưng dùng trong danh mục kết quả nguy cấp.");
        }

        CheckDeadlines(deadlines);
        var today = DateOnly.FromDateTime(nowUtc.ToOffset(localOffset).DateTime);
        var result = new CriticalResult(Guid.CreateVersion7())
        {
            Number = CriticalText.Required(number, NumberMaxLength, "Số hồ sơ"),
            SendingDepartmentId = sendingDepartmentId,
            TestId = test.Id,
            TestName = test.Name,
            TestAdultRange = test.AdultRange,
            TestChildRange = test.ChildRange,
            Logbook = test.Logbook,
            TextResult = test.TextResult,
            NotifyDeadlineMinutes = deadlines.NotifyMinutes,
            ActionDeadlineMinutes = deadlines.ActionMinutes,
            LateEntryMinutes = deadlines.LateEntryMinutes,
            RecordedByUserId = recordedBy.UserId,
            RecordedByName = recordedBy.Name,
            RecordedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Status = CriticalResultStatus.AwaitingNotification,
        };
        result.SetPatient(patient, today, deadlines.RequireAddress);
        result.SetDetails(details, nowUtc, localOffset);
        return result;
    }

    /// <summary>Khoa cận lâm sàng ghi lần báo sau cuộc gọi (người nhận đã đọc lại chính xác).</summary>
    public void RecordNotification(CriticalNotificationDetails details, CriticalActor by, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(details);
        CheckActor(by);
        if (Status != CriticalResultStatus.AwaitingNotification)
        {
            throw new DomainException(IsCancelled ? "Hồ sơ đã hủy." : "Hồ sơ đã có lần báo; khoa nhận phải trả lại thì mới báo lại.");
        }

        if (details.ReceivingDepartmentId == Guid.Empty)
        {
            throw new DomainException("Chọn khoa nhận.");
        }

        if (details.ReceivingDepartmentId == SendingDepartmentId)
        {
            throw new DomainException("Khoa nhận phải khác khoa báo.");
        }

        if (!details.ReadBackCorrect)
        {
            throw new DomainException("Chỉ lưu lần báo khi người nhận đã đọc lại chính xác; chưa đúng thì đọc lại kết quả lần nữa.");
        }

        if (Logbook == CriticalLogbook.Microbiology && !details.RepeatedTwice)
        {
            throw new DomainException("Sổ Vi sinh: người nhận phải nhắc lại 2 lần.");
        }

        var notifiedAt = details.NotifiedAtUtc ?? throw new DomainException("Nhập giờ báo.");
        if (notifiedAt < ResultAtUtc)
        {
            throw new DomainException("Giờ báo phải sau giờ có kết quả.");
        }

        if (notifiedAt > nowUtc.AddMinutes(FutureToleranceMinutes))
        {
            throw new DomainException("Giờ báo chưa tới.");
        }

        var notification = CriticalNotification.Create(Id, _notifications.Count + 1, details, notifiedAt, by, nowUtc);
        _notifications.Add(notification);
        ReceivingDepartmentId = details.ReceivingDepartmentId;
        NotifiedAtUtc = notifiedAt;
        Status = CriticalResultStatus.AwaitingAcknowledgement;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Khoa điều trị (người nghe máy) xác nhận đã nhận trên phần mềm.</summary>
    public void Acknowledge(CriticalActor by, DateTimeOffset nowUtc)
    {
        CheckActor(by);
        if (Status != CriticalResultStatus.AwaitingAcknowledgement)
        {
            throw new DomainException(StepMessage());
        }

        AcknowledgedByUserId = by.UserId;
        AcknowledgedByName = by.Name;
        AcknowledgedAtUtc = nowUtc;
        Status = CriticalResultStatus.AwaitingAction;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Khoa nhận trả lại lần báo: người bệnh không điều trị tại khoa, thông tin không khớp (bắt buộc lý do).</summary>
    public void RejectNotification(string? reason, CriticalActor by, DateTimeOffset nowUtc)
    {
        CheckActor(by);
        if (Status != CriticalResultStatus.AwaitingAcknowledgement || CurrentNotification is not { } current)
        {
            throw new DomainException(
                Status == CriticalResultStatus.AwaitingAction ? "Khoa đã xác nhận đã nhận, không trả lại được." : StepMessage());
        }

        current.Reject(reason, by, nowUtc);
        ReceivingDepartmentId = null;
        NotifiedAtUtc = null;
        Status = CriticalResultStatus.AwaitingNotification;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Bác sĩ xác nhận đã nhận và ghi hướng xử trí ban đầu bằng tài khoản của mình. Khoa chưa xác nhận đã nhận (bác sĩ nghe máy)
    /// thì bác sĩ là người xác nhận đã nhận.
    /// </summary>
    public void RecordAction(string? summary, DateTimeOffset? actionAtUtc, bool recordedInMedicalRecord, CriticalActor doctor, DateTimeOffset nowUtc)
    {
        CheckActor(doctor);
        if (Status is not (CriticalResultStatus.AwaitingAcknowledgement or CriticalResultStatus.AwaitingAction))
        {
            throw new DomainException(StepMessage());
        }

        var text = CriticalText.Required(summary, SummaryMaxLength, "Hướng xử trí ban đầu");
        var at = actionAtUtc ?? nowUtc;
        if (at < NotifiedAtUtc)
        {
            throw new DomainException("Giờ xử trí phải sau giờ khoa nhận cuộc gọi.");
        }

        if (at > nowUtc.AddMinutes(FutureToleranceMinutes))
        {
            throw new DomainException("Giờ xử trí chưa tới.");
        }

        if (Status == CriticalResultStatus.AwaitingAcknowledgement)
        {
            AcknowledgedByUserId = doctor.UserId;
            AcknowledgedByName = doctor.Name;
            AcknowledgedAtUtc = nowUtc;
        }

        ActionByUserId = doctor.UserId;
        ActionByName = doctor.Name;
        ActionAtUtc = at;
        ActionSummary = text;
        RecordedInMedicalRecord = recordedInMedicalRecord;
        ActionRecordedAtUtc = nowUtc;
        Status = CriticalResultStatus.Completed;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Khoa báo hủy hồ sơ báo nhầm (QT-KQNC mục 6.8; bắt buộc lý do). Đã báo khoa nhận thì phải gọi báo khoa nhận về việc hủy trước.
    /// Hồ sơ giữ lại; khoa báo ghi hồ sơ đúng.
    /// </summary>
    public void Cancel(string? reason, bool receivingDepartmentInformed, CriticalActor by, DateTimeOffset nowUtc)
    {
        CheckActor(by);
        if (IsCancelled)
        {
            throw new DomainException("Hồ sơ đã hủy.");
        }

        var text = CriticalText.Required(reason, ReasonMaxLength, "Lý do hủy");
        if (ReceivingDepartmentId is not null && !receivingDepartmentInformed)
        {
            throw new DomainException("Hồ sơ đã báo khoa nhận: gọi báo khoa nhận về việc hủy trước khi hủy.");
        }

        CancelReason = text;
        ReceivingDepartmentInformed = receivingDepartmentInformed;
        CancelledByUserId = by.UserId;
        CancelledByName = by.Name;
        CancelledAtUtc = nowUtc;
        Status = CriticalResultStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    private static void CheckActor(CriticalActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId == Guid.Empty || string.IsNullOrWhiteSpace(actor.Name) || actor.Name.Length > ActorNameMaxLength)
        {
            throw new DomainException("Thiếu người thao tác.");
        }
    }

    private static void CheckDeadlines(CriticalDeadlines deadlines)
    {
        if (deadlines.NotifyMinutes is < 1 or > 1440 || deadlines.ActionMinutes is < 1 or > 1440
            || deadlines.LateEntryMinutes is < 1 or > 10080)
        {
            throw new DomainException("Thông số thời hạn không hợp lệ.");
        }
    }

    private string StepMessage() => Status switch
    {
        CriticalResultStatus.AwaitingNotification => "Hồ sơ chưa báo khoa nhận.",
        CriticalResultStatus.AwaitingAction => "Khoa đã xác nhận đã nhận.",
        CriticalResultStatus.Completed => "Hồ sơ đã hoàn tất.",
        CriticalResultStatus.Cancelled => "Hồ sơ đã hủy.",
        _ => "Hồ sơ chưa tới bước này.",
    };

    private void SetPatient(CriticalPatient patient, DateOnly today, bool requireAddress)
    {
        if (!Enum.IsDefined(patient.Source))
        {
            throw new DomainException("Nguồn thông tin người bệnh không hợp lệ.");
        }

        if (patient.Gender is { } gender && !Enum.IsDefined(gender))
        {
            throw new DomainException("Giới tính không hợp lệ.");
        }

        var dob = patient.DateOfBirth ?? throw new DomainException("Nhập ngày sinh người bệnh.");
        if (dob > today || dob.Year < 1900)
        {
            throw new DomainException("Ngày sinh người bệnh không hợp lệ.");
        }

        PatientCode = CriticalText.Required(patient.Code, PatientCodeMaxLength, "Mã người bệnh");
        PatientName = CriticalText.Required(patient.FullName, PatientNameMaxLength, "Họ tên người bệnh");
        PatientDateOfBirth = dob;
        PatientGender = patient.Gender;
        PatientAddress = requireAddress
            ? CriticalText.Required(patient.Address, AddressMaxLength, "Địa chỉ người bệnh")
            : CriticalText.Optional(patient.Address, AddressMaxLength, "Địa chỉ người bệnh");
        PatientSource = patient.Source;
    }

    private void SetDetails(CriticalResultDetails details, DateTimeOffset nowUtc, TimeSpan localOffset)
    {
        Value = TextResult
            ? CriticalText.Required(details.Value, TextValueMaxLength, "Kết quả")
            : CriticalText.Required(details.Value, NumberValueMaxLength, "Giá trị kết quả");
        Unit = TextResult ? null : CriticalText.Required(details.Unit, CriticalTest.UnitMaxLength, "Đơn vị");
        var resultAt = details.ResultAtUtc ?? throw new DomainException("Nhập giờ có kết quả.");
        if (resultAt > nowUtc.AddMinutes(FutureToleranceMinutes))
        {
            throw new DomainException("Giờ có kết quả chưa tới.");
        }

        if (resultAt < nowUtc.AddDays(-MaxDaysBack))
        {
            throw new DomainException($"Chỉ ghi được kết quả có trong {MaxDaysBack} ngày gần đây.");
        }

        var orderDate = details.OrderDate ?? throw new DomainException("Nhập ngày chỉ định.");
        var resultDate = DateOnly.FromDateTime(resultAt.ToOffset(localOffset).DateTime);
        if (orderDate > resultDate || orderDate < resultDate.AddDays(-MaxDaysBack))
        {
            throw new DomainException($"Ngày chỉ định phải trước hoặc bằng ngày có kết quả, trong {MaxDaysBack} ngày.");
        }

        SampleId = Logbook == CriticalLogbook.Microbiology
            ? CriticalText.Required(details.SampleId, SampleIdMaxLength, "SID")
            : CriticalText.Optional(details.SampleId, SampleIdMaxLength, "SID");
        OrderDate = orderDate;
        ResultAtUtc = resultAt;
        Urgent = details.Urgent;
    }
}
