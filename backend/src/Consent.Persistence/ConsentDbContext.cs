using Consent.Application.Abstractions;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Exports;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Persistence.Configurations;
using Consent.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Consent.Persistence;

/// <summary>
/// DbContext chính của ứng dụng (SQL Server). Các cột nhạy cảm được mã hóa trong suốt bằng <see cref="IFieldEncryptor"/>.
/// Lưu ý: EF Core lưu đệm mô hình cho mỗi kiểu DbContext, nên mọi instance phải dùng cùng một khóa mã hóa.
/// </summary>
public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options, IFieldEncryptor fieldEncryptor) : DbContext(options)
{
    public DbSet<Department> Departments => Set<Department>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<ConsentTemplate> ConsentTemplates => Set<ConsentTemplate>();

    public DbSet<ConsentForm> ConsentForms => Set<ConsentForm>();

    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditLogIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditLogIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsentDbContext).Assembly);
        var encrypted = new EncryptedStringConverter(fieldEncryptor);
        PatientConfiguration.ConfigureEncryption(modelBuilder, encrypted);
        ConsentFormConfiguration.ConfigureEncryption(modelBuilder, encrypted);
        ConsentSignatureConfiguration.ConfigureEncryption(modelBuilder, new EncryptedBytesConverter(fieldEncryptor));
        modelBuilder.HasSequence<long>(ConsentFormNumberGenerator.SequenceName).StartsAt(1).IncrementsBy(1);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Lớp bảo vệ thứ nhất (lớp thứ hai là trigger trong cơ sở dữ liệu).</summary>
    private void GuardAuditLogIsAppendOnly()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Nhật ký audit chỉ được ghi thêm, không được sửa hoặc xóa.");
        }
    }
}
