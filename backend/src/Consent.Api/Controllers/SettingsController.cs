using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.ConsentForms;
using Consent.Application.Kiosk;
using Consent.Application.Settings;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Cấu hình hệ thống (màn hình 19). Không có bí mật nào được trả về hay nhận qua API này.</summary>
[ApiController]
[Route("api/v1/settings")]
[Produces("application/json")]
public sealed class SettingsController(
    SettingsService settings,
    KioskSettingsService kioskSettings,
    EncounterSettingsService encounterSettings,
    FormInheritanceService formInheritance) : ControllerBase
{
    /// <summary>Kế thừa phiếu lần trước (quyết định 27): số ngày được kế thừa (0 = tắt).</summary>
    [HttpGet("form-inheritance")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<FormInheritanceSettingsDto> GetFormInheritance(CancellationToken cancellationToken) =>
        formInheritance.GetAsync(cancellationToken);

    /// <summary>Đặt số ngày được kế thừa phiếu lần trước (0–365, 0 = tắt), có hiệu lực ngay cho phiếu lập sau đó.</summary>
    [HttpPut("form-inheritance")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UpdateFormInheritance(UpdateFormInheritanceRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await formInheritance.UpdateAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Gắn phiếu với lượt tiếp nhận HIS khi ký hoàn tất (quyết định 25): đang bật hay tắt, bật được không.</summary>
    [HttpGet("encounters")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<EncounterSettingsDto> GetEncounters(CancellationToken cancellationToken) => encounterSettings.GetAsync(cancellationToken);

    /// <summary>Bật/tắt bắt buộc chọn lượt tiếp nhận, có hiệu lực ngay (HIS lỗi thì tắt để nhân viên hoàn tất tiếp).</summary>
    [HttpPut("encounters")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UpdateEncounters(UpdateEncounterSettingsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await encounterSettings.UpdateAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Các mẫu người bệnh tự làm trên kiosk (quyết định 24), theo thứ tự làm.</summary>
    [HttpGet("kiosk")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<KioskSettingsDto> GetKiosk(CancellationToken cancellationToken) => kioskSettings.GetAsync(cancellationToken);

    /// <summary>Chọn mẫu cho kiosk: chỉ mẫu đang dùng, đã phát hành, người bệnh tự trả lời được.</summary>
    [HttpPut("kiosk")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UpdateKiosk(UpdateKioskSettingsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await kioskSettings.UpdateAsync(request, cancellationToken).ConfigureAwait(false));

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

    /// <summary>
    /// Tải lên hoặc thay ảnh khung chữ ký (PNG/JPG, tối đa 1 MB): nền thiết kế của bệnh viện quanh khung ký trên màn hình.
    /// Xem ảnh đang dùng ở <c>GET /api/v1/branding/signature-frame</c>.
    /// </summary>
    [HttpPut("signature-frame")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UploadSignatureFrame(UploadSignatureFrameRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await settings.UploadSignatureFrameAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Bỏ ảnh khung chữ ký: khung ký trở lại dạng mặc định.</summary>
    [HttpDelete("signature-frame")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SystemSettingsDto> DeleteSignatureFrame(CancellationToken cancellationToken) =>
        settings.DeleteSignatureFrameAsync(cancellationToken);

    /// <summary>Bỏ logo: phiếu in sau đó không có logo.</summary>
    [HttpDelete("logo")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<SystemSettingsDto> DeleteLogo(CancellationToken cancellationToken) => settings.DeleteLogoAsync(cancellationToken);
}
