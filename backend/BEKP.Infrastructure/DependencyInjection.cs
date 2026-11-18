using BEKP.Application.Abstractions;
using BEKP.Infrastructure.Auditing;
using BEKP.Infrastructure.Realtime;
using BEKP.Infrastructure.Security;
using BEKP.Infrastructure.Simulation;
using BEKP.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace BEKP.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAuditLog, LoggerAuditLog>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpContextCurrentActor>();
        services.AddSingleton<IDashboardNotifier, SignalRDashboardNotifier>();
        services.AddOptions<SimulationOptions>();
        services.AddHostedService<SyntheticActivitySimulator>();
        return services;
    }
}
