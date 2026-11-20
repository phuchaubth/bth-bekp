using System.Globalization;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Application.Settings;
using Consent.Domain.Departments;
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
                d.Id,
                d.ShortName ?? d.Code,
                d.Name,
                d.IsActive && configured.ContainsKey(d.Id),
                configured.TryGetValue(d.Id, out var c) ? c.BedCount : null,
                configured.TryGetValue(d.Id, out var o) ? o.SortOrder : null))
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
    /// Mọi khoa đang dùng (và khoa ngừng dùng còn trong danh sách báo cáo hoặc còn được giao chỉ số), kèm tên viết tắt, phải báo
    /// cáo không, số giường và các chỉ số toàn viện khoa nhập tay khi HIS lỗi. Khoa phải báo cáo đứng trước theo thứ tự trên bảng
    /// tổng hợp (quyết định 45), rồi các khoa khác theo tên.
    /// </summary>
    public async Task<IReadOnlyList<NursingReportDepartmentDto>> ListDepartmentsAsync(CancellationToken cancellationToken)
    {
        var configured = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => d.DepartmentId);
        var assignments = await AssignmentsAsync(cancellationToken).ConfigureAwait(false);
        bool Reporting(Department d) => d.IsActive && configured.ContainsKey(d.Id);
        return [.. (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false))
            .Where(d => d.IsActive || configured.ContainsKey(d.Id) || assignments.Values.Contains(d.Id))
            .OrderBy(d => Reporting(d) ? 0 : 1)
            .ThenBy(d => Reporting(d) ? configured[d.Id].SortOrder : 0)
            .ThenBy(d => Reporting(d) ? d.ShortName ?? d.Code : string.Empty, VietnameseComparer.Instance)
            .ThenBy(d => d.Name, VietnameseComparer.Instance)
            .Select(d => new NursingReportDepartmentDto(
                d.Id,
                d.Code,
                d.Name,
                d.ShortName,
                d.IsActive,
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
        var known = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id);

        // Khoa không dùng được: không có, hoặc đã ngừng sử dụng (khoa nhập tay trước đây không còn trong danh mục khoa lấy từ HIS,
        // quyết định 42). Thông báo ghi tên khoa để Phòng Điều dưỡng biết sửa dòng nào.
        string? Unusable(Guid departmentId, string fix) =>
            !known.TryGetValue(departmentId, out var department) ? "Khoa không tồn tại."
            : department.IsActive ? null
            : $"{department.Name} ({department.Code}) đã ngừng sử dụng (không còn trong danh mục khoa/phòng), {fix}.";

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            if (Unusable(items[i].DepartmentId, "bỏ chọn khoa này") is { } problem)
            {
                errors[$"items[{i}].departmentId"] = [problem];
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
                if (Unusable(id, "chọn khoa khác") is { } problem)
                {
                    errors[$"hisEntry.{key}"] = [problem];
                }
                else
                {
                    hisEntry[key] = id;
                }
            }
        }

        // Tên viết tắt (quyết định 45): chỉ khoa đang dùng; tối đa 20 ký tự; không trùng tên viết tắt (hoặc mã, khi chưa có tên viết
        // tắt) của khoa đang dùng khác, để mục A, E, F không lẫn khoa.
        var shortNames = new Dictionary<Guid, string?>();
        foreach (var (departmentId, value) in request.ShortNames ?? new Dictionary<Guid, string?>())
        {
            var key = $"shortNames.{departmentId}";
            var normalized = Department.NormalizeShortName(value);
            if (Unusable(departmentId, "không đặt tên viết tắt được") is { } problem)
            {
                errors[key] = [problem];
            }
            else if (normalized is { Length: > Department.ShortNameMaxLength } || (normalized?.Any(char.IsControl) ?? false))
            {
                errors[key] = [$"Tên viết tắt tối đa {Department.ShortNameMaxLength} ký tự."];
            }
            else
            {
                shortNames[departmentId] = normalized;
            }
        }

        var labels = known.Values
            .Where(d => d.IsActive)
            .Select(d => (Department: d, Label: shortNames.TryGetValue(d.Id, out var name) ? name ?? d.Code : d.ShortName ?? d.Code))
            .ToList();
        foreach (var same in labels.GroupBy(l => l.Label, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var (department, label) in same.Where(l => shortNames.ContainsKey(l.Department.Id)))
            {
                var other = same.First(l => l.Department.Id != department.Id).Department;
                errors[$"shortNames.{department.Id}"] = [$"Tên viết tắt \"{label}\" trùng với {other.Name} ({other.Code})."];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        foreach (var (departmentId, name) in shortNames)
        {
            known[departmentId].SetShortName(name);
        }

        if (request.HisEntry is not null)
        {
            await StoreAssignmentsAsync(hisEntry, cancellationToken).ConfigureAwait(false);
        }

        // Thứ tự trên bảng tổng hợp là thứ tự trong danh sách gửi lên, đánh số từ 1 (quyết định 45).
        var now = clock.UtcNow;
        var existing = await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        var wanted = items.Select((item, index) => (item, index)).ToDictionary(p => p.item.DepartmentId, p => (p.item.BedCount, Order: p.index + 1));
        foreach (var department in existing)
        {
            if (wanted.TryGetValue(department.DepartmentId, out var want))
            {
                department.SetBedCount(want.BedCount, now);
                department.SetSortOrder(want.Order, now);
            }
            else
            {
                reports.RemoveDepartment(department);
            }
        }

        foreach (var item in items.Where(i => existing.All(e => e.DepartmentId != i.DepartmentId)))
        {
            reports.AddDepartment(NursingReportDepartment.Create(item.DepartmentId, item.BedCount, now, wanted[item.DepartmentId].Order));
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
