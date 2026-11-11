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

/// <summary>Thao tác người dùng hiện tại được phép làm với phiếu (giao diện chỉ hiện nút hợp lệ).</summary>
public sealed record ConsentFormActions(bool CanEdit, bool CanSubmit, bool CanReturnToDraft, bool CanCancel);

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
    ConsentFormActions Actions);

public sealed record CreateConsentFormRequest(Guid PatientId, Guid TemplateId, Guid? DepartmentId);

public sealed record UpdateConsentFormRequest(JsonObject? Data, ConsentSignerKind SignerKind, Guid? RepresentativeId);

public sealed record CancelConsentFormRequest(string? Reason);

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
