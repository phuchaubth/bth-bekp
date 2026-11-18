using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Ký tay trên điện thoại (quyết định 10, 11):
/// <list type="number">
/// <item>Nhân viên mở phiên ký: nhận token phiên ký chỉ dùng được để ký đúng phiếu đó (giao diện đóng phiên đăng nhập của
/// nhân viên trên máy, muốn thoát phải đăng nhập lại).</item>
/// <item>Người bệnh/người đại diện chọn Đồng ý/Từ chối và ký tay; máy chủ kiểm tra và chuẩn hóa ảnh chữ ký.</item>
/// <item>Nếu mẫu có các bước xác nhận trung gian (<c>signatures.steps</c>): nhân viên đúng vai trò của từng bước lần lượt
/// xác nhận bằng tài khoản (mỗi bước một người khác nhau).</item>
/// <item>Nhân viên có vai trò theo cấu hình mẫu xác nhận bằng tài khoản: phiếu hoàn tất, PDF đã ký được lưu (mã hóa) kèm
/// SHA-256.</item>
/// </list>
/// Phiếu hoàn tất chỉ có thể thu hồi (bắt buộc lý do), rồi lập phiếu thay thế.
/// </summary>
public sealed class ConsentSigningService(
    ConsentFormService formService,
    IConsentFormRepository forms,
    IRoleRepository roles,
    IUserRepository users,
    ITokenService tokens,
    IDocumentStore documents,
    IConsentPdfRenderer pdfRenderer,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IValidator<PatientSignatureRequest> signatureValidator,
    IValidator<RevokeConsentFormRequest> revokeValidator,
    ConsentEncounterService encounters)
{
    private static readonly Error NotFound = Error.NotFound("consent_forms.not_found", "Không tìm thấy phiếu.");

    private static readonly Error BundleNotFound = Error.NotFound("consent_forms.bundle_not_found", "Không tìm thấy bộ phiếu.");

    /// <summary>Mở phiên ký cho người bệnh trên điện thoại (phiên mới thay phiên cũ).</summary>
    public async Task<Result<SigningSessionDto>> StartSigningAsync(Guid id, CancellationToken cancellationToken)
    {
        var form = await formService.LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanStartSigning)
        {
            return InvalidStatus(form.Status == ConsentFormStatus.PendingSignature
                ? "Người bệnh đã ký phiếu này."
                : "Chỉ mở phiên ký được cho phiếu đang chờ ký.");
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        var sessionId = Guid.NewGuid();
        form.StartSigning(sessionId, clock.UtcNow);
        var token = tokens.CreateSigningToken(new SigningTokenClaims(userId, currentUser.UserName ?? string.Empty, [form.Id], sessionId));
        formService.AddAudit(AuditActions.ConsentFormSigningStarted, form.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SigningSessionDto(token.Value, token.ExpiresAtUtc);
    }

    /// <summary>
    /// Người bệnh (hoặc người đại diện đã chọn khi soạn phiếu) ký trong phiên ký đang mở. Chỉ chấp nhận token phiên ký
    /// của đúng phiếu và đúng phiên; mỗi phiên chỉ ký được một lần.
    /// </summary>
    public async Task<Result<PatientSignatureResultDto>> SignAsPatientAsync(
        Guid id, PatientSignatureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Token của bộ phiếu chỉ ký được cả bộ một lần (SignBundleAsPatientAsync), không ký lẻ từng phiếu.
        if (currentUser.SigningFormIds is not [var tokenFormId] || tokenFormId != id
            || currentUser.SigningSessionId is not { } sessionId)
        {
            return Error.Forbidden("consent_forms.signing_session_mismatch", "Phiên ký không dùng cho phiếu này.");
        }

        if (await signatureValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var form = await forms.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (form.Status != ConsentFormStatus.PendingSignature)
        {
            return InvalidStatus("Phiếu không còn ở trạng thái chờ ký.");
        }

        if (form.PatientSignature is not null)
        {
            return InvalidStatus("Người bệnh đã ký phiếu này.");
        }

        if (form.SigningSessionId != sessionId)
        {
            return InvalidStatus("Phiên ký đã kết thúc hoặc đã được thay bằng phiên khác.");
        }

        // Mẫu chỉ ký xác nhận: bỏ qua quyết định gửi lên; mẫu còn lại: bắt buộc chọn Đồng ý/Từ chối.
        var (_, version) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        var signatures = ConsentTemplateService.ReadDefinition(version).Signatures;
        var requiresDecision = signatures?.RequiresDecision ?? true;
        if (requiresDecision && request.Decision is null)
        {
            return Error.ValidationField(
                "decision",
                $"Vui lòng chọn {signatures?.AgreeLabel ?? "Đồng ý"} hoặc {signatures?.RefuseLabel ?? "Từ chối"}.");
        }

        var decision = requiresDecision ? request.Decision : null;

        // Lời xác nhận: chỉ nhận khi mẫu có ô này; mẫu bắt buộc thì phải ghi (bấm câu gợi ý hoặc tự gõ).
        var statement = signatures?.Statement is not null && !string.IsNullOrWhiteSpace(request.Statement)
            ? request.Statement.Trim()
            : null;
        if (signatures?.Statement is { Required: true } required && statement is null)
        {
            var example = required.Suggestions is { Count: > 0 } suggestions ? $", ví dụ: “{suggestions[0]}”" : string.Empty;
            return Error.ValidationField("statement", $"Vui lòng ghi lời xác nhận{example}.");
        }

        var normalized = NormalizeSignature(request.Image);
        if (normalized.IsFailure)
        {
            return normalized.Error!;
        }

        var (patient, representative) = await formService.ResolvePeopleAsync(form, cancellationToken).ConfigureAwait(false);
        var signature = form.SignByPatient(
            sessionId,
            decision,
            representative?.FullName ?? patient.FullName,
            normalized.Value,
            requestContext.UserAgent,
            currentUser.UserId,
            clock.UtcNow,
            statement);
        formService.AddAudit(
            AuditActions.ConsentFormPatientSigned,
            form.Id,
            JsonSerializer.Serialize(new { decision = decision?.ToString(), signer = signature.Role.ToString() }));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PatientSignatureResultDto(decision, signature.SignedAtUtc);
    }

    /// <summary>
    /// Nhân viên xác nhận bước trung gian đang chờ (<paramref name="request"/> nêu rõ bước để tránh xác nhận nhầm khi hai
    /// người thao tác cùng lúc). Cần đúng vai trò của bước; một tài khoản chỉ xác nhận một bước trên phiếu.
    /// </summary>
    public async Task<Result<ConsentFormDetailDto>> ConfirmStepAsync(
        Guid id, ConfirmStepRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.StepKey))
        {
            return Error.ValidationField("stepKey", "Thiếu bước cần xác nhận.");
        }

        var signaturePng = await StaffSignatureAsync(request.Image, request.UseSpecimen, cancellationToken).ConfigureAwait(false);
        if (signaturePng.IsFailure)
        {
            return signaturePng.Error!;
        }

        var form = await formService.LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        var confirmed = await ConfirmStepCoreAsync(form, request.StepKey, signaturePng.Value, cancellationToken).ConfigureAwait(false);
        return confirmed.IsFailure
            ? confirmed.Error!
            : await formService.BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ConsentFormStepDto>> ConfirmStepCoreAsync(
        ConsentForm form, string stepKey, StaffSignature signaturePng, CancellationToken cancellationToken)
    {
        if (!form.CanComplete)
        {
            return InvalidStatus(form.Status == ConsentFormStatus.PendingSignature
                ? "Người bệnh chưa ký; các bước xác nhận chỉ thực hiện sau khi người bệnh ký."
                : "Chỉ xác nhận được phiếu đang chờ ký.");
        }

        var (_, version) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        var definition = ConsentTemplateService.ReadDefinition(version);
        var steps = await formService.BuildStepsAsync(form, definition, cancellationToken).ConfigureAwait(false);
        var step = steps.FirstOrDefault(s => s.Key == stepKey);
        if (step is null)
        {
            return Error.ValidationField("stepKey", "Mẫu phiếu không có bước xác nhận này.");
        }

        if (!step.IsNext)
        {
            return InvalidStatus(step.Confirmation is not null
                ? $"Bước \"{step.Label}\" đã được xác nhận."
                : $"Chưa đến lượt bước \"{step.Label}\"; hãy xác nhận các bước trước.");
        }

        if (!currentUser.IsInRole(step.RoleCode))
        {
            return Error.Forbidden(
                "consent_forms.signer_role_required", $"Bước \"{step.Label}\" cần nhân viên có vai trò {step.RoleName} xác nhận.");
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        if (form.StepSignatures.Any(s => s.UserId == userId))
        {
            return SameConfirmer;
        }

        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản người xác nhận.");
        form.ConfirmStep(step.Key, userId, user.FullName, user.Title, signaturePng.Png, clock.UtcNow);
        formService.AddAudit(AuditActions.ConsentFormStepConfirmed, form.Id, JsonSerializer.Serialize(new { step = step.Key, signature = signaturePng.Source }));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return step;
    }

    /// <summary>
    /// Phía bệnh viện xác nhận bằng tài khoản (người có vai trò theo cấu hình mẫu) sau khi người bệnh đã ký: dựng PDF bản
    /// cuối có ảnh chữ ký, lưu file (mã hóa) kèm SHA-256 và khóa phiếu.
    /// </summary>
    public async Task<Result<ConsentFormDetailDto>> CompleteAsync(
        Guid id, CompleteConsentFormRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var signaturePng = await StaffSignatureAsync(request.Image, request.UseSpecimen, cancellationToken).ConfigureAwait(false);
        if (signaturePng.IsFailure)
        {
            return signaturePng.Error!;
        }

        var form = await formService.LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        var completed = await CompleteCoreAsync(
                form,
                signaturePng.Value,
                token => encounters.ResolveAsync(form.PatientId, request.EncounterNo, request.EncounterDate, token),
                cancellationToken)
            .ConfigureAwait(false);
        return completed.IsFailure
            ? completed.Error!
            : await formService.BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="resolveEncounter">
    /// Lượt tiếp nhận gắn vào phiếu (quyết định 25); chỉ gọi sau khi đã kiểm tra người xác nhận đúng vai trò và đủ bước, để
    /// không tra HIS khi không cần.
    /// </param>
    private async Task<Result<bool>> CompleteCoreAsync(
        ConsentForm form,
        StaffSignature signaturePng,
        Func<CancellationToken, Task<Result<ConsentEncounter?>>> resolveEncounter,
        CancellationToken cancellationToken)
    {
        if (!form.CanComplete)
        {
            return InvalidStatus(form.Status == ConsentFormStatus.PendingSignature
                ? "Người bệnh chưa ký; phía bệnh viện chỉ xác nhận sau khi người bệnh ký."
                : "Chỉ hoàn tất được phiếu đang chờ ký.");
        }

        var (template, version) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        var steps = await formService.BuildStepsAsync(form, ConsentTemplateService.ReadDefinition(version), cancellationToken)
            .ConfigureAwait(false);
        var stepKeys = steps.Select(s => s.Key).ToList();
        if (steps.FirstOrDefault(s => s.Confirmation is null) is { } waiting)
        {
            return InvalidStatus($"Còn bước xác nhận chưa thực hiện: {waiting.Label}.");
        }

        if (!currentUser.IsInRole(template.HospitalSignerRoleCode))
        {
            var allRoles = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
            var roleName = allRoles.FirstOrDefault(r => r.Code == template.HospitalSignerRoleCode)?.Name ?? template.HospitalSignerRoleCode;
            return Error.Forbidden(
                "consent_forms.signer_role_required", $"Phiếu này cần nhân viên có vai trò {roleName} xác nhận.");
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        if (form.StepSignatures.Any(s => s.UserId == userId))
        {
            return SameConfirmer;
        }

        var encounter = await resolveEncounter(cancellationToken).ConfigureAwait(false);
        if (encounter.IsFailure)
        {
            return encounter.Error!;
        }

        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản người xác nhận.");
        var now = clock.UtcNow;
        var confirmation = new ConsentPdfConfirmation(
            user.FullName, user.Title, now.ToOffset(ConsentFormService.VietnamOffset), signaturePng.Png);
        var document = await formService.BuildPdfDocumentAsync(form, confirmation, cancellationToken).ConfigureAwait(false);
        var pdf = pdfRenderer.Render(document with { Encounter = encounter.Value ?? document.Encounter });
        var sha256 = ConsentFormService.Sha256(pdf);
        // Lưu file trước; nếu lưu CSDL thất bại thì chỉ còn một file thừa (không ảnh hưởng dữ liệu).
        var key = await documents.SaveAsync($"{form.FormNo}.pdf", pdf, cancellationToken).ConfigureAwait(false);

        form.Complete(userId, user.FullName, user.Title, signaturePng.Png, key, sha256, pdf.LongLength, now, stepKeys, encounter.Value);
        formService.AddAudit(
            AuditActions.ConsentFormCompleted,
            form.Id,
            JsonSerializer.Serialize(new { sha256, signature = signaturePng.Source, encounter = encounter.Value?.Number }));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Mở một phiên ký cho cả bộ phiếu (quyết định 23): mọi phiếu trong bộ đang chờ người bệnh ký nhận cùng mã phiên; token
    /// phiên ký liệt kê đúng các phiếu đó. Các phiếu phải cùng một người ký (người bệnh, hoặc cùng một người đại diện).
    /// </summary>
    public async Task<Result<BundleSigningSessionDto>> StartBundleSigningAsync(Guid bundleId, CancellationToken cancellationToken)
    {
        var bundle = await formService.LoadBundleInScopeAsync(bundleId, cancellationToken).ConfigureAwait(false);
        if (bundle.Count == 0)
        {
            return BundleNotFound;
        }

        var signable = bundle.Where(f => f.CanStartSigning).ToList();
        if (signable.Count == 0)
        {
            return InvalidStatus("Không có phiếu nào trong bộ đang chờ người bệnh ký; hãy trình ký các phiếu trước.");
        }

        if (ConsentFormService.HasMixedSigners(signable))
        {
            return Error.BusinessRule("consent_forms.bundle_mixed_signers", ConsentFormService.MixedSignersMessage);
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        var sessionId = Guid.NewGuid();
        var now = clock.UtcNow;
        foreach (var form in signable)
        {
            form.StartSigning(sessionId, now);
            formService.AddAudit(
                AuditActions.ConsentFormSigningStarted, form.Id, JsonSerializer.Serialize(new { bundle = bundleId, forms = signable.Count }));
        }

        List<Guid> formIds = [.. signable.Select(f => f.Id)];
        var token = tokens.CreateSigningToken(new SigningTokenClaims(userId, currentUser.UserName ?? string.Empty, formIds, sessionId));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BundleSigningSessionDto(token.Value, token.ExpiresAtUtc, formIds);
    }

    /// <summary>
    /// Người bệnh (hoặc người đại diện) ký một lần cho cả bộ (phương án b): một ảnh chữ ký và một lời xác nhận áp cho mọi
    /// phiếu trong phiên ký; phiếu nào hỏi Đồng ý/Không đồng ý thì có quyết định riêng. Kiểm tra hết rồi mới ghi, mọi phiếu
    /// được ký cùng một thời điểm (in trên PDF là "ký chung một lần").
    /// </summary>
    public async Task<Result<BundlePatientSignatureResultDto>> SignBundleAsPatientAsync(
        BundlePatientSignatureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.SigningFormIds is not { Count: > 0 } formIds || currentUser.SigningSessionId is not { } sessionId)
        {
            return Error.Forbidden("consent_forms.signing_session_mismatch", "Phiên ký không dùng cho bộ phiếu này.");
        }

        if (request.Statement is { Length: > ConsentSignature.StatementMaxLength })
        {
            return Error.ValidationField("statement", $"Lời xác nhận tối đa {ConsentSignature.StatementMaxLength} ký tự.");
        }

        if (request.Decisions?.Values.Any(d => !Enum.IsDefined(d)) == true)
        {
            return Error.ValidationField("decisions", "Quyết định không hợp lệ.");
        }

        var loaded = new List<(ConsentForm Form, TemplateSignatures? Signatures, string TemplateName)>(formIds.Count);
        foreach (var formId in formIds)
        {
            var form = await forms.GetByIdAsync(formId, cancellationToken).ConfigureAwait(false);
            if (form is null)
            {
                return NotFound;
            }

            if (form.Status != ConsentFormStatus.PendingSignature || form.PatientSignature is not null)
            {
                return InvalidStatus($"Phiếu {form.FormNo} không còn chờ người bệnh ký.");
            }

            if (form.SigningSessionId != sessionId)
            {
                return InvalidStatus("Phiên ký đã kết thúc hoặc đã được thay bằng phiên khác.");
            }

            var (template, version) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
            loaded.Add((form, ConsentTemplateService.ReadDefinition(version).Signatures, template.Name));
        }

        if (loaded.Select(x => x.Form.BundleId).Distinct().Count() > 1)
        {
            return Error.Forbidden("consent_forms.signing_session_mismatch", "Phiên ký không dùng cho bộ phiếu này.");
        }

        var decisions = new Dictionary<Guid, ConsentDecision?>();
        foreach (var (form, signatures, templateName) in loaded)
        {
            if (signatures?.RequiresDecision ?? true)
            {
                if (request.Decisions?.TryGetValue(form.Id, out var decision) != true)
                {
                    return Error.ValidationField(
                        "decisions",
                        $"Vui lòng chọn {signatures?.AgreeLabel ?? "Đồng ý"} hoặc {signatures?.RefuseLabel ?? "Từ chối"} cho phiếu “{templateName}”.");
                }

                decisions[form.Id] = decision;
            }
            else
            {
                decisions[form.Id] = null;
            }
        }

        // Một lời xác nhận cho cả bộ: bắt buộc nếu có phiếu bắt buộc; chỉ lưu vào phiếu có ô lời xác nhận.
        var statement = string.IsNullOrWhiteSpace(request.Statement) ? null : request.Statement.Trim();
        if (statement is null && loaded.FirstOrDefault(x => x.Signatures?.Statement is { Required: true }).Signatures?.Statement is { } required)
        {
            var example = required.Suggestions is { Count: > 0 } suggestions ? $", ví dụ: “{suggestions[0]}”" : string.Empty;
            return Error.ValidationField("statement", $"Vui lòng ghi lời xác nhận{example}.");
        }

        var normalized = NormalizeSignature(request.Image);
        if (normalized.IsFailure)
        {
            return normalized.Error!;
        }

        var now = clock.UtcNow;
        List<string> formNos = [.. loaded.Select(x => x.Form.FormNo)];
        var signed = new List<BundleSignedFormDto>(loaded.Count);
        foreach (var (form, signatures, _) in loaded)
        {
            var (patient, representative) = await formService.ResolvePeopleAsync(form, cancellationToken).ConfigureAwait(false);
            var decision = decisions[form.Id];
            var signature = form.SignByPatient(
                sessionId,
                decision,
                representative?.FullName ?? patient.FullName,
                normalized.Value,
                requestContext.UserAgent,
                currentUser.UserId,
                now,
                signatures?.Statement is not null ? statement : null);
            formService.AddAudit(
                AuditActions.ConsentFormPatientSigned,
                form.Id,
                JsonSerializer.Serialize(new
                {
                    decision = decision?.ToString(),
                    signer = signature.Role.ToString(),
                    bundle = form.BundleId,
                    signedTogether = formNos,
                }));
            signed.Add(new BundleSignedFormDto(form.Id, form.FormNo, decision));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BundlePatientSignatureResultDto(now, signed);
    }

    /// <summary>
    /// Nhân viên xác nhận một lần các phiếu trong bộ đang đến lượt mình (bước trung gian đúng vai trò, hoặc xác nhận cuối),
    /// bằng một chữ ký (chữ ký mẫu hoặc vẽ). Mỗi phiếu xử lý và lưu riêng; phiếu không làm được được báo lại kèm lý do.
    /// </summary>
    public async Task<Result<ConfirmBundleResultDto>> ConfirmBundleAsync(
        Guid bundleId, ConfirmBundleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var signaturePng = await StaffSignatureAsync(request.Image, request.UseSpecimen, cancellationToken).ConfigureAwait(false);
        if (signaturePng.IsFailure)
        {
            return signaturePng.Error!;
        }

        var bundle = await formService.LoadBundleInScopeAsync(bundleId, cancellationToken).ConfigureAwait(false);
        if (bundle.Count == 0)
        {
            return BundleNotFound;
        }

        // Xem trước các phiếu sẽ xác nhận bước hay hoàn tất: có phiếu hoàn tất thì lượt tiếp nhận (quyết định 25) được kiểm tra
        // một lần cho cả bộ trước khi ghi gì, chọn sai thì không phiếu nào bị đổi.
        var plan = new List<(ConsentForm Form, TemplateDefinition Definition, ConsentFormStepDto? Step, bool Complete)>();
        foreach (var form in bundle.Where(f => f.CanComplete))
        {
            var (template, version) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
            var definition = ConsentTemplateService.ReadDefinition(version);
            var steps = await formService.BuildStepsAsync(form, definition, cancellationToken).ConfigureAwait(false);
            var actions = formService.BuildActions(form, template, steps, hasReplacement: false);
            if (actions.CanConfirmStep && steps.FirstOrDefault(s => s.IsNext) is { } step)
            {
                plan.Add((form, definition, step, false));
            }
            else if (actions.CanConfirm)
            {
                plan.Add((form, definition, null, true));
            }
        }

        Result<ConsentEncounter?> encounter = (ConsentEncounter?)null;
        if (plan.Exists(p => p.Complete))
        {
            encounter = await encounters.ResolveAsync(bundle[0].PatientId, request.EncounterNo, request.EncounterDate, cancellationToken)
                .ConfigureAwait(false);
            if (encounter.IsFailure)
            {
                return encounter.Error!;
            }
        }

        var confirmed = new List<BundleConfirmedFormDto>();
        var failed = new List<BundleConfirmFailureDto>();
        foreach (var (form, definition, step, complete) in plan)
        {
            if (step is not null)
            {
                var result = await ConfirmStepCoreAsync(form, step.Key, signaturePng.Value, cancellationToken).ConfigureAwait(false);
                Record(form, result.IsSuccess, "step", step.Label, result.Error);
            }
            else if (complete)
            {
                var result = await CompleteCoreAsync(form, signaturePng.Value, _ => Task.FromResult(encounter), cancellationToken)
                    .ConfigureAwait(false);
                Record(form, result.IsSuccess, "completed", definition.Signatures?.Hospital ?? "Xác nhận của bệnh viện", result.Error);
            }
        }

        if (confirmed.Count == 0 && failed.Count == 0)
        {
            return Error.BusinessRule("consent_forms.bundle_nothing_to_confirm", "Không có phiếu nào trong bộ đang chờ bạn xác nhận.");
        }

        return new ConfirmBundleResultDto(confirmed, failed);

        void Record(ConsentForm form, bool success, string action, string label, Error? error)
        {
            if (success)
            {
                confirmed.Add(new BundleConfirmedFormDto(form.Id, form.FormNo, action, label));
            }
            else
            {
                failed.Add(new BundleConfirmFailureDto(form.Id, form.FormNo, error?.Message ?? "Không xác nhận được phiếu."));
            }
        }
    }

    /// <summary>Thu hồi phiếu đã hoàn tất (bắt buộc lý do). Muốn thay đổi nội dung thì lập phiếu thay thế.</summary>
    public async Task<Result<ConsentFormDetailDto>> RevokeAsync(Guid id, RevokeConsentFormRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await revokeValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var form = await formService.LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanRevoke)
        {
            return InvalidStatus("Chỉ thu hồi được phiếu đã hoàn tất.");
        }

        form.Revoke(request.Reason!, clock.UtcNow);
        formService.AddAudit(AuditActions.ConsentFormRevoked, form.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await formService.BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Kiểm tra và chuẩn hóa ảnh chữ ký tay (người bệnh hoặc nhân viên): Base64 PNG, không quá lớn, có nét ký đủ dài.
    /// Trả về ảnh PNG xám đã cắt sát nét ký.
    /// </summary>
    /// <summary>Chữ ký của nhân viên xác nhận và nguồn của nó (ghi nhật ký): "specimen" = chữ ký mẫu đã lưu, "drawn" = vẽ khi ký.</summary>
    private sealed record StaffSignature(byte[] Png, string Source);

    /// <summary>
    /// Chữ ký của nhân viên khi xác nhận: chữ ký mẫu đã lưu của tài khoản (không cần vẽ), hoặc ảnh vẽ trong khung ký khi
    /// tài khoản chưa có chữ ký mẫu hoặc người ký chọn ký tay lần này.
    /// </summary>
    private async Task<Result<StaffSignature>> StaffSignatureAsync(string? image, bool useSpecimen, CancellationToken cancellationToken)
    {
        if (useSpecimen)
        {
            var user = currentUser.UserId is { } userId ? await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false) : null;
            return user?.SignatureSpecimenPng is { } specimen
                ? new StaffSignature(specimen, "specimen")
                : Error.ValidationField("image", "Tài khoản chưa có chữ ký mẫu. Vui lòng ký vào khung ký.");
        }

        var drawn = NormalizeSignature(image);
        return drawn.IsFailure ? drawn.Error! : new StaffSignature(drawn.Value, "drawn");
    }

    internal static Result<byte[]> NormalizeSignature(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return Error.ValidationField("image", "Vui lòng ký vào khung ký.");
        }

        if (image.Length > PatientSignatureRequestValidator.MaxImageLength)
        {
            return Error.ValidationField("image", "Ảnh chữ ký quá lớn.");
        }

        var decoded = DecodeImage(image);
        if (decoded is null)
        {
            return Error.ValidationField("image", "Ảnh chữ ký không đúng định dạng PNG.");
        }

        var normalized = SignatureImage.Normalize(decoded);
        if (normalized.IsBlank)
        {
            return Error.BusinessRule("consent_forms.signature_blank", normalized.Error!);
        }

        return normalized.IsValid ? normalized.Png! : Error.ValidationField("image", normalized.Error!);
    }

    /// <summary>Nhận Base64, có hoặc không có tiền tố <c>data:image/png;base64,</c>.</summary>
    private static byte[]? DecodeImage(string image)
    {
        const string DataUrlPrefix = "data:image/png;base64,";
        var base64 = image.StartsWith(DataUrlPrefix, StringComparison.Ordinal) ? image[DataUrlPrefix.Length..] : image;
        var buffer = new byte[(base64.Length * 3 / 4) + 3];
        return Convert.TryFromBase64String(base64, buffer, out var written) ? buffer[..written] : null;
    }

    private static Error SameConfirmer =>
        Error.BusinessRule(
            "consent_forms.same_confirmer",
            "Tài khoản của bạn đã xác nhận một bước trên phiếu này; mỗi bước cần một người khác xác nhận.");

    private static Error InvalidStatus(string message) => Error.Conflict("consent_forms.invalid_status", message);
}
