using Consent.Domain.Common;

namespace Consent.Domain.ConsentForms;

/// <summary>
/// Vòng đời phiếu (giai đoạn chưa có SmartCA, ký tay trên máy tính bảng):
/// Nháp → Chờ ký → Hoàn tất → (Thu hồi); Nháp/Chờ ký → Hủy. Khi tích hợp SmartCA sẽ bổ sung các bước ký số.
/// </summary>
public enum ConsentFormStatus
{
    Draft,
    PendingSignature,
    Completed,
    Cancelled,
    Revoked,
}

/// <summary>Người ký phía người bệnh.</summary>
public enum ConsentSignerKind
{
    Patient,
    Representative,
}

/// <summary>
/// Phiếu đồng thuận/cam kết của một người bệnh, lập theo một phiên bản mẫu cố định. Nội dung điền (<see cref="DataJson"/>)
/// và ảnh chụp thông tin người bệnh lúc trình ký (<see cref="SnapshotJson"/>) có thể chứa dữ liệu cá nhân nên được mã hóa
/// khi lưu và luôn bị che trong nhật ký audit.
/// </summary>
public sealed class ConsentForm : Entity, IAuditableEntity
{
    public const int FormNoMaxLength = 30;
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 500;

    private ConsentForm(
        Guid id,
        string formNo,
        Guid patientId,
        Guid templateId,
        Guid templateVersionId,
        Guid departmentId,
        string dataJson,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        FormNo = formNo;
        PatientId = patientId;
        TemplateId = templateId;
        TemplateVersionId = templateVersionId;
        DepartmentId = departmentId;
        DataJson = dataJson;
        CreatedByUserId = createdByUserId;
        Status = ConsentFormStatus.Draft;
        SignerKind = ConsentSignerKind.Patient;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>Số phiếu, ví dụ <c>PDT-2026-000123</c>.</summary>
    public string FormNo { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid TemplateId { get; private set; }

    /// <summary>Phiên bản mẫu tại thời điểm lập; không đổi trong suốt vòng đời phiếu.</summary>
    public Guid TemplateVersionId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public ConsentFormStatus Status { get; private set; }

    /// <summary>Nội dung đã điền (JSON theo định nghĩa của phiên bản mẫu).</summary>
    [SensitiveData]
    public string DataJson { get; private set; }

    public ConsentSignerKind SignerKind { get; private set; }

    /// <summary>Người đại diện ký thay (khi <see cref="SignerKind"/> là <see cref="ConsentSignerKind.Representative"/>).</summary>
    public Guid? RepresentativeId { get; private set; }

    /// <summary>Thông tin người bệnh và người ký được "đóng băng" khi trình ký, để nội dung đem ký không đổi theo hồ sơ.</summary>
    [SensitiveData]
    public string? SnapshotJson { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevokeReason { get; private set; }

    public bool CanEdit => Status == ConsentFormStatus.Draft;

    public bool CanSubmit => Status == ConsentFormStatus.Draft;

    public bool CanReturnToDraft => Status == ConsentFormStatus.PendingSignature;

    public bool CanCancel => Status is ConsentFormStatus.Draft or ConsentFormStatus.PendingSignature;

    public bool CanComplete => Status == ConsentFormStatus.PendingSignature;

    public bool CanRevoke => Status == ConsentFormStatus.Completed;

    public static ConsentForm Create(
        string formNo,
        Guid patientId,
        Guid templateId,
        Guid templateVersionId,
        Guid departmentId,
        string dataJson,
        Guid createdByUserId,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formNo);
        ArgumentNullException.ThrowIfNull(dataJson);
        return new ConsentForm(
            Guid.CreateVersion7(), formNo, patientId, templateId, templateVersionId, departmentId, dataJson, createdByUserId, nowUtc);
    }

    /// <summary>Lưu nội dung nháp và người ký phía người bệnh.</summary>
    public void UpdateDraft(string dataJson, ConsentSignerKind signerKind, Guid? representativeId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(dataJson);
        Ensure(CanEdit, "Chỉ sửa được phiếu nháp.");
        if (signerKind == ConsentSignerKind.Representative && representativeId is null)
        {
            throw new DomainException("Phải chọn người đại diện ký thay.");
        }

        DataJson = dataJson;
        SignerKind = signerKind;
        RepresentativeId = signerKind == ConsentSignerKind.Representative ? representativeId : null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Trình ký: chốt nội dung đã chuẩn hóa, khóa phiếu và đóng băng thông tin người bệnh/người ký.</summary>
    public void Submit(string dataJson, string snapshotJson, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(dataJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        Ensure(CanSubmit, "Chỉ trình ký được phiếu nháp.");
        DataJson = dataJson;
        SnapshotJson = snapshotJson;
        Status = ConsentFormStatus.PendingSignature;
        SubmittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Trả phiếu chờ ký về nháp để sửa (khi người bệnh chưa ký).</summary>
    public void ReturnToDraft(DateTimeOffset nowUtc)
    {
        Ensure(CanReturnToDraft, "Chỉ trả về nháp được phiếu đang chờ ký.");
        SnapshotJson = null;
        SubmittedAtUtc = null;
        Status = ConsentFormStatus.Draft;
        UpdatedAtUtc = nowUtc;
    }

    public void Cancel(string reason, DateTimeOffset nowUtc)
    {
        Ensure(CanCancel, "Chỉ hủy được phiếu nháp hoặc phiếu chưa ký.");
        CancelReason = RequireReason(reason);
        CancelledAtUtc = nowUtc;
        Status = ConsentFormStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Hoàn tất sau khi đủ chữ ký (bước ký làm ở Sprint 4). Phiếu hoàn tất không sửa được.</summary>
    public void Complete(DateTimeOffset nowUtc)
    {
        Ensure(CanComplete, "Chỉ hoàn tất được phiếu đang chờ ký.");
        Status = ConsentFormStatus.Completed;
        CompletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Thu hồi phiếu đã hoàn tất (bắt buộc lý do). Muốn thay đổi nội dung thì lập phiếu mới.</summary>
    public void Revoke(string reason, DateTimeOffset nowUtc)
    {
        Ensure(CanRevoke, "Chỉ thu hồi được phiếu đã hoàn tất.");
        RevokeReason = RequireReason(reason);
        RevokedAtUtc = nowUtc;
        Status = ConsentFormStatus.Revoked;
        UpdatedAtUtc = nowUtc;
    }

    private static void Ensure(bool allowed, string message)
    {
        if (!allowed)
        {
            throw new DomainException(message);
        }
    }

    private static string RequireReason(string reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        return trimmed.Length is < ReasonMinLength or > ReasonMaxLength
            ? throw new DomainException($"Lý do phải từ {ReasonMinLength} đến {ReasonMaxLength} ký tự.")
            : trimmed;
    }
}
