using Consent.Application.Abstractions;
using Consent.Infrastructure.Security;
using Consent.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Đăng ký các dịch vụ hạ tầng: đồng hồ, băm mật khẩu, JWT (sau này: SmartCA, HIS, PDF…).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.HasValidSigningKey,
                $"Thiếu hoặc khóa ký JWT quá ngắn (cần ≥ {JwtOptions.MinimumSigningKeyBytes} byte). Hãy cấu hình biến môi trường Jwt__SigningKey.")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes phải trong khoảng 1–60 phút.")
            .ValidateOnStart();

        return services;
    }
}
