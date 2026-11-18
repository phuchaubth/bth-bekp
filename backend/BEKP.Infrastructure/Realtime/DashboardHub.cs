using BEKP.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BEKP.Infrastructure.Realtime;

/// <summary>Server-to-client channel for dashboard changes. Clients only listen; all writes go through the REST API.</summary>
[Authorize(Policy = BekpPolicies.DashboardRead)]
public sealed class DashboardHub : Hub
{
    public const string Path = "/hubs/dashboard";

    public const string OperationalEventMethod = "operationalEvent";
}
