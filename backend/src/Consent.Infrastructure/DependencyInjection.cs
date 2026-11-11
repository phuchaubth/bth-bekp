using Consent.Application.Abstractions;
using Consent.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Đăng ký các dịch vụ hạ tầng (đồng hồ, sau này: SmartCA, HIS, PDF, lưu file…).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
