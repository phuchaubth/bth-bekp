using BEKP.Application.Abstractions;
using BEKP.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BEKP.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Bekp";

    /// <param name="connectionString">SQL Server connection string, supplied through configuration or the ConnectionStrings__Bekp environment variable.</param>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<BekpDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured. Set the ConnectionStrings__{ConnectionStringName} environment variable.");
            }

            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", BekpDbContext.Schema);
                sql.EnableRetryOnFailure(3);
            });
        });
        services.AddScoped<IInpatientRepository, InpatientRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BekpDbContext>());
        services.AddOptions<DatabaseOptions>();
        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}
