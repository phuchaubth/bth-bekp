using Consent.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Tính năng tích hợp đang bật, để giao diện ẩn/hiện phần tương ứng. Chỉ cần đăng nhập.</summary>
[ApiController]
[Route("api/v1/features")]
[Produces("application/json")]
public sealed class FeaturesController(IHisGateway his, IPatientSource patientSource, IEncounterSource encounters) : ControllerBase
{
    // Ký số SmartCA tạm hoãn (chưa tích hợp); khi tích hợp, cờ này sẽ lấy từ cấu hình như HIS.
    [HttpGet]
    public FeaturesResponse Get() => new(his.IsEnabled, SmartCa: false, PatientSource: patientSource.IsEnabled, Encounters: encounters.IsEnabled);
}

/// <param name="His">Đã kết nối HIS: tra cứu khi quét CCCD và đối chiếu khi lưu hồ sơ.</param>
/// <param name="SmartCa">Đã tích hợp ký số VNPT SmartCA.</param>
/// <param name="PatientSource">Đã cấu hình nguồn thông tin người bệnh (API thông tin người bệnh): lấy thông tin theo mã người bệnh hoặc CCCD.</param>
/// <param name="Encounters">Bắt buộc gắn phiếu với lượt tiếp nhận trên HIS khi ký hoàn tất (quyết định 25).</param>
public sealed record FeaturesResponse(bool His, bool SmartCa, bool PatientSource = false, bool Encounters = false);
