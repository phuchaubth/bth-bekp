using Consent.Api.Authorization;
using Consent.Api.Infrastructure;
using Consent.Application.NursingReports;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>
/// Ủy quyền báo cáo điều dưỡng khi người báo cáo vắng (quyết định 48): người lập báo cáo của khoa ủy quyền cho người khác làm báo
/// cáo của khoa và/hoặc nhập số liệu HIS của khoa trong một khoảng ngày; hủy ủy quyền; Phòng Điều dưỡng xem mọi ủy quyền.
/// </summary>
[ApiController]
[Route("api/v1/nursing-reports/delegations")]
[Produces("application/json")]
public sealed class NursingDelegationsController(NursingDelegationService delegations) : ControllerBase
{
    /// <summary>Ủy quyền người dùng đã tạo hoặc được giao; <paramref name="all"/> (Phòng Điều dưỡng) là mọi ủy quyền.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? all, CancellationToken cancellationToken) =>
        this.ToActionResult(await delegations.ListAsync(all ?? false, cancellationToken).ConfigureAwait(false));

    /// <summary>Người có thể được ủy quyền: tài khoản đang dùng, thuộc một khoa, có quyền lập báo cáo điều dưỡng.</summary>
    [HttpGet("candidates")]
    [HasPermission(Permissions.NursingReportsSubmit)]
    public async Task<IActionResult> Candidates(CancellationToken cancellationToken) =>
        this.ToActionResult(await delegations.CandidatesAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Ủy quyền mới cho một khoa của người dùng.</summary>
    [HttpPost]
    [HasPermission(Permissions.NursingReportsSubmit)]
    public async Task<IActionResult> Create(CreateNursingDelegationRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await delegations.CreateAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Hủy ủy quyền (người ủy quyền hoặc Phòng Điều dưỡng); báo cáo đã gửi theo ủy quyền vẫn ghi ủy quyền.</summary>
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await delegations.RevokeAsync(id, cancellationToken).ConfigureAwait(false));
}
