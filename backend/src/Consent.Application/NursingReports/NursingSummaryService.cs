using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Application.Settings;
using Consent.Domain.NursingReports;
using Consent.Shared;

namespace Consent.Application.NursingReports;

/// <summary>
/// Bảng tổng hợp báo cáo điều dưỡng hằng ngày của Phòng Điều dưỡng (quyết định 31): xem, in PDF, xuất Excel; cài khoa phải
/// báo cáo và số giường; lấy số liệu HIS (người bệnh đến khám, chẩn đoán hình ảnh, chờ nhập viện) hoặc nhập tay khi HIS lỗi.
/// Quyền được kiểm tra ở API (<c>nursingreports.read</c> để xem, <c>nursingreports.manage</c> để sửa).
/// </summary>
public sealed class NursingSummaryService(
    INursingReportRepository reports,
    IDepartmentRepository departments,
    IUserRepository users,
    IDailyStatisticsSource statisticsSource,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    SettingsService settings,
    INursingReportPdfRenderer pdf,
    INursingSummaryWorkbookWriter workbook)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    public async Task<NursingSummaryDto> GetAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        var allDepartments = await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        var configured = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.DepartmentId);
        var dayReports = await reports.ListByDateAsync(reportDate, cancellationToken).ConfigureAwait(false);
        var statistics = await reports.FindStatisticsAsync(reportDate, cancellationToken).ConfigureAwait(false);

        var names = await NamesAsync(
            dayReports.Where(r => r.IsSubmitted).Select(r => r.SubmittedByUserId).Append(statistics?.UpdatedByUserId),
            cancellationToken).ConfigureAwait(false);
        var summaryDepartments = allDepartments
            .Select(d => new NursingSummaryDepartment(
                d.Id, d.Name, d.IsActive && configured.ContainsKey(d.Id), configured.TryGetValue(d.Id, out var c) ? c.BedCount : null))
            .ToList();
        var info = new NursingStatisticsInfoDto(
            statisticsSource.IsEnabled,
            statistics is not null,
            statistics?.Source,
            statistics?.UpdatedAtUtc,
            statistics is null ? null : names.GetValueOrDefault(statistics.UpdatedByUserId));
        return NursingSummaryBuilder.Build(
            reportDate,
            summaryDepartments,
            dayReports,
            names,
            NursingStatisticsValues.Parse(statistics?.ValuesJson),
            info);
    }

    public async Task<NursingReportFile> RenderPdfAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        var document = await DocumentAsync(reportDate, cancellationToken).ConfigureAwait(false);
        return new NursingReportFile(pdf.RenderSummary(document), "application/pdf", FileName(reportDate, "pdf"));
    }

    public async Task<NursingReportFile> RenderWorkbookAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        var document = await DocumentAsync(reportDate, cancellationToken).ConfigureAwait(false);
        return new NursingReportFile(
            workbook.Write(document),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName(reportDate, "xlsx"));
    }

    /// <summary>
    /// Lấy số liệu của ngày từ HIS và lưu lại (thay số liệu cũ của ngày, kể cả số nhập tay). Mã khoa HIS trả về mà không khớp
    /// khoa nào thì trả lại để phòng CNTT sửa câu truy vấn.
    /// </summary>
    public async Task<Result<NursingStatisticsRefreshDto>> RefreshStatisticsAsync(
        RefreshNursingStatisticsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.required", "Vui lòng đăng nhập.");
        }

        if (request.ReportDate > Today)
        {
            return Error.ValidationField("reportDate", "Không lấy số liệu cho ngày sau hôm nay.");
        }

        if (!statisticsSource.IsEnabled)
        {
            return Error.BusinessRule(
                "nursing_reports.his_disabled",
                "Chưa cấu hình API thông tin người bệnh (PatientSource__Provider = Api) nên chưa lấy được số liệu HIS. Có thể nhập tay.");
        }

        var result = await statisticsSource.GetAsync(request.ReportDate, cancellationToken).ConfigureAwait(false);
        if (result.Status != DailyStatisticsSourceStatus.Found)
        {
            return Error.BusinessRule(
                "nursing_reports.his_unavailable",
                "Không lấy được số liệu từ HIS (HIS lỗi hoặc chưa có câu truy vấn số liệu). Có thể nhập tay.");
        }

        var allDepartments = await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        var byCode = allDepartments.ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var hospital = new Dictionary<string, int>(StringComparer.Ordinal);
        var waiting = new Dictionary<Guid, int>();
        var unmatched = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.Items)
        {
            if (NursingHisIndicators.HospitalWide.Contains(item.Indicator))
            {
                // Chỉ số toàn viện: nếu câu truy vấn trả theo khoa thì cộng lại.
                hospital[item.Indicator] = hospital.GetValueOrDefault(item.Indicator) + item.Value;
            }
            else if (item.Indicator == NursingHisIndicators.WaitingAdmission && item.DepartmentCode is { } code)
            {
                if (byCode.TryGetValue(code.Trim(), out var departmentId))
                {
                    waiting[departmentId] = waiting.GetValueOrDefault(departmentId) + item.Value;
                }
                else
                {
                    unmatched.Add(code.Trim());
                }
            }
        }

        await StoreAsync(request.ReportDate, new NursingStatisticsValues(hospital, waiting), NursingStatisticsSource.His, userId, cancellationToken)
            .ConfigureAwait(false);
        var summary = await GetAsync(request.ReportDate, cancellationToken).ConfigureAwait(false);
        return new NursingStatisticsRefreshDto(summary, [.. unmatched]);
    }

    /// <summary>Phòng Điều dưỡng nhập tay số liệu HIS của ngày (thay toàn bộ số liệu cũ của ngày).</summary>
    public async Task<Result<NursingSummaryDto>> UpdateStatisticsAsync(UpdateNursingStatisticsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.required", "Vui lòng đăng nhập.");
        }

        if (request.ReportDate > Today)
        {
            return Error.ValidationField("reportDate", "Không nhập số liệu cho ngày sau hôm nay.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, value) in request.Hospital ?? new Dictionary<string, int?>())
        {
            if (!NursingHisIndicators.HospitalWide.Contains(key))
            {
                errors[$"hospital.{key}"] = ["Chỉ số không có trên bảng tổng hợp."];
            }
            else if (value is < 0 or > NursingReportForm.MaxValue)
            {
                errors[$"hospital.{key}"] = [$"Nhập số từ 0 đến {NursingReportForm.MaxValue}."];
            }
        }

        var knownDepartments = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .Select(d => d.Id)
            .ToHashSet();
        foreach (var (departmentId, value) in request.WaitingAdmission ?? new Dictionary<Guid, int?>())
        {
            if (!knownDepartments.Contains(departmentId))
            {
                errors[$"waitingAdmission.{departmentId}"] = ["Không tìm thấy khoa."];
            }
            else if (value is < 0 or > NursingReportForm.MaxValue)
            {
                errors[$"waitingAdmission.{departmentId}"] = [$"Nhập số từ 0 đến {NursingReportForm.MaxValue}."];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        var values = new NursingStatisticsValues(
            (request.Hospital ?? new Dictionary<string, int?>())
                .Where(p => p.Value is not null)
                .ToDictionary(p => p.Key, p => p.Value!.Value, StringComparer.Ordinal),
            (request.WaitingAdmission ?? new Dictionary<Guid, int?>())
                .Where(p => p.Value is not null)
                .ToDictionary(p => p.Key, p => p.Value!.Value));
        await StoreAsync(request.ReportDate, values, NursingStatisticsSource.Manual, userId, cancellationToken).ConfigureAwait(false);
        return await GetAsync(request.ReportDate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Mọi khoa đang dùng (và khoa ngừng dùng còn trong danh sách báo cáo), kèm phải báo cáo không và số giường.</summary>
    public async Task<IReadOnlyList<NursingReportDepartmentDto>> ListDepartmentsAsync(CancellationToken cancellationToken)
    {
        var configured = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.DepartmentId);
        return [.. (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .Where(d => d.IsActive || configured.ContainsKey(d.Id))
            .OrderBy(d => d.Name, VietnameseComparer.Instance)
            .Select(d => new NursingReportDepartmentDto(
                d.Id, d.Code, d.Name, configured.ContainsKey(d.Id), configured.TryGetValue(d.Id, out var c) ? c.BedCount : null))];
    }

    /// <summary>Đặt danh sách khoa phải báo cáo và số giường; khoa bỏ ra khỏi danh sách thì không phải báo cáo nữa.</summary>
    public async Task<Result<IReadOnlyList<NursingReportDepartmentDto>>> UpdateDepartmentsAsync(
        UpdateNursingReportDepartmentsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = request.Items ?? [];
        var active = (await departments.ListAsync(includeInactive: false, cancellationToken).ConfigureAwait(false))
            .Select(d => d.Id)
            .ToHashSet();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            if (!active.Contains(items[i].DepartmentId))
            {
                errors[$"items[{i}].departmentId"] = ["Khoa không tồn tại hoặc đã ngừng sử dụng."];
            }
            else if (items.Take(i).Any(other => other.DepartmentId == items[i].DepartmentId))
            {
                errors[$"items[{i}].departmentId"] = ["Khoa bị lặp."];
            }

            if (items[i].BedCount is < 0 or > NursingReportDepartment.MaxBedCount)
            {
                errors[$"items[{i}].bedCount"] = [$"Số giường phải từ 0 đến {NursingReportDepartment.MaxBedCount}."];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        var now = clock.UtcNow;
        var existing = await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        var wanted = items.ToDictionary(i => i.DepartmentId, i => i.BedCount);
        foreach (var department in existing)
        {
            if (wanted.TryGetValue(department.DepartmentId, out var beds))
            {
                department.SetBedCount(beds, now);
            }
            else
            {
                reports.RemoveDepartment(department);
            }
        }

        foreach (var item in items.Where(i => existing.All(e => e.DepartmentId != i.DepartmentId)))
        {
            reports.AddDepartment(NursingReportDepartment.Create(item.DepartmentId, item.BedCount, now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<NursingReportDepartmentDto>>.Success(await ListDepartmentsAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task StoreAsync(
        DateOnly reportDate, NursingStatisticsValues values, NursingStatisticsSource source, Guid userId, CancellationToken cancellationToken)
    {
        var existing = await reports.FindStatisticsAsync(reportDate, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            reports.AddStatistics(NursingDailyStatistics.Create(reportDate, values.ToJson(), source, userId, clock.UtcNow));
        }
        else
        {
            existing.Set(values.ToJson(), source, userId, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NursingSummaryDocument> DocumentAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        var summary = await GetAsync(reportDate, cancellationToken).ConfigureAwait(false);
        var hospital = await settings.ResolveHospitalAsync(cancellationToken).ConfigureAwait(false);
        var source = summary.Statistics switch
        {
            { HasValues: false } => "chưa có số liệu HIS",
            { Source: NursingStatisticsSource.Manual, UpdatedAtUtc: { } at } => $"số liệu HIS nhập tay lúc {NursingReportService.Local(at)}",
            { UpdatedAtUtc: { } at } => $"số liệu HIS lấy lúc {NursingReportService.Local(at)}",
            _ => string.Empty,
        };
        var printed = string.Create(
            CultureInfo.InvariantCulture,
            $"In lúc {NursingReportService.Local(clock.UtcNow)} · Khoa đã gửi: {summary.SubmittedCount}/{summary.ReportingCount} · {source}");
        return new NursingSummaryDocument(hospital.Name, summary, printed);
    }

    private static string FileName(DateOnly reportDate, string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"tong-hop-dieu-duong-{reportDate:yyyy-MM-dd}.{extension}");

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
