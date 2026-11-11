using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Consent.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Đăng ký <see cref="ConsentDbContext"/> với SQL Server. Chuỗi kết nối lấy từ cấu hình
    /// <c>ConnectionStrings:Default</c> (biến môi trường <c>ConnectionStrings__Default</c>), không lưu trong mã nguồn.
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

        services.AddDbContext<ConsentDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        return services;
    }
}
