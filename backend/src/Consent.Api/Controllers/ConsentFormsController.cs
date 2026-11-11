using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Phiếu đồng thuận (màn hình 3, 5, 8). Chỉ thấy phiếu trong phạm vi khoa, trừ quyền xem toàn viện.</summary>
[ApiController]
[Route("api/v1/consent-forms")]
[Produces("application/json")]
public sealed class ConsentFormsController(ConsentFormService forms) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.ConsentsRead)]
    public Task<PagedResult<ConsentFormListItemDto>> Search([FromQuery] ConsentFormQuery query, CancellationToken cancellationToken) =>
        forms.SearchAsync(query, cancellationToken);

    /// <summary>Chi tiết phiếu (mỗi lần xem được ghi nhật ký).</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.ConsentsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await forms.GetAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Lập phiếu nháp theo phiên bản đang phát hành của mẫu; điền sẵn thông tin từ hồ sơ người bệnh.</summary>
    [HttpPost]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> Create(CreateConsentFormRequest request, CancellationToken cancellationToken)
    {
        var result = await forms.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    /// <summary>Lưu nháp (chưa bắt buộc đầy đủ; thiếu sót trả về trong <c>issues</c>).</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> Update(Guid id, UpdateConsentFormRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await forms.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Trình ký: kiểm tra đầy đủ và đóng băng thông tin người bệnh, người ký.</summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await forms.SubmitAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/return-to-draft")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> ReturnToDraft(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await forms.ReturnToDraftAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> Cancel(Guid id, CancelConsentFormRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await forms.CancelAsync(id, request, cancellationToken).ConfigureAwait(false));
}
