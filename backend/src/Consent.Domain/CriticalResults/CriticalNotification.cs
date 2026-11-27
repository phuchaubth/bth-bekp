using Consent.Domain.Common;

namespace Consent.Domain.CriticalResults;

/// <summary>Cách báo: điện thoại nội bộ (QT-KQNC mục 4) hoặc trực tiếp tại khoa (không liên lạc được, CĐHA tại giường).</summary>
public enum CriticalNotificationMethod
{
    Phone,
    InPerson,
}

/// <summary>Người thao tác: tài khoản đăng nhập và họ tên lúc thao tác (in vào sổ).</summary>
public sealed record CriticalActor(Guid UserId, string Name);

/// <summary>Thông tin một lần báo khi khoa cận lâm sàng ghi sau cuộc gọi.</summary>
/// <param name="ReadBackCorrect">Người nhận đã đọc lại chính xác (chỉ lưu được khi đúng).</param>
/// <param name="IncorrectReadBacks">Số lần đọc lại không chính xác trước lần đúng.</param>
/// <param name="RepeatedTwice">Người nhận đã nhắc lại 2 lần (sổ Khoa Vi sinh).</param>
public sealed record CriticalNotificationDetails(
    Guid ReceivingDepartmentId,
    CriticalNotificationMethod Method,
    DateTimeOffset? NotifiedAtUtc,
    string? ReceiverName,
    bool ReadBackCorrect,
    int IncorrectReadBacks,
    bool RepeatedTwice);

/// <summary>
/// Một lần khoa cận lâm sàng báo kết quả nguy cấp cho một khoa điều trị (quyết định 60): cách báo, giờ báo, người nghe máy (họ
/// tên người nhận đọc qua điện thoại), số lần đọc lại chưa chính xác, người báo. Khoa nhận trả lại ("không phải khoa tôi") thì lần
/// báo được đánh dấu trả lại kèm lý do, không xóa; khoa báo ghi lần báo mới cho đúng khoa.
/// </summary>
public sealed class CriticalNotification : Entity, IAuditableEntity
{
    public const int ReceiverNameMaxLength = 200;
    public const int ReasonMaxLength = 500;
    public const int MaxIncorrectReadBacks = 20;

    private CriticalNotification(Guid id)
        : base(id)
    {
    }

    public Guid ResultId { get; private set; }

    /// <summary>Lần báo thứ mấy của hồ sơ (1, 2…).</summary>
    public int AttemptNo { get; private set; }

    public Guid ReceivingDepartmentId { get; private set; }

    public CriticalNotificationMethod Method { get; private set; }

    /// <summary>Giờ báo do người báo nhập (giờ sự việc).</summary>
    public DateTimeOffset NotifiedAtUtc { get; private set; }

    public string ReceiverName { get; private set; } = string.Empty;

    public int IncorrectReadBacks { get; private set; }

    public bool RepeatedTwice { get; private set; }

    public Guid NotifiedByUserId { get; private set; }

    public string NotifiedByName { get; private set; } = string.Empty;

    /// <summary>Giờ hệ thống lúc ghi lần báo (khác giờ sự việc khi ghi bù).</summary>
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public DateTimeOffset? RejectedAtUtc { get; private set; }

    public Guid? RejectedByUserId { get; private set; }

    public string? RejectedByName { get; private set; }

    public string? RejectReason { get; private set; }

    public bool IsRejected => RejectedAtUtc is not null;

    internal static CriticalNotification Create(
        Guid resultId, int attemptNo, CriticalNotificationDetails details, DateTimeOffset notifiedAtUtc, CriticalActor by, DateTimeOffset nowUtc)
    {
        if (details.IncorrectReadBacks is < 0 or > MaxIncorrectReadBacks)
        {
            throw new DomainException($"Số lần đọc lại chưa chính xác từ 0 đến {MaxIncorrectReadBacks}.");
        }

        if (!Enum.IsDefined(details.Method))
        {
            throw new DomainException("Chọn cách báo.");
        }

        return new CriticalNotification(Guid.CreateVersion7())
        {
            ResultId = resultId,
            AttemptNo = attemptNo,
            ReceivingDepartmentId = details.ReceivingDepartmentId,
            Method = details.Method,
            NotifiedAtUtc = notifiedAtUtc,
            ReceiverName = CriticalText.Required(details.ReceiverName, ReceiverNameMaxLength, "Họ tên người nhận"),
            IncorrectReadBacks = details.IncorrectReadBacks,
            RepeatedTwice = details.RepeatedTwice,
            NotifiedByUserId = by.UserId,
            NotifiedByName = by.Name,
            RecordedAtUtc = nowUtc,
        };
    }

    internal void Reject(string? reason, CriticalActor by, DateTimeOffset nowUtc)
    {
        if (IsRejected)
        {
            throw new DomainException("Lần báo này đã được trả lại.");
        }

        RejectReason = CriticalText.Required(reason, ReasonMaxLength, "Lý do trả lại");
        RejectedAtUtc = nowUtc;
        RejectedByUserId = by.UserId;
        RejectedByName = by.Name;
    }
}
