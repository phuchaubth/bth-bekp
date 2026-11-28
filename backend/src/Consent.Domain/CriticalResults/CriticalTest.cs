using Consent.Domain.Common;

namespace Consent.Domain.CriticalResults;

/// <summary>Mẫu sổ của khoa báo kết quả (quyết định 60): sổ khoa cận lâm sàng ST01 hoặc sổ Khoa Vi sinh (có SID, mẫu khẩn).</summary>
public enum CriticalLogbook
{
    /// <summary>Sổ theo dõi thông báo kết quả nguy cấp khoa cận lâm sàng (QT-KQNC/ST01).</summary>
    Laboratory,

    /// <summary>Sổ thông báo kết quả nguy cấp qua điện thoại của Khoa Vi sinh (QT-VS-21/ST02): người nhận nhắc lại 2 lần.</summary>
    Microbiology,
}

/// <summary>Thông tin một mục danh mục kết quả nguy cấp khi thêm hoặc sửa.</summary>
public sealed record CriticalTestDetails(
    string? Name,
    string? AdultRange,
    string? ChildRange,
    string? Unit,
    string? Section,
    CriticalLogbook Logbook,
    bool TextResult,
    int SortOrder);

/// <summary>
/// Một mục của danh mục kết quả nguy cấp (Phụ lục 01 QT-KQNC; quyết định 60): tên xét nghiệm hoặc kỹ thuật, khoảng nguy cấp người
/// lớn và trẻ em (ghi bằng chữ, người ghi tự đối chiếu như quy trình), đơn vị, khoa thực hiện, mẫu sổ. Rà soát hằng năm: sửa
/// hoặc ngưng dùng, không xóa (hồ sơ đã ghi chép lại tên và khoảng nguy cấp lúc ghi).
/// </summary>
public sealed class CriticalTest : Entity, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int RangeMaxLength = 500;
    public const int UnitMaxLength = 30;
    public const int SectionMaxLength = 200;
    public const int MaxSortOrder = 9999;

    private CriticalTest(Guid id, DateTimeOffset createdAtUtc)
        : base(id)
    {
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Khoảng nguy cấp cho người lớn, ví dụ "≤ 2,5 hoặc ≥ 6,0 mmol/L".</summary>
    public string AdultRange { get; private set; } = string.Empty;

    /// <summary>Khoảng nguy cấp cho trẻ em (trống: như người lớn).</summary>
    public string? ChildRange { get; private set; }

    /// <summary>Đơn vị mặc định khi ghi kết quả (kết quả ghi bằng chữ thì trống).</summary>
    public string? Unit { get; private set; }

    /// <summary>Khoa/phòng thực hiện theo danh mục, ví dụ "Huyết sinh học".</summary>
    public string? Section { get; private set; }

    public CriticalLogbook Logbook { get; private set; }

    /// <summary>Kết quả ghi bằng chữ (vi sinh, chẩn đoán hình ảnh, điện tim…) thay vì giá trị và đơn vị.</summary>
    public bool TextResult { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static CriticalTest Create(CriticalTestDetails details, DateTimeOffset nowUtc)
    {
        var test = new CriticalTest(Guid.CreateVersion7(), nowUtc);
        test.Apply(details);
        return test;
    }

    public void Update(CriticalTestDetails details, bool isActive, DateTimeOffset nowUtc)
    {
        Apply(details);
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }

    private void Apply(CriticalTestDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!Enum.IsDefined(details.Logbook))
        {
            throw new DomainException("Chọn mẫu sổ.");
        }

        if (details.SortOrder is < 0 or > MaxSortOrder)
        {
            throw new DomainException($"Thứ tự từ 0 đến {MaxSortOrder}.");
        }

        Name = CriticalText.Required(details.Name, NameMaxLength, "Tên xét nghiệm, kỹ thuật");
        AdultRange = CriticalText.Required(details.AdultRange, RangeMaxLength, "Khoảng nguy cấp người lớn");
        ChildRange = CriticalText.Optional(details.ChildRange, RangeMaxLength, "Khoảng nguy cấp trẻ em");
        Unit = details.TextResult ? null : CriticalText.Optional(details.Unit, UnitMaxLength, "Đơn vị");
        Section = CriticalText.Optional(details.Section, SectionMaxLength, "Khoa thực hiện");
        Logbook = details.Logbook;
        TextResult = details.TextResult;
        SortOrder = details.SortOrder;
    }
}

/// <summary>Kiểm tra chữ nhập của kết quả nguy cấp: bỏ khoảng trắng hai đầu, gộp ký tự điều khiển, giới hạn độ dài.</summary>
internal static class CriticalText
{
    public static string Required(string? value, int maxLength, string name) =>
        Optional(value, maxLength, name) ?? throw new DomainException($"Nhập {LowerFirst(name)}.");

    public static string? Optional(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
        {
            throw new DomainException($"{name} có ký tự không hợp lệ.");
        }

        return text.Length <= maxLength ? text : throw new DomainException($"{name} tối đa {maxLength} ký tự.");
    }

    private static string LowerFirst(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
