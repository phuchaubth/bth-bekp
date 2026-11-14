using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Abstractions;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Mẫu phiếu và các phiên bản (màn hình 11, 12).</summary>
[ApiController]
[Route("api/v1/consent-templates")]
[Produces("application/json")]
public sealed class ConsentTemplatesController(ConsentTemplateService templates, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.TemplatesRead)]
    public Task<IReadOnlyList<ConsentTemplateListItemDto>> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        templates.ListAsync(includeInactive, cancellationToken);

    /// <summary>Mẫu đang phát hành dùng được để lập phiếu cho khoa (mặc định: khoa của người dùng).</summary>
    [HttpGet("available")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IReadOnlyList<ConsentTemplateVersionDto>> Available([FromQuery] Guid? departmentId, CancellationToken cancellationToken) =>
        (departmentId ?? currentUser.DepartmentId) is { } department
            ? await templates.ListAvailableAsync(department, cancellationToken).ConfigureAwait(false)
            : [];

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.TemplatesRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await templates.GetAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}/versions/{versionNo:int}")]
    [HasPermission(Permissions.TemplatesRead)]
    public async Task<IActionResult> GetVersion(Guid id, int versionNo, CancellationToken cancellationToken) =>
        this.ToActionResult(await templates.GetVersionAsync(id, versionNo, cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> Create(CreateConsentTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await templates.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> Update(Guid id, UpdateConsentTemplateRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await templates.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Tạo phiên bản nháp mới từ phiên bản mới nhất.</summary>
    [HttpPost("{id:guid}/versions")]
    [HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> CreateDraft(Guid id, CancellationToken cancellationToken)
    {
        var result = await templates.CreateDraftAsync(id, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(
            result, dto => CreatedAtAction(nameof(GetVersion), new { id, versionNo = dto.VersionNo }, dto));
    }

    [HttpPut("{id:guid}/versions/{versionNo:int}")]
    [HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> UpdateDraft(
        Guid id, int versionNo, UpdateTemplateVersionRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await templates.UpdateDraftAsync(id, versionNo, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/versions/{versionNo:int}/publish")]
    [HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> Publish(Guid id, int versionNo, CancellationToken cancellationToken) =>
        this.ToActionResult(await templates.PublishAsync(id, versionNo, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/versions/{versionNo:int}/retire")]
    [HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> Retire(Guid id, int versionNo, CancellationToken cancellationToken) =>
        this.ToActionResult(await templates.RetireAsync(id, versionNo, cancellationToken).ConfigureAwait(false));
}
