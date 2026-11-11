using System.Text.Json;
using System.Text.Json.Nodes;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Patients;
using Consent.Shared;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Lập và quản lý phiếu đồng thuận (màn hình 3, 5, 8). Phiếu luôn gắn với phiên bản mẫu tại thời điểm lập.
/// Người dùng chỉ thấy phiếu của khoa mình hoặc do mình lập, trừ khi có quyền xem toàn viện; phiếu ngoài phạm vi
/// được trả về như "không tìm thấy" để không lộ sự tồn tại.
/// </summary>
public sealed class ConsentFormService(
    IConsentFormRepository forms,
    IConsentTemplateRepository templates,
    IPatientRepository patients,
    IDepartmentRepository departments,
    IUserRepository users,
    IConsentFormNumberGenerator numbers,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IValidator<CreateConsentFormRequest> createValidator,
    IValidator<UpdateConsentFormRequest> updateValidator,
    IValidator<CancelConsentFormRequest> cancelValidator,
    IConsentPdfRenderer pdfRenderer,
    IOptions<HospitalOptions> hospital)
{
    /// <summary>Bệnh viện ở Việt Nam (UTC+7, không đổi giờ theo mùa): lọc theo ngày dùng giờ địa phương.</summary>
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static readonly Error NotFound = Error.NotFound("consent_forms.not_found", "Không tìm thấy phiếu.");

    private bool ScopeAll => currentUser.HasPermission(Permissions.ScopeAllDepartments);

    public Task<PagedResult<ConsentFormListItemDto>> SearchAsync(ConsentFormQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var search = new ConsentFormSearch(
            query.Status,
            query.TemplateId,
            query.PatientId,
            query.DepartmentId,
            query.From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), VietnamOffset) : null,
            query.To is { } to ? new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset) : null,
            string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim(),
            page,
            pageSize,
            ScopeAll,
            currentUser.DepartmentId,
            currentUser.UserId);
        return forms.SearchAsync(search, cancellationToken);
    }

    /// <summary>Xem phiếu (có thông tin cá nhân của người bệnh) — mỗi lần xem được ghi nhật ký.</summary>
    public async Task<Result<ConsentFormDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        var detail = await BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
        AddAudit(AuditActions.ConsentFormViewed, form.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return detail;
    }

    public async Task<Result<ConsentFormDetailDto>> CreateAsync(CreateConsentFormRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await createValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var departmentId = request.DepartmentId ?? currentUser.DepartmentId;
        if (departmentId is null)
        {
            return Error.ValidationField("departmentId", "Vui lòng chọn khoa/phòng lập phiếu.");
        }

        if (!ScopeAll && departmentId != currentUser.DepartmentId)
        {
            return Error.Forbidden("consent_forms.department_forbidden", "Bạn chỉ lập phiếu được cho khoa của mình.");
        }

        var department = await departments.GetByIdAsync(departmentId.Value, cancellationToken).ConfigureAwait(false);
        if (department is not { IsActive: true })
        {
            return Error.ValidationField("departmentId", "Khoa/phòng không tồn tại hoặc đã ngừng sử dụng.");
        }

        var patient = await patients.GetByIdAsync(request.PatientId, cancellationToken).ConfigureAwait(false);
        if (patient is null)
        {
            return Error.ValidationField("patientId", "Không tìm thấy người bệnh.");
        }

        var template = await templates.GetByIdAsync(request.TemplateId, cancellationToken).ConfigureAwait(false);
        var version = template?.PublishedVersion;
        if (template is not { IsActive: true } || version is null
            || (template.DepartmentId is not null && template.DepartmentId != department.Id))
        {
            return Error.ValidationField("templateId", "Mẫu phiếu chưa phát hành hoặc không dùng cho khoa này.");
        }

        var definition = ConsentTemplateService.ReadDefinition(version);
        var formNo = await numbers.NextAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        var form = ConsentForm.Create(
            formNo, patient.Id, template.Id, version.Id, department.Id, Prefill(definition, patient).ToJsonString(),
            currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng."), clock.UtcNow);
        forms.Add(form);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lưu nháp: dữ liệu được chuẩn hóa nhưng chưa bắt buộc đầy đủ (thiếu sót trả về trong <c>issues</c>).</summary>
    public async Task<Result<ConsentFormDetailDto>> UpdateAsync(Guid id, UpdateConsentFormRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await updateValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var form = await LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanEdit)
        {
            return InvalidStatus("Chỉ sửa được phiếu nháp.");
        }

        if (request.SignerKind == ConsentSignerKind.Representative)
        {
            var patient = await patients.GetByIdAsync(form.PatientId, cancellationToken).ConfigureAwait(false);
            if (patient?.Representatives.Any(r => r.Id == request.RepresentativeId) != true)
            {
                return Error.ValidationField("representativeId", "Người đại diện không thuộc hồ sơ người bệnh này.");
            }
        }

        var definition = await LoadDefinitionAsync(form, cancellationToken).ConfigureAwait(false);
        var evaluation = FormDataEvaluator.Evaluate(definition, request.Data, keepHidden: true);
        form.UpdateDraft(evaluation.Data.ToJsonString(), request.SignerKind, request.RepresentativeId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Trình ký: nội dung phải đầy đủ, hợp lệ; thông tin người bệnh và người ký được đóng băng.</summary>
    public async Task<Result<ConsentFormDetailDto>> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanSubmit)
        {
            return InvalidStatus("Chỉ trình ký được phiếu nháp.");
        }

        var definition = await LoadDefinitionAsync(form, cancellationToken).ConfigureAwait(false);
        var evaluation = FormDataEvaluator.Evaluate(definition, FormDataEvaluator.ParseData(form.DataJson));
        if (!evaluation.IsValid)
        {
            return Error.Validation("consent_forms.incomplete", "Phiếu chưa đủ thông tin để trình ký.", evaluation.Errors);
        }

        var patient = await patients.GetByIdAsync(form.PatientId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy người bệnh của phiếu.");
        var representative = form.SignerKind == ConsentSignerKind.Representative
            ? patient.Representatives.FirstOrDefault(r => r.Id == form.RepresentativeId)
            : null;
        if (form.SignerKind == ConsentSignerKind.Representative && representative is null)
        {
            return Error.ValidationField("representativeId", "Người đại diện đã bị xóa khỏi hồ sơ; hãy chọn lại người ký.");
        }

        var snapshot = new ConsentFormSnapshot(ToPatientInfo(patient), representative is null ? null : ToRepresentativeInfo(representative));
        form.Submit(evaluation.Data.ToJsonString(), JsonSerializer.Serialize(snapshot, TemplateJson.Options), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ConsentFormDetailDto>> ReturnToDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanReturnToDraft)
        {
            return InvalidStatus("Chỉ trả về nháp được phiếu đang chờ ký.");
        }

        form.ReturnToDraft(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ConsentFormDetailDto>> CancelAsync(Guid id, CancelConsentFormRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await cancelValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var form = await LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanCancel)
        {
            return InvalidStatus("Chỉ hủy được phiếu nháp hoặc phiếu chưa ký.");
        }

        form.Cancel(request.Reason!, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>PDF khổ A4 của phiếu; chưa hoàn tất thì ghi rõ "bản xem trước". Mỗi lần tải được ghi nhật ký.</summary>
    public async Task<Result<ConsentPdfFile>> GetPdfAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        var (template, version) = await LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        var definition = ConsentTemplateService.ReadDefinition(version);
        // Chỉ in trường đang hiển thị (giá trị của trường ẩn trong bản nháp không được in).
        var evaluation = FormDataEvaluator.Evaluate(definition, FormDataEvaluator.ParseData(form.DataJson));
        var (patient, representative) = await ResolvePeopleAsync(form, cancellationToken).ConfigureAwait(false);
        var department = await departments.GetByIdAsync(form.DepartmentId, cancellationToken).ConfigureAwait(false);
        var documentDate = DateOnly.FromDateTime((form.SubmittedAtUtc ?? form.CreatedAtUtc).ToOffset(VietnamOffset).DateTime);

        var content = pdfRenderer.Render(new ConsentPdfDocument(
            hospital.Value,
            department?.Name ?? string.Empty,
            template.Code,
            version.VersionNo,
            form.FormNo,
            definition,
            evaluation.Data,
            evaluation.Scores,
            patient,
            representative,
            documentDate,
            IsFinal: form.Status == ConsentFormStatus.Completed));
        AddAudit(AuditActions.ConsentFormPdfViewed, form.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ConsentPdfFile(content, $"{form.FormNo}.pdf");
    }

    /// <summary>Phiếu trong phạm vi người dùng (khoa của mình, phiếu mình lập, hoặc toàn viện).</summary>
    internal async Task<ConsentForm?> LoadInScopeAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await forms.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return form is not null && (ScopeAll || form.DepartmentId == currentUser.DepartmentId || form.CreatedByUserId == currentUser.UserId)
            ? form
            : null;
    }

    internal async Task<(ConsentTemplate Template, ConsentTemplateVersion Version)> LoadTemplateAsync(
        ConsentForm form, CancellationToken cancellationToken)
    {
        var template = await templates.GetByIdAsync(form.TemplateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy mẫu của phiếu.");
        var version = template.Versions.FirstOrDefault(v => v.Id == form.TemplateVersionId)
            ?? throw new InvalidOperationException("Không tìm thấy phiên bản mẫu của phiếu.");
        return (template, version);
    }

    internal async Task<ConsentFormDetailDto> BuildDetailAsync(ConsentForm form, CancellationToken cancellationToken)
    {
        var (template, version) = await LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        var definition = ConsentTemplateService.ReadDefinition(version);
        var evaluation = FormDataEvaluator.Evaluate(definition, FormDataEvaluator.ParseData(form.DataJson), keepHidden: form.CanEdit);
        var (patient, representative) = await ResolvePeopleAsync(form, cancellationToken).ConfigureAwait(false);
        var department = await departments.GetByIdAsync(form.DepartmentId, cancellationToken).ConfigureAwait(false);
        var creator = await users.GetByIdAsync(form.CreatedByUserId, cancellationToken).ConfigureAwait(false);

        return new ConsentFormDetailDto(
            form.Id,
            form.FormNo,
            form.Status,
            template.Id,
            template.Code,
            template.Name,
            version.Id,
            version.VersionNo,
            template.HospitalSignerRoleCode,
            definition,
            evaluation.Data,
            evaluation.Scores,
            form.CanSubmit ? evaluation.Errors : new Dictionary<string, string[]>(),
            patient,
            form.SignerKind,
            representative,
            form.DepartmentId,
            department?.Name ?? string.Empty,
            form.CreatedByUserId,
            creator?.FullName ?? string.Empty,
            form.CreatedAtUtc,
            form.UpdatedAtUtc,
            form.SubmittedAtUtc,
            form.CompletedAtUtc,
            form.CancelledAtUtc,
            form.CancelReason,
            form.RevokedAtUtc,
            form.RevokeReason,
            new ConsentFormActions(form.CanEdit, form.CanSubmit, form.CanReturnToDraft, form.CanCancel));
    }

    /// <summary>Đã trình ký: dùng thông tin đóng băng. Đang nháp: lấy từ hồ sơ hiện tại.</summary>
    private async Task<(ConsentPatientInfo Patient, ConsentRepresentativeInfo? Representative)> ResolvePeopleAsync(
        ConsentForm form, CancellationToken cancellationToken)
    {
        if (form.SnapshotJson is not null
            && JsonSerializer.Deserialize<ConsentFormSnapshot>(form.SnapshotJson, TemplateJson.Options) is { } snapshot)
        {
            return (snapshot.Patient, snapshot.Representative);
        }

        var patient = await patients.GetByIdAsync(form.PatientId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy người bệnh của phiếu.");
        var representative = form.SignerKind == ConsentSignerKind.Representative
            ? patient.Representatives.FirstOrDefault(r => r.Id == form.RepresentativeId)
            : null;
        return (ToPatientInfo(patient), representative is null ? null : ToRepresentativeInfo(representative));
    }

    private async Task<TemplateDefinition> LoadDefinitionAsync(ConsentForm form, CancellationToken cancellationToken)
    {
        var (_, version) = await LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        return ConsentTemplateService.ReadDefinition(version);
    }

    /// <summary>Điền sẵn các trường có nguồn tự điền từ hồ sơ người bệnh.</summary>
    private static JsonObject Prefill(TemplateDefinition definition, Patient patient)
    {
        var data = new JsonObject();
        foreach (var block in definition.InputBlocks.Where(b => b.Prefill is not null))
        {
            var value = block.Prefill switch
            {
                PrefillSources.HealthInsuranceNumber => patient.HealthInsuranceNumber,
                PrefillSources.PhoneNumber => patient.PhoneNumber,
                PrefillSources.PatientCode => patient.PatientCode,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[block.Key!] = value;
            }
        }

        return data;
    }

    private static ConsentPatientInfo ToPatientInfo(Patient patient) =>
        new(
            patient.Id,
            patient.FullName,
            patient.DateOfBirth,
            patient.Gender,
            patient.CitizenId,
            patient.PhoneNumber,
            patient.CurrentAddress ?? patient.PermanentAddress,
            patient.PatientCode);

    private static ConsentRepresentativeInfo ToRepresentativeInfo(LegalRepresentative representative) =>
        new(representative.Id, representative.FullName, representative.Relationship, representative.CitizenId, representative.PhoneNumber);

    private static Error InvalidStatus(string message) => Error.Conflict("consent_forms.invalid_status", message);

    private void AddAudit(string action, Guid formId)
    {
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(clock.UtcNow, action, actor, nameof(ConsentForm), formId.ToString(), null));
    }
}
