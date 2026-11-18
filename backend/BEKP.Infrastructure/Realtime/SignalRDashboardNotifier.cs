using BEKP.Application.Abstractions;
using BEKP.Application.Dashboard;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BEKP.Infrastructure.Realtime;

internal sealed partial class SignalRDashboardNotifier(IHubContext<DashboardHub> hub, ILogger<SignalRDashboardNotifier> logger) : IDashboardNotifier
{
    public async Task PublishAsync(OperationalEventDto change, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.All.SendAsync(DashboardHub.OperationalEventMethod, change, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The change is already committed; clients also poll as a fallback.
            LogPublishFailed(logger, ex, change.Type.ToString());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to broadcast dashboard change {Type}.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string type);
}
