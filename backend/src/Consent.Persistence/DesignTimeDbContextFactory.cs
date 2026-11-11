using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Consent.Persistence;

/// <summary>
/// Chỉ dùng cho công cụ <c>dotnet ef</c> khi tạo migration (không kết nối cơ sở dữ liệu).
/// Có thể ghi đè bằng biến môi trường <c>ConnectionStrings__Default</c>.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConsentDbContext>
{
    public ConsentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Database=ConsentManagement;Integrated Security=False;Encrypt=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ConsentDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ConsentDbContext(options);
    }
}
