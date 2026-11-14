using System.Text.Json;
using Consent.Domain.ConsentTemplates;
using FluentValidation;

namespace Consent.Application.ConsentTemplates;

public sealed record ConsentTemplateListItemDto(
    Guid Id,
    string Code,
    string Name,
    string HospitalSignerRoleCode,
    Guid? DepartmentId,
    bool IsActive,
    int? PublishedVersionNo,
    DateTimeOffset? PublishedAtUtc,
    int? DraftVersionNo,
    DateTimeOffset UpdatedAtUtc)
{
    public static ConsentTemplateListItemDto From(ConsentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new ConsentTemplateListItemDto(
            template.Id,
            template.Code,
            template.Name,
            template.HospitalSignerRoleCode,
            template.DepartmentId,
            template.IsActive,
            template.PublishedVersion?.VersionNo,
            template.PublishedVersion?.PublishedAtUtc,
            template.DraftVersion?.VersionNo,
            template.UpdatedAtUtc);
    }
}

public sealed record ConsentTemplateVersionSummaryDto(
    int VersionNo,
    TemplateVersionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? RetiredAtUtc);

public sealed record ConsentTemplateDetailDto(
    Guid Id,
    string Code,
    string Name,
    string HospitalSignerRoleCode,
    Guid? DepartmentId,
    bool IsActive,
    IReadOnlyList<ConsentTemplateVersionSummaryDto> Versions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ConsentTemplateDetailDto From(ConsentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new ConsentTemplateDetailDto(
            template.Id,
            template.Code,
            template.Name,
            template.HospitalSignerRoleCode,
            template.DepartmentId,
            template.IsActive,
            template.Versions
                .OrderByDescending(v => v.VersionNo)
                .Select(v => new ConsentTemplateVersionSummaryDto(
                    v.VersionNo, v.Status, v.CreatedAtUtc, v.UpdatedAtUtc, v.PublishedAtUtc, v.RetiredAtUtc))
                .ToList(),
            template.CreatedAtUtc,
            template.UpdatedAtUtc);
    }
}

/// <summary>Một phiên bản kèm định nghĩa đầy đủ (để xem trước, soạn, hoặc dựng form lập phiếu).</summary>
public sealed record ConsentTemplateVersionDto(
    Guid TemplateId,
    string TemplateCode,
    string TemplateName,
    Guid VersionId,
    int VersionNo,
    TemplateVersionStatus Status,
    TemplateDefinition Definition,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc);

public sealed record CreateConsentTemplateRequest(string Code, string Name, string HospitalSignerRoleCode, Guid? DepartmentId);

public sealed record UpdateConsentTemplateRequest(string Name, string HospitalSignerRoleCode, Guid? DepartmentId, bool IsActive);

/// <summary>Định nghĩa nhận dạng JSON thô để tự kiểm tra và trả lỗi tiếng Việt (không dựa vào model binding).</summary>
public sealed record UpdateTemplateVersionRequest(JsonElement Definition);

public sealed class CreateConsentTemplateRequestValidator : AbstractValidator<CreateConsentTemplateRequest>
{
    public CreateConsentTemplateRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Vui lòng nhập mã mẫu phiếu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9-]{1,19}$").WithMessage("Mã mẫu gồm 2–20 ký tự: chữ không dấu, số, dấu gạch ngang.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên mẫu phiếu.")
            .MaximumLength(ConsentTemplate.NameMaxLength).WithMessage($"Tên mẫu tối đa {ConsentTemplate.NameMaxLength} ký tự.");
        RuleFor(x => x.HospitalSignerRoleCode).NotEmpty().WithMessage("Vui lòng chọn vai trò xác nhận phía bệnh viện.");
    }
}

public sealed class UpdateConsentTemplateRequestValidator : AbstractValidator<UpdateConsentTemplateRequest>
{
    public UpdateConsentTemplateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vui lòng nhập tên mẫu phiếu.")
            .MaximumLength(ConsentTemplate.NameMaxLength).WithMessage($"Tên mẫu tối đa {ConsentTemplate.NameMaxLength} ký tự.");
        RuleFor(x => x.HospitalSignerRoleCode).NotEmpty().WithMessage("Vui lòng chọn vai trò xác nhận phía bệnh viện.");
    }
}
