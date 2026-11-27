using System.Globalization;
using Consent.Domain.Common;

namespace Consent.Domain.QualityControl;

/// <summary>Kết quả một mức dung dịch chứng khi ghi: lô đang dùng, ngày mở lọ, kết quả đo.</summary>
public sealed record QcControlReading(QcLot Lot, DateOnly OpenedOn, decimal Value);

/// <summary>
/// Một lần chạy nội kiểm khoa ghi vào sổ: ngày thực hiện, lô que thử (và ngày mở hộp), hai mức dung dịch chứng (mức thấp hơn ở
/// cột đầu, như hai cột "Kết quả thực hiện" của sổ giấy).
/// </summary>
public sealed record QcRunReading(
    DateOnly RunDate, QcLot StripLot, DateOnly StripOpenedOn, QcControlReading Control1, QcControlReading Control2);

/// <summary>
/// Kết quả nội kiểm của một ngày trong sổ (quyết định 57): chép lại số lô, hạn dùng, ngày mở, ngưỡng chấp nhận lúc ghi; phần mềm
/// tự đánh giá Đạt/Không đạt theo ngưỡng. Không đạt thì khoa ghi nguyên nhân và kết quả chạy lại (một lần). Không sửa kết quả đã
/// ghi: ghi sai thì khoa hủy (ghi lý do) hoặc ghi kết quả điều chỉnh thay thế; kết quả cũ vẫn giữ, in gạch ngang.
/// </summary>
public sealed class QcRun : Entity, IAuditableEntity
{
    public const int ReasonMaxLength = 500;

    private QcRun(Guid id)
        : base(id)
    {
    }

    public Guid BookId { get; private set; }

    /// <summary>Ngày thực hiện.</summary>
    public DateOnly RunDate { get; private set; }

    public Guid StripLotId { get; private set; }

    public string StripLotNumber { get; private set; } = string.Empty;

    public DateOnly StripExpiryDate { get; private set; }

    /// <summary>Ngày mở hộp que thử.</summary>
    public DateOnly StripOpenedOn { get; private set; }

    public Guid Control1LotId { get; private set; }

    public int Control1Level { get; private set; }

    public string Control1LotNumber { get; private set; } = string.Empty;

    public DateOnly Control1ExpiryDate { get; private set; }

    public DateOnly Control1OpenedOn { get; private set; }

    public decimal Control1Low { get; private set; }

    public decimal Control1High { get; private set; }

    public QcUnit Control1Unit { get; private set; }

    public decimal Control1Value { get; private set; }

    public decimal? Control1RepeatValue { get; private set; }

    public Guid Control2LotId { get; private set; }

    public int Control2Level { get; private set; }

    public string Control2LotNumber { get; private set; } = string.Empty;

    public DateOnly Control2ExpiryDate { get; private set; }

    public DateOnly Control2OpenedOn { get; private set; }

    public decimal Control2Low { get; private set; }

    public decimal Control2High { get; private set; }

    public QcUnit Control2Unit { get; private set; }

    public decimal Control2Value { get; private set; }

    public decimal? Control2RepeatValue { get; private set; }

    /// <summary>Nguyên nhân không đạt (khoa ghi khi kết quả không đạt).</summary>
    public string? FailureReason { get; private set; }

    public Guid? FollowUpByUserId { get; private set; }

    public DateTimeOffset? FollowUpAtUtc { get; private set; }

    /// <summary>Nhân viên thực hiện: người ghi kết quả.</summary>
    public Guid PerformedByUserId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>Ngày ghi theo giờ bệnh viện (ghi sau ngày thực hiện là ghi muộn).</summary>
    public DateOnly RecordedOn { get; private set; }

    /// <summary>Kết quả này ghi điều chỉnh, thay thế kết quả đã hủy này.</summary>
    public Guid? ReplacesRunId { get; private set; }

    /// <summary>Kết quả điều chỉnh thay thế kết quả này (khi hủy để ghi lại).</summary>
    public Guid? ReplacedByRunId { get; private set; }

    /// <summary>Lý do hủy (ghi sai): bắt buộc.</summary>
    public string? CancelReason { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public bool Control1Passed => QcLot.InRange(Control1Value, Control1Low, Control1High);

    public bool Control2Passed => QcLot.InRange(Control2Value, Control2Low, Control2High);

    /// <summary>Đánh giá kết quả: Đạt khi cả hai mức nằm trong ngưỡng chấp nhận.</summary>
    public bool Passed => Control1Passed && Control2Passed;

    public bool HasRepeat => Control1RepeatValue is not null && Control2RepeatValue is not null;

    public bool? Control1RepeatPassed =>
        Control1RepeatValue is { } value ? QcLot.InRange(value, Control1Low, Control1High) : null;

    public bool? Control2RepeatPassed =>
        Control2RepeatValue is { } value ? QcLot.InRange(value, Control2Low, Control2High) : null;

    /// <summary>Đánh giá lại kết quả (khi có chạy lại).</summary>
    public bool? RepeatPassed => HasRepeat ? Control1RepeatPassed == true && Control2RepeatPassed == true : null;

    /// <summary>Đạt ngay hoặc chạy lại đạt.</summary>
    public bool FinalPassed => Passed || RepeatPassed == true;

    public bool HasFollowUp => FollowUpAtUtc is not null;

    public bool IsActive => CancelledAtUtc is null;

    public bool IsLate => RecordedOn > RunDate;

    internal static QcRun Create(
        Guid bookId, QcRunReading reading, Guid userId, DateOnly today, DateTimeOffset nowUtc, Guid? replacesRunId)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người ghi kết quả.");
        }

