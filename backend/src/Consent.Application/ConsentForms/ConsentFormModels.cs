using System.Text.Json.Nodes;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentForms;
using Consent.Domain.Patients;
using FluentValidation;

namespace Consent.Application.ConsentForms;

/// <summary>Tham số tìm kiếm phiếu từ query string (ngày theo giờ Việt Nam).</summary>
public sealed record ConsentFormQuery(
    ConsentFormStatus? Status = null,
    Guid? TemplateId = null,
    Guid? PatientId = null,
    Guid? DepartmentId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// Truy vấn đã chuẩn hóa cho repository. <see cref="ScopeAll"/> = xem toàn viện; ngược lại chỉ phiếu của khoa
/// <see cref="ScopeDepartmentId"/> hoặc do người dùng <see cref="ScopeUserId"/> lập.
/// </summary>
public sealed record ConsentFormSearch(
    ConsentFormStatus? Status,
    Guid? TemplateId,
    Guid? PatientId,
    Guid? DepartmentId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Keyword,
    int Page,
    int PageSize,
    bool ScopeAll,
    Guid? ScopeDepartmentId,
    Guid? ScopeUserId);

public sealed record ConsentFormListItemDto(
    Guid Id,
    string FormNo,
    ConsentFormStatus Status,
    Guid TemplateId,
    string TemplateCode,
    string TemplateName,
    int TemplateVersionNo,
    Guid PatientId,
    string PatientName,
    string? PatientCode,
    Guid DepartmentId,
    string DepartmentName,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Thông tin người bệnh in trên phiếu (lấy từ hồ sơ khi đang nháp, "đóng băng" khi trình ký).</summary>
public sealed record ConsentPatientInfo(
    Guid Id,
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? CitizenId,
    string? PhoneNumber,
    string? Address,
    string? PatientCode);

public sealed record ConsentRepresentativeInfo(
    Guid Id,
    string FullName,
    RepresentativeRelationship Relationship,
    string? CitizenId,
    string? PhoneNumber);

/// <summary>Nội dung đóng băng khi trình ký.</summary>
public sealed record ConsentFormSnapshot(ConsentPatientInfo Patient, ConsentRepresentativeInfo? Representative);

/// <summary>Thao tác người dùng hiện tại được phép làm với phiếu (giao diện chỉ hiện nút hợp lệ; API vẫn kiểm tra lại).</summary>
public sealed record ConsentFormActions(
    bool CanEdit,
    bool CanSubmit,
    bool CanReturnToDraft,
    bool CanCancel,
    bool CanStartSigning,
    bool CanConfirmStep,
    bool CanConfirm,
    bool CanRevoke,
    bool CanReplace);

/// <summary>Một chữ ký trên phiếu (không kèm ảnh: ảnh chữ ký chỉ có trong PDF).</summary>
public sealed record ConsentSignatureDto(
    ConsentSignerRole Role,
    ConsentSignatureMethod Method,
    string SignerName,
    string? SignerTitle,
    DateTimeOffset SignedAtUtc,
    string? StepKey = null);

/// <summary>
/// Một bước xác nhận trung gian theo định nghĩa mẫu và trạng thái của nó. <paramref name="IsNext"/>: bước đang chờ
/// (các bước làm lần lượt sau khi người bệnh ký).
/// </summary>
public sealed record ConsentFormStepDto(
    string Key,
    string Label,
    string RoleCode,
    string RoleName,
    bool IsNext,
    ConsentSignatureDto? Confirmation);

/// <summary>
/// Xác nhận bước <paramref name="StepKey"/> (phải là bước đang chờ, để tránh xác nhận nhầm bước) kèm chữ ký tay của nhân
/// viên: ảnh PNG Base64 của khung ký (quyết định 17).
/// </summary>
public sealed record ConfirmStepRequest(string? StepKey, string? Image = null);

/// <summary>Xác nhận cuối và hoàn tất phiếu, kèm chữ ký tay của nhân viên (ảnh PNG Base64).</summary>
public sealed record CompleteConsentFormRequest(string? Image);

/// <summary>Tham chiếu tới một phiếu khác (phiếu bị thay thế / phiếu thay thế).</summary>
public sealed record ConsentFormRef(Guid Id, string FormNo);

public sealed record ConsentFormDetailDto(
    Guid Id,
    string FormNo,
    ConsentFormStatus Status,
    Guid TemplateId,
    string TemplateCode,
    string TemplateName,
    Guid TemplateVersionId,
    int TemplateVersionNo,
    string HospitalSignerRoleCode,
    TemplateDefinition Definition,
    JsonObject Data,
    IReadOnlyDictionary<string, ScoreResult> Scores,
    IReadOnlyDictionary<string, string[]> Issues,
    ConsentPatientInfo Patient,
    ConsentSignerKind SignerKind,
    ConsentRepresentativeInfo? Representative,
    Guid DepartmentId,
    string DepartmentName,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancelReason,
    DateTimeOffset? RevokedAtUtc,
    string? RevokeReason,
    ConsentDecision? Decision,
    IReadOnlyList<ConsentSignatureDto> Signatures,
    IReadOnlyList<ConsentFormStepDto> Steps,
    string? SignedPdfSha256,
    ConsentFormRef? ReplacesForm,
    ConsentFormRef? ReplacedBy,
    ConsentFormActions Actions);

/// <summary>Lập phiếu. <paramref name="ReplacesFormId"/>: lập phiếu thay thế cho phiếu đã thu hồi (cùng người bệnh, cùng mẫu).</summary>
public sealed record CreateConsentFormRequest(Guid PatientId, Guid TemplateId, Guid? DepartmentId, Guid? ReplacesFormId = null);

public sealed record UpdateConsentFormRequest(JsonObject? Data, ConsentSignerKind SignerKind, Guid? RepresentativeId);

public sealed record CancelConsentFormRequest(string? Reason);

public sealed record RevokeConsentFormRequest(string? Reason);

/// <summary>Phiên ký vừa mở: token chỉ dùng để người bệnh ký đúng phiếu này, hết hạn sau ít phút.</summary>
public sealed record SigningSessionDto(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Chữ ký tay của người bệnh/người đại diện: quyết định (bắt buộc, trừ mẫu chỉ ký xác nhận) và ảnh PNG của khung ký (Base64).
/// </summary>
public sealed record PatientSignatureRequest(ConsentDecision? Decision, string? Image);

/// <summary>Kết quả ký: <paramref name="Decision"/> rỗng khi mẫu không hỏi Đồng ý/Từ chối.</summary>
public sealed record PatientSignatureResultDto(ConsentDecision? Decision, DateTimeOffset SignedAtUtc);

public sealed class CreateConsentFormRequestValidator : AbstractValidator<CreateConsentFormRequest>
{
    public CreateConsentFormRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty().WithMessage("Vui lòng chọn người bệnh.");
        RuleFor(x => x.TemplateId).NotEmpty().WithMessage("Vui lòng chọn mẫu phiếu.");
    }
}

public sealed class UpdateConsentFormRequestValidator : AbstractValidator<UpdateConsentFormRequest>
{
    /// <summary>Giới hạn kích thước nội dung phiếu (chống gửi dữ liệu quá lớn).</summary>
    public const int MaxDataLength = 100_000;

    public UpdateConsentFormRequestValidator()
    {
        RuleFor(x => x.SignerKind).IsInEnum().WithMessage("Người ký không hợp lệ.");
        RuleFor(x => x.RepresentativeId)
            .NotEmpty().When(x => x.SignerKind == ConsentSignerKind.Representative)
            .WithMessage("Vui lòng chọn người đại diện ký thay.");
        RuleFor(x => x.Data)
            .Must(data => data is null || data.ToJsonString().Length <= MaxDataLength)
            .WithMessage("Nội dung phiếu quá lớn.");
    }
}

public sealed class RevokeConsentFormRequestValidator : AbstractValidator<RevokeConsentFormRequest>
{
    public RevokeConsentFormRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Vui lòng nhập lý do thu hồi.")
            .Length(ConsentForm.ReasonMinLength, ConsentForm.ReasonMaxLength)
            .WithMessage($"Lý do từ {ConsentForm.ReasonMinLength} đến {ConsentForm.ReasonMaxLength} ký tự.");
    }
}

public sealed class PatientSignatureRequestValidator : AbstractValidator<PatientSignatureRequest>
{
    /// <summary>Độ dài tối đa của chuỗi Base64 (tương ứng <see cref="SignatureImage.MaxBytes"/>).</summary>
    public const int MaxImageLength = ((SignatureImage.MaxBytes + 2) / 3 * 4) + 64;

    public PatientSignatureRequestValidator()
    {
        // Có bắt buộc chọn hay không tùy mẫu phiếu: kiểm tra ở ConsentSigningService.
        RuleFor(x => x.Decision)
            .IsInEnum().WithMessage("Quyết định không hợp lệ.");
        RuleFor(x => x.Image)
            .NotEmpty().WithMessage("Chưa có chữ ký.")
            .MaximumLength(MaxImageLength).WithMessage($"Ảnh chữ ký quá lớn (tối đa {SignatureImage.MaxBytes / 1024} KB).");
    }
}

public sealed class CancelConsentFormRequestValidator : AbstractValidator<CancelConsentFormRequest>
{
    public CancelConsentFormRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Vui lòng nhập lý do.")
            .Length(ConsentForm.ReasonMinLength, ConsentForm.ReasonMaxLength)
            .WithMessage($"Lý do từ {ConsentForm.ReasonMinLength} đến {ConsentForm.ReasonMaxLength} ký tự.");
    }
}
