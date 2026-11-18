namespace BEKP.Application.Abstractions;

/// <summary>Audit trail entry for every state-changing operation. Contains codes and actor only, never patient data.</summary>
public sealed record AuditEntry(string Actor, string Action, string EntityType, string EntityKey, DateTime OccurredAtUtc);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>The authenticated caller (user or integration client).</summary>
public interface ICurrentActor
{
    string Name { get; }
}
