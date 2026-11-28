using Consent.Application.Abstractions;
using Consent.Application.Settings;
using Consent.Domain.CriticalResults;
using Consent.Domain.Patients;

namespace Consent.Application.CriticalResults;

/// <summary>Mẫu sổ in: ST01 (khoa cận lâm sàng), ST02 (khoa điều trị), sổ Khoa Vi sinh.</summary>
public enum CriticalLogbookForm
{
    Laboratory,
    Clinical,
    Microbiology,
}

/// <summary>Kho hồ sơ và danh mục kết quả nguy cấp (quyết định 60).</summary>
public interface ICriticalResultRepository
{
    void Add(CriticalTest test);

    void Add(CriticalResult result);

    Task<IReadOnlyList<CriticalTest>> ListTestsAsync(CancellationToken cancellationToken);

    Task<CriticalTest?> GetTestAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Hồ sơ kèm các lần báo.</summary>
    Task<CriticalResult?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Hồ sơ của các khoa báo: đang mở, hoặc hoàn tất, hủy từ <paramref name="closedSinceUtc"/>; hồ sơ hoàn tất mà chưa gọi xác nhận
    /// người nhận (quyết định 68) thì luôn có.
    /// </summary>
    Task<IReadOnlyList<CriticalResult>> ListSentAsync(
        IReadOnlyCollection<Guid>? departmentIds, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken);

    /// <summary>Hồ sơ gửi đến các khoa nhận (lần báo đang có hiệu lực): đang mở, hoặc hoàn tất, hủy từ <paramref name="closedSinceUtc"/>.</summary>
    Task<IReadOnlyList<CriticalResult>> ListReceivedAsync(
        IReadOnlyCollection<Guid>? departmentIds, DateTimeOffset closedSinceUtc, CancellationToken cancellationToken);

