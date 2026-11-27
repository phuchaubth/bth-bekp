using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.CriticalResults;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Kết quả nguy cấp (quyết định 60, QT-KQNC lần 04): khoa cận lâm sàng ghi kết quả và lần báo sau cuộc gọi; khoa điều trị xác
/// nhận đã nhận, bác sĩ ghi hướng xử trí; cảnh báo cho dải đỏ và màn hình theo dõi tại khoa; sổ ST01, ST02, sổ Vi sinh; dashboard
/// QLCL; danh mục và thông số vận hành. Quyền theo khoa do nghiệp vụ kiểm tra.
/// </summary>
[ApiController]
[Route("api/v1/critical-results")]
[Produces("application/json")]
public sealed class CriticalResultsController(
    CriticalResultService results,
    CriticalCatalogService catalog,
    CriticalSettingsService settings,
    CriticalDashboardService dashboard,
    CriticalLogbookService logbook) : ControllerBase
{
    /// <summary>Thông số vận hành (thời hạn, cảnh báo, mục tiêu, mã sổ) và lần sửa cuối.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        this.ToActionResult(await settings.GetAsync(cancellationToken).ConfigureAwait(false));

    [HttpPut("settings")]
    [HasPermission(Permissions.CriticalManage)]
    public async Task<IActionResult> UpdateSettings(CriticalSettings request, CancellationToken cancellationToken) =>
        this.ToActionResult(await settings.UpdateAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Danh mục kết quả nguy cấp (người quản lý thấy cả mục ngưng dùng).</summary>
    [HttpGet("tests")]
    public async Task<IActionResult> ListTests(CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.ListAsync(cancellationToken).ConfigureAwait(false));

    [HttpPost("tests")]
    [HasPermission(Permissions.CriticalManage)]
    public async Task<IActionResult> CreateTest(SaveCriticalTestRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.CreateAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPut("tests/{id:guid}")]
    [HasPermission(Permissions.CriticalManage)]
    public async Task<IActionResult> UpdateTest(Guid id, SaveCriticalTestRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Tra người bệnh trên HIS theo mã để điền sẵn.</summary>
    [HttpGet("patient")]
    [HasPermission(Permissions.CriticalRecord)]
    public async Task<IActionResult> LookupPatient([FromQuery] string? code, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.LookupPatientAsync(code, cancellationToken).ConfigureAwait(false));

    /// <summary>Bàn làm việc khoa báo: hồ sơ đang mở và vừa xong.</summary>
    [HttpGet("sent")]
    public async Task<IActionResult> ListSent([FromQuery] Guid? departmentId, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.ListSentAsync(departmentId, cancellationToken).ConfigureAwait(false));

    /// <summary>Bàn làm việc khoa nhận: hồ sơ đang mở và vừa xong.</summary>
    [HttpGet("received")]
    public async Task<IActionResult> ListReceived([FromQuery] Guid? departmentId, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.ListReceivedAsync(departmentId, cancellationToken).ConfigureAwait(false));

    /// <summary>Cảnh báo của tài khoản đang đăng nhập (dải đỏ, màn hình theo dõi tại khoa); giao diện hỏi lại theo chu kỳ cấu hình.</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(CancellationToken cancellationToken) =>
        this.ToActionResult(await results.AlertsAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Dashboard của một tháng hoặc quý (<c>period=quarter</c>), mặc định tháng này.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] string? month, [FromQuery] string? period, CancellationToken cancellationToken) =>
        this.ToActionResult(await dashboard.GetAsync(month, period, cancellationToken).ConfigureAwait(false));

    /// <summary>Sổ một khoa một tháng (xem trước).</summary>
    [HttpGet("logbook")]
    public async Task<IActionResult> Logbook(
        [FromQuery] CriticalLogbookForm form, [FromQuery] Guid departmentId, [FromQuery] string? month, CancellationToken cancellationToken) =>
        this.ToActionResult(await logbook.GetAsync(form, departmentId, month, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF sổ (A4 ngang).</summary>
    [HttpGet("logbook/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> LogbookPdf(
        [FromQuery] CriticalLogbookForm form, [FromQuery] Guid departmentId, [FromQuery] string? month, CancellationToken cancellationToken)
    {
        var result = await logbook.RenderPdfAsync(form, departmentId, month, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, file => File(file.Content, "application/pdf", file.FileName));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.GetAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Ghi kết quả nguy cấp (kèm lần báo nếu đã gọi điện).</summary>
    [HttpPost]
    [HasPermission(Permissions.CriticalRecord)]
    public async Task<IActionResult> Create(CreateCriticalResultRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.CreateAsync(request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/notifications")]
    [HasPermission(Permissions.CriticalRecord)]
    public async Task<IActionResult> Notify(Guid id, CriticalNotificationRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.NotifyAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.AcknowledgeAsync(id, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CriticalReasonRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.RejectAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/action")]
    [HasPermission(Permissions.CriticalTreat)]
    public async Task<IActionResult> Act(Guid id, CriticalActionRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.ActAsync(id, request, cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.CriticalRecord)]
    public async Task<IActionResult> Cancel(Guid id, CriticalCancelRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await results.CancelAsync(id, request, cancellationToken).ConfigureAwait(false));
}
