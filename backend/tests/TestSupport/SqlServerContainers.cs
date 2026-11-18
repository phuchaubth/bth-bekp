using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;

namespace TestSupport;

/// <summary>
/// Khởi động SQL Server trong container cho test. Khi nhiều container SQL Server khởi động cùng lúc trên một máy (CI chạy
/// song song các project test, cùng chờ một lần tải image), SQL Server đôi khi dừng ngay lúc khởi động ("This program has
/// encountered a fatal error", Testcontainers báo <see cref="ContainerNotRunningException"/>). Khi đó tạo container mới và
/// thử lại, tối đa <c>maxAttempts</c> lần; các lỗi khác báo ngay.
/// </summary>
internal static class SqlServerContainers
{
    public static async Task<MsSqlContainer> StartAsync(Func<MsSqlContainer> create, int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(create);
        for (var attempt = 1; ; attempt++)
        {
            var container = create();
            try
            {
                await container.StartAsync();
                return container;
            }
            catch (ContainerNotRunningException) when (attempt < maxAttempts)
            {
                await Console.Error.WriteLineAsync(
                    $"SQL Server trong container dừng khi khởi động (lần {attempt}/{maxAttempts}); thử lại với container mới.");
                await container.DisposeAsync();
            }
        }
    }
}
