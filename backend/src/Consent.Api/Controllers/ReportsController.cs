using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Reports;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Báo cáo thống kê (màn hình 14) và trang tổng quan (màn hình 2).</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class ReportsController(ReportService reports, DashboardService dashboard) : ControllerBase
{
    /// <summary>Thống kê các phiếu lập trong khoảng thời gian (mặc định: từ đầu tháng đến hôm nay), trong phạm vi khoa.</summary>
    [HttpGet("reports/summary")]
    [HasPermission(Permissions.ReportsRead)]
    public async Task<IActionResult> Summary([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        this.ToActionResult(await reports.SummaryAsync(query, cancellationToken).ConfigureAwait(false));

    /// <summary>Số liệu trang tổng quan của người dùng hiện tại.</summary>
    [HttpGet("dashboard")]
    [HasPermission(Permissions.ConsentsRead)]
    public Task<DashboardDto> Dashboard(CancellationToken cancellationToken) => dashboard.GetAsync(cancellationToken);
}
