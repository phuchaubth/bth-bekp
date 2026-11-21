using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Maintenance;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Chế độ bảo trì (quyết định 53): trạng thái cho mọi người (giao diện hiện trang "Hệ thống đang bảo trì"); người có quyền bảo
/// trì xem chi tiết, bật, sửa, tắt.
/// </summary>
[ApiController]
[Route("api/v1/maintenance")]
[Produces("application/json")]
public sealed class MaintenanceController(MaintenanceService maintenance) : ControllerBase
{
    /// <summary>Đang bảo trì không, lý do, giờ dự kiến xong. Không cần đăng nhập, vẫn trả lời khi đang bảo trì.</summary>
    [HttpGet]
    [AllowAnonymous]
    [AllowDuringMaintenance]
    public Task<MaintenanceStatusDto> GetStatus(CancellationToken cancellationToken) => maintenance.GetStatusAsync(cancellationToken);

    /// <summary>Trạng thái kèm thời điểm bật và người bật (trang Bảo trì hệ thống).</summary>
    [HttpGet("settings")]
    [HasPermission(Permissions.SystemMaintenance)]
    public Task<MaintenanceSettingsDto> GetSettings(CancellationToken cancellationToken) => maintenance.GetSettingsAsync(cancellationToken);

    /// <summary>Bật (ghi lý do, giờ dự kiến xong), sửa khi đang bật, hoặc tắt bảo trì; có hiệu lực ngay.</summary>
    [HttpPut("settings")]
    [HasPermission(Permissions.SystemMaintenance)]
    public async Task<IActionResult> Update(UpdateMaintenanceRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await maintenance.UpdateAsync(request, cancellationToken).ConfigureAwait(false));
}
