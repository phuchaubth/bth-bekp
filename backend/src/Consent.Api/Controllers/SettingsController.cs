using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Settings;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Cấu hình hệ thống (màn hình 19). Không có bí mật nào được trả về hay nhận qua API này.</summary>
[ApiController]
[Route("api/v1/settings")]
[Produces("application/json")]
public sealed class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SystemSettingsDto> Get(CancellationToken cancellationToken) => settings.GetAsync(cancellationToken);

    /// <summary>Tên đơn vị in trên PDF (áp dụng cho phiếu in sau khi lưu; PDF đã ký giữ nguyên).</summary>
    [HttpPut("hospital")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UpdateHospital(UpdateHospitalSettingsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await settings.UpdateHospitalAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Ảnh logo đang dùng (để xem trước). Chưa có logo → 404.</summary>
    [HttpGet("logo")]
    [HasPermission(Permissions.SettingsManage)]
    [Produces("image/png", "image/jpeg")]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await settings.ResolveLogoAsync(cancellationToken).ConfigureAwait(false) is { } logo
            ? File(logo.Content, logo.ContentType)
            : NotFound();
    }

    /// <summary>Tải lên hoặc thay logo in ở đầu phiếu (PNG/JPG, tối đa 1 MB). PDF đã ký giữ nguyên.</summary>
    [HttpPut("logo")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UploadLogo(UploadLogoRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await settings.UploadLogoAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Bỏ logo: phiếu in sau đó không có logo.</summary>
    [HttpDelete("logo")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SystemSettingsDto> DeleteLogo(CancellationToken cancellationToken) => settings.DeleteLogoAsync(cancellationToken);
}
