using Consent.Api.Infrastructure;
using Consent.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Tài khoản của chính mình: chữ ký mẫu (hiện cạnh khung ký khi xác nhận phiếu để đối chiếu).</summary>
[ApiController]
[Route("api/v1/account")]
[Authorize]
public sealed class AccountController(SignatureSpecimenService specimens) : ControllerBase
{
    /// <summary>Ảnh PNG chữ ký mẫu của mình; chưa có → 404.</summary>
    [HttpGet("signature-specimen")]
    [Produces("image/png")]
    public async Task<IActionResult> GetSignatureSpecimen(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await specimens.GetMineAsync(cancellationToken).ConfigureAwait(false) is { } png
            ? File(png, "image/png")
            : NotFound();
    }

    /// <summary>Lưu hoặc thay chữ ký mẫu: <c>{ image }</c> là PNG Base64 vẽ trong khung ký (kiểm tra như chữ ký trên phiếu).</summary>
    [HttpPut("signature-specimen")]
    [Produces("application/json")]
    public async Task<IActionResult> SaveSignatureSpecimen(SignatureSpecimenRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await specimens.SaveMineAsync(request, cancellationToken).ConfigureAwait(false),
            updatedAt => Ok(new { updatedAtUtc = updatedAt }));

    [HttpDelete("signature-specimen")]
    public async Task<IActionResult> DeleteSignatureSpecimen(CancellationToken cancellationToken)
    {
        await specimens.DeleteMineAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
