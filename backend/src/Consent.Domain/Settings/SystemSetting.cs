using Consent.Domain.Common;

namespace Consent.Domain.Settings;

/// <summary>
/// Một giá trị cấu hình do quản trị hệ thống sửa trên giao diện (màn hình 19), ví dụ tên đơn vị in trên PDF.
/// Bí mật (khóa, mật khẩu, chuỗi kết nối) không bao giờ lưu ở đây: chúng chỉ nằm trong biến môi trường.
/// Mọi thay đổi được ghi nhật ký audit (giá trị cũ/mới).
/// </summary>
public sealed class SystemSetting : Entity, IAuditableEntity
{
    public const int KeyMaxLength = 100;
    public const int ValueMaxLength = 500;

    private SystemSetting(Guid id, string key, string value, DateTimeOffset updatedAtUtc)
        : base(id)
    {
        Key = key;
        Value = value;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static SystemSetting Create(string key, string value, DateTimeOffset nowUtc)
    {
        if (!SettingKeys.IsDefined(key))
        {
            throw new DomainException($"Khóa cấu hình không hợp lệ: {key}.");
        }

        return new SystemSetting(Guid.CreateVersion7(), key, Normalize(value), nowUtc);
    }

    public void Update(string value, DateTimeOffset nowUtc)
    {
        Value = Normalize(value);
        UpdatedAtUtc = nowUtc;
    }

    private static string Normalize(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > ValueMaxLength)
        {
            throw new DomainException($"Giá trị cấu hình phải có từ 1 đến {ValueMaxLength} ký tự.");
        }

        return normalized;
    }
}

/// <summary>Các khóa cấu hình được phép lưu trong CSDL.</summary>
public static class SettingKeys
{
    /// <summary>Cơ quan chủ quản in ở đầu phiếu, ví dụ "SỞ Y TẾ ...".</summary>
    public const string HospitalParentAgency = "Hospital.ParentAgency";

    public const string HospitalName = "Hospital.Name";

    /// <summary>Địa danh ghi ở dòng ngày tháng của phiếu.</summary>
    public const string HospitalPlace = "Hospital.Place";

    /// <summary>Khóa lưu trữ của file logo bệnh viện (file nằm trong kho tài liệu, không lưu trong CSDL).</summary>
    public const string LogoStorageKey = "Logo.StorageKey";

    /// <summary>Kiểu ảnh của logo: <c>image/png</c> hoặc <c>image/jpeg</c>.</summary>
    public const string LogoContentType = "Logo.ContentType";

    /// <summary>
    /// Mã các mẫu người bệnh tự làm trên kiosk, theo thứ tự làm, phân tách bằng dấu phẩy (quyết định 24), ví dụ
    /// "QT-KNGT-BM02,TTNB-NGOAITRU". Chưa đặt thì kiosk chưa có phiếu nào để làm.
    /// </summary>
    public const string KioskTemplateCodes = "Kiosk.TemplateCodes";

    /// <summary>
    /// Bắt buộc chọn lượt tiếp nhận HIS khi ký hoàn tất phiếu (quyết định 25): <c>true</c> hoặc <c>false</c>. Quản trị bật/tắt
    /// ở màn hình Cấu hình hệ thống; chưa đặt là tắt.
    /// </summary>
    public const string EncountersEnabled = "Encounters.Enabled";

    /// <summary>
    /// Khóa lưu trữ của ảnh khung chữ ký: ảnh nền thiết kế của bệnh viện hiện quanh khung ký trên màn hình (điện thoại, kiosk,
    /// máy tính). Chỉ để hiển thị, không in vào ảnh chữ ký hay PDF. File nằm trong kho tài liệu.
    /// </summary>
    public const string SignatureFrameStorageKey = "SignatureFrame.StorageKey";

    /// <summary>Kiểu ảnh của khung chữ ký: <c>image/png</c> hoặc <c>image/jpeg</c>.</summary>
    public const string SignatureFrameContentType = "SignatureFrame.ContentType";

    /// <summary>
    /// Số ngày được kế thừa phiếu lần trước (quyết định 27): người bệnh làm lại cùng mẫu ở cùng khoa trong số ngày này thì
    /// phiếu mới điền sẵn câu trả lời của phiếu đã hoàn tất gần nhất. <c>0</c> hoặc chưa đặt là tắt.
    /// </summary>
    public const string FormInheritanceDays = "Forms.InheritanceDays";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            [
                HospitalParentAgency, HospitalName, HospitalPlace, LogoStorageKey, LogoContentType, KioskTemplateCodes, EncountersEnabled,
                SignatureFrameStorageKey, SignatureFrameContentType, FormInheritanceDays,
            ],
            StringComparer.Ordinal);

    public static bool IsDefined(string key) => key is not null && All.Contains(key);
}
