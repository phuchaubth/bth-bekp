using System.Text;
using Consent.Domain.Common;

namespace Consent.Domain.Departments;

/// <summary>Khoa/phòng của bệnh viện.</summary>
public sealed class Department : Entity, IAuditableEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;
    public const int ShortNameMaxLength = 20;

    private Department(Guid id, string code, string name)
        : base(id)
    {
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Mã khoa (viết hoa, duy nhất), ví dụ <c>KKB</c>.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Tên viết tắt in trên bảng tổng hợp điều dưỡng thay cho mã khoa (quyết định 45); <c>null</c> thì dùng mã khoa. Không đổi
    /// khi lấy danh mục từ HIS; mã HIS vẫn là mã để ghép số liệu.
    /// </summary>
    public string? ShortName { get; private set; }

    public static Department Create(string code, string name) =>
        new(Guid.CreateVersion7(), NormalizeCode(code), NormalizeName(name));

    public void Update(string code, string name, bool isActive)
    {
        Code = NormalizeCode(code);
        Name = NormalizeName(name);
        IsActive = isActive;
    }

    /// <summary>
    /// Cập nhật theo danh mục khoa/phòng của HIS (quyết định 42): mã, tên như HIS và đang sử dụng. Trả về <c>true</c> nếu có
    /// thay đổi.
    /// </summary>
    public bool SyncFromHis(string code, string name)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedName = NormalizeName(name);
        if (Code == normalizedCode && Name == normalizedName && IsActive)
        {
            return false;
        }

        Code = normalizedCode;
        Name = normalizedName;
        IsActive = true;
        return true;
    }

    /// <summary>Ngừng sử dụng, ví dụ khi khoa/phòng không còn trong danh mục HIS. Không xóa vì phiếu, người dùng còn gắn.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Đặt tên viết tắt (bỏ khoảng trắng hai đầu, chuẩn Unicode dựng sẵn như tên khoa lấy từ HIS); để trống là bỏ tên viết tắt,
    /// dùng lại mã khoa.
    /// </summary>
    public void SetShortName(string? shortName)
    {
        var normalized = NormalizeShortName(shortName);
        if (normalized is { Length: > ShortNameMaxLength } || (normalized is not null && normalized.Any(char.IsControl)))
        {
            throw new DomainException($"Tên viết tắt tối đa {ShortNameMaxLength} ký tự, không có ký tự điều khiển.");
        }

        ShortName = normalized;
    }

    /// <summary>Tên viết tắt chuẩn hóa: bỏ khoảng trắng hai đầu, Unicode dựng sẵn (NFC); trống thì <c>null</c>.</summary>
    public static string? NormalizeShortName(string? shortName) =>
        string.IsNullOrWhiteSpace(shortName) ? null : shortName.Trim().Normalize(NormalizationForm.FormC);

    public static string NormalizeCode(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0 || normalized.Length > CodeMaxLength)
        {
            throw new DomainException($"Mã khoa phải có từ 1 đến {CodeMaxLength} ký tự.");
        }

        return normalized;
    }

    private static string NormalizeName(string name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > NameMaxLength)
        {
            throw new DomainException($"Tên khoa phải có từ 1 đến {NameMaxLength} ký tự.");
        }

        return normalized;
    }
}
