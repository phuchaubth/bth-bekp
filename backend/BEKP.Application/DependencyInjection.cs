using BEKP.Application.Common;
using BEKP.Application.Dashboard;
using BEKP.Application.Inpatient;
using BEKP.Application.TimeStandards;
using Microsoft.Extensions.DependencyInjection;

namespace BEKP.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<HospitalTimeOptions>();
        services.AddOptions<TimeStandardsOptions>();
        services.AddSingleton<HospitalCalendar>();
        services.AddSingleton<TimeStandardCatalog>();
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        services.AddScoped<ITimeStandardsService, TimeStandardsService>();
        services.AddScoped<IInpatientCommandService, InpatientCommandService>();
        return services;
    }
}
