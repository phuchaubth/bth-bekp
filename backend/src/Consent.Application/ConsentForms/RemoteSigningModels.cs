namespace Consent.Application.ConsentForms;

/// <summary>
/// Mã ký vừa tạo (quyết định 32). <paramref name="Code"/> chỉ trả một lần để dựng mã QR; máy chủ chỉ giữ mã băm.
/// </summary>
public sealed record RemoteSigningLinkDto(Guid Id, string Code, DateTimeOffset ExpiresAtUtc, IReadOnlyList<Guid> FormIds);

/// <summary>Trạng thái mã ký trên điện thoại riêng, để màn hình nhân viên theo dõi.</summary>
public enum RemoteSigningState
{
    /// <summary>Chờ người bệnh quét mã và nhập ngày sinh.</summary>
    Waiting,

    /// <summary>Đã mở trên điện thoại người bệnh, đang chờ ký.</summary>
    Opened,

    /// <summary>Người bệnh đã ký xong mọi phiếu của mã.</summary>
    Signed,

    /// <summary>Quá hạn mở mã, hoặc quá thời gian ký sau khi mở.</summary>
    Expired,

    /// <summary>Nhân viên đã hủy mã.</summary>
    Cancelled,

    /// <summary>Nhập sai ngày sinh quá số lần: mã bị hủy.</summary>
    Locked,

    /// <summary>Đã mở phiên ký khác cho các phiếu (mã mới, hoặc ký trên máy của bệnh viện).</summary>
    Superseded,
}

public sealed record RemoteSigningStatusDto(
    Guid Id,
    RemoteSigningState State,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? OpenedAtUtc,
    DateTimeOffset? SignedAtUtc);

/// <summary>
/// Điện thoại người bệnh mở mã: lần đầu gửi <paramref name="DateOfBirth"/> (ngày sinh của người bệnh); tải lại trang thì gửi
/// <paramref name="DeviceKey"/> đã nhận ở lần đầu.
/// </summary>
public sealed record ClaimRemoteSigningRequest(string? Code, DateOnly? DateOfBirth, string? DeviceKey = null);

/// <summary>
/// Phiên ký trên điện thoại người bệnh: token phiên ký (chỉ ký đúng các phiếu này), nội dung các phiếu để đọc và ký.
/// <paramref name="DeviceKey"/> chỉ có ở lần mở đầu tiên: điện thoại giữ lại để tải lại trang mà không phải nhập lại ngày sinh.
/// </summary>
public sealed record RemoteSigningSessionDto(
    string Token,
    DateTimeOffset ExpiresAtUtc,
    string? DeviceKey,
    Guid? BundleId,
    IReadOnlyList<ConsentFormDetailDto> Forms);
