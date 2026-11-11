using Consent.Application.Abstractions;
using Consent.Persistence.Auditing;
using Consent.Persistence.Repositories;
using Consent.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Consent.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Đăng ký <see cref="ConsentDbContext"/> (SQL Server), repository, unit of work, audit và seeding.
    /// Chuỗi kết nối lấy từ <c>ConnectionStrings:Default</c> (biến môi trường <c>ConnectionStrings__Default</c>).
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Thiếu chuỗi kết nối 'ConnectionStrings:{ConnectionStringName}'. " +
                "Hãy cấu hình biến môi trường ConnectionStrings__Default.");
        }

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<ConsentDbContext>((serviceProvider, options) => options
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
