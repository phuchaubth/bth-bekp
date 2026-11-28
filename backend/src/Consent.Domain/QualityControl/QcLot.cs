using System.Globalization;
using Consent.Domain.Common;

namespace Consent.Domain.QualityControl;

/// <summary>
/// Một lô que thử hoặc dung dịch chứng dùng chung toàn viện (quyết định 57), Khoa HSH khai báo: số lô, hạn dùng; dung dịch chứng
/// có thêm mức (Level 1–3), ngưỡng chấp nhận và đơn vị. Khoa chọn lô đang dùng khi ghi kết quả; mỗi kết quả chép lại số lô, hạn
/// dùng, ngưỡng lúc ghi nên sửa lô sau này không làm đổi kết quả hay đánh giá đã ghi. Không xóa lô: hết dùng thì ngưng sử dụng.
/// </summary>
public sealed class QcLot : Entity, IAuditableEntity
{
    public const int ProductMaxLength = 100;
    public const int LotNumberMaxLength = 50;
    public const int MinLevel = 1;
    public const int MaxLevel = 3;

    /// <summary>Kết quả và ngưỡng tối đa (mg/dL hoặc mmol/L), một chữ số thập phân.</summary>
    public const decimal MaxValue = 9999.9m;

    private QcLot(Guid id)
        : base(id)
    {
    }

    public QcLotKind Kind { get; private set; }

    /// <summary>Tên vật tư để khoa chọn đúng lô, ví dụ "Que thử A1 (giả lập)".</summary>
    public string ProductName { get; private set; } = string.Empty;

    public string LotNumber { get; private set; } = string.Empty;

    /// <summary>Hạn dùng (tính cả ngày này): kết quả ghi sau hạn dùng không nhận lô này.</summary>
    public DateOnly ExpiryDate { get; private set; }

    /// <summary>Mức dung dịch chứng (1–3); que thử để trống.</summary>
    public int? Level { get; private set; }

    public decimal? RangeLow { get; private set; }

    public decimal? RangeHigh { get; private set; }

    public QcUnit? Unit { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static QcLot CreateTestStrip(string productName, string lotNumber, DateOnly expiryDate, DateTimeOffset nowUtc)
    {
        var lot = new QcLot(Guid.CreateVersion7()) { Kind = QcLotKind.TestStrip, CreatedAtUtc = nowUtc, IsActive = true };
        lot.Set(productName, lotNumber, expiryDate, null, null, null, null);
        return lot;
    }

    public static QcLot CreateControl(
        string productName,
        string lotNumber,
        DateOnly expiryDate,
        int level,
        decimal rangeLow,
        decimal rangeHigh,
        QcUnit unit,
        DateTimeOffset nowUtc)
    {
        var lot = new QcLot(Guid.CreateVersion7()) { Kind = QcLotKind.ControlSolution, CreatedAtUtc = nowUtc, IsActive = true };
        lot.Set(productName, lotNumber, expiryDate, level, rangeLow, rangeHigh, unit);
        return lot;
    }

    /// <summary>Sửa lô (không đổi được loại vật tư); chỉ áp dụng cho kết quả ghi sau khi sửa.</summary>
    public void Update(
        string productName,
        string lotNumber,
        DateOnly expiryDate,
        int? level,
        decimal? rangeLow,
        decimal? rangeHigh,
        QcUnit? unit,
        bool isActive)
    {
        Set(productName, lotNumber, expiryDate, level, rangeLow, rangeHigh, unit);
        IsActive = isActive;
    }

    /// <summary>Kết quả nằm trong ngưỡng chấp nhận (tính cả hai đầu ngưỡng).</summary>
    public static bool InRange(decimal value, decimal low, decimal high) => value >= low && value <= high;

    /// <summary>Kiểm tra một kết quả hoặc ngưỡng: lớn hơn 0, tối đa <see cref="MaxValue"/>, một chữ số thập phân.</summary>
    public static bool IsValidValue(decimal value) => value > 0 && value <= MaxValue && decimal.Round(value, 1) == value;

    /// <summary>Số in trên sổ: bỏ phần thập phân bằng 0 (140; 6.5).</summary>
    public static string Format(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    public static string UnitLabel(QcUnit unit) => unit == QcUnit.MmolPerL ? "mmol/L" : "mg/dL";

    /// <summary>Kiểm tra hết rồi mới gán, để lô không bị sửa dở khi dữ liệu không hợp lệ.</summary>
    private void Set(
        string productName, string lotNumber, DateOnly expiryDate, int? level, decimal? rangeLow, decimal? rangeHigh, QcUnit? unit)
    {
        var product = QcText.Required(productName, ProductMaxLength, "Tên vật tư");
        var lot = QcText.Required(lotNumber, LotNumberMaxLength, "Số lô");
        if (expiryDate == default)
        {
            throw new DomainException("Nhập hạn dùng của lô.");
        }

        if (Kind == QcLotKind.TestStrip)
        {
            if (level is not null || rangeLow is not null || rangeHigh is not null || unit is not null)
            {
                throw new DomainException("Que thử không có mức và ngưỡng chấp nhận.");
            }
        }
        else
        {
            if (level is not { } controlLevel || controlLevel is < MinLevel or > MaxLevel)
            {
                throw new DomainException($"Mức dung dịch chứng từ {MinLevel} đến {MaxLevel}.");
            }

            if (rangeLow is not { } low || rangeHigh is not { } high || !IsValidValue(low) || !IsValidValue(high))
            {
                throw new DomainException($"Ngưỡng chấp nhận là số lớn hơn 0, tối đa {Format(MaxValue)}, một chữ số thập phân.");
            }

            if (low >= high)
            {
                throw new DomainException("Ngưỡng dưới phải nhỏ hơn ngưỡng trên.");
            }

            if (unit is not { } controlUnit || !Enum.IsDefined(controlUnit))
            {
                throw new DomainException("Chọn đơn vị của ngưỡng chấp nhận.");
            }
        }

        ProductName = product;
        LotNumber = lot;
        ExpiryDate = expiryDate;
        Level = level;
        RangeLow = rangeLow;
        RangeHigh = rangeHigh;
        Unit = unit;
    }
}