    /// <summary>Hồ sơ có kết quả trong khoảng [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), kèm các lần báo.</summary>
    Task<IReadOnlyList<CriticalResult>> ListByResultTimeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

/// <summary>Số hồ sơ <c>KQNC-{năm}-{số thứ tự}</c>, không trùng kể cả khi ghi đồng thời.</summary>
public interface ICriticalResultNumberGenerator
{
    Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

/// <summary>Dựng PDF sổ kết quả nguy cấp theo mẫu (tầng Infrastructure).</summary>
public interface ICriticalLogbookPdfRenderer
{
    byte[] Render(CriticalLogbookDocument document);
}

public sealed record SaveCriticalTestRequest(
    string? Name,
    string? AdultRange,
    string? ChildRange,
    string? Unit,
    string? Section,
    CriticalLogbook Logbook,
    bool TextResult,
    int SortOrder,
    bool IsActive = true);

public sealed record CriticalTestDto(
    Guid Id,
    string Name,
    string AdultRange,
    string? ChildRange,
    string? Unit,
    string? Section,
    CriticalLogbook Logbook,
    bool TextResult,
    int SortOrder,
    bool IsActive)
{
    public static CriticalTestDto From(CriticalTest test) => new(
        test.Id, test.Name, test.AdultRange, test.ChildRange, test.Unit, test.Section, test.Logbook, test.TextResult, test.SortOrder, test.IsActive);
}

/// <summary>Người bệnh lấy từ HIS theo mã người bệnh (để điền sẵn); HIS lỗi, chưa kết nối thì nhập tay.</summary>
public sealed record CriticalPatientLookupDto(
    PatientSourceStatus Status, string? Code, string? FullName, DateOnly? DateOfBirth, Gender? Gender, string? Address);

public sealed record CriticalPatientRequest(
    string? Code, string? FullName, DateOnly? DateOfBirth, Gender? Gender, string? Address, CriticalPatientSource Source);

/// <summary>
/// Lần báo. <see cref="Method"/> là <see cref="CriticalNotificationMethod.App"/> (quyết định 68, "Lưu, gọi sau"): báo khoa nhận
/// trên phần mềm lúc lưu, chỉ cần khoa nhận; các trường của cuộc gọi bỏ qua, ghi sau bằng <see cref="CriticalCallRequest"/>.
/// </summary>
public sealed record CriticalNotificationRequest(
    Guid ReceivingDepartmentId,
    CriticalNotificationMethod Method,
    DateTimeOffset? NotifiedAtUtc,
    string? ReceiverName,
    bool ReadBackCorrect,
    int IncorrectReadBacks,
    bool RepeatedTwice);

/// <summary>Ghi kết quả nguy cấp; có <see cref="Notification"/> thì ghi luôn lần báo (gọi điện xong mới ghi).</summary>
public sealed record CreateCriticalResultRequest(
    Guid SendingDepartmentId,
    Guid TestId,
    CriticalPatientRequest? Patient,
    string? Value,
    string? Unit,
    DateOnly? OrderDate,
    DateTimeOffset? ResultAtUtc,
    string? SampleId,
    bool Urgent,
    CriticalNotificationRequest? Notification);

/// <summary>Cuộc gọi xác nhận người nhận thông tin sau khi đã báo trên phần mềm (quyết định 68).</summary>
public sealed record CriticalCallRequest(
    CriticalNotificationMethod Method,
    DateTimeOffset? CalledAtUtc,
    string? ReceiverName,
    bool ReadBackCorrect,
    int IncorrectReadBacks,
    bool RepeatedTwice);

public sealed record CriticalReasonRequest(string? Reason);

public sealed record CriticalActionRequest(string? Summary, DateTimeOffset? ActionAtUtc, bool RecordedInMedicalRecord);

public sealed record CriticalCancelRequest(string? Reason, bool ReceivingDepartmentInformed);

/// <param name="Method">Cách báo; <c>App</c> khi đã báo trên phần mềm, chưa gọi xác nhận (quyết định 68).</param>
/// <param name="ReceiverName">Người nhận thông tin; <c>null</c> khi chưa gọi xác nhận.</param>
/// <param name="CalledAtUtc">Giờ gọi xác nhận, khi đã báo trên phần mềm trước rồi gọi sau.</param>
/// <param name="CallPending">Đã báo trên phần mềm, chưa gọi xác nhận, lần báo chưa bị trả lại.</param>
public sealed record CriticalNotificationDto(
    Guid Id,
    int AttemptNo,
    Guid ReceivingDepartmentId,
    string ReceivingDepartmentName,
    CriticalNotificationMethod Method,
    DateTimeOffset NotifiedAtUtc,
    string? ReceiverName,
    int IncorrectReadBacks,
    bool RepeatedTwice,
    string NotifiedByName,
    DateTimeOffset RecordedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    string? RejectedByName,
    string? RejectReason,
    DateTimeOffset? CalledAtUtc,
    string? CallRecordedByName,
    DateTimeOffset? CallRecordedAtUtc,
    bool CallPending);

/// <summary>Người đang xem làm được gì với hồ sơ (giao diện ẩn nút; máy chủ vẫn kiểm tra).</summary>
/// <param name="ConfirmCall">Ghi cuộc gọi xác nhận người nhận (khoa báo, hồ sơ chưa gọi xác nhận).</param>
public sealed record CriticalResultPermissionsDto(bool Notify, bool Acknowledge, bool Reject, bool Act, bool Cancel, bool ConfirmCall);

public sealed record CriticalResultDto(
    Guid Id,
    string Number,
    CriticalResultStatus Status,
    Guid SendingDepartmentId,
    string SendingDepartmentName,
    Guid? ReceivingDepartmentId,
    string? ReceivingDepartmentName,
    string PatientCode,
    string PatientName,
    string PatientInitials,
    DateOnly PatientDateOfBirth,
    Gender? PatientGender,
    string? PatientAddress,
    CriticalPatientSource PatientSource,
    Guid TestId,
    string TestName,
    string TestAdultRange,
    string? TestChildRange,
    CriticalLogbook Logbook,
    bool TextResult,
    string Value,
    string? Unit,
    DateOnly OrderDate,
    DateTimeOffset ResultAtUtc,
    string? SampleId,
    bool Urgent,
    int NotifyDeadlineMinutes,
    int ActionDeadlineMinutes,
    DateTimeOffset NotifyDueAtUtc,
    DateTimeOffset? NotifiedAtUtc,
    DateTimeOffset? ActionDueAtUtc,
    string RecordedByName,
    DateTimeOffset RecordedAtUtc,
    string? AcknowledgedByName,
    DateTimeOffset? AcknowledgedAtUtc,
    string? ActionByName,
    DateTimeOffset? ActionAtUtc,
    string? ActionSummary,
    bool RecordedInMedicalRecord,
    DateTimeOffset? ActionRecordedAtUtc,
    string? CancelledByName,
    DateTimeOffset? CancelledAtUtc,
    string? CancelReason,
    bool ReceivingDepartmentInformed,
    bool? NotifiedOnTime,
    bool? ActedOnTime,
    bool LateEntry,
    bool CallPending,
    IReadOnlyList<CriticalNotificationDto> Notifications,
    CriticalResultPermissionsDto Can);

/// <summary>Danh sách hồ sơ của bàn làm việc kèm giờ máy chủ (đồng hồ đếm ngược trên giao diện theo giờ máy chủ).</summary>
public sealed record CriticalResultListDto(DateTimeOffset ServerTimeUtc, IReadOnlyList<CriticalResultDto> Items);

/// <summary>Một hồ sơ trên dải cảnh báo và màn hình theo dõi tại khoa (tối thiểu thông tin cần để theo dõi).</summary>
/// <param name="PatientName">Họ tên đầy đủ, hoặc tên viết tắt với tài khoản màn hình theo dõi (trừ khi cấu hình cho hiện đầy đủ).</param>
/// <param name="ReceiverName">Người nhận thông tin; <c>null</c> khi khoa báo đã báo trên phần mềm, chưa gọi xác nhận.</param>
/// <param name="CallPending">Báo trên phần mềm, khoa báo chưa gọi xác nhận (quyết định 68).</param>
public sealed record CriticalAlertItemDto(
    Guid Id,
    string Number,
    CriticalResultStatus Status,
    Guid ReceivingDepartmentId,
    string ReceivingDepartmentName,
    string SendingDepartmentName,
    string PatientName,
    int PatientBirthYear,
    string PatientCodeTail,
    string TestName,
    string Value,
    string? Unit,
    DateTimeOffset NotifiedAtUtc,
    DateTimeOffset ActionDueAtUtc,
    string? ReceiverName,
    bool CallPending,
    string NotifiedByName,
    string? AcknowledgedByName,
    DateTimeOffset? AcknowledgedAtUtc,
    string? ActionByName,
    DateTimeOffset? ActionAtUtc,
    bool? ActedOnTime);

/// <summary>Thông số giao diện dùng cho cảnh báo (không cần quyền quản lý cấu hình).</summary>
public sealed record CriticalAlertSettingsDto(
    int WarnMinutes, int PollSeconds, bool SoundEnabled, int SoundRepeatSeconds, bool FlashEnabled, int FlashPeriodMs);

/// <summary>
/// Cảnh báo cho tài khoản đang đăng nhập: hồ sơ gửi đến các khoa nhận của mình chờ xác nhận, chờ bác sĩ, vừa hoàn tất; số hồ sơ
/// khoa báo của mình chưa báo (và đã quá hạn báo).
/// </summary>
public sealed record CriticalAlertsDto(
    DateTimeOffset ServerTimeUtc,
    CriticalAlertSettingsDto Settings,
    IReadOnlyList<string> ReceivingDepartmentNames,
    IReadOnlyList<CriticalAlertItemDto> AwaitingAcknowledgement,
    IReadOnlyList<CriticalAlertItemDto> AwaitingAction,
    IReadOnlyList<CriticalAlertItemDto> RecentlyCompleted,
    int TodayReceived,
    int TodayActedOnTime,
    int TodayActed,
    int AwaitingNotification,
    int OverdueNotification);

/// <summary>Tỷ lệ: tử số trên mẫu số (mẫu số 0 là không áp dụng); phần trăm làm tròn 1 chữ số thập phân (0,05 làm tròn lên).</summary>
public sealed record CriticalRateDto(int Numerator, int Denominator)
{
    public double? Percent => Denominator == 0 ? null : Math.Round(100.0 * Numerator / Denominator, 1, MidpointRounding.AwayFromZero);
}

public sealed record CriticalDashboardKpisDto(
    int Total,
    int Cancelled,
    CriticalRateDto NotifyOnTime,
    CriticalRateDto ActionOnTime,
    CriticalRateDto ReadBackFirstTime,
    CriticalRateDto Complete,
    double? MedianNotifyMinutes,
    double? NotifyQ1Minutes,
    double? NotifyQ3Minutes,
    double? MedianActionMinutes,
    double? ActionQ1Minutes,
    double? ActionQ3Minutes);

public sealed record CriticalDashboardMonthDto(string Month, int Total, double? NotifyOnTimePercent, double? ActionOnTimePercent);

public sealed record CriticalDashboardDepartmentDto(Guid DepartmentId, string Name, int Total, CriticalRateDto OnTime, double? MedianMinutes, int Open);

/// <summary>Một trường hợp quá hạn (báo chậm, bác sĩ xử trí chậm, hoặc đang quá hạn chưa xong) để phân tích nguyên nhân.</summary>
public sealed record CriticalDashboardLateDto(
    Guid Id,
    string Number,
    DateTimeOffset ResultAtUtc,
    string PatientInitials,
    string PatientCode,
    string TestName,
    string Value,
    string? Unit,
    string SendingDepartmentName,
    string? ReceivingDepartmentName,
    string Miss);

public sealed record CriticalDashboardTargetsDto(int NotifyPercent, int ActionPercent, int ReadBackPercent, int CompletePercent, int NotifyMinutes, int ActionMinutes);

public sealed record CriticalDashboardDto(
    string Period,
    string Month,
    string Label,
    DateOnly From,
    DateOnly To,
    DateTimeOffset ServerTimeUtc,
    CriticalDashboardTargetsDto Targets,
    CriticalDashboardKpisDto Kpis,
    IReadOnlyList<CriticalDashboardMonthDto> Trend,
    IReadOnlyList<CriticalDashboardDepartmentDto> Senders,
    IReadOnlyList<CriticalDashboardDepartmentDto> Receivers,
    IReadOnlyList<CriticalDashboardLateDto> Late,
    Guid? DepartmentId = null,
    string? DepartmentName = null);

/// <summary>Một dòng sổ: các ô theo đúng cột của mẫu; dòng của hồ sơ đã hủy in gạch ngang kèm ghi chú.</summary>
public sealed record CriticalLogbookRow(IReadOnlyList<string> Cells, bool Cancelled, string? Note);

/// <summary>Sổ một khoa một tháng, dùng chung cho xem trước và PDF.</summary>
public sealed record CriticalLogbookDto(
    CriticalLogbookForm Form,
    string Code,
    Guid DepartmentId,
    string DepartmentName,
    string Month,
    IReadOnlyList<string> Columns,
    IReadOnlyList<CriticalLogbookRow> Rows);

/// <summary>Dữ liệu dựng PDF sổ: đầu trang (cơ quan, đơn vị, logo), mã số, khoa, tháng, các dòng.</summary>
public sealed record CriticalLogbookDocument(
    CriticalLogbookForm Form,
    string Code,
    string ParentAgency,
    string HospitalName,
    LogoImage? Logo,
    string DepartmentName,
    string PeriodText,
    IReadOnlyList<string> Columns,
    IReadOnlyList<CriticalLogbookRow> Rows,
    string PrintedText);

public sealed record CriticalLogbookFile(byte[] Content, string FileName);
