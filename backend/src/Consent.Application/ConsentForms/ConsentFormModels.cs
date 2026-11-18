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
/// Truy vấn đã chuẩn hóa cho repository. <see cref="ScopeAll"/> = xem toàn viện; ngược lại chỉ phiếu của các khoa
/// <see cref="ScopeDepartmentIds"/> (khoa chính và các khoa khác của người dùng, quyết định 41) hoặc do người dùng
/// <see cref="ScopeUserId"/> lập.
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
    IReadOnlyList<Guid> ScopeDepartmentIds,
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
    string? StepKey = null,
    string? Statement = null);

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
/// <param name="UseSpecimen">Ký bằng chữ ký mẫu đã lưu của tài khoản (không cần vẽ); khi đó bỏ qua <paramref name="Image"/>.</param>
public sealed record ConfirmStepRequest(string? StepKey, string? Image = null, bool UseSpecimen = false);

/// <summary>Xác nhận cuối và hoàn tất phiếu, kèm chữ ký tay của nhân viên (ảnh PNG Base64).</summary>
/// <param name="UseSpecimen">Ký bằng chữ ký mẫu đã lưu của tài khoản (không cần vẽ); khi đó bỏ qua <paramref name="Image"/>.</param>
/// <param name="EncounterNo">Số tiếp nhận (HIS) của lượt khám mà phiếu thuộc về: bắt buộc khi bệnh viện bật gắn lượt tiếp nhận (quyết định 25).</param>
/// <param name="EncounterDate">Ngày tiếp nhận của lượt đã chọn (máy chủ tra lại HIS theo ngày này để kiểm tra).</param>
public sealed record CompleteConsentFormRequest(string? Image, bool UseSpecimen = false, string? EncounterNo = null, DateOnly? EncounterDate = null);

/// <summary>Lượt tiếp nhận đã gắn với phiếu (quyết định 25).</summary>
public sealed record ConsentFormEncounterDto(string EncounterNo, DateTimeOffset ReceivedAt, string? Place, string? ReceivedBy);

/// <summary>Một phiếu đã gắn vào lượt tiếp nhận.</summary>
public sealed record EncounterFormDto(Guid Id, string FormNo, string TemplateName, ConsentFormStatus Status);

/// <summary>Dòng đọc từ CSDL: phiếu đã gắn vào lượt tiếp nhận <paramref name="EncounterNo"/>.</summary>
public sealed record EncounterFormRecord(string EncounterNo, Guid Id, string FormNo, string TemplateName, ConsentFormStatus Status);

/// <summary>Một lượt tiếp nhận để chọn khi ký hoàn tất, kèm các phiếu đã gắn vào lượt đó.</summary>
public sealed record EncounterOptionDto(
    string EncounterNo, DateTimeOffset ReceivedAt, string? Place, string? ReceivedBy, IReadOnlyList<EncounterFormDto> Forms);

/// <summary>Danh sách lượt tiếp nhận của người bệnh trong khoảng ngày, mới nhất trước.</summary>
public sealed record EncounterListDto(DateOnly From, DateOnly To, IReadOnlyList<EncounterOptionDto> Items);

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
    ConsentFormActions Actions,
    IReadOnlyList<string> Notices,
    Guid? BundleId = null,
    KioskIdentityMethod? KioskIdentity = null,
    ConsentFormEncounterDto? Encounter = null);

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
/// <param name="Statement">Lời xác nhận của người ký (khi mẫu có ô này), ví dụ "Tôi đã nghe, hiểu và đồng ý".</param>
public sealed record PatientSignatureRequest(ConsentDecision? Decision, string? Image, string? Statement = null);

/// <summary>Kết quả ký: <paramref name="Decision"/> rỗng khi mẫu không hỏi Đồng ý/Từ chối.</summary>
public sealed record PatientSignatureResultDto(ConsentDecision? Decision, DateTimeOffset SignedAtUtc);

