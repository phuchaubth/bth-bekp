using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Consent.Api.Controllers;

/// <summary>
/// Ký trên điện thoại riêng của người bệnh qua mã QR (quyết định 32): nhân viên tạo mã, theo dõi, hủy; điện thoại người bệnh
/// mở mã bằng ngày sinh rồi ký bằng token phiên ký (cùng API ký như trên máy của bệnh viện).
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class RemoteSigningController(RemoteSigningService remoteSigning) : ControllerBase
{
    /// <summary>Tạo mã QR cho một phiếu đang chờ người bệnh ký (mở phiên ký mới, phiên cũ hết hiệu lực).</summary>
    [HttpPost("consent-forms/{id:guid}/remote-signing")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> CreateForForm(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(await remoteSigning.CreateForFormAsync(id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Tạo một mã QR cho mọi phiếu của bộ đang chờ người bệnh ký (ký một lần cho cả bộ).</summary>
    [HttpPost("consent-forms/bundles/{bundleId:guid}/remote-signing")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> CreateForBundle(Guid bundleId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(await remoteSigning.CreateForBundleAsync(bundleId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Trạng thái mã: chờ quét, đã mở trên điện thoại, đã ký, hết hạn, đã hủy.</summary>
    [HttpGet("remote-signing/{id:guid}")]
    [HasPermission(Permissions.ConsentsRead)]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await remoteSigning.GetStatusAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Hủy mã: điện thoại đã mở mã cũng không ký được nữa.</summary>
    [HttpPost("remote-signing/{id:guid}/cancel")]
    [HasPermission(Permissions.ConsentsCreate)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await remoteSigning.CancelAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Điện thoại người bệnh mở mã (không cần đăng nhập): lần đầu kèm ngày sinh của người bệnh, tải lại trang thì kèm khóa thiết
    /// bị. Giới hạn tần suất theo địa chỉ IP; sai ngày sinh 5 lần thì mã bị hủy.
    /// </summary>
    [HttpPost("remote-signing/claim")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.RemoteSigning)]
    public async Task<IActionResult> Claim(ClaimRemoteSigningRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(await remoteSigning.ClaimAsync(request, cancellationToken).ConfigureAwait(false));
    }
}
