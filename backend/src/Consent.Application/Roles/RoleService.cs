using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.Authorization;
using Consent.Shared;

namespace Consent.Application.Roles;

public sealed class RoleService(IRoleRepository roles, IUnitOfWork unitOfWork)
{
    public static IReadOnlyList<PermissionDto> GetPermissionCatalog() => [.. PermissionCatalog.All.Select(PermissionDto.From)];

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. items.OrderByDescending(r => r.IsAdministrator).ThenBy(r => r.Name, VietnameseComparer.Instance).Select(RoleDto.From)];
    }

    public async Task<Result<RoleDto>> UpdatePermissionsAsync(
        Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var role = await roles.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Result<RoleDto>.Failure(Error.NotFound("roles.not_found", "Không tìm thấy vai trò."));
        }

        if (role.IsAdministrator)
        {
            return Result<RoleDto>.Failure(Error.BusinessRule(
                "roles.admin_immutable", "Vai trò Quản trị hệ thống luôn có toàn bộ quyền và không thể thay đổi."));
        }

        var requested = request.Permissions ?? [];
        var unknown = requested.Where(code => !PermissionCatalog.IsDefined(code)).Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            return Result<RoleDto>.Failure(Error.ValidationField("permissions", $"Quyền không tồn tại: {string.Join(", ", unknown)}."));
        }

        role.SetPermissions(requested);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleDto.From(role);
    }
}
