using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.NursingReports;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Consent.Api.Controllers;

/// <summary>
/// Báo cáo điều dưỡng hằng ngày (quyết định 31): khoa lập và gửi báo cáo của mình, và nhập tay số liệu HIS của khoa khi HIS
/// lỗi (quyết định 35); Phòng Điều dưỡng xem bảng tổng hợp, in PDF, xuất Excel, mở lại báo cáo, cài khoa báo cáo, số giường và
/// khoa nhập từng chỉ số, lấy số liệu HIS.
/// </summary>
[ApiController]
[Route("api/v1/nursing-reports")]
[Produces("application/json")]
public sealed class NursingReportsController(NursingReportService reports, NursingSummaryService summaries) : ControllerBase
{
    /// <summary>Mẫu báo cáo khoa (mục, dòng, phần tick) để dựng màn hình nhập.</summary>
    [HttpGet("form")]
    public NursingReportFormDto GetForm() => NursingReportService.Form;

    /// <summary>
    /// Báo cáo của khoa trong ngày (bỏ trống <paramref name="departmentId"/> là khoa của người dùng). Chưa có thì trả mẫu
    /// trống, điền sẵn nhân sự và chỉ tiêu theo báo cáo đã gửi gần nhất. Điều dưỡng xem được khoa mình, Phòng Điều dưỡng
    /// xem được mọi khoa.
    /// </summary>
    [HttpGet("daily")]
    public async Task<IActionResult> GetDaily([FromQuery] Guid? departmentId, [FromQuery, BindRequired] DateOnly date, CancellationToken cancellationToken) =>
        this.ToActionResult(await reports.GetAsync(departmentId, date, cancellationToken).ConfigureAwait(false));

    /// <summary>Lưu báo cáo của khoa trong ngày (tạo nếu chưa có); <c>submit = true</c> thì gửi luôn và khóa lại.</summary>
    [HttpPut("daily")]
    [HasPermission(Permissions.NursingReportsSubmit)]
    public async Task<IActionResult> SaveDaily(SaveNursingReportRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await reports.SaveAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Khoa nhập tay số liệu HIS của khoa khi HIS lỗi (quyết định 35): "NB PK chờ nhập viện" của khoa và chỉ số toàn viện Phòng
    /// Điều dưỡng giao cho khoa. Trả lại báo cáo của khoa trong ngày.
    /// </summary>
    [HttpPut("his-entry")]
    [HasPermission(Permissions.NursingReportsSubmit)]
    public async Task<IActionResult> SaveHisEntry(SaveNursingHisEntryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await reports.SaveHisEntryAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Phòng Điều dưỡng mở lại báo cáo đã gửi để khoa sửa (bắt buộc ghi lý do).</summary>
    [HttpPost("{id:guid}/reopen")]
    [HasPermission(Permissions.NursingReportsManage)]
    public async Task<IActionResult> Reopen(Guid id, ReopenNursingReportRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await reports.ReopenAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF báo cáo của khoa theo bố cục bản giấy.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await reports.RenderPdfAsync(id, cancellationToken).ConfigureAwait(false);
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(result, file => File(file.Content, file.ContentType, file.FileName));
    }

    /// <summary>Bảng tổng hợp của ngày (mục A–F như bản giấy).</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.NursingReportsRead)]
    public Task<NursingSummaryDto> GetSummary([FromQuery, BindRequired] DateOnly date, CancellationToken cancellationToken) =>
        summaries.GetAsync(date, cancellationToken);

    [HttpGet("summary/pdf")]
    [HasPermission(Permissions.NursingReportsRead)]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetSummaryPdf([FromQuery, BindRequired] DateOnly date, CancellationToken cancellationToken)
    {
        var file = await summaries.RenderPdfAsync(date, cancellationToken).ConfigureAwait(false);
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("summary/xlsx")]
    [HasPermission(Permissions.NursingReportsRead)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> GetSummaryWorkbook([FromQuery, BindRequired] DateOnly date, CancellationToken cancellationToken)
    {
        var file = await summaries.RenderWorkbookAsync(date, cancellationToken).ConfigureAwait(false);
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Lấy số liệu của ngày từ HIS (người bệnh đến khám, chẩn đoán hình ảnh, chờ nhập viện) và lưu lại.</summary>
    [HttpPost("statistics/refresh")]
    [HasPermission(Permissions.NursingReportsManage)]
    public async Task<IActionResult> RefreshStatistics(RefreshNursingStatisticsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await summaries.RefreshStatisticsAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Các khoa, khoa nào phải báo cáo, số giường thực kê, chỉ số toàn viện khoa nhập tay khi HIS lỗi.</summary>
    [HttpGet("departments")]
    [HasPermission(Permissions.NursingReportsManage)]
    public Task<IReadOnlyList<NursingReportDepartmentDto>> GetDepartments(CancellationToken cancellationToken) =>
        summaries.ListDepartmentsAsync(cancellationToken);

    /// <summary>Đặt danh sách khoa phải báo cáo, số giường từng khoa và (khi có <c>hisEntry</c>) khoa nhập từng chỉ số.</summary>
    [HttpPut("departments")]
    [HasPermission(Permissions.NursingReportsManage)]
    public async Task<IActionResult> UpdateDepartments(UpdateNursingReportDepartmentsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await summaries.UpdateDepartmentsAsync(request, cancellationToken).ConfigureAwait(false));
}
