using Consent.Api.Infrastructure;
using Consent.Application;
using Consent.Infrastructure;
using Consent.Infrastructure.His;
using Consent.Persistence;
using Consent.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPersistence(builder.Configuration);

builder.Services.AddApiServices(builder.Configuration);

// HIS giả lập sinh dữ liệu không có thật (mã người bệnh, đợt khám, thẻ BHYT): không bao giờ chạy ở Production.
builder.Services.AddOptions<HisGatewayOptions>()
    .Validate(o => !(o.IsFake && builder.Environment.IsProduction()),
        "HIS giả lập (His:Provider=Fake) không được dùng ở môi trường Production. Đặt His:Provider=None.");

// Dữ liệu minh họa (khoa, tài khoản demo, người bệnh giả lập) không bao giờ được tạo ở Production.
builder.Services.AddOptions<SeedOptions>()
    .Validate(o => !(o.DemoData && builder.Environment.IsProduction()),
        "Dữ liệu minh họa (Seed:DemoData) không được bật ở môi trường Production.")
    .ValidateOnStart();

// Yêu cầu lớn nhất hợp lệ là ảnh chữ ký (≤ 512 KB Base64) hoặc định nghĩa mẫu; chặn sớm các yêu cầu quá lớn.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

var app = builder.Build();

app.UseApiPipeline();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.ApplyMigrationsAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
{
    await app.Services.SeedDatabaseAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

await app.RunAsync().ConfigureAwait(false);

/// <summary>Khai báo để WebApplicationFactory trong integration test truy cập được.</summary>
public partial class Program;
