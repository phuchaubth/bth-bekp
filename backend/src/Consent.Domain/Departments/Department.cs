using Consent.Domain.Common;

namespace Consent.Domain.Departments;

/// <summary>Khoa/phòng của bệnh viện.</summary>
public sealed class Department : Entity, IAuditableEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;

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
