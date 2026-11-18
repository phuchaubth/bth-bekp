using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Users;
using Consent.Domain.Authorization;
using Consent.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class UsersController(UserAdminService users) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.UsersRead)]
    public async Task<PagedResult<UserListItemDto>> Search([FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        await users.SearchAsync(query, cancellationToken).ConfigureAwait(false);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.UsersRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.GetAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/lock")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.LockAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.UnlockAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.ResetPasswordAsync(id, request, cancellationToken).ConfigureAwait(false));
}
