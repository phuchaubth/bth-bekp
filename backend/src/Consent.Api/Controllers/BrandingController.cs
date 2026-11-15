using Consent.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Ảnh nhận diện của bệnh viện dùng trên màn hình. Không cần đăng nhập, vì điện thoại đang ở chế độ ký (token phiên ký) và
/// kiosk cũng phải tải được; chỉ trả ảnh do quản trị tải lên, không có dữ liệu người bệnh hay cấu hình nào khác.
/// </summary>
[ApiController]
[Route("api/v1/branding")]
[AllowAnonymous]
public sealed class BrandingController(SettingsService settings) : ControllerBase
{
    /// <summary>Khung chữ ký đang dùng: kiểu ảnh, kích thước, thời điểm cập nhật. Chưa có → 404 (khung ký mặc định).</summary>
    [HttpGet("signature-frame")]
    [Produces("application/json")]
    public async Task<IActionResult> GetSignatureFrame(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        return await settings.GetSignatureFrameInfoAsync(cancellationToken).ConfigureAwait(false) is { } frame
            ? Ok(frame)
            : NotFound();
    }

    /// <summary>
    /// Ảnh khung chữ ký. Màn hình ký gọi kèm <c>?v=</c> theo thời điểm cập nhật, nên trình duyệt được giữ ảnh một ngày: ảnh mới
    /// có địa chỉ mới.
    /// </summary>
    [HttpGet("signature-frame/image")]
    [Produces("image/png", "image/jpeg")]
    public async Task<IActionResult> GetSignatureFrameImage(CancellationToken cancellationToken)
    {
        if (await settings.ResolveSignatureFrameAsync(cancellationToken).ConfigureAwait(false) is not { } frame)
        {
            Response.Headers.CacheControl = "no-store";
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(frame.Content, frame.ContentType);
    }
}
