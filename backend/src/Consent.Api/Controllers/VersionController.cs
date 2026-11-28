using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Phiên bản bản đang chạy (quyết định 67): số gói, mã commit, thời điểm đóng gói, đọc từ <c>version.json</c> lúc API khởi động.
/// Trang Bảo trì hệ thống so với phiên bản giao diện đang mở trên trình duyệt để báo khi hai bên lệch nhau (chưa nhấn Ctrl+F5,
/// chưa chép đủ thư mục khi cài gói). Chỉ người có quyền bảo trì hệ thống xem.
/// </summary>
[ApiController]
[Route("api/v1/version")]
[Produces("application/json")]
public sealed class VersionController(AppVersion version) : ControllerBase
{
    /// <summary>Phiên bản API đang chạy; các giá trị là <c>null</c> khi không rõ (bản phát triển, cài từ gói chưa ghi phiên bản).</summary>
    [HttpGet]
    [HasPermission(Permissions.SystemMaintenance)]
    public AppVersion Get() => version;
}
