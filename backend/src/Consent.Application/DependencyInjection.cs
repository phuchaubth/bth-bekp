using Microsoft.Extensions.DependencyInjection;

namespace Consent.Application;

public static class DependencyInjection
{
    /// <summary>Đăng ký các use case và validator của tầng Application.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
