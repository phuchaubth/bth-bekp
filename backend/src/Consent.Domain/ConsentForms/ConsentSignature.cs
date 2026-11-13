using Consent.Domain.Common;

namespace Consent.Domain.ConsentForms;

/// <summary>Quyết định của người bệnh (hoặc người đại diện) ghi nhận khi ký.</summary>
public enum ConsentDecision
{
    Agreed,
    Refused,
}

/// <summary>Vai trò của người ký trên phiếu.</summary>
public enum ConsentSignerRole
{
    Patient,
    Representative,

    /// <summary>Xác nhận cuối của phía bệnh viện (vai trò cấu hình trên mẫu); hoàn tất phiếu.</summary>
    Hospital,

    /// <summary>Một bước xác nhận trung gian của nhân viên theo vai trò (ví dụ điều dưỡng trước bác sĩ), xem <see cref="ConsentSignature.StepKey"/>.</summary>
    Staff,
}

/// <summary>Cách ký: ký tay trên điện thoại, hoặc xác nhận bằng tài khoản đăng nhập (phía bệnh viện).</summary>
public enum ConsentSignatureMethod
{
    Handwritten,
    AccountConfirmation,
}

/// <summary>
/// Một chữ ký trên phiếu. Chữ ký tay chỉ lưu ảnh PNG đã chuẩn hóa (không lưu nét ký, thời gian từng nét), ảnh được mã
/// hóa khi lưu và luôn bị che trong nhật ký audit. Xác nhận phía bệnh viện không có ảnh, chỉ có tài khoản người xác nhận.
/// </summary>
public sealed class ConsentSignature : Entity, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int TitleMaxLength = 100;
    public const int DeviceMaxLength = 300;
    public const int StepKeyMaxLength = 50;

    private ConsentSignature(
        Guid id,
        Guid formId,
        ConsentSignerRole role,
        ConsentSignatureMethod method,
        string signerName,
        string? signerTitle,
        Guid? userId,
        byte[]? imagePng,
        string? device,
        DateTimeOffset signedAtUtc,
        string? stepKey = null)
        : base(id)
    {
        StepKey = stepKey;
        FormId = formId;
        Role = role;
        Method = method;
        SignerName = signerName;
        SignerTitle = signerTitle;
        UserId = userId;
        ImagePng = imagePng;
        Device = device;
        SignedAtUtc = signedAtUtc;
    }

    public Guid FormId { get; private set; }

    public ConsentSignerRole Role { get; private set; }

    public ConsentSignatureMethod Method { get; private set; }

    /// <summary>Họ tên người ký (người bệnh, người đại diện, hoặc nhân viên xác nhận).</summary>
    public string SignerName { get; private set; }

    /// <summary>Chức danh của nhân viên xác nhận phía bệnh viện.</summary>
    public string? SignerTitle { get; private set; }

    /// <summary>Nhân viên xác nhận (phía bệnh viện) hoặc nhân viên mở phiên ký (phía người bệnh).</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Ảnh chữ ký tay (PNG đã chuẩn hóa ở máy chủ).</summary>
    [SensitiveData]
    public byte[]? ImagePng { get; private set; }

    /// <summary>Thiết bị dùng để ký (User-Agent của trình duyệt, đã cắt ngắn).</summary>
    public string? Device { get; private set; }

    public DateTimeOffset SignedAtUtc { get; private set; }

    /// <summary>Bước xác nhận theo định nghĩa mẫu (chỉ với <see cref="ConsentSignerRole.Staff"/>).</summary>
    public string? StepKey { get; private set; }

    internal static ConsentSignature Handwritten(
        Guid formId, ConsentSignerRole role, string signerName, byte[] imagePng, string? device, Guid? staffUserId, DateTimeOffset nowUtc)
    {
        if (role == ConsentSignerRole.Hospital)
        {
            throw new DomainException("Phía bệnh viện xác nhận bằng tài khoản, không ký tay.");
        }

        if (imagePng is not { Length: > 0 })
        {
            throw new DomainException("Thiếu ảnh chữ ký.");
        }

        return new ConsentSignature(
            Guid.CreateVersion7(),
            formId,
            role,
            ConsentSignatureMethod.Handwritten,
            RequireName(signerName),
            null,
            staffUserId,
            imagePng,
            Truncate(device, DeviceMaxLength),
            nowUtc);
    }

    internal static ConsentSignature AccountConfirmation(Guid formId, Guid userId, string name, string? title, DateTimeOffset nowUtc) =>
        new(
            Guid.CreateVersion7(),
            formId,
            ConsentSignerRole.Hospital,
            ConsentSignatureMethod.AccountConfirmation,
            RequireName(name),
            Truncate(title, TitleMaxLength),
            userId,
            null,
            null,
            nowUtc);

    internal static ConsentSignature StepConfirmation(
        Guid formId, string stepKey, Guid userId, string name, string? title, DateTimeOffset nowUtc)
    {
        var key = stepKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > StepKeyMaxLength)
        {
            throw new DomainException("Bước xác nhận không hợp lệ.");
        }

        return new(
            Guid.CreateVersion7(),
            formId,
            ConsentSignerRole.Staff,
            ConsentSignatureMethod.AccountConfirmation,
            RequireName(name),
            Truncate(title, TitleMaxLength),
            userId,
            null,
            null,
            nowUtc,
            key);
    }

    private static string RequireName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > NameMaxLength ? throw new DomainException("Họ tên người ký không hợp lệ.") : trimmed;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
