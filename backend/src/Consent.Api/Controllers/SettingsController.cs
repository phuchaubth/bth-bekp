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
}
