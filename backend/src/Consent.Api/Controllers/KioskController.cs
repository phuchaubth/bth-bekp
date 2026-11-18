using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Kiosk;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Kiosk cho người bệnh tự làm phiếu (quyết định 24). Chỉ tài khoản có quyền dùng kiosk gọi được; tài khoản vai trò Kiosk
/// không có quyền nào khác nên không gọi được các API xem người bệnh hay phiếu.
/// </summary>
[ApiController]
[Route("api/v1/kiosk")]
[Produces("application/json")]
[HasPermission(Permissions.KioskUse)]
public sealed class KioskController(KioskService kiosk) : ControllerBase
{
    /// <summary>Khoa của máy kiosk, các mẫu người bệnh tự làm và ô lời xác nhận chung.</summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config(CancellationToken cancellationToken) =>
        this.ToActionResult(await kiosk.GetConfigAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Nhận diện người bệnh (quét thẻ, nhập mã y tế/số CCCD kèm ngày sinh, hoặc tự khai). Nhập sai 5 lần thì tạm khóa tra
    /// cứu 5 phút (422, mã <c>kiosk.lookup_locked</c>).
    /// </summary>
    [HttpPost("identify")]
    public async Task<IActionResult> Identify(KioskIdentityRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await kiosk.IdentifyAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Nộp câu trả lời và chữ ký: lập bộ phiếu và ghi chữ ký người bệnh một lần cho cả bộ.</summary>
    [HttpPost("submit")]
    public async Task<IActionResult> Submit(KioskSubmitRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await kiosk.SubmitAsync(request, cancellationToken).ConfigureAwait(false));
}
