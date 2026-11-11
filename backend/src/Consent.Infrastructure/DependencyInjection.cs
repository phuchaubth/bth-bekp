using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Infrastructure.His;
using Consent.Infrastructure.Pdf;
using Consent.Infrastructure.Security;
using Consent.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        services.AddSingleton<IConsentPdfRenderer, MigraDocConsentPdfRenderer>();
        services.AddOptions<HospitalOptions>().Bind(configuration.GetSection(HospitalOptions.SectionName));

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

        // Hiện chưa kết nối HIS (mặc định "None"). "Fake" chỉ để phát triển/kiểm thử; Program.cs chặn ở Production.
        services.AddOptions<HisGatewayOptions>()
            .Bind(configuration.GetSection(HisGatewayOptions.SectionName))
            .Validate(o => o.IsKnownProvider,
                "His:Provider chỉ nhận 'None' (chưa kết nối HIS, mặc định) hoặc 'Fake' (HIS giả lập để phát triển và kiểm thử).")
            .ValidateOnStart();
        services.AddSingleton<IHisGateway>(sp => sp.GetRequiredService<IOptions<HisGatewayOptions>>().Value.IsFake
            ? ActivatorUtilities.CreateInstance<FakeHisGateway>(sp)
            : new DisabledHisGateway());

        return services;
    }
}