/// <summary>
/// Lập bộ phiếu (quyết định 23): nhiều mẫu cho cùng một người bệnh trong một lần (2–10 mẫu khác nhau, cùng khoa của phiếu).
/// </summary>
public sealed record CreateConsentFormBundleRequest(Guid PatientId, IReadOnlyList<Guid>? TemplateIds, Guid? DepartmentId);

/// <summary>Một phiếu trong bộ phiếu, kèm thao tác người dùng hiện tại được làm với phiếu đó.</summary>
/// <param name="RequiresDecision">Mẫu hỏi Đồng ý/Không đồng ý khi người bệnh ký.</param>
/// <param name="NextStepLabel">Bước xác nhận đang chờ (sau khi người bệnh ký), ví dụ "Điều dưỡng khoa".</param>
public sealed record ConsentFormBundleItemDto(
    Guid Id,
    string FormNo,
    ConsentFormStatus Status,
    Guid TemplateId,
    string TemplateCode,
    string TemplateName,
    bool PatientSigned,
    bool RequiresDecision,
    string? NextStepLabel,
    ConsentFormActions Actions);

/// <summary>
/// Bộ phiếu của một người bệnh. <paramref name="CanStartSigning"/>: mở được một phiên ký cho mọi phiếu đang chờ người bệnh
/// ký; <paramref name="CanConfirm"/>: người dùng hiện tại có phiếu đến lượt xác nhận; <paramref name="Notices"/>: lưu ý
/// (còn phiếu nháp, người ký khác nhau…).
/// </summary>
public sealed record ConsentFormBundleDto(
    Guid BundleId,
    Guid PatientId,
    string PatientName,
    IReadOnlyList<ConsentFormBundleItemDto> Forms,
    bool CanStartSigning,
    bool CanConfirm,
    IReadOnlyList<string> Notices,
    KioskIdentityMethod? KioskIdentity = null);

/// <summary>Phiên ký cho cả bộ: một token cho mọi phiếu đang chờ người bệnh ký trong bộ.</summary>
public sealed record BundleSigningSessionDto(string Token, DateTimeOffset ExpiresAtUtc, IReadOnlyList<Guid> FormIds);

/// <summary>
/// Người bệnh ký một lần cho cả bộ (phương án b): một ảnh chữ ký và một lời xác nhận dùng cho mọi phiếu trong phiên ký;
/// quyết định Đồng ý/Không đồng ý riêng cho từng phiếu có hỏi (khóa là mã phiếu).
/// </summary>
public sealed record BundlePatientSignatureRequest(
    string? Image,
    string? Statement,
    IReadOnlyDictionary<Guid, ConsentDecision>? Decisions);

public sealed record BundleSignedFormDto(Guid Id, string FormNo, ConsentDecision? Decision);

public sealed record BundlePatientSignatureResultDto(DateTimeOffset SignedAtUtc, IReadOnlyList<BundleSignedFormDto> Forms);

/// <summary>Xác nhận các phiếu trong bộ đang đến lượt người dùng hiện tại, bằng một chữ ký (chữ ký mẫu hoặc vẽ).</summary>
/// <param name="EncounterNo">Lượt tiếp nhận gắn vào các phiếu được hoàn tất trong lần xác nhận này (xem <see cref="CompleteConsentFormRequest"/>).</param>
public sealed record ConfirmBundleRequest(string? Image, bool UseSpecimen = false, string? EncounterNo = null, DateOnly? EncounterDate = null);

/// <param name="Action"><c>step</c>: xác nhận một bước trung gian; <c>completed</c>: xác nhận cuối, phiếu hoàn tất.</param>
public sealed record BundleConfirmedFormDto(Guid Id, string FormNo, string Action, string Label);

public sealed record BundleConfirmFailureDto(Guid Id, string FormNo, string Message);

public sealed record ConfirmBundleResultDto(
    IReadOnlyList<BundleConfirmedFormDto> Confirmed,
    IReadOnlyList<BundleConfirmFailureDto> Failed);

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
        RuleFor(x => x.Statement)
            .MaximumLength(ConsentSignature.StatementMaxLength).WithMessage($"Lời xác nhận tối đa {ConsentSignature.StatementMaxLength} ký tự.");
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
