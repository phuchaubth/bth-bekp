using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Departments;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

[ApiController]
[Route("api/v1/departments")]
[Produces("application/json")]
public sealed class DepartmentsController(DepartmentService departments) : ControllerBase
{
    /// <summary>Danh sách khoa/phòng (mọi người dùng đã đăng nhập; dùng cho danh sách chọn).</summary>
    [HttpGet]
    public async Task<IReadOnlyList<DepartmentDto>> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        await departments.ListAsync(includeInactive, cancellationToken).ConfigureAwait(false);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await departments.GetAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [HasPermission(Permissions.DepartmentsManage)]
    public async Task<IActionResult> Create(DepartmentRequest request, CancellationToken cancellationToken)
    {
        var result = await departments.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DepartmentsManage)]
    public async Task<IActionResult> Update(Guid id, DepartmentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await departments.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Xóa khoa chưa có người dùng; khoa đang được dùng thì trả 422 (hãy ngừng sử dụng thay vì xóa).</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.DepartmentsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await departments.DeleteAsync(id, cancellationToken).ConfigureAwait(false));
}
