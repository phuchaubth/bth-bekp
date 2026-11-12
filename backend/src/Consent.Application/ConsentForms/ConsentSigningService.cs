using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Shared;
using FluentValidation;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Ký tay trên máy tính bảng (quyết định 10, 11):
/// <list type="number">
/// <item>Nhân viên mở phiên ký: nhận token phiên ký chỉ dùng được để ký đúng phiếu đó (giao diện đóng phiên đăng nhập của
/// nhân viên trên máy, muốn thoát phải đăng nhập lại).</item>
/// <item>Người bệnh/người đại diện chọn Đồng ý/Từ chối và ký tay; máy chủ kiểm tra và chuẩn hóa ảnh chữ ký.</item>
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

    /// <summary>Mở phiên ký cho người bệnh trên máy tính bảng (phiên mới thay phiên cũ).</summary>
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

        var image = DecodeImage(request.Image!);
        if (image is null)
        {
            return Error.ValidationField("image", "Ảnh chữ ký không đúng định dạng PNG.");
        }

        var normalized = SignatureImage.Normalize(image);
        if (normalized.IsBlank)
        {
            return Error.BusinessRule("consent_forms.signature_blank", normalized.Error!);
        }

        if (!normalized.IsValid)
        {
            return Error.ValidationField("image", normalized.Error!);
        }

        var (patient, representative) = await formService.ResolvePeopleAsync(form, cancellationToken).ConfigureAwait(false);
        var decision = request.Decision!.Value;
        var signature = form.SignByPatient(
            sessionId,
            decision,
            representative?.FullName ?? patient.FullName,
            normalized.Png!,
            requestContext.UserAgent,
            currentUser.UserId,
            clock.UtcNow);
        formService.AddAudit(
            AuditActions.ConsentFormPatientSigned,
            form.Id,
            JsonSerializer.Serialize(new { decision = decision.ToString(), signer = signature.Role.ToString() }));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PatientSignatureResultDto(decision, signature.SignedAtUtc);
    }

    /// <summary>
    /// Phía bệnh viện xác nhận bằng tài khoản (người có vai trò theo cấu hình mẫu) sau khi người bệnh đã ký: dựng PDF bản
    /// cuối có ảnh chữ ký, lưu file (mã hóa) kèm SHA-256 và khóa phiếu.
    /// </summary>
    public async Task<Result<ConsentFormDetailDto>> CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
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

        var (template, _) = await formService.LoadTemplateAsync(form, cancellationToken).ConfigureAwait(false);
        if (!currentUser.IsInRole(template.HospitalSignerRoleCode))
        {
            var allRoles = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
            var roleName = allRoles.FirstOrDefault(r => r.Code == template.HospitalSignerRoleCode)?.Name ?? template.HospitalSignerRoleCode;
            return Error.Forbidden(
                "consent_forms.signer_role_required", $"Phiếu này cần nhân viên có vai trò {roleName} xác nhận.");
        }

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        var user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản người xác nhận.");
        var now = clock.UtcNow;
        var confirmation = new ConsentPdfConfirmation(user.FullName, user.Title, now.ToOffset(ConsentFormService.VietnamOffset));
        var pdf = pdfRenderer.Render(await formService.BuildPdfDocumentAsync(form, confirmation, cancellationToken).ConfigureAwait(false));
        var sha256 = ConsentFormService.Sha256(pdf);
        // Lưu file trước; nếu lưu CSDL thất bại thì chỉ còn một file thừa (không ảnh hưởng dữ liệu).
        var key = await documents.SaveAsync($"{form.FormNo}.pdf", pdf, cancellationToken).ConfigureAwait(false);

        form.Complete(userId, user.FullName, user.Title, key, sha256, pdf.LongLength, now);
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

    /// <summary>Nhận Base64, có hoặc không có tiền tố <c>data:image/png;base64,</c>.</summary>
    private static byte[]? DecodeImage(string image)
    {
        const string DataUrlPrefix = "data:image/png;base64,";
        var base64 = image.StartsWith(DataUrlPrefix, StringComparison.Ordinal) ? image[DataUrlPrefix.Length..] : image;
        var buffer = new byte[(base64.Length * 3 / 4) + 3];
        return Convert.TryFromBase64String(base64, buffer, out var written) ? buffer[..written] : null;
    }

    private static Error InvalidStatus(string message) => Error.Conflict("consent_forms.invalid_status", message);
}
