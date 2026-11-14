using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Shared;

namespace Consent.Application.Auditing;

/// <summary>Tra cứu nhật ký audit (chỉ đọc; không có thao tác sửa/xóa).</summary>
public sealed class AuditLogService(IAuditLogRepository auditLogs)
{
    public Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        return auditLogs.SearchAsync(query with { Page = page, PageSize = pageSize }, cancellationToken);
    }
}
