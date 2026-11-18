using Consent.Api.Authorization;
using Consent.Application.Auditing;
using Consent.Domain.Authorization;
using Consent.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Controllers;

/// <summary>Tra cứu nhật ký audit. Không có API sửa hoặc xóa nhật ký.</summary>
[ApiController]
[Route("api/v1/audit-logs")]
[Produces("application/json")]
public sealed class AuditLogsController(AuditLogService auditLogs) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.AuditRead)]
    public async Task<PagedResult<AuditLogDto>> Search([FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        await auditLogs.SearchAsync(query, cancellationToken).ConfigureAwait(false);
}
