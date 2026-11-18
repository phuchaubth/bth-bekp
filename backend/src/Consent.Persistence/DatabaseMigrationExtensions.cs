using Consent.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Consent.Persistence;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Áp dụng các migration còn thiếu. Chỉ bật trên môi trường phát triển/kiểm thử
    /// (cấu hình <c>Database:ApplyMigrationsOnStartup</c>); production chạy migration bằng script có kiểm soát.
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsentDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Khởi tạo vai trò hệ thống, tài khoản quản trị đầu tiên và (tùy chọn) dữ liệu minh họa.</summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
    }
}
