using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Exports;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.Exports;

/// <summary>
/// Xuất danh sách phiếu ra Excel/CSV (màn hình 13): theo bộ lọc và phạm vi khoa của người xuất, chọn cột, che dữ liệu nhạy
/// cảm mặc định. File được tạo ngay và trả về cho người dùng, không lưu trên máy chủ; mỗi lần xuất được ghi lịch sử
/// (<see cref="ExportJob"/>, kèm SHA-256 của file) và nhật ký audit.
/// </summary>
public sealed class ConsentFormExportService(
    IConsentFormRepository forms,
    IConsentTemplateRepository templates,
    IDepartmentRepository departments,
    IUserRepository users,
    IExportJobRepository jobs,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IEnumerable<ITabularFileWriter> writers,
    IValidator<ConsentFormExportRequest> validator)
{
    /// <summary>Số phiếu tối đa mỗi lần xuất (khoảng một tháng ở quy mô lớn nhất); nhiều hơn thì thu hẹp bộ lọc.</summary>
    public const int MaxRows = 100_000;

    public const string SheetName = "Phiếu đồng thuận";

    private static readonly JsonSerializerOptions FilterJson = new(JsonSerializerDefaults.Web);

    private bool ScopeAll => currentUser.HasPermission(Permissions.ScopeAllDepartments);

    public static IReadOnlyList<ExportColumnDto> Columns() => ConsentFormExportColumns.Describe();

    public async Task<Result<ExportFile>> ExportAsync(ConsentFormExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await validator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        if (request.IncludeSensitiveData && !currentUser.HasPermission(Permissions.ExportsSensitive))
        {
            return Error.Forbidden(
                "exports.sensitive_forbidden", "Bạn không có quyền xuất dữ liệu nhạy cảm đầy đủ (số CCCD, số điện thoại, số thẻ BHYT).");
        }

        var filter = request.Filter ?? new ConsentFormExportFilter();
        if (!ScopeAll && filter.DepartmentId is { } requestedDepartment && requestedDepartment != currentUser.DepartmentId)
        {
            return Error.Forbidden("exports.department_forbidden", "Bạn chỉ xuất được dữ liệu của khoa mình.");
        }

        ConsentTemplate? template = null;
        if (filter.TemplateId is { } templateId)
        {
            template = await templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
            if (template is null)
            {
                return Error.ValidationField("filter.templateId", "Không tìm thấy mẫu phiếu.");
            }
        }

        var department = filter.DepartmentId is { } departmentId
            ? await departments.GetByIdAsync(departmentId, cancellationToken).ConfigureAwait(false)
            : null;
        if (filter.DepartmentId is not null && department is null)
        {
            return Error.ValidationField("filter.departmentId", "Không tìm thấy khoa/phòng.");
        }

        var search = new ConsentFormSearch(
            filter.Status,
            filter.TemplateId,
            PatientId: null,
            filter.DepartmentId,
            ConsentFormService.StartOfDayUtc(filter.From),
            ConsentFormService.StartOfDayUtc(filter.To?.AddDays(1)),
            Keyword: null,
            Page: 1,
            PageSize: MaxRows,
            ScopeAll,
            currentUser.DepartmentId,
            currentUser.UserId);
        var total = await forms.CountAsync(search, cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return Error.BusinessRule("exports.no_rows", "Không có phiếu nào khớp bộ lọc.");
        }

        if (total > MaxRows)
        {
            return Error.BusinessRule(
                "exports.too_many_rows",
                string.Create(CultureInfo.GetCultureInfo("vi-VN"),
                    $"Có {total:N0} phiếu khớp bộ lọc, vượt giới hạn {MaxRows:N0} phiếu mỗi lần xuất. Hãy thu hẹp khoảng thời gian hoặc lọc theo khoa, mẫu phiếu."));
        }

        var writer = writers.FirstOrDefault(w => w.Format == request.Format)
            ?? throw new InvalidOperationException($"Chưa đăng ký bộ ghi file {request.Format}.");
        var columns = ConsentFormExportColumns.Select(request.Columns!);
        var content = request.IncludeContent ? TemplateContentColumns.Create(template!) : null;
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        var requester = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var jobId = Guid.CreateVersion7(now);
        var snapshot = new ExportFilterSnapshot(
            filter.Status, template?.Id, template?.Code, template?.Name, department?.Id, department?.Name, filter.From, filter.To);

        var started = Stopwatch.GetTimestamp();
        using var buffer = new MemoryStream();
        var data = new TabularData(
            SheetName,
            [.. columns.Select(c => c.ToTabular()), .. content?.Columns ?? []],
            Rows(forms.StreamForExportAsync(search, request.IncludeContent, cancellationToken), columns, content,
                request.IncludeSensitiveData, cancellationToken),
            Properties(jobId, requester?.FullName ?? currentUser.UserName, now, snapshot, total, request.IncludeSensitiveData));
        var rowCount = await writer.WriteAsync(data, buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        var sha256 = ConsentFormService.Sha256(bytes);
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"phieu-dong-thuan_{now.ToOffset(ConsentFormService.VietnamOffset):yyyyMMdd-HHmmss}.{writer.FileExtension}");

        var columnKeys = columns.Select(c => c.Key).ToList();
        var job = ExportJob.Create(
            jobId, ExportJob.ConsentFormsKind, userId, now, request.Format!.Value, JsonSerializer.Serialize(snapshot, FilterJson),
            columnKeys, request.IncludeContent, request.IncludeSensitiveData, rowCount, fileName, bytes.LongLength, sha256,
            Stopwatch.GetElapsedTime(started));
        jobs.Add(job);
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(
            now,
            AuditActions.ConsentFormsExported,
            actor,
            nameof(ExportJob),
            job.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                format = job.Format.ToString(),
                rows = rowCount,
                columns = columnKeys,
                content = request.IncludeContent,
                sensitive = request.IncludeSensitiveData,
                filter = snapshot,
                sha256,
            }, FilterJson)));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ExportFile(bytes, writer.ContentType, fileName, job.Id, rowCount);
    }

    /// <summary>Lịch sử xuất: của chính người dùng; người có quyền xem nhật ký thấy mọi lần xuất.</summary>
    public async Task<PagedResult<ExportJobDto>> HistoryAsync(ExportJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var requestedBy = currentUser.HasPermission(Permissions.AuditRead) ? null : currentUser.UserId;
        var result = await jobs.SearchAsync(new ExportJobSearch(requestedBy, page, pageSize), cancellationToken).ConfigureAwait(false);
        return new PagedResult<ExportJobDto>(
            [.. result.Items.Select(job => new ExportJobDto(
                job.Id,
                job.RequestedAtUtc,
                job.RequestedByUserId,
                job.RequestedByName,
                job.Format,
                JsonSerializer.Deserialize<ExportFilterSnapshot>(job.FiltersJson, FilterJson)
                    ?? new ExportFilterSnapshot(null, null, null, null, null, null, null, null),
                job.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries),
                job.IncludesContent,
                job.IncludesSensitiveData,
                job.RowCount,
                job.FileName,
                job.FileSizeBytes,
                job.FileSha256,
                job.DurationMs))],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    private static async IAsyncEnumerable<IReadOnlyList<object?>> Rows(
        IAsyncEnumerable<ConsentFormExportRecord> records,
        IReadOnlyList<ConsentFormExportColumn> columns,
        TemplateContentColumns? content,
        bool includeSensitiveData,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var (patient, representative) = ResolvePeople(record);
            var context = new ExportRowContext(record, patient, representative, includeSensitiveData);
            var values = new List<object?>(columns.Count + (content?.Columns.Count ?? 0));
            values.AddRange(columns.Select(c => c.Value(context)));
            if (content is not null)
            {
                values.AddRange(content.Values(record.TemplateVersionId, record.DataJson, includeSensitiveData));
            }

            yield return values;
        }
    }

    /// <summary>Đã trình ký: thông tin đóng băng (đúng như trên phiếu đem ký). Chưa trình ký: hồ sơ hiện tại.</summary>
    internal static (ConsentPatientInfo Patient, ConsentRepresentativeInfo? Representative) ResolvePeople(ConsentFormExportRecord record)
    {
        if (record.SnapshotJson is not null
            && JsonSerializer.Deserialize<ConsentFormSnapshot>(record.SnapshotJson, TemplateJson.Options) is { } snapshot)
        {
            return (snapshot.Patient, snapshot.Representative);
        }

        var patient = new ConsentPatientInfo(
            record.PatientId,
            record.PatientName,
            record.PatientDateOfBirth,
            record.PatientGender,
            record.PatientCitizenId,
            record.PatientPhoneNumber,
            record.PatientAddress,
            record.PatientCode);
        var representative = record.SignerKind == ConsentSignerKind.Representative
            && record is { RepresentativeId: { } id, RepresentativeName: { } name, RepresentativeRelationship: { } relationship }
                ? new ConsentRepresentativeInfo(id, name, relationship, record.RepresentativeCitizenId, record.RepresentativePhoneNumber)
                : null;
        return (patient, representative);
    }

    private List<KeyValuePair<string, string>> Properties(
        Guid jobId, string? requester, DateTimeOffset now, ExportFilterSnapshot filter, int total, bool includeSensitiveData)
    {
        static string Date(DateOnly? value) => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
        var scope = ScopeAll ? "Toàn viện" : "Khoa của người xuất và phiếu do người xuất lập";
        return
        [
            new("Dữ liệu", "Danh sách phiếu đồng thuận"),
            new("Mã lần xuất", jobId.ToString()),
            new("Người xuất", requester ?? string.Empty),
            new("Thời điểm xuất", now.ToOffset(ConsentFormService.VietnamOffset).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
            new("Phạm vi dữ liệu", scope),
            new("Trạng thái", filter.Status is { } status ? VietnameseLabels.Status(status) : "Tất cả"),
            new("Mẫu phiếu", filter.TemplateCode is null ? "Tất cả" : $"{filter.TemplateCode} – {filter.TemplateName}"),
            new("Khoa/phòng", filter.DepartmentName ?? "Tất cả"),
            new("Lập từ ngày", Date(filter.From)),
            new("Lập đến ngày", Date(filter.To)),
            new("Số phiếu", total.ToString(CultureInfo.InvariantCulture)),
            new("Dữ liệu nhạy cảm", includeSensitiveData
                ? "Đầy đủ (số CCCD, số điện thoại, số thẻ BHYT không bị che): chỉ dùng đúng mục đích, không chia sẻ file"
                : "Đã che một phần (số CCCD, số điện thoại, số thẻ BHYT)"),
        ];
    }
}
