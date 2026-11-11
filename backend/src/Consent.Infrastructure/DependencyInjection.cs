using Consent.Application.Abstractions;
using Consent.Infrastructure.His;
using Consent.Infrastructure.Security;
using Consent.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Đăng ký dịch vụ hạ tầng: đồng hồ, băm mật khẩu, JWT, mã hóa dữ liệu, HIS (sau này: SmartCA, PDF…).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
        services.AddSingleton<IBlindIndexer, HmacBlindIndexer>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.HasValidSigningKey,
                $"Thiếu hoặc khóa ký JWT quá ngắn (cần ≥ {JwtOptions.MinimumSigningKeyBytes} byte). Hãy cấu hình biến môi trường Jwt__SigningKey.")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes phải trong khoảng 1–60 phút.")
            .ValidateOnStart();

        services.AddOptions<DataProtectionOptions>()
            .Bind(configuration.GetSection(DataProtectionOptions.SectionName))
            .Validate(o => DataProtectionOptions.IsValidKey(o.FieldEncryptionKey) && DataProtectionOptions.IsValidKey(o.BlindIndexKey),
                "Thiếu khóa bảo vệ dữ liệu: DataProtection__FieldEncryptionKey và DataProtection__BlindIndexKey phải là Base64 của 32 byte ngẫu nhiên.")
            .Validate(o => o.FieldEncryptionKey != o.BlindIndexKey, "Khóa mã hóa và khóa chỉ mục mù phải khác nhau.")
            .ValidateOnStart();

        services.AddOptions<HisGatewayOptions>()
            .Bind(configuration.GetSection(HisGatewayOptions.SectionName))
            .Validate(o => string.Equals(o.Provider, HisGatewayOptions.FakeProvider, StringComparison.OrdinalIgnoreCase),
                "His:Provider hiện chỉ hỗ trợ 'Fake' (kết nối FPT eHospital sẽ bổ sung khi có tài liệu API).")
            .ValidateOnStart();
        services.AddSingleton<IHisGateway, FakeHisGateway>();

        return services;
    }
}
