using Consent.Domain.Authorization;
using Consent.Domain.Common;

namespace Consent.Domain.Identity;

/// <summary>Vai trò, chứa tập mã quyền. Vai trò Quản trị hệ thống luôn có toàn bộ quyền.</summary>
public sealed class Role : Entity, IAuditableEntity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    /// <summary>
    /// Tiền tố mã của vai trò do quản trị tạo (quyết định 40): không trùng mã vai trò có sẵn, kể cả vai trò có sẵn thêm ở bản
    /// sau.
    /// </summary>
    public const string CustomCodePrefix = "VT_";

    private readonly List<RolePermission> _permissions = [];

    private Role(Guid id, string code, string name, string? description, bool isSystem)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsSystem = isSystem;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Vai trò hệ thống (tạo sẵn), không được xóa.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public IEnumerable<string> PermissionCodes => _permissions.Select(p => p.PermissionCode);

    public bool IsAdministrator => Code == SystemRoles.Administrator;

    /// <summary>Vai trò Kiosk (quyết định 24): quyền cố định, chỉ dùng màn hình kiosk.</summary>
    public bool IsKiosk => Code == SystemRoles.Kiosk;

    public static Role Create(string code, string name, string? description, bool isSystem)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > CodeMaxLength)
        {
            throw new DomainException("Mã vai trò không hợp lệ.");
        }

        return new Role(
            Guid.CreateVersion7(), code.Trim().ToUpperInvariant(), ValidName(name), ValidDescription(description), isSystem);
    }

    /// <summary>Đổi tên và mô tả. Mã vai trò giữ nguyên: quyền của người dùng và cấu hình ký của mẫu phiếu dùng mã.</summary>
    public void UpdateDetails(string name, string? description)
    {
        Name = ValidName(name);
        Description = ValidDescription(description);
    }

    /// <summary>
    /// Thay toàn bộ tập quyền. Không áp dụng cho vai trò Quản trị hệ thống; vai trò Kiosk chỉ nhận đúng tập quyền cố định
    /// của nó (máy kiosk đặt ở nơi công cộng không được thêm quyền xem dữ liệu).
    /// </summary>
    public void SetPermissions(IEnumerable<string> permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);
        if (IsAdministrator)
        {
            throw new DomainException("Không thể thay đổi quyền của vai trò Quản trị hệ thống.");
        }

        var codes = permissionCodes.ToArray();
        if (IsKiosk && !codes.ToHashSet(StringComparer.Ordinal).SetEquals(SystemRoles.KioskPermissions))
        {
            throw new DomainException("Vai trò Kiosk chỉ có đúng quyền dùng kiosk; không thể thêm hay bớt quyền.");
        }

        // Tài khoản dùng kiosk phải là tài khoản chỉ có vai trò Kiosk (quyết định 24), nên quyền này không gán cho vai trò khác.
        if (!IsKiosk && codes.Intersect(SystemRoles.KioskPermissions, StringComparer.Ordinal).Any())
        {
            throw new DomainException("Quyền dùng kiosk chỉ dành cho vai trò Kiosk người bệnh.");
        }

        ReplacePermissions(codes);
    }

    /// <summary>Bảo đảm vai trò có đủ mọi quyền trong danh mục (dùng cho vai trò Quản trị hệ thống).</summary>
    public void GrantAllPermissions() => ReplacePermissions(PermissionCatalog.AllCodes);

    private static string ValidName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException("Tên vai trò không hợp lệ.");
        }

        return trimmed;
    }

    private static string? ValidDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (trimmed?.Length > DescriptionMaxLength)
        {
            throw new DomainException($"Mô tả vai trò tối đa {DescriptionMaxLength} ký tự.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void ReplacePermissions(IEnumerable<string> permissionCodes)
    {
        var desired = new HashSet<string>(permissionCodes, StringComparer.Ordinal);
        var unknown = desired.Where(code => !PermissionCatalog.IsDefined(code)).ToArray();
        if (unknown.Length > 0)
        {
            throw new DomainException($"Quyền không tồn tại: {string.Join(", ", unknown)}.");
        }

        _permissions.RemoveAll(p => !desired.Contains(p.PermissionCode));
        foreach (var code in desired.Where(code => _permissions.TrueForAll(p => p.PermissionCode != code)))
        {
            _permissions.Add(new RolePermission(Id, code));
        }
    }
}

/// <summary>Một quyền được gán cho vai trò.</summary>
public sealed class RolePermission : IAuditableEntity
{
    public const int CodeMaxLength = 100;

    internal RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; }
}
