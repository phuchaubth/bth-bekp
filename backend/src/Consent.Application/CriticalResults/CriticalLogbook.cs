using System.Globalization;
using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.QualityControl;
using Consent.Application.Settings;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.CriticalResults;
using Consent.Shared;

namespace Consent.Application.CriticalResults;

/// <summary>
/// Sổ kết quả nguy cấp điện tử (quyết định 60; QT-KQNC mục 7 cho phép bản điện tử): ST01 của khoa cận lâm sàng, ST02 của khoa
/// điều trị, sổ Khoa Vi sinh; một khoa một tháng (theo giờ có kết quả), đúng các cột của sổ giấy. Mỗi dòng lấy từ hồ sơ, không
/// nhập lại; hồ sơ đã hủy in gạch ngang kèm lý do. Khoa xem sổ của khoa mình, QLCL xem mọi khoa.
/// </summary>
public sealed class CriticalLogbookService(
    ICriticalResultRepository critical,
    IDepartmentRepository departments,
    IUserRepository users,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IClock clock,
    CriticalSettingsService settings,
    SettingsService hospital,
    ICriticalLogbookPdfRenderer pdf)
{
    public const int MaxMonthsBack = 24;

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    /// <summary>Các cột của từng mẫu sổ, theo thứ tự in.</summary>
    public static IReadOnlyList<string> ColumnsOf(CriticalLogbookForm form) => form switch
    {
        CriticalLogbookForm.Laboratory =>
        [
            "STT", "Thông tin NB (họ tên, ngày sinh)", "Tên xét nghiệm / kỹ thuật CĐHA", "Kết quả nguy cấp", "Đơn vị",
            "Thời gian có KQ", "Thời gian thông báo KQ", "Họ tên KTV thông báo", "Người nhận – khoa nhận", "Xác nhận lại thông tin",
        ],
        CriticalLogbookForm.Clinical =>
        [
            "Ngày chỉ định", "Thông tin NB (họ tên, ngày sinh)", "Tên xét nghiệm", "Kết quả (kèm đơn vị)", "Ngày, giờ báo KQ",
            "Người báo", "Người nhận điện thoại", "Ngày, giờ BS xử trí", "BS xử trí (xác nhận bằng tài khoản)", "Hướng xử trí",
        ],
        _ =>
        [
            "SID", "Ngày chỉ định", "Họ tên người bệnh", "Ngày sinh", "Nơi gửi", "Chỉ định", "Mẫu khẩn", "Giờ có KQ", "Giờ báo",
            "Người báo", "Người nhận ĐT", "Kết quả", "Ghi chú (người nhận nhắc lại 2 lần)",
        ],
    };

    public async Task<Result<CriticalLogbookDto>> GetAsync(
        CriticalLogbookForm form, Guid departmentId, string? month, CancellationToken cancellationToken)
    {
        var built = await BuildAsync(form, departmentId, month, cancellationToken).ConfigureAwait(false);
        return built.IsFailure ? built.Error! : built.Value.Dto;
    }

    /// <summary>PDF sổ (A4 ngang); ghi nhật ký vì sổ có thông tin người bệnh.</summary>
    public async Task<Result<CriticalLogbookFile>> RenderPdfAsync(
        CriticalLogbookForm form, Guid departmentId, string? month, CancellationToken cancellationToken)
    {
        var built = await BuildAsync(form, departmentId, month, cancellationToken).ConfigureAwait(false);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var (dto, first) = built.Value;
        var info = await hospital.ResolveHospitalAsync(cancellationToken).ConfigureAwait(false);
        var logo = await hospital.ResolveLogoAsync(cancellationToken).ConfigureAwait(false);
        var user = currentUser.UserId is { } id ? await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) : null;
        var now = clock.UtcNow;
        var last = first.AddMonths(1).AddDays(-1);
        var document = new CriticalLogbookDocument(
            form,
            dto.Code,
            info.ParentAgency,
            info.Name,
            logo,
            dto.DepartmentName,
            $"Từ {first:dd/MM/yyyy} đến {last:dd/MM/yyyy}",
            dto.Columns,
            dto.Rows,
            $"In lúc {Local(now)} · {user?.FullName ?? currentUser.UserName ?? "Không rõ"}");
        var content = pdf.Render(document);
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(
            now,
            AuditActions.CriticalLogbookPrinted,
            actor,
            details: JsonSerializer.Serialize(new { form = form.ToString(), departmentId, month = dto.Month, rows = dto.Rows.Count }, AuditJson)));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var prefix = form switch
        {
            CriticalLogbookForm.Laboratory => "so-ST01",
            CriticalLogbookForm.Clinical => "so-ST02",
            _ => "so-vi-sinh",
        };
        return new CriticalLogbookFile(content, $"ket-qua-nguy-cap-{prefix}-{dto.Month}.pdf");
    }

    private static string Local(DateTimeOffset utc) =>
        utc.ToOffset(ConsentFormService.VietnamOffset).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string WithUnit(CriticalResult r) => r.Unit is { Length: > 0 } unit ? $"{r.Value} {unit}" : r.Value;

    /// <summary>Ô người nhận khi khoa báo đã báo trên phần mềm, chưa gọi xác nhận (quyết định 68).</summary>
    internal const string CallPendingText = "Chưa gọi xác nhận";

    private static string Receiver(CriticalNotification notification) => notification.ReceiverName ?? CallPendingText;

    private bool InScope(Guid departmentId) =>
        currentUser.HasPermission(Permissions.ScopeAllDepartments) || currentUser.DepartmentIds.Contains(departmentId);

    private bool CanOpen(CriticalLogbookForm form, Guid departmentId)
    {
        if (currentUser.HasPermission(Permissions.CriticalMonitor))
        {
            return true;
        }

        return form == CriticalLogbookForm.Clinical
            ? (currentUser.HasPermission(Permissions.CriticalReceive) || currentUser.HasPermission(Permissions.CriticalTreat)) && InScope(departmentId)
            : currentUser.HasPermission(Permissions.CriticalRecord) && InScope(departmentId);
    }

    private async Task<Result<(CriticalLogbookDto Dto, DateOnly First)>> BuildAsync(
        CriticalLogbookForm form, Guid departmentId, string? month, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(form))
        {
            return Error.ValidationField("form", "Chọn mẫu sổ.");
        }

        if (!CanOpen(form, departmentId))
        {
            return Error.Forbidden("critical.forbidden", "Khoa xem sổ kết quả nguy cấp của khoa mình; QLCL xem mọi khoa.");
        }

        var today = CriticalResultService.LocalDate(clock.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        if (QcBookService.ParseMonth(month ?? thisMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture)) is not { } first
            || first > thisMonth || first < thisMonth.AddMonths(-MaxMonthsBack))
        {
            return Error.ValidationField("month", $"Chọn tháng dạng yyyy-MM, trong {MaxMonthsBack} tháng gần đây.");
        }

        var all = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id);
        if (!all.TryGetValue(departmentId, out var department))
        {
            return Error.ValidationField("departmentId", "Chọn khoa.");
        }

        string Name(Guid id) => all.TryGetValue(id, out var d) ? d.Name : "Không rõ";
        var values = await settings.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var results = await critical.ListByResultTimeAsync(
            CriticalDashboardService.Utc(first), CriticalDashboardService.Utc(first.AddMonths(1)), cancellationToken).ConfigureAwait(false);
        var selected = results
            .Where(r => form switch
            {
                CriticalLogbookForm.Laboratory => r.SendingDepartmentId == departmentId && r.Logbook == CriticalLogbook.Laboratory,
                CriticalLogbookForm.Microbiology => r.SendingDepartmentId == departmentId && r.Logbook == CriticalLogbook.Microbiology,
                _ => r.ReceivingDepartmentId == departmentId,
            })
            .OrderBy(r => r.ResultAtUtc)
            .ToList();

        var rows = selected.Select((r, index) =>
        {
            var current = r.CurrentNotification;
            var rejected = r.Notifications.Where(n => n.IsRejected).ToList();
            var notes = new List<string>();
            if (r.IsCancelled)
            {
                notes.Add($"Đã hủy {Local(r.CancelledAtUtc!.Value)} ({r.CancelledByName}): {r.CancelReason}");
            }

            notes.AddRange(rejected.Select(n => $"Lần báo {n.AttemptNo} cho {Name(n.ReceivingDepartmentId)} bị trả lại: {n.RejectReason}"));

            // Báo trên phần mềm trước, gọi sau (quyết định 68): ghi rõ hai mốc; chưa gọi thì sổ ghi Chưa gọi xác nhận.
            if (current is { CallPending: true })
            {
                notes.Add($"Báo trên phần mềm lúc {Local(current.NotifiedAtUtc)}, chưa gọi xác nhận người nhận");
            }
            else if (current?.CalledAtUtc is { } called)
            {
                notes.Add($"Báo trên phần mềm lúc {Local(current.NotifiedAtUtc)}, gọi xác nhận lúc {Local(called)} ({current.CallRecordedByName})");
            }

            if (r.LateEntry)
            {
                notes.Add("Ghi bù sau sự việc");
            }

            IReadOnlyList<string> cells = form switch
            {
                CriticalLogbookForm.Laboratory =>
                [
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    $"{r.PatientName}, {Date(r.PatientDateOfBirth)}",
                    r.TestName,
                    r.Value,
                    r.Unit ?? string.Empty,
                    Local(r.ResultAtUtc),
                    current is null ? "Chưa báo" : Local(current.NotifiedAtUtc),
                    current?.NotifiedByName ?? string.Empty,
                    current is null ? string.Empty : $"{Receiver(current)} – {Name(current.ReceivingDepartmentId)}",
                    current is null
                        ? string.Empty
                        : current.CallPending
                            ? CallPendingText
                            : current.IncorrectReadBacks == 0 ? "☒ Chính xác" : $"☒ Chính xác (đọc lại lần {current.IncorrectReadBacks + 1})",
                ],
                CriticalLogbookForm.Clinical =>
                [
                    Date(r.OrderDate),
                    $"{r.PatientName}, {Date(r.PatientDateOfBirth)}",
                    r.TestName,
                    WithUnit(r),
                    current is null ? string.Empty : Local(current.NotifiedAtUtc),
                    current is null ? string.Empty : $"{current.NotifiedByName} ({Name(r.SendingDepartmentId)})",
                    current is null ? string.Empty : Receiver(current),
                    r.ActionAtUtc is { } acted ? Local(acted) : string.Empty,
                    r.ActionByName is { } doctor ? $"{doctor} · xác nhận {Local(r.ActionRecordedAtUtc!.Value)}" : "Chưa xử trí",
                    r.ActionSummary ?? string.Empty,
                ],
                _ =>
                [
                    r.SampleId ?? string.Empty,
                    Date(r.OrderDate),
                    r.PatientName,
                    Date(r.PatientDateOfBirth),
                    r.ReceivingDepartmentId is { } receiving ? Name(receiving) : string.Empty,
                    r.TestName,
                    r.Urgent ? "☒" : "☐",
                    Local(r.ResultAtUtc),
                    current is null ? "Chưa báo" : Local(current.NotifiedAtUtc),
                    current?.NotifiedByName ?? string.Empty,
                    current is null ? string.Empty : Receiver(current),
                    WithUnit(r),
                    current is { RepeatedTwice: true } ? "Đã nhắc lại 2 lần" : string.Empty,
                ],
            };
            return new CriticalLogbookRow(cells, r.IsCancelled, notes.Count == 0 ? null : string.Join("; ", notes));
        }).ToList();

        var dto = new CriticalLogbookDto(
            form,
            values.CodeOf(form),
            departmentId,
            department.Name,
            first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ColumnsOf(form),
            rows);
        return (dto, first);
    }
}
