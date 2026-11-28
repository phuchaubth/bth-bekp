using Consent.Application.Abstractions;
using Consent.Application.Maintenance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Khởi động API trong bộ nhớ, KHÔNG có cơ sở dữ liệu thật (chuỗi kết nối giả). Dùng cho các test
/// pipeline/xác thực/phân quyền không chạm tới dữ liệu.
/// </summary>
public class ConsentApiFactory : WebApplicationFactory<Program>
{
    public const string ThrowingPath = "/__test/throw";

    protected virtual IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(TestSettings.RequiredSecrets())
    {
        ["ConnectionStrings:Default"] =
            "Server=127.0.0.1,1;Database=ConsentTest;User Id=test;Password=not-a-real-password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1",
    };

    /// <summary>Tạo access token hợp lệ (cùng khóa ký với API) cho người dùng giả với các quyền chỉ định.</summary>
    public string CreateAccessToken(params string[] permissions)
    {
        var tokens = Services.GetRequiredService<ITokenService>();
        return tokens.CreateAccessToken(new AccessTokenClaims(
            Guid.NewGuid(), "kiem.thu", "Kiểm thử", null, [], permissions, MustChangePassword: false)).Value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new ThrowingEndpointStartupFilter());
            // Không có CSDL: chế độ bảo trì (quyết định 53) luôn tắt, không đọc cấu hình trong CSDL ở mỗi yêu cầu.
            services.Replace(ServiceDescriptor.Singleton(AlwaysOffMaintenance()));
        });
    }

    /// <summary>Trạng thái "không bảo trì" giữ mãi trong bộ nhớ.</summary>
    private static MaintenanceStateCache AlwaysOffMaintenance()
    {
        var cache = new MaintenanceStateCache(TimeSpan.MaxValue);
        cache.Set(MaintenanceState.Off, DateTimeOffset.MinValue);
        return cache;
    }

    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowingPath, branch => branch.Run(
                (HttpContext _) => throw new InvalidOperationException("SECRET-INTERNAL-DETAIL")));
        };
    }
}
