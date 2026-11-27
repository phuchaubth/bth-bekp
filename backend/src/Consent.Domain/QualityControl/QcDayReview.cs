using Consent.Domain.Common;

namespace Consent.Domain.QualityControl;

/// <summary>
/// Nhân viên Khoa HSH xem xét một ngày của sổ (cột cuối của sổ giấy, quyết định 57). Ngày đã xem xét thì khoa không ghi, không
/// điều chỉnh kết quả ngày đó nữa; cần sửa thì Khoa HSH trả lại ngày (bắt buộc lý do). Lần xem xét bị trả lại vẫn giữ, xem xét
/// lại là một bản ghi mới.
/// </summary>
public sealed class QcDayReview : Entity, IAuditableEntity
{
    public const int NoteMaxLength = 500;

    private QcDayReview(Guid id)
        : base(id)
    {
    }

    public Guid BookId { get; private set; }

    public DateOnly Date { get; private set; }

    public QcReviewOutcome Outcome { get; private set; }

    /// <summary>Ghi chú của Khoa HSH (không bắt buộc).</summary>
    public string? Note { get; private set; }

    public Guid ReviewedByUserId { get; private set; }

    public DateTimeOffset ReviewedAtUtc { get; private set; }

    public string? ReturnReason { get; private set; }

    public Guid? ReturnedByUserId { get; private set; }

    public DateTimeOffset? ReturnedAtUtc { get; private set; }

    /// <summary>Lần xem xét đang có hiệu lực (chưa bị trả lại).</summary>
    public bool IsEffective => ReturnedAtUtc is null;

    internal static QcDayReview Create(
        Guid bookId, DateOnly date, QcReviewOutcome outcome, string? note, Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người xem xét.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new DomainException("Chọn kết quả xem xét.");
        }

        return new QcDayReview(Guid.CreateVersion7())
        {
            BookId = bookId,
            Date = date,
            Outcome = outcome,
            Note = QcText.Optional(note, NoteMaxLength, "Ghi chú"),
            ReviewedByUserId = userId,
            ReviewedAtUtc = nowUtc,
        };
    }

    internal void Return(string? reason, Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người trả lại ngày.");
        }

        if (!IsEffective)
        {
            throw new DomainException("Lần xem xét này đã được trả lại.");
        }

        ReturnReason = QcText.Required(reason, NoteMaxLength, "Lý do trả lại");
        ReturnedByUserId = userId;
        ReturnedAtUtc = nowUtc;
    }
}
