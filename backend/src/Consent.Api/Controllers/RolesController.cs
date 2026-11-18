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

    /// <summary>Thêm vai trò (quyết định 40): tên, mô tả, quyền. Mã vai trò do máy chủ sinh từ tên (tiền tố VT_).</summary>
    [HttpPost("roles")]
    [HasPermission(Domain.Authorization.Permissions.RolesManage)]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await roles.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => Created($"/api/v1/roles/{dto.Id}", dto));
    }

    /// <summary>Đổi tên, mô tả của vai trò (mã giữ nguyên).</summary>
    [HttpPut("roles/{id:guid}")]
    [HasPermission(Domain.Authorization.Permissions.RolesManage)]
    public async Task<IActionResult> Update(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await roles.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Xóa vai trò do quản trị tạo, khi không còn tài khoản nào có vai trò và không mẫu phiếu nào dùng vai trò.</summary>
    [HttpDelete("roles/{id:guid}")]
    [HasPermission(Domain.Authorization.Permissions.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await roles.DeleteAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Cập nhật toàn bộ quyền của một vai trò (không áp dụng cho vai trò Quản trị hệ thống).</summary>
    [HttpPut("roles/{id:guid}/permissions")]
    [HasPermission(Domain.Authorization.Permissions.RolesManage)]
    public async Task<IActionResult> UpdatePermissions(
        Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await roles.UpdatePermissionsAsync(id, request, cancellationToken).ConfigureAwait(false));
}
