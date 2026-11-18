using BEKP.Application.Dashboard;

namespace BEKP.Application.Abstractions;

/// <summary>Pushes realtime changes to connected dashboards (implemented with SignalR in Infrastructure).</summary>
public interface IDashboardNotifier
{
    Task PublishAsync(OperationalEventDto change, CancellationToken cancellationToken);
}
