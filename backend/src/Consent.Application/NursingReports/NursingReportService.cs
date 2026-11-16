using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Application.Settings;
using Consent.Domain.Authorization;
using Consent.Domain.Departments;
using Consent.Domain.NursingReports;
using Consent.Shared;

namespace Consent.Application.NursingReports;

/// <summary>
/// Báo cáo công tác điều dưỡng hằng ngày của khoa (quyết định 31): điều dưỡng của khoa lập, lưu nháp và gửi; gửi xong khóa,
/// Phòng Điều dưỡng mở lại khi cần sửa (bắt buộc ghi lý do). Mọi thay đổi được ghi nhật ký; phần báo cáo nhập thêm (có thể có
/// thông tin người bệnh) được mã hóa khi lưu và che trong nhật ký.
/// </summary>
public sealed class NursingReportService(
    INursingReportRepository reports,
    IDepartmentRepository departments,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    SettingsService settings,
    INursingReportPdfRenderer pdf)
{
    /// <summary>Lập hoặc sửa được báo cáo của tối đa bấy nhiêu ngày trước (bổ sung báo cáo quên gửi).</summary>
    public const int MaxDaysBack = 366;

    private static readonly Error Forbidden = Error.Forbidden(
        "nursing_reports.forbidden", "Chỉ điều dưỡng của khoa lập được báo cáo của khoa; Phòng Điều dưỡng xem được mọi khoa.");

    private static readonly Error NotFound = Error.NotFound("nursing_reports.not_found", "Không tìm thấy báo cáo.");

    public static NursingReportFormDto Form => NursingReportForm.Definition;

    private bool CanSubmit => currentUser.HasPermission(Permissions.NursingReportsSubmit);

    private bool CanReadAll =>
        currentUser.HasPermission(Permissions.NursingReportsRead) || currentUser.HasPermission(Permissions.NursingReportsManage);

    private bool CanManage => currentUser.HasPermission(Permissions.NursingReportsManage);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    /// <summary>Báo cáo của khoa trong ngày; chưa có thì trả mẫu trống, điền sẵn nhân sự và chỉ tiêu theo báo cáo đã gửi gần nhất.</summary>
    public async Task<Result<NursingReportDto>> GetAsync(Guid? departmentId, DateOnly reportDate, CancellationToken cancellationToken)
    {
        if ((departmentId ?? currentUser.DepartmentId) is not { } id)
        {
            return Error.ValidationField("departmentId", "Vui lòng chọn khoa.");
        }

        if (!CanView(id))
        {
            return Forbidden;
        }

        var department = await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            return Error.NotFound("departments.not_found", "Không tìm thấy khoa.");
        }

        var report = await reports.FindAsync(id, reportDate, cancellationToken).ConfigureAwait(false);
        if (report is not null)
        {
            return await ToDtoAsync(report, department, cancellationToken).ConfigureAwait(false);
        }

        var previous = await reports.FindLatestSubmittedBeforeAsync(id, reportDate, cancellationToken).ConfigureAwait(false);
        var carried = previous is null ? NursingReportFigures.Empty : NursingReportFigures.Parse(previous.FiguresJson).CarryOver();
        var reporting = await IsReportingAsync(id, cancellationToken).ConfigureAwait(false);
        return new NursingReportDto(
            null, department.Id, department.Name, reportDate, NursingReportState.New, carried.Values, carried.Checks, null,
            CanEdit: CanEdit(id) && reporting && IsEditableDate(reportDate), CanReopen: false,
            null, null, null, null, null, null, null, [.. carried.Values.Keys], reporting);
    }

    /// <summary>Lưu báo cáo của khoa trong ngày (tạo mới nếu chưa có); <see cref="SaveNursingReportRequest.Submit"/> thì gửi luôn.</summary>
    public async Task<Result<NursingReportDto>> SaveAsync(SaveNursingReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.DepartmentId ?? currentUser.DepartmentId) is not { } id)
        {
            return Error.ValidationField("departmentId", "Vui lòng chọn khoa.");
        }

        if (!CanEdit(id) || currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        if (request.ReportDate > Today)
        {
            return Error.ValidationField("reportDate", "Không lập báo cáo cho ngày sau hôm nay.");
        }

        if (!IsEditableDate(request.ReportDate))
        {
            return Error.ValidationField("reportDate", $"Chỉ lập hoặc sửa được báo cáo trong {MaxDaysBack} ngày gần đây.");
        }

        var department = await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (department is not { IsActive: true })
        {
            return Error.ValidationField("departmentId", "Khoa không tồn tại hoặc đã ngừng sử dụng.");
        }

        if (!await IsReportingAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return Error.BusinessRule(
                "nursing_reports.department_not_reporting",
                "Khoa chưa có trong danh sách khoa báo cáo điều dưỡng. Vui lòng liên hệ Phòng Điều dưỡng.");
        }

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var figures = Figures(request);
        if (request.Submit && !figures.AllChecksAnswered)
        {
            return Error.ValidationField("checks", "Chọn \"Ổn\" hoặc \"Không ổn\" cho mọi dòng của phần tick trước khi gửi.");
        }

        var now = clock.UtcNow;
        var report = await reports.FindAsync(id, request.ReportDate, cancellationToken).ConfigureAwait(false);
        if (report is { IsSubmitted: true })
        {
            return Error.BusinessRule(
                "nursing_reports.submitted",
                "Báo cáo đã gửi, không sửa được. Cần sửa thì đề nghị Phòng Điều dưỡng mở lại.");
        }

        if (report is null)
        {
            report = NursingDailyReport.Create(id, request.ReportDate, figures.ToJson(), request.Notes, userId, now);
            reports.Add(report);
        }
        else
        {
            report.Update(figures.ToJson(), request.Notes, userId, now);
        }

        if (request.Submit)
        {
            report.Submit(userId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(report, department, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Phòng Điều dưỡng mở lại báo cáo đã gửi để khoa sửa và gửi lại (bắt buộc ghi lý do, ghi nhật ký).</summary>
    public async Task<Result<NursingReportDto>> ReopenAsync(Guid id, ReopenNursingReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanManage || currentUser.UserId is not { } userId)
        {
            return Error.Forbidden("nursing_reports.forbidden", "Chỉ Phòng Điều dưỡng mở lại được báo cáo đã gửi.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > NursingDailyReport.ReopenReasonMaxLength)
        {
            return Error.ValidationField("reason", $"Nhập lý do mở lại (tối đa {NursingDailyReport.ReopenReasonMaxLength} ký tự).");
        }

        var report = await reports.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return NotFound;
        }

        if (!report.IsSubmitted)
        {
            return Error.BusinessRule("nursing_reports.not_submitted", "Báo cáo chưa gửi nên không cần mở lại.");
        }

        report.Reopen(reason, userId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var department = await departments.GetByIdAsync(report.DepartmentId, cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(report, department!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>PDF báo cáo của khoa theo bố cục bản giấy (bản nháp có ghi chú "Bản nháp" ở cuối trang).</summary>
    public async Task<Result<NursingReportFile>> RenderPdfAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await reports.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (report is null || !CanView(report.DepartmentId))
        {
            return NotFound;
        }

        var department = await departments.GetByIdAsync(report.DepartmentId, cancellationToken).ConfigureAwait(false);
        var hospital = await settings.ResolveHospitalAsync(cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync([report.SubmittedByUserId], cancellationToken).ConfigureAwait(false);
        var status = report.IsSubmitted
            ? $"Đã gửi lúc {Local(report.SubmittedAtUtc!.Value)}{By(names, report.SubmittedByUserId)}"
            : $"Bản nháp, chưa gửi Phòng Điều dưỡng (in lúc {Local(clock.UtcNow)})";
        var content = pdf.RenderReport(new NursingReportPdfDocument(
            hospital.Name,
            department?.Name ?? string.Empty,
            report.ReportDate,
            NursingReportFigures.Parse(report.FiguresJson),
            report.Notes,
            status));
        return new NursingReportFile(
            content,
            "application/pdf",
            string.Create(CultureInfo.InvariantCulture, $"bao-cao-dieu-duong-{department?.Code ?? "khoa"}-{report.ReportDate:yyyy-MM-dd}.pdf"));
    }

    internal static string Local(DateTimeOffset utc) =>
        utc.ToOffset(ConsentFormService.VietnamOffset).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string By(IReadOnlyDictionary<Guid, string> names, Guid? userId) =>
        userId is { } id && names.TryGetValue(id, out var name) ? $" bởi {name}" : string.Empty;

    private bool CanEdit(Guid departmentId) =>
        CanSubmit && (currentUser.HasPermission(Permissions.ScopeAllDepartments) || departmentId == currentUser.DepartmentId);

    private bool CanView(Guid departmentId) => CanReadAll || CanEdit(departmentId);

    private bool IsEditableDate(DateOnly date) => date <= Today && Today.DayNumber - date.DayNumber <= MaxDaysBack;

    private async Task<bool> IsReportingAsync(Guid departmentId, CancellationToken cancellationToken) =>
        (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false)).Any(d => d.DepartmentId == departmentId);

    private static Error? Validate(SaveNursingReportRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, value) in request.Values ?? new Dictionary<string, int?>())
        {
            if (!NursingReportForm.ValueKeys.Contains(key))
            {
                errors[$"values.{key}"] = ["Dòng số liệu không có trong mẫu báo cáo."];
            }
            else if (value is < 0 or > NursingReportForm.MaxValue)
            {
                errors[$"values.{key}"] = [$"Nhập số từ 0 đến {NursingReportForm.MaxValue}."];
            }
        }

        foreach (var (key, value) in request.Checks ?? new Dictionary<string, string?>())
        {
            if (!NursingReportForm.CheckKeys.Contains(key))
            {
                errors[$"checks.{key}"] = ["Dòng không có trong phần tick."];
            }
            else if (value is not (null or NursingReportForm.CheckOk or NursingReportForm.CheckIssue))
            {
                errors[$"checks.{key}"] = ["Chọn \"Ổn\" hoặc \"Không ổn\"."];
            }
        }

        if (request.Notes is { Length: > NursingDailyReport.NotesMaxLength })
        {
            errors["notes"] = [$"Phần báo cáo nhập thêm tối đa {NursingDailyReport.NotesMaxLength} ký tự."];
        }

        return errors.Count == 0 ? null : Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
    }

    private static NursingReportFigures Figures(SaveNursingReportRequest request) => new(
        (request.Values ?? new Dictionary<string, int?>())
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value!.Value, StringComparer.Ordinal),
        (request.Checks ?? new Dictionary<string, string?>())
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal));

    private async Task<NursingReportDto> ToDtoAsync(NursingDailyReport report, Department department, CancellationToken cancellationToken)
    {
        var figures = NursingReportFigures.Parse(report.FiguresJson);
        var names = await NamesAsync([report.UpdatedByUserId, report.SubmittedByUserId, report.ReopenedByUserId], cancellationToken)
            .ConfigureAwait(false);
        var reporting = await IsReportingAsync(department.Id, cancellationToken).ConfigureAwait(false);
        return new NursingReportDto(
            report.Id,
            department.Id,
            department.Name,
            report.ReportDate,
            report.IsSubmitted ? NursingReportState.Submitted : NursingReportState.Draft,
            figures.Values,
            figures.Checks,
            report.Notes,
            CanEdit: !report.IsSubmitted && CanEdit(department.Id) && reporting && IsEditableDate(report.ReportDate),
            CanReopen: report.IsSubmitted && CanManage,
            names.GetValueOrDefault(report.UpdatedByUserId),
            report.UpdatedAtUtc,
            report.SubmittedByUserId is { } submittedBy && report.IsSubmitted ? names.GetValueOrDefault(submittedBy) : null,
            report.IsSubmitted ? report.SubmittedAtUtc : null,
            report.ReopenedByUserId is { } reopenedBy ? names.GetValueOrDefault(reopenedBy) : null,
            report.ReopenedAtUtc,
            report.ReopenReason,
            [],
            reporting);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in userIds.OfType<Guid>().Distinct())
        {
            if (await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        return names;
    }
}
