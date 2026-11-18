using Consent.Domain.ConsentForms;
using Consent.Domain.Exports;
using FluentValidation;

namespace Consent.Application.Exports;

/// <summary>
/// Bộ lọc khi xuất phiếu (ngày theo giờ Việt Nam, tính theo ngày lập phiếu). Không lọc theo từ khóa: xuất dữ liệu là
/// thao tác hàng loạt, và bộ lọc được lưu vào lịch sử xuất nên không chứa họ tên người bệnh.
/// </summary>
public sealed record ConsentFormExportFilter(
    ConsentFormStatus? Status = null,
    Guid? TemplateId = null,
    Guid? DepartmentId = null,
    DateOnly? From = null,
    DateOnly? To = null);

/// <param name="Columns">Mã các cột cần xuất (xem <c>GET /exports/consent-forms/columns</c>).</param>
/// <param name="IncludeContent">Kèm nội dung phiếu theo mẫu (các trường của mẫu); cần lọc theo đúng một mẫu.</param>
/// <param name="IncludeSensitiveData">Xuất đầy đủ số CCCD, số điện thoại, số thẻ BHYT (cần quyền <c>exports.sensitive</c>).</param>
public sealed record ConsentFormExportRequest(
    ExportFormat? Format,
    ConsentFormExportFilter? Filter,
    IReadOnlyList<string>? Columns,
    bool IncludeContent = false,
    bool IncludeSensitiveData = false);

/// <summary>Một cột có thể chọn khi xuất phiếu.</summary>
public sealed record ExportColumnDto(string Key, string Header, string Group, bool Default, bool Sensitive);

/// <summary>File đã xuất (trả thẳng cho người dùng, không lưu lại trên máy chủ).</summary>
public sealed record ExportFile(byte[] Content, string ContentType, string FileName, Guid JobId, int RowCount);

/// <summary>Bộ lọc đã dùng, kèm tên mẫu/khoa tại thời điểm xuất (để lịch sử vẫn đúng khi danh mục đổi tên).</summary>
public sealed record ExportFilterSnapshot(
    ConsentFormStatus? Status,
    Guid? TemplateId,
    string? TemplateCode,
    string? TemplateName,
    Guid? DepartmentId,
    string? DepartmentName,
    DateOnly? From,
    DateOnly? To);

public sealed record ExportJobQuery(int Page = 1, int PageSize = 20);

/// <summary>Truy vấn lịch sử xuất cho repository; <see cref="RequestedByUserId"/> null = mọi người dùng.</summary>
public sealed record ExportJobSearch(Guid? RequestedByUserId, int Page, int PageSize);

/// <summary>Bản ghi lịch sử xuất như lưu trong CSDL (bộ lọc dạng JSON).</summary>
public sealed record ExportJobRecord(
    Guid Id,
    DateTimeOffset RequestedAtUtc,
    Guid RequestedByUserId,
    string RequestedByName,
    ExportFormat Format,
    string FiltersJson,
    string Columns,
    bool IncludesContent,
    bool IncludesSensitiveData,
    int RowCount,
    string FileName,
    long FileSizeBytes,
    string FileSha256,
    int DurationMs);

public sealed record ExportJobDto(
    Guid Id,
    DateTimeOffset RequestedAtUtc,
    Guid RequestedByUserId,
    string RequestedByName,
    ExportFormat Format,
    ExportFilterSnapshot Filter,
    IReadOnlyList<string> Columns,
    bool IncludesContent,
    bool IncludesSensitiveData,
    int RowCount,
    string FileName,
    long FileSizeBytes,
    string FileSha256,
    int DurationMs);

public sealed class ConsentFormExportRequestValidator : AbstractValidator<ConsentFormExportRequest>
{
    public ConsentFormExportRequestValidator()
    {
        RuleFor(x => x.Format)
            .NotNull().WithMessage("Vui lòng chọn định dạng file.")
            .IsInEnum().WithMessage("Định dạng file không hợp lệ.");
        RuleFor(x => x.Columns)
            .NotEmpty().WithMessage("Vui lòng chọn ít nhất một cột.");
        RuleFor(x => x.Columns)
            .Must(columns => columns is null || columns.All(ConsentFormExportColumns.IsDefined))
            .WithMessage("Có cột không hợp lệ.");
        RuleFor(x => x.Filter)
            .Must(filter => filter?.Status is not { } status || Enum.IsDefined(status))
            .WithMessage("Trạng thái không hợp lệ.")
            .OverridePropertyName("Filter.Status");
        RuleFor(x => x.Filter)
            .Must(filter => filter is not { From: { } from, To: { } to } || from <= to)
            .WithMessage("Đến ngày phải sau hoặc bằng từ ngày.")
            .OverridePropertyName("Filter.To");
        RuleFor(x => x.IncludeContent)
            .Must(include => !include)
            .When(x => x.Filter?.TemplateId is null)
            .WithMessage("Chọn một mẫu phiếu để xuất kèm nội dung phiếu.");
    }
}
