using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence;

/// <summary>DbContext chính của ứng dụng (SQL Server). Các bảng nghiệp vụ được thêm từ Sprint 1.</summary>
public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsentDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
