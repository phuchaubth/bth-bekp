using Consent.Api.Infrastructure;
using Consent.Application;
using Consent.Infrastructure;
using Consent.Infrastructure.His;
using Consent.Persistence;

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
