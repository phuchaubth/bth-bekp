using Consent.Domain.Authorization;
using Consent.Domain.Common;

namespace Consent.Domain.Identity;

/// <summary>Vai trò, chứa tập mã quyền. Vai trò Quản trị hệ thống luôn có toàn bộ quyền.</summary>
public sealed class Role : Entity, IAuditableEntity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

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

    public static Role Create(string code, string name, string? description, bool isSystem)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > CodeMaxLength)
        {
            throw new DomainException("Mã vai trò không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            throw new DomainException("Tên vai trò không hợp lệ.");
        }

        return new Role(Guid.CreateVersion7(), code.Trim().ToUpperInvariant(), name.Trim(), description?.Trim(), isSystem);
    }

    /// <summary>Thay toàn bộ tập quyền. Không áp dụng cho vai trò Quản trị hệ thống.</summary>
    public void SetPermissions(IEnumerable<string> permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);
        if (IsAdministrator)
        {
            throw new DomainException("Không thể thay đổi quyền của vai trò Quản trị hệ thống.");
        }

        ReplacePermissions(permissionCodes);
    }

    /// <summary>Bảo đảm vai trò có đủ mọi quyền trong danh mục (dùng cho vai trò Quản trị hệ thống).</summary>
    public void GrantAllPermissions() => ReplacePermissions(PermissionCatalog.AllCodes);

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
