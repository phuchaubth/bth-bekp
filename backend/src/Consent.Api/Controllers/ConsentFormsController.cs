using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Phiếu đồng thuận (màn hình 3, 5, 8). Chỉ thấy phiếu trong phạm vi khoa, trừ quyền xem toàn viện.</summary>
[ApiController]
[Route("api/v1/consent-forms")]
[Produces("application/json")]
public sealed class ConsentFormsController(ConsentFormService forms, ConsentSigningService signing, ConsentEncounterService encounters)
    : ControllerBase
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

    /// <summary>PDF khổ A4 (ghi "bản xem trước" khi chưa ký). Mỗi lần tải được ghi nhật ký.</summary>
    [HttpGet("{id:guid}/pdf")]
    [HasPermission(Permissions.ConsentsRead)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await forms.GetPdfAsync(id, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, file => File(file.Content, "application/pdf", file.FileName));
    }

    /// <summary>Lập phiếu nháp theo phiên bản đang phát hành của mẫu; điền sẵn thông tin từ hồ sơ người bệnh.</summary>
    [HttpPost]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> Create(CreateConsentFormRequest request, CancellationToken cancellationToken)
    {
        var result = await forms.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    /// <summary>Lập bộ phiếu (quyết định 23): nhiều mẫu cho cùng một người bệnh trong một lần, các phiếu nháp cùng mã bộ.</summary>
    [HttpPost("bundle")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> CreateBundle(CreateConsentFormBundleRequest request, CancellationToken cancellationToken)
    {
        var result = await forms.CreateBundleAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, dto => CreatedAtAction(nameof(GetBundle), new { bundleId = dto.BundleId }, dto));
    }

    /// <summary>Bộ phiếu: các phiếu, trạng thái và thao tác được làm (ký cả bộ, xác nhận cả bộ).</summary>
    [HttpGet("bundles/{bundleId:guid}")]
    [HasPermission(Permissions.ConsentsRead)]
    public async Task<IActionResult> GetBundle(Guid bundleId, CancellationToken cancellationToken) =>
        this.ToActionResult(await forms.GetBundleAsync(bundleId, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Lượt tiếp nhận (HIS) của người bệnh của bộ phiếu, để chọn khi ký hoàn tất (quyết định 25): mặc định trong ngày, hoặc
    /// <paramref name="from"/>–<paramref name="to"/> (yyyy-MM-dd); mới nhất trước, kèm các phiếu đã gắn vào từng lượt.
    /// </summary>
    [HttpGet("bundles/{bundleId:guid}/encounters")]
    [HasPermission(Permissions.ConsentsSign)]
    public async Task<IActionResult> BundleEncounters(
        Guid bundleId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken) =>
        this.ToActionResult(await encounters.ListForBundleAsync(bundleId, from, to, cancellationToken).ConfigureAwait(false));

    /// <summary>Lượt tiếp nhận (HIS) của người bệnh của phiếu, để chọn khi ký hoàn tất (quyết định 25).</summary>
    [HttpGet("{id:guid}/encounters")]
    [HasPermission(Permissions.ConsentsSign)]
    public async Task<IActionResult> Encounters(
        Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken) =>
        this.ToActionResult(await encounters.ListForFormAsync(id, from, to, cancellationToken).ConfigureAwait(false));

    /// <summary>Mở một phiên ký cho mọi phiếu của bộ đang chờ người bệnh ký (cùng một người ký).</summary>
    [HttpPost("bundles/{bundleId:guid}/signing-session")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> StartBundleSigning(Guid bundleId, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.StartBundleSigningAsync(bundleId, cancellationToken).ConfigureAwait(false));

    /// <summary>Người bệnh ký một lần cho cả bộ (chỉ nhận token phiên ký của bộ phiếu).</summary>
    [HttpPost("signing-bundle/patient-signature")]
    [Authorize(Policy = SigningSession.PolicyName)]
    public async Task<IActionResult> SignBundleAsPatient(BundlePatientSignatureRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.SignBundleAsPatientAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Nhân viên xác nhận một lần các phiếu trong bộ đang đến lượt mình.</summary>
    [HttpPost("bundles/{bundleId:guid}/confirm")]
    [HasPermission(Permissions.ConsentsSign)]
    public async Task<IActionResult> ConfirmBundle(Guid bundleId, ConfirmBundleRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.ConfirmBundleAsync(bundleId, request, cancellationToken).ConfigureAwait(false));

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

    /// <summary>Mở phiên ký trên điện thoại: trả token phiên ký chỉ dùng để người bệnh ký đúng phiếu này.</summary>
    [HttpPost("{id:guid}/signing-session")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> StartSigning(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.StartSigningAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Người bệnh/người đại diện ký tay (chỉ nhận token phiên ký, không nhận token đăng nhập).</summary>
    [HttpPost("{id:guid}/patient-signature")]
    [Authorize(Policy = SigningSession.PolicyName)]
    public async Task<IActionResult> SignAsPatient(Guid id, PatientSignatureRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.SignAsPatientAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Nhân viên xác nhận bước trung gian đang chờ (vai trò theo bước trong định nghĩa mẫu).</summary>
    [HttpPost("{id:guid}/confirm-step")]
    [HasPermission(Permissions.ConsentsSign)]
    public async Task<IActionResult> ConfirmStep(Guid id, ConfirmStepRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.ConfirmStepAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Phía bệnh viện xác nhận bằng tài khoản kèm chữ ký tay (vai trò theo cấu hình mẫu) và hoàn tất phiếu.</summary>
    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.ConsentsSign)]
    public async Task<IActionResult> Complete(Guid id, CompleteConsentFormRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.CompleteAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/revoke")]
    [HasPermission(Permissions.ConsentsRevoke)]
    public async Task<IActionResult> Revoke(Guid id, RevokeConsentFormRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await signing.RevokeAsync(id, request, cancellationToken).ConfigureAwait(false));
}
