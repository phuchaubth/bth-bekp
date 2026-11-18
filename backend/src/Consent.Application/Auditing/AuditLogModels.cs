namespace Consent.Application.Auditing;

public sealed record AuditLogQuery(
    Guid? UserId = null,
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string? EntityType,
    string? EntityId,
    Guid? UserId,
    string? UserName,
    string? IpAddress,
    string? CorrelationId,
    string? Details);
