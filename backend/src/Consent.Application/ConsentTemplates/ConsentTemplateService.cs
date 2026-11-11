using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.ConsentTemplates;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.ConsentTemplates;

/// <summary>
/// Quản lý mẫu phiếu (màn hình 11, 12): mẫu có nhiều phiên bản; chỉ phiên bản nháp sửa được; phát hành phiên bản mới
/// thì phiên bản cũ ngừng dùng cho phiếu mới (phiếu đã lập vẫn giữ phiên bản của nó).
/// </summary>
public sealed class ConsentTemplateService(
    IConsentTemplateRepository templates,
    IRoleRepository roles,
    IDepartmentRepository departments,
    IUnitOfWork unitOfWork,
    IClock clock,
    IValidator<CreateConsentTemplateRequest> createValidator,
    IValidator<UpdateConsentTemplateRequest> updateValidator)
{
    private static readonly Error NotFound = Error.NotFound("consent_templates.not_found", "Không tìm thấy mẫu phiếu.");
    private static readonly Error VersionNotFound =
        Error.NotFound("consent_templates.version_not_found", "Không tìm thấy phiên bản mẫu phiếu.");

    public async Task<IReadOnlyList<ConsentTemplateListItemDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var list = await templates.ListAsync(includeInactive, cancellationToken).ConfigureAwait(false);
        return list.OrderBy(t => t.Code, StringComparer.Ordinal).Select(ConsentTemplateListItemDto.From).ToList();
    }

    public async Task<Result<ConsentTemplateDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return template is null ? NotFound : ConsentTemplateDetailDto.From(template);
    }

    public async Task<Result<ConsentTemplateVersionDto>> GetVersionAsync(Guid id, int versionNo, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return NotFound;
        }

        var version = template.FindVersion(versionNo);
        return version is null ? VersionNotFound : ToVersionDto(template, version);
    }

    /// <summary>Mẫu đang phát hành, dùng được cho khoa <paramref name="departmentId"/> (mẫu toàn viện hoặc của khoa đó).</summary>
    public async Task<IReadOnlyList<ConsentTemplateVersionDto>> ListAvailableAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var list = await templates.ListAsync(includeInactive: false, cancellationToken).ConfigureAwait(false);
        return list
            .Where(t => t.PublishedVersion is not null && (t.DepartmentId is null || t.DepartmentId == departmentId))
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => ToVersionDto(t, t.PublishedVersion!))
            .ToList();
    }

    public async Task<Result<ConsentTemplateDetailDto>> CreateAsync(CreateConsentTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await createValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var code = ConsentTemplate.NormalizeCode(request.Code);
        if (await templates.CodeExistsAsync(code, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("consent_templates.duplicate_code", $"Đã có mẫu phiếu mã {code}.");
        }

        if (await CheckReferencesAsync(request.HospitalSignerRoleCode, request.DepartmentId, cancellationToken).ConfigureAwait(false) is { } reference)
        {
            return reference;
        }

        var initial = new TemplateDefinition(
            request.Name.Trim(),
            null,
            [new TemplateSection("II. Nội dung", [new TemplateBlock { Type = BlockTypes.Paragraph, Text = "Nhập nội dung phiếu tại đây." }])]);
        var template = ConsentTemplate.Create(
            code, request.Name, request.HospitalSignerRoleCode, request.DepartmentId, TemplateJson.Serialize(initial), clock.UtcNow);
        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConsentTemplateDetailDto.From(template);
    }

    public async Task<Result<ConsentTemplateDetailDto>> UpdateAsync(Guid id, UpdateConsentTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await updateValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return NotFound;
        }

        if (await CheckReferencesAsync(request.HospitalSignerRoleCode, request.DepartmentId, cancellationToken).ConfigureAwait(false) is { } reference)
        {
            return reference;
        }

        template.Update(request.Name, request.HospitalSignerRoleCode, request.DepartmentId, request.IsActive, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConsentTemplateDetailDto.From(template);
    }

    /// <summary>Tạo phiên bản nháp mới từ nội dung phiên bản mới nhất.</summary>
    public async Task<Result<ConsentTemplateVersionDto>> CreateDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return NotFound;
        }

        if (!template.CanCreateDraft)
        {
            return Error.Conflict(
                "consent_templates.draft_exists", $"Mẫu đang có phiên bản nháp số {template.DraftVersion!.VersionNo}; hãy sửa hoặc phát hành phiên bản đó.");
        }

        var draft = template.CreateDraftFromLatest(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToVersionDto(template, draft);
    }

    public async Task<Result<ConsentTemplateVersionDto>> UpdateDraftAsync(
        Guid id, int versionNo, UpdateTemplateVersionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return NotFound;
        }

        var version = template.FindVersion(versionNo);
        if (version is null)
        {
            return VersionNotFound;
        }

        if (version.Status != TemplateVersionStatus.Draft)
        {
            return NotDraft;
        }

        var (definition, errors) = TemplateDefinitionValidator.Parse(
            request.Definition.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : request.Definition.GetRawText());
        if (definition is null)
        {
            return InvalidDefinition(errors);
        }

        template.UpdateDraft(versionNo, TemplateJson.Serialize(definition), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToVersionDto(template, version);
    }

    public async Task<Result<ConsentTemplateDetailDto>> PublishAsync(Guid id, int versionNo, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return NotFound;
        }

        var version = template.FindVersion(versionNo);
        if (version is null)
        {
            return VersionNotFound;
        }

        if (version.Status != TemplateVersionStatus.Draft)
        {
            return NotDraft;
        }

        // Kiểm tra lại trước khi đưa vào sử dụng (phòng dữ liệu được ghi trực tiếp vào CSDL).
        var (definition, errors) = TemplateDefinitionValidator.Parse(version.ContentJson);
        if (definition is null)
        {
            return InvalidDefinition(errors);
        }

        template.Publish(versionNo, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConsentTemplateDetailDto.From(template);
    }

    public async Task<Result<ConsentTemplateDetailDto>> RetireAsync(Guid id, int versionNo, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return NotFound;
        }

        var version = template.FindVersion(versionNo);
        if (version is null)
        {
            return VersionNotFound;
        }

        if (version.Status != TemplateVersionStatus.Published)
        {
            return Error.Conflict("consent_templates.not_published", "Chỉ ngừng dùng được phiên bản đang phát hành.");
        }

        template.Retire(versionNo, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ConsentTemplateDetailDto.From(template);
    }

    internal static ConsentTemplateVersionDto ToVersionDto(ConsentTemplate template, ConsentTemplateVersion version) =>
        new(
            template.Id,
            template.Code,
            template.Name,
            version.Id,
            version.VersionNo,
            version.Status,
            ReadDefinition(version),
            version.UpdatedAtUtc,
            version.PublishedAtUtc);

    /// <summary>Định nghĩa đã lưu luôn được kiểm tra trước khi lưu; đọc lỗi nghĩa là dữ liệu bị sửa ngoài ứng dụng.</summary>
    internal static TemplateDefinition ReadDefinition(ConsentTemplateVersion version) =>
        TemplateJson.TryDeserialize(version.ContentJson, out var error)
        ?? throw new InvalidOperationException($"Định nghĩa của phiên bản mẫu {version.Id} bị hỏng: {error}");

    private static Error NotDraft =>
        Error.Conflict("consent_templates.not_draft", "Chỉ sửa hoặc phát hành được phiên bản nháp. Hãy tạo phiên bản mới.");

    private static Error InvalidDefinition(IReadOnlyList<string> errors) =>
        Error.Validation(
            "consent_templates.invalid_definition",
            "Định nghĩa mẫu chưa hợp lệ.",
            new Dictionary<string, string[]> { ["definition"] = [.. errors] });

    private async Task<Error?> CheckReferencesAsync(string roleCode, Guid? departmentId, CancellationToken cancellationToken)
    {
        var allRoles = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        if (!allRoles.Any(r => string.Equals(r.Code, roleCode.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Error.ValidationField("hospitalSignerRoleCode", "Vai trò không tồn tại.");
        }

        if (departmentId is { } id && await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.ValidationField("departmentId", "Khoa/phòng không tồn tại.");
        }

        return null;
    }
}
