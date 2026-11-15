using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Infrastructure.Exports;
using Consent.Infrastructure.His;
using Consent.Infrastructure.Pdf;
using Consent.Infrastructure.PatientSource;
using Consent.Infrastructure.Security;
using Consent.Infrastructure.Storage;
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
        services.AddSingleton<IDocumentStore, EncryptedFileDocumentStore>();
        services.AddSingleton<ITabularFileWriter, XlsxTabularFileWriter>();
        services.AddSingleton<ITabularFileWriter, CsvTabularFileWriter>();
        services.AddOptions<DocumentStorageOptions>()
            .Bind(configuration.GetSection(DocumentStorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DocumentsPath),
                "Thiếu Storage:DocumentsPath (thư mục lưu PDF phiếu đã ký).")
            .ValidateOnStart();
        services.AddOptions<HospitalOptions>().Bind(configuration.GetSection(HospitalOptions.SectionName));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.HasValidSigningKey,
                $"Thiếu hoặc khóa ký JWT quá ngắn (cần ≥ {JwtOptions.MinimumSigningKeyBytes} byte). Hãy cấu hình biến môi trường Jwt__SigningKey.")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes phải trong khoảng 1–60 phút.")
            .Validate(o => o.SigningTokenMinutes is >= 5 and <= 120, "Jwt:SigningTokenMinutes phải trong khoảng 5–120 phút.")
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

        // Nguồn thông tin hành chính người bệnh: mặc định tắt; "Api" gọi dịch vụ riêng PatientInfo.Api (chỉ đọc CSDL bệnh viện).
        services.AddOptions<PatientSourceOptions>()
            .Bind(configuration.GetSection(PatientSourceOptions.SectionName))
            .Validate(o => o.IsKnownProvider, "PatientSource:Provider chỉ nhận 'None' (mặc định) hoặc 'Api'.")
            .Validate(o => !o.IsApi || o.HasValidBaseUrl,
                "PatientSource:BaseUrl phải là địa chỉ HTTPS của API thông tin người bệnh (chỉ cho phép HTTP khi cùng máy: localhost).")
            .Validate(o => !o.IsApi || o.HasValidApiKey,
                $"Thiếu khóa API: đặt biến môi trường PatientSource__ApiKey (≥ {PatientSourceOptions.MinimumApiKeyLength} ký tự).")
            .ValidateOnStart();
        services.TryAddSingleton(_ => new PatientSourceHttp(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }));
        services.AddSingleton<IPatientSource>(sp => sp.GetRequiredService<IOptions<PatientSourceOptions>>().Value.IsApi
            ? ActivatorUtilities.CreateInstance<ApiPatientSource>(sp)
            : new DisabledPatientSource());
        // Lượt tiếp nhận (quyết định 25) lấy qua cùng API; bắt buộc gắn hay không do quản trị bật/tắt ở Cấu hình hệ thống.
        services.AddSingleton<IEncounterSource>(sp => sp.GetRequiredService<IOptions<PatientSourceOptions>>().Value.IsApi
            ? ActivatorUtilities.CreateInstance<ApiEncounterSource>(sp)
            : new DisabledEncounterSource());

        return services;
    }
}
