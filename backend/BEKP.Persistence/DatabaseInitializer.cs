using BEKP.Application.Abstractions;
using BEKP.Application.Common;
using BEKP.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BEKP.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply EF Core migrations at startup. Recommended only for development and demo environments.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>Seed synthetic demo data into an empty database. Refused in the Production environment.</summary>
    public bool SeedSyntheticData { get; set; }
}

internal sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.ApplyMigrationsOnStartup && !settings.SeedSyntheticData)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BekpDbContext>();

        if (settings.ApplyMigrationsOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            LogMigrated(logger);
        }

        if (settings.SeedSyntheticData)
        {
            if (environment.IsProduction())
            {
                LogSeedRefused(logger);
                return;
            }

            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var calendar = scope.ServiceProvider.GetRequiredService<HospitalCalendar>();
            var now = clock.UtcNow;
            if (await SyntheticDataSeeder.SeedAsync(db, now, calendar.LocalDayStartUtc(now), calendar.LocalDate(now), cancellationToken).ConfigureAwait(false))
            {
                LogSeeded(logger);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied.")]
    private static partial void LogMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic data seeding is disabled in the Production environment.")]
    private static partial void LogSeedRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic demo data seeded (no real patient data).")]
    private static partial void LogSeeded(ILogger logger);
}
