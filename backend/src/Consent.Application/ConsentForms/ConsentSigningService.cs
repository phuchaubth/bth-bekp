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
    IValidator<RevokeConsentFormRequest> revokeValidator)
{
    private static readonly Error NotFound = Error.NotFound("consent_forms.not_found", "Không tìm thấy phiếu.");

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
        var token = tokens.CreateSigningToken(new SigningTokenClaims(userId, currentUser.UserName ?? string.Empty, form.Id, sessionId));
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
        if (currentUser.SigningFormId != id || currentUser.SigningSessionId is not { } sessionId)
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
            clock.UtcNow);
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

        var signaturePng = NormalizeSignature(request.Image);
        if (signaturePng.IsFailure)
        {
            return signaturePng.Error!;
        }

        var form = await formService.LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanComplete)
        {
            return InvalidStatus(form.Status == ConsentFormStatus.PendingSignature
                ? "Người bệnh chưa ký; các bước xác nhận chỉ thực hiện sau khi người bệnh ký."
                : "Chỉ xác nhận được phiếu đang chờ ký.");
        }

        var (_, version) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        var definition = ConsentTemplateService.ReadDefinition(version);
        var steps = await formService.BuildStepsAsync(form, definition, cancellationToken).ConfigureAwait(false);
        var step = steps.FirstOrDefault(s => s.Key == request.StepKey);
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
        form.ConfirmStep(step.Key, userId, user.FullName, user.Title, signaturePng.Value, clock.UtcNow);
        formService.AddAudit(AuditActions.ConsentFormStepConfirmed, form.Id, JsonSerializer.Serialize(new { step = step.Key }));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await formService.BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phía bệnh viện xác nhận bằng tài khoản (người có vai trò theo cấu hình mẫu) sau khi người bệnh đã ký: dựng PDF bản
    /// cuối có ảnh chữ ký, lưu file (mã hóa) kèm SHA-256 và khóa phiếu.
    /// </summary>
    public async Task<Result<ConsentFormDetailDto>> CompleteAsync(
        Guid id, CompleteConsentFormRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var signaturePng = NormalizeSignature(request.Image);
        if (signaturePng.IsFailure)
        {
            return signaturePng.Error!;
        }

        var form = await formService.LoadInScopeAsync(id, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

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

        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản người xác nhận.");
        var now = clock.UtcNow;
        var confirmation = new ConsentPdfConfirmation(
            user.FullName, user.Title, now.ToOffset(ConsentFormService.VietnamOffset), signaturePng.Value);
        var pdf = pdfRenderer.Render(await formService.BuildPdfDocumentAsync(form, confirmation, cancellationToken).ConfigureAwait(false));
        var sha256 = ConsentFormService.Sha256(pdf);
        // Lưu file trước; nếu lưu CSDL thất bại thì chỉ còn một file thừa (không ảnh hưởng dữ liệu).
        var key = await documents.SaveAsync($"{form.FormNo}.pdf", pdf, cancellationToken).ConfigureAwait(false);

        form.Complete(userId, user.FullName, user.Title, signaturePng.Value, key, sha256, pdf.LongLength, now, stepKeys);
        formService.AddAudit(AuditActions.ConsentFormCompleted, form.Id, JsonSerializer.Serialize(new { sha256 }));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await formService.BuildDetailAsync(form, cancellationToken).ConfigureAwait(false);
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
    private static Result<byte[]> NormalizeSignature(string? image)
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
