using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Khởi động API trong bộ nhớ. Chuỗi kết nối là giá trị giả, không trỏ tới máy chủ thật
/// (các test ở đây không truy cập cơ sở dữ liệu).
/// </summary>
public sealed class ConsentApiFactory : WebApplicationFactory<Program>
{
    public const string ThrowingPath = "/__test/throw";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Server=127.0.0.1,1;Database=ConsentTest;User Id=test;Password=not-a-real-password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1");

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new ThrowingEndpointStartupFilter()));
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
