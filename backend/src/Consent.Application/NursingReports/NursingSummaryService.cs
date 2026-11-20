using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Application.Settings;
using Consent.Domain.NursingReports;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.NursingReports;

/// <summary>
/// Bảng tổng hợp báo cáo điều dưỡng hằng ngày của Phòng Điều dưỡng (quyết định 31): xem, in PDF, xuất Excel; cài khoa phải
/// báo cáo, số giường và khoa nhập tay từng chỉ số toàn viện khi HIS lỗi; lấy số liệu HIS (người bệnh đến khám, chẩn đoán
/// hình ảnh, chờ nhập viện). Khi HIS lỗi, các khoa nhập số liệu thuộc nhiệm vụ của khoa, Phòng Điều dưỡng chỉ tổng hợp
/// (quyết định 35). Quyền được kiểm tra ở API (<c>nursingreports.read</c> để xem, <c>nursingreports.manage</c> để sửa).
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
    INursingSummaryWorkbookWriter workbook,
    ISystemSettingRepository systemSettings)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    public async Task<NursingSummaryDto> GetAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        var allDepartments = await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        var configured = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.DepartmentId);
        var dayReports = await reports.ListByDateAsync(reportDate, cancellationToken).ConfigureAwait(false);
        var statistics = await reports.FindStatisticsAsync(reportDate, cancellationToken).ConfigureAwait(false);
        var entries = await reports.ListDepartmentStatisticsAsync(reportDate, cancellationToken).ConfigureAwait(false);
        var assignments = await AssignmentsAsync(cancellationToken).ConfigureAwait(false);
        var merged = NursingHisEntry.Merge(NursingStatisticsValues.Parse(statistics?.ValuesJson), entries);

        var names = await NamesAsync(
            dayReports.Where(r => r.IsSubmitted).Select(r => r.SubmittedByUserId)
                .Append(statistics?.UpdatedByUserId)
                .Concat(entries.Select(e => (Guid?)e.UpdatedByUserId)),
            cancellationToken).ConfigureAwait(false);
        var departmentNames = allDepartments.ToDictionary(d => d.Id, d => d.Name);
        var summaryDepartments = allDepartments
            .Select(d => new NursingSummaryDepartment(
                d.Id, d.Code, d.Name, d.IsActive && configured.ContainsKey(d.Id), configured.TryGetValue(d.Id, out var c) ? c.BedCount : null))
            .ToList();

        // Khoa phải nhập tay khi HIS lỗi: khoa phải báo cáo (NB PK chờ nhập viện) và khoa được giao chỉ số toàn viện.
        var expected = summaryDepartments.Where(d => d.IsReporting).Select(d => d.Id)
            .Concat(assignments.Values.Where(departmentNames.ContainsKey))
            .Distinct()
            .Select(id => (Id: id, Keys: NursingHisEntry.LinesFor(id, summaryDepartments.Any(d => d.Id == id && d.IsReporting), assignments)))
            .Where(d => d.Keys.Count > 0)
            .Select(d =>
            {
                var entry = entries.FirstOrDefault(e => e.DepartmentId == d.Id);
                return new NursingHisEntryStatusDto(
                    d.Id,
                    departmentNames[d.Id],
                    [.. d.Keys.Select(k => k.Key)],
                    entry?.UpdatedAtUtc,
                    entry is null ? null : names.GetValueOrDefault(entry.UpdatedByUserId));
            })
            .OrderBy(d => d.DepartmentName, VietnameseComparer.Instance)
            .ToList();
        var info = new NursingStatisticsInfoDto(
            statisticsSource.IsEnabled,
            statistics is not null,
            statistics?.Source,
            statistics?.UpdatedAtUtc,
            statistics is null ? null : names.GetValueOrDefault(statistics.UpdatedByUserId),
            expected,
            merged.UsesDepartmentEntries);
        return NursingSummaryBuilder.Build(
            reportDate,
            summaryDepartments,
            dayReports,
            names,
            merged.Values,
            info,
            merged.HospitalEnteredBy);
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
                "Chưa cấu hình API thông tin người bệnh (PatientSource__Provider = Api) nên chưa lấy được số liệu HIS. Các khoa nhập tay số liệu của khoa.");
        }

        var result = await statisticsSource.GetAsync(request.ReportDate, cancellationToken).ConfigureAwait(false);
        if (result.Status != DailyStatisticsSourceStatus.Found)
        {
            return Error.BusinessRule(
                "nursing_reports.his_unavailable",
                "Không lấy được số liệu từ HIS (HIS lỗi hoặc chưa có câu truy vấn số liệu). Các khoa nhập tay số liệu của khoa.");
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

    /// <summary>
    /// Mọi khoa đang dùng (và khoa ngừng dùng còn trong danh sách báo cáo hoặc còn được giao chỉ số), kèm phải báo cáo không, số
    /// giường và các chỉ số toàn viện khoa nhập tay khi HIS lỗi.
    /// </summary>
    public async Task<IReadOnlyList<NursingReportDepartmentDto>> ListDepartmentsAsync(CancellationToken cancellationToken)
    {
        var configured = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.DepartmentId);
        var assignments = await AssignmentsAsync(cancellationToken).ConfigureAwait(false);
        return [.. (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .Where(d => d.IsActive || configured.ContainsKey(d.Id) || assignments.Values.Contains(d.Id))
            .OrderBy(d => d.Name, VietnameseComparer.Instance)
            .Select(d => new NursingReportDepartmentDto(
                d.Id,
                d.Code,
                d.Name,
                configured.ContainsKey(d.Id),
                configured.TryGetValue(d.Id, out var c) ? c.BedCount : null,
                [.. NursingHisEntry.HospitalLines.Where(l => assignments.TryGetValue(l.Key, out var assigned) && assigned == d.Id).Select(l => l.Key)]))];
    }

    /// <summary>
    /// Đặt danh sách khoa phải báo cáo và số giường (khoa bỏ ra khỏi danh sách thì không phải báo cáo nữa), và khoa nhập tay
    /// từng chỉ số toàn viện khi HIS lỗi (khi có <see cref="UpdateNursingReportDepartmentsRequest.HisEntry"/>).
    /// </summary>
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

        var hisEntry = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (key, departmentId) in request.HisEntry ?? new Dictionary<string, Guid?>())
        {
            if (!NursingHisIndicators.HospitalWide.Contains(key))
            {
                errors[$"hisEntry.{key}"] = ["Chỉ số không có trên bảng tổng hợp."];
            }
            else if (departmentId is { } id)
            {
                if (!active.Contains(id))
                {
                    errors[$"hisEntry.{key}"] = ["Khoa không tồn tại hoặc đã ngừng sử dụng."];
                }
                else
                {
                    hisEntry[key] = id;
                }
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        if (request.HisEntry is not null)
        {
            await StoreAssignmentsAsync(hisEntry, cancellationToken).ConfigureAwait(false);
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

    private async Task<IReadOnlyDictionary<string, Guid>> AssignmentsAsync(CancellationToken cancellationToken) =>
        NursingHisEntry.ParseAssignments((await FindAssignmentSettingAsync(cancellationToken).ConfigureAwait(false))?.Value);

    private async Task<SystemSetting?> FindAssignmentSettingAsync(CancellationToken cancellationToken) =>
        (await systemSettings.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(s => s.Key == SettingKeys.NursingHisEntryDepartments);

    /// <summary>Ghi cấu hình khoa nhập từng chỉ số (lưu cùng lần lưu danh sách khoa); không giao chỉ số nào thì xóa cấu hình.</summary>
    private async Task StoreAssignmentsAsync(IReadOnlyDictionary<string, Guid> assignments, CancellationToken cancellationToken)
    {
        var value = NursingHisEntry.FormatAssignments(assignments);
        var existing = await FindAssignmentSettingAsync(cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            if (existing is not null)
            {
                systemSettings.Remove(existing);
            }
        }
        else if (existing is null)
        {
            systemSettings.Add(SystemSetting.Create(SettingKeys.NursingHisEntryDepartments, value, clock.UtcNow));
        }
        else if (existing.Value != value)
        {
            existing.Update(value, clock.UtcNow);
        }
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
            { HasValues: false, UsesDepartmentEntries: true } => "số liệu HIS do khoa nhập tay",
            { HasValues: false } => "chưa có số liệu HIS",
            { Source: NursingStatisticsSource.Manual, UpdatedAtUtc: { } at } => $"số liệu HIS nhập tay lúc {NursingReportService.Local(at)}",
            { UpdatedAtUtc: { } at, UsesDepartmentEntries: true } =>
                $"số liệu HIS lấy lúc {NursingReportService.Local(at)}, phần HIS không có do khoa nhập tay",
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
