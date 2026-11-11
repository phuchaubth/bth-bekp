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

        return new ConsentDbContext(options, new DesignTimeFieldEncryptor());
    }

    /// <summary>Công cụ migration chỉ cần mô hình (kiểu cột), không mã hóa dữ liệu thật.</summary>
    private sealed class DesignTimeFieldEncryptor : Application.Abstractions.IFieldEncryptor
    {
        public string Encrypt(string plaintext) => throw new NotSupportedException("Không dùng mã hóa ở chế độ design-time.");

        public string Decrypt(string ciphertext) => throw new NotSupportedException("Không dùng mã hóa ở chế độ design-time.");
    }
}