        var date = reading.RunDate;
        CheckLot(reading.StripLot, QcLotKind.TestStrip, reading.StripOpenedOn, date, "Que thử");
        CheckLot(reading.Control1.Lot, QcLotKind.ControlSolution, reading.Control1.OpenedOn, date, "Dung dịch chứng");
        CheckLot(reading.Control2.Lot, QcLotKind.ControlSolution, reading.Control2.OpenedOn, date, "Dung dịch chứng");
        if (reading.Control1.Lot.Level >= reading.Control2.Lot.Level)
        {
            throw new DomainException("Chọn hai mức dung dịch chứng khác nhau, mức thấp hơn ở cột đầu.");
        }

        CheckValue(reading.Control1.Value);
        CheckValue(reading.Control2.Value);

        var run = new QcRun(Guid.CreateVersion7())
        {
            BookId = bookId,
            RunDate = date,
            StripLotId = reading.StripLot.Id,
            StripLotNumber = reading.StripLot.LotNumber,
            StripExpiryDate = reading.StripLot.ExpiryDate,
            StripOpenedOn = reading.StripOpenedOn,
            PerformedByUserId = userId,
            RecordedAtUtc = nowUtc,
            RecordedOn = today,
            ReplacesRunId = replacesRunId,
        };
        run.SetControl1(reading.Control1);
        run.SetControl2(reading.Control2);
        return run;
    }

    /// <summary>Kết quả không đạt: ghi nguyên nhân (bắt buộc) và kết quả chạy lại hai mức (bỏ trống cả hai nếu không chạy lại).</summary>
    internal void RecordFollowUp(string? reason, decimal? repeat1, decimal? repeat2, Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người ghi kết quả.");
        }

        if (Passed)
        {
            throw new DomainException("Kết quả đạt, không cần ghi nguyên nhân và chạy lại.");
        }

        if (HasFollowUp)
        {
            throw new DomainException("Đã ghi nguyên nhân và kết quả chạy lại; ghi sai thì ghi điều chỉnh cả kết quả của ngày.");
        }

        var text = QcText.Required(reason, ReasonMaxLength, "Nguyên nhân không đạt");
        if (repeat1 is null != repeat2 is null)
        {
            throw new DomainException("Nhập kết quả chạy lại của cả hai mức, hoặc bỏ trống cả hai nếu không chạy lại.");
        }

        if (repeat1 is { } value1 && repeat2 is { } value2)
        {
            CheckValue(value1);
            CheckValue(value2);
        }

        FailureReason = text;
        Control1RepeatValue = repeat1;
        Control2RepeatValue = repeat2;
        FollowUpByUserId = userId;
        FollowUpAtUtc = nowUtc;
    }

    /// <summary>Hủy kết quả ghi sai (bắt buộc lý do); <paramref name="replacedBy"/>: kết quả điều chỉnh thay thế.</summary>
    internal void Cancel(string? reason, Guid userId, DateTimeOffset nowUtc, Guid? replacedBy)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người hủy kết quả.");
        }

        if (!IsActive)
        {
            throw new DomainException("Kết quả đã hủy.");
        }

        CancelReason = QcText.Required(reason, ReasonMaxLength, "Lý do điều chỉnh");
        CancelledByUserId = userId;
        CancelledAtUtc = nowUtc;
        ReplacedByRunId = replacedBy;
    }

    private static void CheckLot(QcLot lot, QcLotKind kind, DateOnly openedOn, DateOnly runDate, string name)
    {
        ArgumentNullException.ThrowIfNull(lot);
        if (lot.Kind != kind)
        {
            throw new DomainException($"Chọn lô {name.ToLowerInvariant()}.");
        }

        if (!lot.IsActive)
        {
            throw new DomainException($"{name} lô {lot.LotNumber} đã ngưng sử dụng.");
        }

        if (lot.ExpiryDate < runDate)
        {
            throw new DomainException(
                $"{name} lô {lot.LotNumber} hết hạn ngày {lot.ExpiryDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}: không dùng được.");
        }

        if (openedOn == default || openedOn > runDate)
        {
            throw new DomainException($"Ngày mở {name.ToLowerInvariant()} lô {lot.LotNumber} phải trước hoặc bằng ngày thực hiện.");
        }
    }

    private static void CheckValue(decimal value)
    {
        if (!QcLot.IsValidValue(value))
        {
            throw new DomainException($"Kết quả là số lớn hơn 0, tối đa {QcLot.Format(QcLot.MaxValue)}, một chữ số thập phân.");
        }
    }

    private void SetControl1(QcControlReading reading)
    {
        Control1LotId = reading.Lot.Id;
        Control1Level = reading.Lot.Level!.Value;
        Control1LotNumber = reading.Lot.LotNumber;
        Control1ExpiryDate = reading.Lot.ExpiryDate;
        Control1OpenedOn = reading.OpenedOn;
        Control1Low = reading.Lot.RangeLow!.Value;
        Control1High = reading.Lot.RangeHigh!.Value;
        Control1Unit = reading.Lot.Unit!.Value;
        Control1Value = reading.Value;
    }

    private void SetControl2(QcControlReading reading)
    {
        Control2LotId = reading.Lot.Id;
        Control2Level = reading.Lot.Level!.Value;
        Control2LotNumber = reading.Lot.LotNumber;
        Control2ExpiryDate = reading.Lot.ExpiryDate;
        Control2OpenedOn = reading.OpenedOn;
        Control2Low = reading.Lot.RangeLow!.Value;
        Control2High = reading.Lot.RangeHigh!.Value;
        Control2Unit = reading.Lot.Unit!.Value;
        Control2Value = reading.Value;
    }
}
