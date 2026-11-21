using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.Testing;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Phân công kiểm thử tính năng mới (quyết định 49): người quản lý kiểm thử phân công từng tính năng cho từng người, có thời gian;
/// người được phân công xem và gửi báo cáo việc của mình; người quản lý ghi hướng xử lý, xuất Excel để nâng cấp phần mềm.
/// </summary>
[ApiController]
[Route("api/v1/testing/assignments")]
[Produces("application/json")]
public sealed class TestAssignmentsController(TestAssignmentService assignments) : ControllerBase
{
    /// <summary>Phân công của một đợt kiểm thử (bỏ trống là mọi đợt) và các đợt đã có.</summary>
    [HttpGet]
    [HasPermission(Permissions.TestingManage)]
    public async Task<IActionResult> List([FromQuery] string? version, CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.ListAsync(version, cancellationToken).ConfigureAwait(false));

    /// <summary>Việc kiểm thử của người đang đăng nhập.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.MineAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Người có thể được phân công kiểm thử.</summary>
    [HttpGet("candidates")]
    [HasPermission(Permissions.TestingManage)]
    public async Task<IActionResult> Candidates(CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.CandidatesAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Excel phân công và báo cáo kiểm thử của một đợt (bỏ trống là mọi đợt).</summary>
    [HttpGet("xlsx")]
    [HasPermission(Permissions.TestingManage)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Export([FromQuery] string? version, CancellationToken cancellationToken)
    {
        var result = await assignments.ExportAsync(version, cancellationToken).ConfigureAwait(false);
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(result, file => File(file.Content, file.ContentType, file.FileName));
    }

    /// <summary>Phân công một tính năng cho nhiều người, mỗi người một thời gian.</summary>
    [HttpPost]
    [HasPermission(Permissions.TestingManage)]
    public async Task<IActionResult> Create(CreateTestAssignmentsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.CreateAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Sửa phân công chưa có báo cáo.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.TestingManage)]
    public async Task<IActionResult> Update(Guid id, UpdateTestAssignmentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Hủy phân công chưa có báo cáo.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.TestingManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.CancelAsync(id, cancellationToken).ConfigureAwait(false));

    /// <summary>Người được phân công gửi hoặc sửa báo cáo kiểm thử.</summary>
    [HttpPut("{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, SubmitTestReportRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.ReportAsync(id, request, cancellationToken).ConfigureAwait(false));

    /// <summary>Ghi hướng xử lý cho báo cáo kiểm thử.</summary>
    [HttpPost("{id:guid}/review")]
    [HasPermission(Permissions.TestingManage)]
    public async Task<IActionResult> Review(Guid id, ReviewTestReportRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await assignments.ReviewAsync(id, request, cancellationToken).ConfigureAwait(false));
}
