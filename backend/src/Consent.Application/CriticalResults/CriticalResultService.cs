using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.Patients;
using Consent.Domain.Authorization;
using Consent.Domain.Common;
using Consent.Domain.CriticalResults;
using Consent.Domain.Departments;
using Consent.Shared;

namespace Consent.Application.CriticalResults;

/// <summary>
/// Hồ sơ kết quả nguy cấp (quyết định 60, QT-KQNC lần 04). Khoa cận lâm sàng (<see cref="Permissions.CriticalRecord"/>, khoa của
/// mình) gọi điện rồi ghi kết quả và lần báo, hủy hồ sơ báo nhầm; khoa điều trị (<see cref="Permissions.CriticalReceive"/>) xác
/// nhận đã nhận hoặc trả lại lần báo không đúng khoa; bác sĩ (<see cref="Permissions.CriticalTreat"/>) ghi hướng xử trí bằng tài
/// khoản của mình; màn hình theo dõi tại khoa (<see cref="Permissions.CriticalDisplay"/>) chỉ đọc cảnh báo; QLCL
/// (<see cref="Permissions.CriticalMonitor"/>) xem mọi hồ sơ. Quy tắc của hồ sơ nằm ở <see cref="CriticalResult"/>.
/// </summary>
public sealed class CriticalResultService(
    ICriticalResultRepository critical,
    ICriticalResultNumberGenerator numbers,
    IDepartmentRepository departments,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    CriticalSettingsService settings,
    PatientSourceService patientSource)
{
    /// <summary>Dải cảnh báo, màn hình theo dõi: tối đa bấy nhiêu hồ sơ vừa hoàn tất.</summary>
    public const int RecentLimit = 10;

    private static readonly Error Forbidden = Error.Forbidden(
        "critical.forbidden", "Không có quyền với hồ sơ kết quả nguy cấp này.");

    private static readonly Error NotFound = Error.NotFound("critical.not_found", "Không tìm thấy hồ sơ kết quả nguy cấp.");

    private bool AllDepartments => currentUser.HasPermission(Permissions.ScopeAllDepartments);

    private bool CanMonitor => currentUser.HasPermission(Permissions.CriticalMonitor);

    /// <summary>Tra người bệnh trên HIS theo mã để điền sẵn (khoa cận lâm sàng khi ghi).</summary>
    public async Task<Result<CriticalPatientLookupDto>> LookupPatientAsync(string? code, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.CriticalRecord))
        {
            return Forbidden;
        }

        var lookup = await patientSource.LookupAsync(new PatientSourceLookupRequest(code, null), cancellationToken).ConfigureAwait(false);
        if (lookup.IsFailure)
        {
            return lookup.Error!;
        }

        var patient = lookup.Value.Patient;
        return new CriticalPatientLookupDto(
            lookup.Value.Status,
            patient?.PatientCode ?? code?.Trim(),
            patient?.FullName,
            patient?.DateOfBirth,
            patient?.Gender,
            patient?.CurrentAddress ?? patient?.PermanentAddress);
    }

    /// <summary>Khoa cận lâm sàng ghi kết quả nguy cấp; có lần báo thì ghi luôn (thường ghi sau khi gọi điện xong).</summary>
    public async Task<Result<CriticalResultDto>> CreateAsync(CreateCriticalResultRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!currentUser.HasPermission(Permissions.CriticalRecord))
        {
            return Forbidden;
        }

        if (!CanRecord(request.SendingDepartmentId))
        {
            return Error.Forbidden("critical.forbidden", "Chỉ ghi kết quả nguy cấp cho khoa của mình.");
        }

        var all = await DepartmentsAsync(cancellationToken).ConfigureAwait(false);
        if (!all.TryGetValue(request.SendingDepartmentId, out var sending) || !sending.IsActive)
        {
            return Error.ValidationField("sendingDepartmentId", "Chọn khoa báo đang sử dụng.");
        }

        if (request.TestId == Guid.Empty || await critical.GetTestAsync(request.TestId, cancellationToken).ConfigureAwait(false) is not { } test)
        {
            return Error.ValidationField("testId", "Chọn xét nghiệm, kỹ thuật trong danh mục kết quả nguy cấp.");
        }

        if (request.Notification is { } notification
            && (!all.TryGetValue(notification.ReceivingDepartmentId, out var receiving) || !receiving.IsActive))
        {
            return Error.ValidationField("notification.receivingDepartmentId", "Chọn khoa nhận đang sử dụng.");
        }

        if (await ActorAsync(cancellationToken).ConfigureAwait(false) is not { } actor)
        {
            return Forbidden;
        }

        var current = await settings.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var patient = request.Patient ?? new CriticalPatientRequest(null, null, null, null, null, CriticalPatientSource.Manual);
        CriticalResult result;
        try
        {
            var number = await numbers.NextAsync(now, cancellationToken).ConfigureAwait(false);
            result = CriticalResult.Create(
                number,
                request.SendingDepartmentId,
                new CriticalPatient(patient.Code, patient.FullName, patient.DateOfBirth, patient.Gender, patient.Address, patient.Source),
                test,
                new CriticalResultDetails(request.Value, request.Unit, request.OrderDate, request.ResultAtUtc, request.SampleId, request.Urgent),
                current.Deadlines,
                actor,
                now,
                ConsentFormService.VietnamOffset);
            if (request.Notification is { } call)
            {
                result.RecordNotification(Details(call), actor, now);
            }
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("critical.rule", ex.Message);
        }

        critical.Add(result);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(result, all);
    }

    /// <summary>Khoa cận lâm sàng ghi lần báo (sau cuộc gọi) cho hồ sơ chưa báo hoặc bị khoa nhận trả lại.</summary>
    public Task<Result<CriticalResultDto>> NotifyAsync(Guid id, CriticalNotificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, r => CanRecord(r.SendingDepartmentId), (r, actor, now, all) =>
        {
            if (!all.TryGetValue(request.ReceivingDepartmentId, out var receiving) || !receiving.IsActive)
            {
                return Error.ValidationField("receivingDepartmentId", "Chọn khoa nhận đang sử dụng.");
            }

            r.RecordNotification(Details(request), actor, now);
            return null;
        }, cancellationToken);
    }

    /// <summary>Khoa điều trị (người nghe máy) xác nhận đã nhận.</summary>
    public Task<Result<CriticalResultDto>> AcknowledgeAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, CanReceiveResult, (r, actor, now, _) =>
        {
            r.Acknowledge(actor, now);
            return null;
        }, cancellationToken);

    /// <summary>Khoa nhận trả lại lần báo: không phải khoa của người bệnh, thông tin không khớp.</summary>
    public Task<Result<CriticalResultDto>> RejectAsync(Guid id, CriticalReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, CanReceiveResult, (r, actor, now, _) =>
        {
            r.RejectNotification(request.Reason, actor, now);
            return null;
        }, cancellationToken);
    }

    /// <summary>Bác sĩ khoa nhận xác nhận đã nhận và ghi hướng xử trí ban đầu.</summary>
    public Task<Result<CriticalResultDto>> ActAsync(Guid id, CriticalActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, r => r.ReceivingDepartmentId is { } d && CanTreat(d), (r, actor, now, _) =>
        {
            r.RecordAction(request.Summary, request.ActionAtUtc, request.RecordedInMedicalRecord, actor, now);
            return null;
        }, cancellationToken);
    }

    /// <summary>Khoa báo hủy hồ sơ báo nhầm (bắt buộc lý do).</summary>
    public Task<Result<CriticalResultDto>> CancelAsync(Guid id, CriticalCancelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, r => CanRecord(r.SendingDepartmentId), (r, actor, now, _) =>
        {
            r.Cancel(request.Reason, request.ReceivingDepartmentInformed, actor, now);
            return null;
        }, cancellationToken);
    }

    public async Task<Result<CriticalResultDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await critical.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } result || !CanViewDetail(result))
        {
            return NotFound;
        }

        return ToDto(result, await DepartmentsAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Bàn làm việc "Khoa tôi báo": hồ sơ đang mở và vừa xong (trong thời gian giữ trên bàn làm việc) của khoa báo.</summary>
    public async Task<Result<CriticalResultListDto>> ListSentAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.CriticalRecord) && !CanMonitor)
        {
            return Forbidden;
        }

        if (departmentId is { } only && !CanRecord(only) && !CanMonitor)
        {
            return Forbidden;
        }

        var current = await settings.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        IReadOnlyCollection<Guid>? scope = departmentId is { } d ? [d] : AllDepartments || CanMonitor ? null : currentUser.DepartmentIds;
        var items = await critical.ListSentAsync(scope, now.AddHours(-current.KeepHours), cancellationToken).ConfigureAwait(false);
        return await ListAsync(items, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bàn làm việc "Gửi đến khoa tôi": hồ sơ khoa nhận đang mở và vừa xong.</summary>
    public async Task<Result<CriticalResultListDto>> ListReceivedAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        var receives = currentUser.HasPermission(Permissions.CriticalReceive) || currentUser.HasPermission(Permissions.CriticalTreat);
        if (!receives && !CanMonitor)
        {
            return Forbidden;
        }

        if (departmentId is { } only && !(receives && InScope(only)) && !CanMonitor)
        {
            return Forbidden;
        }

        var current = await settings.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        IReadOnlyCollection<Guid>? scope = departmentId is { } d ? [d] : AllDepartments || CanMonitor ? null : currentUser.DepartmentIds;
        var items = await critical.ListReceivedAsync(scope, now.AddHours(-current.KeepHours), cancellationToken).ConfigureAwait(false);
        return await ListAsync(items, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cảnh báo cho dải đỏ trên mọi trang và màn hình theo dõi tại khoa: hồ sơ gửi đến khoa của mình chờ xác nhận, chờ bác sĩ, vừa
    /// hoàn tất; số hồ sơ khoa báo của mình chưa báo. Tài khoản chỉ có quyền màn hình theo dõi thấy tên viết tắt (trừ khi cấu hình
    /// cho hiện đầy đủ).
    /// </summary>
    public async Task<Result<CriticalAlertsDto>> AlertsAsync(CancellationToken cancellationToken)
    {
        var staff = currentUser.HasPermission(Permissions.CriticalReceive) || currentUser.HasPermission(Permissions.CriticalTreat);
        var display = currentUser.HasPermission(Permissions.CriticalDisplay);
        var records = currentUser.HasPermission(Permissions.CriticalRecord);
        if (!staff && !display && !records)
        {
            return Forbidden;
        }

        var current = await settings.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var all = await DepartmentsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<Guid>? scope = AllDepartments ? null : currentUser.DepartmentIds;
        IReadOnlyList<CriticalResult> received = staff || display
            ? await critical.ListReceivedAsync(scope, now.AddHours(-current.KeepHours), cancellationToken).ConfigureAwait(false)
            : [];
        IReadOnlyList<CriticalResult> sent = records
            ? [.. (await critical.ListSentAsync(scope, now, cancellationToken).ConfigureAwait(false))
                .Where(r => r.Status == CriticalResultStatus.AwaitingNotification)]
            : [];
        var fullNames = staff || records || CanMonitor || current.DisplayFullName;
        var today = LocalDate(now);
        var todayReceived = received.Where(r => r.NotifiedAtUtc is { } at && LocalDate(at) == today).ToList();
        var todayActed = todayReceived.Where(r => r.ActedOnTime is not null).ToList();
        return new CriticalAlertsDto(
            now,
            new CriticalAlertSettingsDto(
                current.WarnMinutes, current.PollSeconds, current.SoundEnabled, current.SoundRepeatSeconds, current.FlashEnabled, current.FlashPeriodMs),
            scope is null
                ? ["Toàn viện"]
                : [.. scope.Select(id => all.TryGetValue(id, out var dept) ? dept.Name : null).OfType<string>()],
            [.. received.Where(r => r.Status == CriticalResultStatus.AwaitingAcknowledgement).OrderBy(r => r.NotifiedAtUtc).Select(r => Alert(r, all, fullNames))],
            [.. received.Where(r => r.Status == CriticalResultStatus.AwaitingAction).OrderBy(r => r.ActionDueAtUtc).Select(r => Alert(r, all, fullNames))],
            [.. received.Where(r => r.Status == CriticalResultStatus.Completed).OrderByDescending(r => r.ActionAtUtc).Take(RecentLimit).Select(r => Alert(r, all, fullNames))],
            todayReceived.Count,
            todayActed.Count(r => r.ActedOnTime == true),
            todayActed.Count,
            sent.Count,
            sent.Count(r => now > r.NotifyDueAtUtc));
    }

    /// <summary>Tên viết tắt: chữ đầu mỗi tiếng, viết hoa, cách nhau bằng dấu chấm ("Nguyễn Thị Hoa" thành "N.T.H").</summary>
    public static string Initials(string fullName) =>
        string.Join('.', (fullName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]).ToString()));

    internal static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(utc.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    private static CriticalNotificationDetails Details(CriticalNotificationRequest request) => new(
        request.ReceivingDepartmentId,
        request.Method,
        request.NotifiedAtUtc,
        request.ReceiverName,
        request.ReadBackCorrect,
        request.IncorrectReadBacks,
        request.RepeatedTwice);

    private static string Name(IReadOnlyDictionary<Guid, Department> all, Guid id) =>
        all.TryGetValue(id, out var department) ? department.Name : "Không rõ";

    private static CriticalAlertItemDto Alert(CriticalResult r, IReadOnlyDictionary<Guid, Department> all, bool fullNames)
    {
        var notification = r.CurrentNotification!;
        var code = r.PatientCode;
        return new CriticalAlertItemDto(
            r.Id,
            r.Number,
            r.Status,
            r.ReceivingDepartmentId!.Value,
            Name(all, r.ReceivingDepartmentId.Value),
            Name(all, r.SendingDepartmentId),
            fullNames ? r.PatientName : Initials(r.PatientName),
            r.PatientDateOfBirth.Year,
            code.Length <= 4 ? code : "…" + code[^4..],
            r.TestName,
            r.Value,
            r.Unit,
            r.NotifiedAtUtc!.Value,
            r.ActionDueAtUtc!.Value,
            notification.ReceiverName,
            notification.NotifiedByName,
            r.AcknowledgedByName,
            r.AcknowledgedAtUtc,
            r.ActionByName,
            r.ActionAtUtc,
            r.ActedOnTime);
    }

    private bool InScope(Guid departmentId) => AllDepartments || currentUser.DepartmentIds.Contains(departmentId);

    private bool CanRecord(Guid departmentId) => currentUser.HasPermission(Permissions.CriticalRecord) && InScope(departmentId);

    private bool CanReceive(Guid departmentId) => currentUser.HasPermission(Permissions.CriticalReceive) && InScope(departmentId);

    private bool CanTreat(Guid departmentId) => currentUser.HasPermission(Permissions.CriticalTreat) && InScope(departmentId);

    /// <summary>Người nghe máy hoặc bác sĩ của khoa nhận.</summary>
    private bool CanReceiveResult(CriticalResult result) =>
        result.ReceivingDepartmentId is { } d && (CanReceive(d) || CanTreat(d));

    /// <summary>Xem chi tiết: khoa báo, khoa nhận (người nghe máy, bác sĩ), QLCL; tài khoản màn hình theo dõi thì không.</summary>
    private bool CanViewDetail(CriticalResult result) =>
        CanMonitor || CanRecord(result.SendingDepartmentId) || CanReceiveResult(result);

    private async Task<Result<CriticalResultListDto>> ListAsync(
        IReadOnlyList<CriticalResult> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var all = await DepartmentsAsync(cancellationToken).ConfigureAwait(false);
        static bool Closed(CriticalResult r) => r.Status is CriticalResultStatus.Completed or CriticalResultStatus.Cancelled;

        // Hồ sơ đang mở theo hạn gần nhất trước (hạn báo khi chưa báo, hạn bác sĩ khi đã báo); hồ sơ đã xong mới nhất trước.
        var open = items
            .Where(r => !Closed(r))
            .OrderBy(r => r.Status == CriticalResultStatus.AwaitingNotification ? r.NotifyDueAtUtc : r.ActionDueAtUtc ?? r.NotifyDueAtUtc);
        var closed = items.Where(Closed).OrderByDescending(r => r.UpdatedAtUtc);
        return new CriticalResultListDto(now, [.. open.Concat(closed).Select(r => ToDto(r, all))]);
    }

    private async Task<Result<CriticalResultDto>> ChangeAsync(
        Guid id,
        Func<CriticalResult, bool> allowed,
        Func<CriticalResult, CriticalActor, DateTimeOffset, IReadOnlyDictionary<Guid, Department>, Error?> change,
        CancellationToken cancellationToken)
    {
        if (await critical.GetAsync(id, cancellationToken).ConfigureAwait(false) is not { } result || !CanViewDetail(result))
        {
            return NotFound;
        }

        if (!allowed(result) || await ActorAsync(cancellationToken).ConfigureAwait(false) is not { } actor)
        {
            return Forbidden;
        }

        var all = await DepartmentsAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (change(result, actor, clock.UtcNow, all) is { } error)
            {
                return error;
            }
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("critical.rule", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(result, all);
    }

    private async Task<CriticalActor?> ActorAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } id)
        {
            return null;
        }

        var user = await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        var name = user?.FullName ?? currentUser.UserName ?? "Không rõ";
        return new CriticalActor(id, name.Length <= CriticalResult.ActorNameMaxLength ? name : name[..CriticalResult.ActorNameMaxLength]);
    }

    private async Task<IReadOnlyDictionary<Guid, Department>> DepartmentsAsync(CancellationToken cancellationToken) =>
        (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id);

    private CriticalResultDto ToDto(CriticalResult r, IReadOnlyDictionary<Guid, Department> all)
    {
        var receiving = r.ReceivingDepartmentId;
        var can = new CriticalResultPermissionsDto(
            Notify: r.Status == CriticalResultStatus.AwaitingNotification && CanRecord(r.SendingDepartmentId),
            Acknowledge: r.Status == CriticalResultStatus.AwaitingAcknowledgement && CanReceiveResult(r),
            Reject: r.Status == CriticalResultStatus.AwaitingAcknowledgement && CanReceiveResult(r),
            Act: r.Status is CriticalResultStatus.AwaitingAcknowledgement or CriticalResultStatus.AwaitingAction
                && receiving is { } d && CanTreat(d),
            Cancel: r.Status != CriticalResultStatus.Cancelled && CanRecord(r.SendingDepartmentId));
        return new CriticalResultDto(
            r.Id,
            r.Number,
            r.Status,
            r.SendingDepartmentId,
            Name(all, r.SendingDepartmentId),
            receiving,
            receiving is { } id ? Name(all, id) : null,
            r.PatientCode,
            r.PatientName,
            Initials(r.PatientName),
            r.PatientDateOfBirth,
            r.PatientGender,
            r.PatientAddress,
            r.PatientSource,
            r.TestId,
            r.TestName,
            r.TestAdultRange,
            r.TestChildRange,
            r.Logbook,
            r.TextResult,
            r.Value,
            r.Unit,
            r.OrderDate,
            r.ResultAtUtc,
            r.SampleId,
            r.Urgent,
            r.NotifyDeadlineMinutes,
            r.ActionDeadlineMinutes,
            r.NotifyDueAtUtc,
            r.NotifiedAtUtc,
            r.ActionDueAtUtc,
            r.RecordedByName,
            r.RecordedAtUtc,
            r.AcknowledgedByName,
            r.AcknowledgedAtUtc,
            r.ActionByName,
            r.ActionAtUtc,
            r.ActionSummary,
            r.RecordedInMedicalRecord,
            r.ActionRecordedAtUtc,
            r.CancelledByName,
            r.CancelledAtUtc,
            r.CancelReason,
            r.ReceivingDepartmentInformed,
            r.NotifiedOnTime,
            r.ActedOnTime,
            r.LateEntry,
            [.. r.Notifications.OrderBy(n => n.AttemptNo).Select(n => new CriticalNotificationDto(
                n.Id,
                n.AttemptNo,
                n.ReceivingDepartmentId,
                Name(all, n.ReceivingDepartmentId),
                n.Method,
                n.NotifiedAtUtc,
                n.ReceiverName,
                n.IncorrectReadBacks,
                n.RepeatedTwice,
                n.NotifiedByName,
                n.RecordedAtUtc,
                n.RejectedAtUtc,
                n.RejectedByName,
                n.RejectReason))],
            can);
    }
}
