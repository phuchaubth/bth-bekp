using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Roles;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class RolesController(RoleService roles) : ControllerBase
{
    /// <summary>Danh mục quyền (mã, tên tiếng Việt, nhóm).</summary>
    [HttpGet("permissions")]
    public IReadOnlyList<PermissionDto> Permissions() => RoleService.GetPermissionCatalog();

    /// <summary>Danh sách vai trò kèm quyền.</summary>
    [HttpGet("roles")]
    public async Task<IReadOnlyList<RoleDto>> List(CancellationToken cancellationToken) =>
        await roles.ListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Cập nhật toàn bộ quyền của một vai trò (không áp dụng cho vai trò Quản trị hệ thống).</summary>
    [HttpPut("roles/{id:guid}/permissions")]
    [HasPermission(Domain.Authorization.Permissions.RolesManage)]
    public async Task<IActionResult> UpdatePermissions(
        Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await roles.UpdatePermissionsAsync(id, request, cancellationToken).ConfigureAwait(false));
}
