using Consent.Domain.Authorization;
using Consent.Domain.Identity;

namespace Consent.Application.Roles;

public sealed record PermissionDto(string Code, string Name, string Group)
{
    public static PermissionDto From(PermissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new PermissionDto(definition.Code, definition.Name, definition.Group);
    }
}

public sealed record RoleDto(
    Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsAdministrator, IReadOnlyList<string> Permissions)
{
    public static RoleDto From(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        IReadOnlyList<string> permissions = role.IsAdministrator
            ? [.. PermissionCatalog.AllCodes.Order(StringComparer.Ordinal)]
            : [.. role.PermissionCodes.Order(StringComparer.Ordinal)];
        return new RoleDto(role.Id, role.Code, role.Name, role.Description, role.IsSystem, role.IsAdministrator, permissions);
    }
}

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
