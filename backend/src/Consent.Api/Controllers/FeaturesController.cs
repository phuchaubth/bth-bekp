using Consent.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Tính năng tích hợp đang bật, để giao diện ẩn/hiện phần tương ứng. Chỉ cần đăng nhập.</summary>
[ApiController]
[Route("api/v1/features")]
[Produces("application/json")]
public sealed class FeaturesController(IHisGateway his) : ControllerBase
{
    // Ký số SmartCA tạm hoãn (chưa tích hợp); khi tích hợp, cờ này sẽ lấy từ cấu hình như HIS.
    [HttpGet]
    public FeaturesResponse Get() => new(his.IsEnabled, SmartCa: false);
}

/// <param name="His">Đã kết nối HIS: tra cứu khi quét CCCD và đối chiếu khi lưu hồ sơ.</param>
/// <param name="SmartCa">Đã tích hợp ký số VNPT SmartCA.</param>
public sealed record FeaturesResponse(bool His, bool SmartCa);
