using Consent.Domain.Common;

namespace Consent.Domain.ConsentForms;

/// <summary>Lý do mã ký trên điện thoại riêng bị hủy.</summary>
public enum RemoteSigningCancelReason
{
    /// <summary>Nhân viên hủy mã (ví dụ người bệnh muốn ký trên máy của bệnh viện).</summary>
    Staff,

    /// <summary>Nhập sai ngày sinh quá số lần cho phép.</summary>
    TooManyAttempts,
}

/// <summary>
/// Mã ký trên điện thoại riêng của người bệnh (quyết định 32): nhân viên hiện mã QR, người bệnh quét bằng điện thoại của mình,
/// nhập ngày sinh để mở phiếu rồi ký trong phiên ký của phiếu (hoặc của cả bộ phiếu).
/// <list type="bullet">
/// <item>Chỉ lưu mã băm SHA-256 của mã trong QR và của khóa thiết bị; mã gốc chỉ có trên màn hình nhân viên và điện thoại.</item>
/// <item>Phải mở trong <see cref="OpenWindow"/> kể từ lúc tạo; mở được trên đúng một điện thoại (máy đầu tiên nhập đúng ngày
/// sinh); điện thoại đó tải lại trang thì mở tiếp được trong <see cref="SignWindow"/>.</item>
/// <item>Nhập sai ngày sinh <see cref="MaxBirthDateAttempts"/> lần thì mã bị hủy.</item>
/// </list>
/// Mã chỉ dùng được khi phiên ký của các phiếu vẫn là phiên của mã: mở phiên ký khác (mã mới, hoặc ký trên máy của bệnh
/// viện) thì mã cũ hết hiệu lực.
/// </summary>
public sealed class RemoteSigningLink : Entity, IAuditableEntity
{
    /// <summary>Độ dài chuỗi hex của mã băm SHA-256.</summary>
    public const int HashLength = 64;

    public const int MaxBirthDateAttempts = 5;

    /// <summary>Thời gian để người bệnh quét mã và nhập ngày sinh.</summary>
    public static readonly TimeSpan OpenWindow = TimeSpan.FromMinutes(15);

    /// <summary>Thời gian ký kể từ lúc mở trên điện thoại (đọc phiếu, ký; tải lại trang vẫn tiếp tục được).</summary>
    public static readonly TimeSpan SignWindow = TimeSpan.FromMinutes(60);

    private RemoteSigningLink(
        Guid id,
        string codeHash,
        Guid patientId,
        Guid sessionId,
        Guid? bundleId,
        Guid[] formIds,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        CodeHash = codeHash;
        PatientId = patientId;
        SessionId = sessionId;
        BundleId = bundleId;
        FormIds = formIds;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc + OpenWindow;
    }

    /// <summary>SHA-256 (hex) của mã trong QR.</summary>
    [SensitiveData]
    public string CodeHash { get; private set; }

    public Guid PatientId { get; private set; }

    /// <summary>Phiên ký của các phiếu (<see cref="ConsentForm.SigningSessionId"/>) mà mã này được ký.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>Bộ phiếu (khi ký cả bộ); rỗng khi ký một phiếu.</summary>
    public Guid? BundleId { get; private set; }

    /// <summary>Các phiếu trong phiên ký.</summary>
    public Guid[] FormIds { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Hạn mở mã trên điện thoại.</summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Lúc mở được trên điện thoại (đã nhập đúng ngày sinh).</summary>
    public DateTimeOffset? OpenedAtUtc { get; private set; }

    /// <summary>SHA-256 (hex) của khóa thiết bị cấp cho điện thoại đã mở mã.</summary>
    [SensitiveData]
    public string? DeviceKeyHash { get; private set; }

    /// <summary>Số lần nhập sai ngày sinh.</summary>
    public int FailedAttempts { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public RemoteSigningCancelReason? CancelReason { get; private set; }

    public bool IsOpened => OpenedAtUtc is not null;

    public bool IsCancelled => CancelledAtUtc is not null;

    /// <summary>Còn mở được trên một điện thoại: chưa mở, chưa hủy, chưa quá hạn mở.</summary>
    public bool CanOpen(DateTimeOffset nowUtc) => !IsCancelled && !IsOpened && nowUtc < ExpiresAtUtc;

    /// <summary>Điện thoại đã mở mã còn tiếp tục được (ví dụ tải lại trang): chưa hủy, còn trong thời gian ký.</summary>
    public bool CanResume(DateTimeOffset nowUtc) => !IsCancelled && OpenedAtUtc is { } opened && nowUtc < opened + SignWindow;

    public static RemoteSigningLink Create(
        string codeHash,
        Guid patientId,
        Guid sessionId,
        IReadOnlyCollection<Guid> formIds,
        Guid? bundleId,
        Guid createdByUserId,
        DateTimeOffset nowUtc)
    {
        RequireHash(codeHash);
        ArgumentNullException.ThrowIfNull(formIds);
        if (formIds.Count == 0)
        {
            throw new DomainException("Mã ký phải có ít nhất một phiếu.");
        }

        return new RemoteSigningLink(
            Guid.CreateVersion7(), codeHash, patientId, sessionId, bundleId, [.. formIds.Distinct()], createdByUserId, nowUtc);
    }

    /// <summary>Người bệnh nhập đúng ngày sinh: mã gắn với điện thoại này (khóa thiết bị), máy khác không mở được nữa.</summary>
    public void Open(string deviceKeyHash, DateTimeOffset nowUtc)
    {
        RequireHash(deviceKeyHash);
        if (!CanOpen(nowUtc))
        {
            throw new DomainException("Mã ký đã được mở, đã hủy hoặc đã hết hạn.");
        }

        OpenedAtUtc = nowUtc;
        DeviceKeyHash = deviceKeyHash;
        FailedAttempts = 0;
    }

    /// <summary>Nhập sai ngày sinh; đủ số lần tối đa thì hủy mã. Trả về <c>true</c> khi mã vừa bị hủy.</summary>
    public bool RegisterFailedAttempt(DateTimeOffset nowUtc)
    {
        if (!CanOpen(nowUtc))
        {
            throw new DomainException("Mã ký đã được mở, đã hủy hoặc đã hết hạn.");
        }

        FailedAttempts++;
        if (FailedAttempts < MaxBirthDateAttempts)
        {
            return false;
        }

        Cancel(RemoteSigningCancelReason.TooManyAttempts, nowUtc);
        return true;
    }

    /// <summary>Hủy mã (đã hủy thì giữ nguyên lý do và thời điểm hủy đầu tiên).</summary>
    public void Cancel(RemoteSigningCancelReason reason, DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Lý do hủy mã ký không hợp lệ.");
        }

        if (IsCancelled)
        {
            return;
        }

        CancelledAtUtc = nowUtc;
        CancelReason = reason;
    }

    private static void RequireHash(string value)
    {
        if (value is not { Length: HashLength } || !value.All(char.IsAsciiHexDigitLower))
        {
            throw new DomainException("Mã băm không hợp lệ.");
        }
    }
}
