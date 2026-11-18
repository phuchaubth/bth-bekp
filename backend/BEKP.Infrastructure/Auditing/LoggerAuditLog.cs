using BEKP.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BEKP.Infrastructure.Auditing;

/// <summary>Writes the audit trail as structured log events (category "BEKP.Audit"). A persistent store can replace it later.</summary>
internal sealed partial class LoggerAuditLog(ILoggerFactory loggerFactory) : IAuditLog
{
    private readonly ILogger logger = loggerFactory.CreateLogger("BEKP.Audit");

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LogAudit(logger, entry.Actor, entry.Action, entry.EntityType, entry.EntityKey, entry.OccurredAtUtc);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 9000, Level = LogLevel.Information, Message = "AUDIT actor={Actor} action={Action} entity={EntityType}:{EntityKey} at={OccurredAtUtc:O}")]
    private static partial void LogAudit(ILogger logger, string actor, string action, string entityType, string entityKey, DateTime occurredAtUtc);
}
