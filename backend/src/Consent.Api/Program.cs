using Consent.Api.Infrastructure;
using Consent.Application;
using Consent.Infrastructure;
using Consent.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPersistence(builder.Configuration);

builder.Services.AddApiServices(builder.Configuration);

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
