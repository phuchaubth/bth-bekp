using Consent.Domain.Common;

namespace Consent.Domain.QualityControl;

/// <summary>
/// Máy đo đường huyết tại giường của một khoa (quyết định 57), Khoa HSH khai báo. Mỗi máy có một sổ nội kiểm mỗi tháng. Không xóa
/// máy: máy hỏng hoặc thu hồi thì ngưng sử dụng (sổ cũ vẫn còn); chuyển máy sang khoa khác thì khoa mới mở sổ mới.
/// </summary>
public sealed class QcMeter : Entity, IAuditableEntity
{
    public const int ModelMaxLength = 100;
    public const int SerialMaxLength = 50;

    private QcMeter(Guid id)
        : base(id)
    {
    }

    public Guid DepartmentId { get; private set; }

    /// <summary>Loại máy (hãng, đời máy), ví dụ "Máy đo A1 (giả lập)".</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>Số seri máy, không trùng giữa các máy.</summary>
    public string SerialNumber { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Ghi ở ô "Seri máy/ Serial Number" của sổ: loại máy và số seri.</summary>
    public string Label => $"{Model} {SerialNumber}";

    public static QcMeter Create(Guid departmentId, string model, string serialNumber, DateTimeOffset nowUtc)
    {
        var meter = new QcMeter(Guid.CreateVersion7()) { CreatedAtUtc = nowUtc, IsActive = true };
        meter.Set(departmentId, model, serialNumber);
        return meter;
    }

    /// <summary>Sửa khoa, loại máy, số seri; ngưng hoặc dùng lại máy.</summary>
    public void Update(Guid departmentId, string model, string serialNumber, bool isActive)
    {
        Set(departmentId, model, serialNumber);
        IsActive = isActive;
    }

    private void Set(Guid departmentId, string model, string serialNumber)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Chọn khoa dùng máy.");
        }

        var modelText = QcText.Required(model, ModelMaxLength, "Loại máy");
        var serialText = QcText.Required(serialNumber, SerialMaxLength, "Số seri máy");
        DepartmentId = departmentId;
        Model = modelText;
        SerialNumber = serialText;
    }
}

/// <summary>Kiểm tra chữ dùng chung của sổ nội kiểm: bỏ khoảng trắng đầu cuối, giới hạn độ dài.</summary>
internal static class QcText
{
    public static string Required(string? value, int maxLength, string name) =>
        Optional(value, maxLength, name) ?? throw new DomainException($"Nhập {name.ToLowerInvariant()}.");

    public static string? Optional(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return text.Length <= maxLength ? text : throw new DomainException($"{name} tối đa {maxLength} ký tự.");
    }
}
