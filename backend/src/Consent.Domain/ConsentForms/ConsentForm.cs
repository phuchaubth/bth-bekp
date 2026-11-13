using Consent.Domain.Common;

namespace Consent.Domain.ConsentForms;

/// <summary>
/// Vòng đời phiếu (giai đoạn chưa có SmartCA, ký tay trên máy tính bảng):
/// Nháp → Chờ ký (người bệnh ký tay, rồi phía bệnh viện xác nhận) → Hoàn tất → (Thu hồi); Nháp/Chờ ký → Hủy.
/// Khi tích hợp SmartCA sẽ bổ sung các bước ký số.
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

    private readonly List<ConsentSignature> _signatures = [];

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

    /// <summary>Quyết định của người bệnh/người đại diện ghi nhận khi ký.</summary>
    public ConsentDecision? Decision { get; private set; }

    /// <summary>Phiên ký đang mở trên máy tính bảng; chỉ phiên này được nhận chữ ký của người bệnh (dùng một lần).</summary>
    public Guid? SigningSessionId { get; private set; }

    /// <summary>Phiếu đã thu hồi mà phiếu này thay thế.</summary>
    public Guid? ReplacesFormId { get; private set; }

    /// <summary>Khóa lưu trữ của file PDF đã ký (file được mã hóa khi lưu).</summary>
    public string? SignedPdfKey { get; private set; }

    /// <summary>SHA-256 (hex) của file PDF đã ký, để phát hiện file bị sửa.</summary>
    public string? SignedPdfSha256 { get; private set; }

    public long? SignedPdfSize { get; private set; }

    public IReadOnlyCollection<ConsentSignature> Signatures => _signatures;

    /// <summary>Chữ ký tay của người bệnh hoặc người đại diện (nếu đã ký).</summary>
    public ConsentSignature? PatientSignature => _signatures.FirstOrDefault(s => s.Role != ConsentSignerRole.Hospital);

    /// <summary>Xác nhận của nhân viên phía bệnh viện (nếu đã hoàn tất).</summary>
    public ConsentSignature? HospitalSignature => _signatures.FirstOrDefault(s => s.Role == ConsentSignerRole.Hospital);

    public bool CanEdit => Status == ConsentFormStatus.Draft;

    public bool CanSubmit => Status == ConsentFormStatus.Draft;

    public bool CanReturnToDraft => Status == ConsentFormStatus.PendingSignature;

    public bool CanCancel => Status is ConsentFormStatus.Draft or ConsentFormStatus.PendingSignature;

    /// <summary>Mở phiên ký cho người bệnh: phiếu chờ ký và người bệnh chưa ký.</summary>
    public bool CanStartSigning => Status == ConsentFormStatus.PendingSignature && PatientSignature is null;

    /// <summary>Phía bệnh viện xác nhận để hoàn tất: người bệnh đã ký (luật BR-02: người bệnh ký trước).</summary>
    public bool CanComplete => Status == ConsentFormStatus.PendingSignature && PatientSignature is not null;

    public bool CanRevoke => Status == ConsentFormStatus.Completed;

    public static ConsentForm Create(
        string formNo,
        Guid patientId,
        Guid templateId,
        Guid templateVersionId,
        Guid departmentId,
        string dataJson,
        Guid createdByUserId,
        DateTimeOffset nowUtc,
        Guid? replacesFormId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formNo);
        ArgumentNullException.ThrowIfNull(dataJson);
        return new ConsentForm(
            Guid.CreateVersion7(), formNo, patientId, templateId, templateVersionId, departmentId, dataJson, createdByUserId, nowUtc)
        {
            ReplacesFormId = replacesFormId,
        };
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

    /// <summary>
    /// Trả phiếu chờ ký về nháp để sửa. Nếu người bệnh đã ký mà phía bệnh viện chưa xác nhận, chữ ký bị bỏ vì nội dung
    /// sẽ thay đổi (nhật ký audit vẫn giữ dấu vết).
    /// </summary>
    public void ReturnToDraft(DateTimeOffset nowUtc)
    {
        Ensure(CanReturnToDraft, "Chỉ trả về nháp được phiếu đang chờ ký.");
        SnapshotJson = null;
        SubmittedAtUtc = null;
        _signatures.Clear();
        Decision = null;
        SigningSessionId = null;
        Status = ConsentFormStatus.Draft;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Mở phiên ký trên máy tính bảng. Mở phiên mới thì phiên cũ hết hiệu lực.</summary>
    public void StartSigning(Guid sessionId, DateTimeOffset nowUtc)
    {
        Ensure(CanStartSigning, "Chỉ mở phiên ký được cho phiếu đang chờ ký mà người bệnh chưa ký.");
        SigningSessionId = sessionId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Người bệnh (hoặc người đại diện đã chọn khi soạn phiếu) ký tay trong phiên ký đang mở. <paramref name="decision"/>
    /// rỗng khi mẫu không hỏi Đồng ý/Từ chối (người bệnh chỉ ký xác nhận nội dung).
    /// </summary>
    public ConsentSignature SignByPatient(
        Guid sessionId,
        ConsentDecision? decision,
        string signerName,
        byte[] imagePng,
        string? device,
        Guid? staffUserId,
        DateTimeOffset nowUtc)
    {
        Ensure(Status == ConsentFormStatus.PendingSignature, "Phiếu không ở trạng thái chờ ký.");
        Ensure(PatientSignature is null, "Người bệnh đã ký phiếu này.");
        Ensure(SigningSessionId is { } current && current == sessionId, "Phiên ký đã kết thúc hoặc đã được thay bằng phiên khác.");
        if (decision is { } value && !Enum.IsDefined(value))
        {
            throw new DomainException("Quyết định của người bệnh không hợp lệ.");
        }

        var role = SignerKind == ConsentSignerKind.Representative ? ConsentSignerRole.Representative : ConsentSignerRole.Patient;
        var signature = ConsentSignature.Handwritten(Id, role, signerName, imagePng, device, staffUserId, nowUtc);
        _signatures.Add(signature);
        Decision = decision;
        SigningSessionId = null;
        UpdatedAtUtc = nowUtc;
        return signature;
    }

    public void Cancel(string reason, DateTimeOffset nowUtc)
    {
        Ensure(CanCancel, "Chỉ hủy được phiếu nháp hoặc phiếu chưa ký.");
        CancelReason = RequireReason(reason);
        CancelledAtUtc = nowUtc;
        Status = ConsentFormStatus.Cancelled;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Phía bệnh viện xác nhận bằng tài khoản và hoàn tất phiếu, kèm file PDF đã ký (khóa lưu trữ, SHA-256).
    /// Phiếu hoàn tất không sửa được; muốn thay đổi thì thu hồi rồi lập phiếu thay thế.
    /// </summary>
    public void Complete(
        Guid confirmedByUserId,
        string confirmedByName,
        string? confirmedByTitle,
        string signedPdfKey,
        string signedPdfSha256,
        long signedPdfSize,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedPdfKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedPdfSha256);
        Ensure(Status == ConsentFormStatus.PendingSignature, "Chỉ hoàn tất được phiếu đang chờ ký.");
        Ensure(CanComplete, "Người bệnh chưa ký; phía bệnh viện chỉ xác nhận sau khi người bệnh ký.");
        _signatures.Add(ConsentSignature.AccountConfirmation(Id, confirmedByUserId, confirmedByName, confirmedByTitle, nowUtc));
        SignedPdfKey = signedPdfKey;
        SignedPdfSha256 = signedPdfSha256;
        SignedPdfSize = signedPdfSize;
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
