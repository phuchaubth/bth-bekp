using System.Globalization;
using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Exports;
using Consent.Domain.Authorization;
using Consent.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Consent.Api.Controllers;

/// <summary>Xuất dữ liệu (màn hình 13): file Excel/CSV theo bộ lọc, lịch sử xuất.</summary>
[ApiController]
[Route("api/v1/exports")]
[Produces("application/json")]
public sealed class ExportsController(ConsentFormExportService exports) : ControllerBase
{
    /// <summary>Danh mục cột có thể chọn khi xuất phiếu (cột nhạy cảm bị che, trừ khi xuất đầy đủ).</summary>
    [HttpGet("consent-forms/columns")]
    [HasPermission(Permissions.ExportsRun)]
    public IReadOnlyList<ExportColumnDto> Columns() => ConsentFormExportService.Columns();

    /// <summary>
    /// Xuất phiếu ra file. Trả file ngay (không lưu trên máy chủ); mỗi lần xuất được ghi lịch sử và nhật ký.
    /// Không có phiếu nào khớp bộ lọc hoặc quá giới hạn số phiếu: <c>422</c>.
    /// </summary>
    [HttpPost("consent-forms")]
    [HasPermission(Permissions.ExportsRun)]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> ExportConsentForms(ConsentFormExportRequest request, CancellationToken cancellationToken)
    {
        var result = await exports.ExportAsync(request, cancellationToken).ConfigureAwait(false);
        return this.ToActionResult(result, file =>
        {
            // File có dữ liệu cá nhân: không để trình duyệt hay proxy lưu đệm.
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Export-Job-Id"] = file.JobId.ToString();
            Response.Headers["X-Export-Row-Count"] = file.RowCount.ToString(CultureInfo.InvariantCulture);
            return File(file.Content, file.ContentType, file.FileName);
        });
    }

    /// <summary>Lịch sử xuất của người dùng (người có quyền xem nhật ký thấy mọi lần xuất).</summary>
    [HttpGet]
    [HasPermission(Permissions.ExportsRun)]
    public Task<PagedResult<ExportJobDto>> History([FromQuery] ExportJobQuery query, CancellationToken cancellationToken) =>
        exports.HistoryAsync(query, cancellationToken);
}
