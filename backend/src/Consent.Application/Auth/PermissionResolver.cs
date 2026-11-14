using Consent.Domain.Authorization;
using Consent.Domain.Identity;

namespace Consent.Application.Auth;

public static class PermissionResolver
{
    /// <summary>Hợp các quyền của mọi vai trò. Vai trò Quản trị hệ thống luôn có toàn bộ quyền.</summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var list = roles.ToList();
        var codes = list.Exists(r => r.IsAdministrator)
            ? PermissionCatalog.AllCodes
            : list.SelectMany(r => r.PermissionCodes).Where(PermissionCatalog.IsDefined);
        return [.. codes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
